namespace AkademVault_API.Services;

public record MultimodalAttachment(string MimeType, byte[] Data);

public interface IMultimodalAIClient
{
    Task<string> CallAsync(string systemPrompt, string userPrompt, IEnumerable<MultimodalAttachment> attachments, CancellationToken ct = default);
}
