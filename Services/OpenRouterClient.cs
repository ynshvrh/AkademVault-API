using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkademVault_API.Services;

// OpenRouter-backed implementation of both digest (text) and multimodal AI clients.
public class OpenRouterClient : IDigestAIClient, IMultimodalAIClient
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _models;

    // Pool constructor: models are tried in order, falling through to the next on
    // any failure (rate-limit / out-of-credit / error / empty reply). Lets a free-tier
    // key stay up across several models' separate limits, with an optional paid model
    // last as a guaranteed backstop. Blank entries are ignored; an empty list throws.
    public OpenRouterClient(HttpClient http, string apiKey, IEnumerable<string> models, string baseUrl)
    {
        _models = models.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (_models.Count == 0)
            throw new ArgumentException("OpenRouterClient needs at least one model", nameof(models));

        _http = http;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Add("X-Title", "AkademVault");
    }

    // Single-model convenience overload — kept so existing callers (and smoke tests)
    // that pass one model id keep compiling. Delegates to the pool constructor.
    public OpenRouterClient(HttpClient http, string apiKey, string model, string baseUrl)
        : this(http, apiKey, new[] { model }, baseUrl)
    {
    }

    // Single-turn text completion used for the activity digest.
    public async Task<string> SummarizeAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        return await PostAndExtractAsync(model => new
        {
            model,
            max_tokens = 1024,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        }, ct);
    }

    // Multimodal completion — inlines each attachment as a data: URI (image_url / file).
    public async Task<string> CallAsync(string systemPrompt, string userPrompt, IEnumerable<MultimodalAttachment> attachments, CancellationToken ct = default)
    {
        var userContent = new List<object> { new { type = "text", text = userPrompt } };

        foreach (var att in attachments)
        {
            var dataUri = $"data:{att.MimeType};base64,{Convert.ToBase64String(att.Data)}";

            if (att.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                userContent.Add(new { type = "image_url", image_url = new { url = dataUri } });
            }
            else if (att.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                userContent.Add(new { type = "file", file = new { filename = "document.pdf", file_data = dataUri } });
            }
            else
            {
                throw new InvalidOperationException($"Непідтримуваний тип для multimodal: {att.MimeType}");
            }
        }

        var content = userContent.ToArray();
        return await PostAndExtractAsync(model => new
        {
            model,
            max_tokens = 2048,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content }
            }
        }, ct);
    }

    // Upper bound on how long we'll honour an upstream "retry-after" before giving up on a
    // model and moving to the next. Free models often report ~15-16s; we wait once, but never
    // longer than this so a request can't hang for minutes.
    private const double MaxRetryWaitSeconds = 20;

    // Tries each model in the pool until one returns content. The payload is rebuilt per
    // model (only the "model" field changes). On a 429 that carries a short retry-after, we
    // wait once and retry the SAME model before falling through (free models are often only
    // briefly rate-limited). Falls through on other non-2xx / empty reply. Throws with the
    // last error only when every model has failed, preserving the throw-on-failure contract.
    private async Task<string> PostAndExtractAsync(Func<string, object> buildPayload, CancellationToken ct)
    {
        string lastError = "no models configured";

        for (var i = 0; i < _models.Count; i++)
        {
            var model = _models[i];
            for (var attempt = 0; attempt < 2; attempt++)
            {
                HttpResponseMessage response;
                try
                {
                    response = await _http.PostAsJsonAsync("chat/completions", buildPayload(model), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = $"{model}: transport error: {ex.Message}";
                    break; // transport failure — move to next model
                }

                var raw = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"{model}: OpenRouter returned {(int)response.StatusCode}: {raw}";
                    // On the first 429 with a short retry-after, wait once and retry this model.
                    if ((int)response.StatusCode == 429 && attempt == 0
                        && TryGetRetryAfter(raw, out var delay) && delay <= MaxRetryWaitSeconds)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                        continue;
                    }
                    break; // other error, or no usable retry-after — move to next model
                }

                var parsed = JsonSerializer.Deserialize<ChatResponse>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    lastError = $"{model}: OpenRouter returned an empty response: {raw}";
                    break; // empty — move to next model
                }

                return content.Trim();
            }
        }

        throw new InvalidOperationException($"All OpenRouter models failed. Last error — {lastError}");
    }

    // Pulls retry_after_seconds out of OpenRouter's 429 error body, if present.
    private static bool TryGetRetryAfter(string raw, out double seconds)
    {
        seconds = 0;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("metadata", out var meta)
                && meta.TryGetProperty("retry_after_seconds", out var ra)
                && ra.TryGetDouble(out var v) && v > 0)
            {
                seconds = v;
                return true;
            }
        }
        catch (JsonException) { /* not JSON or unexpected shape — no retry hint */ }
        return false;
    }


    private class ChatResponse
    {
        [JsonPropertyName("choices")] public Choice[]? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private class ChatMessage
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
