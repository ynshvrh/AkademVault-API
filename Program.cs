using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Hubs;
using AkademVault_API.Services;
using Amazon.S3;
using Amazon.Runtime;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);


DotNetEnv.Env.Load();

var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("DB_NAME");
var dbUser = Environment.GetEnvironmentVariable("DB_USER");
var dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");
var dbSsl  = Environment.GetEnvironmentVariable("DB_SSLMODE") ?? "require";


var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass};SSL Mode={dbSsl};Trust Server Certificate=true;";


if (string.IsNullOrEmpty(dbHost))
{
    Console.WriteLine("❌ ПОМИЛКА: Не вдалося прочитати DB_HOST з .env!");
}
else
{
    Console.WriteLine($"✅ Підключення до бази на хості: {dbHost}");
}


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));


var r2AccountId = Environment.GetEnvironmentVariable("R2_ACCOUNT_ID");
var r2AccessKey = Environment.GetEnvironmentVariable("R2_ACCESS_KEY");
var r2SecretKey = Environment.GetEnvironmentVariable("R2_SECRET_KEY");
var r2Bucket    = Environment.GetEnvironmentVariable("R2_BUCKET_NAME");
var r2Endpoint  = Environment.GetEnvironmentVariable("R2_ENDPOINT")
                  ?? (string.IsNullOrEmpty(r2AccountId) ? null : $"https://{r2AccountId}.r2.cloudflarestorage.com");

if (string.IsNullOrEmpty(r2AccessKey) || string.IsNullOrEmpty(r2SecretKey) || string.IsNullOrEmpty(r2Bucket) || string.IsNullOrEmpty(r2Endpoint))
{
    Console.WriteLine("❌ ПОМИЛКА: Не вдалося прочитати конфігурацію R2 з .env!");
}
else
{
    Console.WriteLine($"✅ R2 endpoint: {r2Endpoint}, bucket: {r2Bucket}");
}

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var credentials = new BasicAWSCredentials(r2AccessKey, r2SecretKey);
    var config = new AmazonS3Config
    {
        ServiceURL = r2Endpoint,
        ForcePathStyle = true,
        AuthenticationRegion = "auto"
    };
    return new AmazonS3Client(credentials, config);
});

builder.Services.AddSingleton<IR2StorageService>(sp =>
    new R2StorageService(sp.GetRequiredService<IAmazonS3>(), r2Bucket ?? string.Empty));


var openRouterKey   = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
var openRouterModel = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "anthropic/claude-haiku-4-5";
var openRouterUrl   = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL") ?? "https://openrouter.ai/api/v1";

if (string.IsNullOrEmpty(openRouterKey))
{
    Console.WriteLine("❌ ПОМИЛКА: Не вдалося прочитати OPENROUTER_API_KEY з .env!");
}
else
{
    Console.WriteLine($"✅ OpenRouter модель: {openRouterModel}");
}

builder.Services.AddHttpClient(nameof(OpenRouterClient))
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(60));

builder.Services.AddTransient<IDigestAIClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new OpenRouterClient(factory.CreateClient(nameof(OpenRouterClient)), openRouterKey ?? string.Empty, openRouterModel, openRouterUrl);
});


var corsOriginsRaw = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? "http://localhost:4200";
var corsOrigins = corsOriginsRaw
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

Console.WriteLine($"✅ CORS allowed origins: {string.Join(", ", corsOrigins)}");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});


builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "AkademVault.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });


builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "AkademVault.AntiForgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddSignalR();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 26_214_400;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}


app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        logger.LogError(feature?.Error, "Необроблена помилка на шляху {Path}", context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var payload = app.Environment.IsDevelopment()
            ? new { message = "Сталася внутрішня помилка сервера.", detail = feature?.Error.Message }
            : new { message = "Сталася внутрішня помилка сервера.", detail = (string?)null };

        await context.Response.WriteAsJsonAsync(payload);
    });
});

app.UseCors("AngularPolicy");

app.UseAuthentication();
app.UseAuthorization();


app.Use(async (context, next) =>
{
    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
    {
        HttpOnly = false,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps
    });
    await next();
});

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
