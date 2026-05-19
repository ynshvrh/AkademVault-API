using Xunit;
using FluentAssertions;
using Amazon.S3;
using Amazon.Runtime;
using AkademVault_API.Services;
using System.Text;
using DotNetEnv;

namespace Tests;


// Smoke test for R2StorageService — exercises real Cloudflare R2 using credentials from .env.
[Trait("Category", "Integration")]
public class R2SmokeTest
{
    // Full upload → presigned download → delete → 404 roundtrip against a live R2 bucket.
    [Fact]
    public async Task FullRoundtrip_ShouldUploadDownloadAndDelete()
    {

        var envPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));
        Env.Load(envPath);

        var accountId = Environment.GetEnvironmentVariable("R2_ACCOUNT_ID");
        var accessKey = Environment.GetEnvironmentVariable("R2_ACCESS_KEY");
        var secretKey = Environment.GetEnvironmentVariable("R2_SECRET_KEY");
        var bucket    = Environment.GetEnvironmentVariable("R2_BUCKET_NAME");
        var endpoint  = Environment.GetEnvironmentVariable("R2_ENDPOINT")
                        ?? $"https://{accountId}.r2.cloudflarestorage.com";

        accessKey.Should().NotBeNullOrEmpty("R2_ACCESS_KEY має бути в .env");
        secretKey.Should().NotBeNullOrEmpty("R2_SECRET_KEY має бути в .env");
        bucket.Should().NotBeNullOrEmpty("R2_BUCKET_NAME має бути в .env");


        var s3 = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "auto"
            });

        var storage = new R2StorageService(s3, bucket!);
        var key = $"smoke-test/{Guid.NewGuid()}.txt";
        var payload = Encoding.UTF8.GetBytes("Привіт з AkademVault smoke-test " + Guid.NewGuid());


        await using (var ms = new MemoryStream(payload))
        {
            await storage.UploadAsync(key, ms, "text/plain");
        }


        var url = storage.GetPresignedDownloadUrl(key, "smoke.txt", TimeSpan.FromMinutes(2));
        url.Should().StartWith(endpoint);


        using var http = new HttpClient();
        var response = await http.GetAsync(url);
        response.IsSuccessStatusCode.Should().BeTrue(
            $"presigned URL має повертати 200, але отримали {(int)response.StatusCode} {response.ReasonPhrase}");

        var downloaded = await response.Content.ReadAsByteArrayAsync();
        downloaded.Should().BeEquivalentTo(payload);


        await storage.DeleteAsync(key);


        var getAfterDelete = await http.GetAsync(storage.GetPresignedDownloadUrl(key, "smoke.txt", TimeSpan.FromMinutes(1)));
        getAfterDelete.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound,
            "після видалення об'єкт не має знаходитися");
    }
}
