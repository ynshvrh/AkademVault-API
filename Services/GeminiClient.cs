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
    private readonly string _model;

    public GeminiClient(HttpClient http, string apiKey, string model, string baseUrl)
    {
        _http = http;
        _apiKey = apiKey;
        _model = model;

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> CallAsync(string systemPrompt, string userPrompt, IEnumerable<MultimodalAttachment> attachments, CancellationToken ct = default)
    {
        // One user "content" with the text part first, then any inline image/PDF parts.
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

        // Key goes in the query string per the AI Studio REST convention.
        var url = $"v1beta/models/{_model}:generateContent?key={_apiKey}";

        var response = await _http.PostAsJsonAsync(url, payload, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini returned {(int)response.StatusCode}: {raw}");

        var parsed = JsonSerializer.Deserialize<GeminiResponse>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var text = parsed?.Candidates?
            .FirstOrDefault()?.Content?.Parts?
            .Select(p => p.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Gemini returned an empty response: {raw}");

        return text.Trim();
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
