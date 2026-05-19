namespace AkademVault_API.Services;

// Inline file payload sent to a multimodal LLM (image or PDF).
public record MultimodalAttachment(string MimeType, byte[] Data);

// Multimodal LLM client (text + image/PDF) used by ScheduleParser.
public interface IMultimodalAIClient
{
    // Calls the LLM with a system prompt, a user prompt and any number of inline attachments.
    Task<string> CallAsync(string systemPrompt, string userPrompt, IEnumerable<MultimodalAttachment> attachments, CancellationToken ct = default);
}
