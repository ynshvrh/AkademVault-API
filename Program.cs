using Microsoft.AspNetCore.Authentication.Cookies;
// TODO(xsrf-reenable): restore when antiforgery is wired up for cross-origin SPA — see notes near AddAntiforgery below.
// using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
// TODO(xsrf-reenable): JsonAntiforgeryFilter consumer — restore when XSRF is re-enabled.
// using Microsoft.AspNetCore.Mvc.Filters;
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

// S3 client points at Cloudflare R2; ForcePathStyle is required for R2's bucket-in-path URLs.
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


builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<IShortCodeGenerator, ShortCodeGenerator>();


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

builder.Services.AddTransient<OpenRouterClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new OpenRouterClient(factory.CreateClient(nameof(OpenRouterClient)), openRouterKey ?? string.Empty, openRouterModel, openRouterUrl);
});

builder.Services.AddTransient<IDigestAIClient>(sp => sp.GetRequiredService<OpenRouterClient>());
builder.Services.AddTransient<IMultimodalAIClient>(sp => sp.GetRequiredService<OpenRouterClient>());
builder.Services.AddTransient<IScheduleParser, ScheduleParser>();


var appBaseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL");
if (!string.IsNullOrEmpty(appBaseUrl))
{
    Console.WriteLine($"✅ APP_BASE_URL: {appBaseUrl}");
}

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


// Cross-origin deployments (web and API on different domains) require SameSite=None + Secure.
// Development stays on Lax/SameAsRequest so cookies work over plain http://localhost.
var crossSiteCookies = !builder.Environment.IsDevelopment();
var cookieSameSite = crossSiteCookies ? SameSiteMode.None : SameSiteMode.Lax;
var cookieSecure = crossSiteCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "AkademVault.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecure;
        options.Cookie.SameSite = cookieSameSite;

        // Persist the auth cookie for 30 days and refresh it on each activity (sliding window).
        // Combined with `IsPersistent = true` in AuthController.Login, this means the cookie
        // survives browser restarts — without these, it's a session cookie that dies on tab close
        // and the user has to log in again every visit.
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;

        // SPA expects JSON 401 on unauth instead of a 302 redirect.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });


// TODO(xsrf-reenable): Antiforgery is disabled because the SPA on a separate origin
// (web on Cloudflare Pages, API on Render) cannot read a cookie set by this API origin via
// document.cookie, so Angular has no way to echo the token back as X-XSRF-TOKEN.
// Current CSRF defenses: CORS allowlist + SameSite=None+Secure cookies + JSON-only controllers
// (POST application/json always triggers a CORS preflight against the origin allowlist).
// To re-enable: return the token in a response header (e.g., X-XSRF-Token) from /auth/me and
// /auth/login, add an Angular HttpInterceptor that reads it on every response and includes
// it on subsequent requests. Also restore JsonAntiforgeryFilter registration below.
/*
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "AkademVault.AntiForgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecure;
    options.Cookie.SameSite = cookieSameSite;
});
*/

builder.Services.AddControllersWithViews(/* TODO(xsrf-reenable): options => options.Filters.Add<JsonAntiforgeryFilter>() */);

builder.Services.AddSignalR();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 26_214_400;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// IHttpContextAccessor lets GraphQL resolvers read the cookie-authenticated ClaimsPrincipal.
builder.Services.AddHttpContextAccessor();

// Read-only GraphQL module — co-exists with REST under /graphql; reuses the same cookie auth + EF context.
builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<AkademVault_API.GraphQL.Query>();

var app = builder.Build();


// Apply pending EF migrations on startup. Idempotent — safe to run on every cold start.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("✅ EF міграції застосовані");
}


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}


// Global JSON exception handler so the SPA always receives a structured 500 body.
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

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapGraphQL("/graphql");

app.Run();
