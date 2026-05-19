namespace AkademVault_API.Services;

// Text-only LLM client used by DigestController to summarise group activity.
public interface IDigestAIClient
{
    // Sends a system+user prompt to the LLM and returns the trimmed completion text.
    Task<string> SummarizeAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
