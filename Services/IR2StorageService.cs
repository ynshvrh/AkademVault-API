namespace AkademVault_API.Services;

public interface IR2StorageService
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    string GetPresignedDownloadUrl(string key, string fileName, TimeSpan ttl);
}
