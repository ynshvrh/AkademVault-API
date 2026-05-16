using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkademVault_API.Services;

public class OpenRouterClient : IDigestAIClient, IMultimodalAIClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OpenRouterClient(HttpClient http, string apiKey, string model, string baseUrl)
    {
        _http = http;
        _model = model;

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Add("X-Title", "AkademVault");
    }

    public async Task<string> SummarizeAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            max_tokens = 1024,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        return await PostAndExtractAsync(payload, ct);
    }

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

        var payload = new
        {
            model = _model,
            max_tokens = 2048,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent.ToArray() }
            }
        };

        return await PostAndExtractAsync(payload, ct);
    }

    private async Task<string> PostAndExtractAsync(object payload, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("chat/completions", payload, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenRouter повернув {(int)response.StatusCode}: {raw}");

        var parsed = JsonSerializer.Deserialize<ChatResponse>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"OpenRouter повернув порожню відповідь: {raw}");

        return content.Trim();
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
