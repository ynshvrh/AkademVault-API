using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkademVault_API.Services;

// Google Gemini (AI Studio) implementation of the multimodal client, used for schedule
// parsing. Talks to the native generateContent REST API — a different shape from
// OpenRouter's OpenAI-style endpoint — so it can use Google's separate free tier, which
// includes vision. Selected over OpenRouterClient only when GEMINI_API_KEY is set
// (see Program.cs); otherwise the OpenRouter multimodal pool stays in charge.
public class GeminiClient : IMultimodalAIClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IReadOnlyList<string> _models;

    // Pool constructor: models are tried in order, falling through to the next on any
    // failure (the free tier's 429 rate-limit is the common one). Blank entries are
    // ignored; an empty list throws.
    public GeminiClient(HttpClient http, string apiKey, IEnumerable<string> models, string baseUrl)
    {
        _models = models.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (_models.Count == 0)
            throw new ArgumentException("GeminiClient needs at least one model", nameof(models));

        _http = http;
        _apiKey = apiKey;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // Single-model convenience overload.
    public GeminiClient(HttpClient http, string apiKey, string model, string baseUrl)
        : this(http, apiKey, new[] { model }, baseUrl)
    {
    }

    public async Task<string> CallAsync(string systemPrompt, string userPrompt, IEnumerable<MultimodalAttachment> attachments, CancellationToken ct = default)
    {
        // One user "content" with the text part first, then any inline image/PDF parts.
        // Built once and reused across model attempts — only the URL's model changes.
        var parts = new List<object> { new { text = userPrompt } };

        foreach (var att in attachments)
        {
            if (att.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || att.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(new
                {
                    inline_data = new
                    {
                        mime_type = att.MimeType,
                        data = Convert.ToBase64String(att.Data),
                    }
                });
            }
            else
            {
                throw new InvalidOperationException($"Непідтримуваний тип для multimodal: {att.MimeType}");
            }
        }

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = parts.ToArray() } },
            generationConfig = new { maxOutputTokens = 2048 },
        };

        string lastError = "no models configured";

        for (var i = 0; i < _models.Count; i++)
        {
            var model = _models[i];
            // Key goes in the query string per the AI Studio REST convention.
            var url = $"v1beta/models/{model}:generateContent?key={_apiKey}";

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync(url, payload, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = $"{model}: transport error: {ex.Message}";
                continue;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                lastError = $"{model}: Gemini returned {(int)response.StatusCode}: {raw}";
                continue;
            }

            var parsed = JsonSerializer.Deserialize<GeminiResponse>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var text = parsed?.Candidates?
                .FirstOrDefault()?.Content?.Parts?
                .Select(p => p.Text)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            if (string.IsNullOrWhiteSpace(text))
            {
                lastError = $"{model}: Gemini returned an empty response: {raw}";
                continue;
            }

            return text.Trim();
        }

        throw new InvalidOperationException($"All Gemini models failed. Last error — {lastError}");
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")] public Candidate[]? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonPropertyName("content")] public Content? Content { get; set; }
    }

    private class Content
    {
        [JsonPropertyName("parts")] public Part[]? Parts { get; set; }
    }

    private class Part
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
