namespace AkademVault_API.Services;

// Thin abstraction over Cloudflare R2 used by the storage controller and tests.
public interface IR2StorageService
{
    // Uploads a stream under the given object key with the supplied content-type.
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    // Deletes the object at the given key (no-op if it does not exist).
    Task DeleteAsync(string key, CancellationToken ct = default);

    // Returns a time-limited presigned GET URL that forces a download with the given filename.
    string GetPresignedDownloadUrl(string key, string fileName, TimeSpan ttl);
}
