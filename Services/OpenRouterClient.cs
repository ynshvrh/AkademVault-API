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

    // Tries each model in the pool until one returns content. The payload is rebuilt per
    // model (only the "model" field changes). Falls through on non-2xx or empty reply;
    // throws with the last error only when every model in the pool has failed, preserving
    // the original "this method throws on failure" contract for callers.
    private async Task<string> PostAndExtractAsync(Func<string, object> buildPayload, CancellationToken ct)
    {
        string lastError = "no models configured";

        for (var i = 0; i < _models.Count; i++)
        {
            var model = _models[i];
            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync("chat/completions", buildPayload(model), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = $"{model}: transport error: {ex.Message}";
                continue;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                lastError = $"{model}: OpenRouter returned {(int)response.StatusCode}: {raw}";
                continue;
            }

            var parsed = JsonSerializer.Deserialize<ChatResponse>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                lastError = $"{model}: OpenRouter returned an empty response: {raw}";
                continue;
            }

            return content.Trim();
        }

        throw new InvalidOperationException($"All OpenRouter models failed. Last error — {lastError}");
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
