using Xunit;
using FluentAssertions;
using AkademVault_API.Services;
using DotNetEnv;

namespace Tests;


[Trait("Category", "Integration")]
public class OpenRouterSmokeTest
{
    [Fact]
    public async Task SummarizeAsync_ShouldReturnNonEmptyResponse()
    {

        var envPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));
        Env.Load(envPath);

        var apiKey  = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var model   = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "anthropic/claude-haiku-4-5";
        var baseUrl = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL") ?? "https://openrouter.ai/api/v1";

        apiKey.Should().NotBeNullOrEmpty("OPENROUTER_API_KEY має бути в .env");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new OpenRouterClient(http, apiKey!, model, baseUrl);

        var result = await client.SummarizeAsync(
            "Відповідай українською мовою, дуже коротко.",
            "Скажи одне слово: 'Тест'.");

        result.Should().NotBeNullOrWhiteSpace();
        Console.WriteLine($"OpenRouter ({model}) відповідь: {result}");
    }
}