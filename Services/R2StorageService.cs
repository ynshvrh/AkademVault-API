using Amazon.S3;
using Amazon.S3.Model;

namespace AkademVault_API.Services;

// IR2StorageService implementation backed by an S3-compatible client pointed at Cloudflare R2.
public class R2StorageService : IR2StorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public R2StorageService(IAmazonS3 s3, string bucket)
    {
        _s3 = s3;
        _bucket = bucket;
    }

    // PutObject with DisablePayloadSigning=true — R2 rejects AWS chunked signing.
    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true
        };

        await _s3.PutObjectAsync(request, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = key
        }, ct);
    }

    // Signs a download URL and overrides Content-Disposition so the browser saves with the real filename.
    public string GetPresignedDownloadUrl(string key, string fileName, TimeSpan ttl)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl),
            ResponseHeaderOverrides =
            {
                ContentDisposition = $"attachment; filename=\"{fileName}\""
            }
        };

        return _s3.GetPreSignedURL(request);
    }
}
