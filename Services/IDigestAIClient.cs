namespace AkademVault_API.Services;

public interface IDigestAIClient
{
    Task<string> SummarizeAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}