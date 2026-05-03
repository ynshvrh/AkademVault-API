using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using DotNetEnv; // Не забудь додати цей пакет

var builder = WebApplication.CreateBuilder(args);

// 1. ЗАВАНТАЖЕННЯ КОНФІГУРАЦІЇ З .ENV
DotNetEnv.Env.Load();

var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("DB_NAME");
var dbUser = Environment.GetEnvironmentVariable("DB_USER");
var dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");
var dbSsl  = Environment.GetEnvironmentVariable("DB_SSLMODE") ?? "require";

// Формуємо рядок підключення для Neon
var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass};SSL Mode={dbSsl};Trust Server Certificate=true;";

// Тимчасовий дебаг (видали після перевірки)
if (string.IsNullOrEmpty(dbHost)) 
{
    Console.WriteLine("❌ ПОМИЛКА: Не вдалося прочитати DB_HOST з .env!");
} 
else 
{
    Console.WriteLine($"✅ Підключення до бази на хості: {dbHost}");
}

// 2. ПІДКЛЮЧЕННЯ ДО БД
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 3. НАЛАШТУВАННЯ CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200") 
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); 
    });
});

// 4. АУТЕНТИФІКАЦІЯ (COOKIES)
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 5. ПАЙПЛАЙН ОБРОБКИ ЗАПИТІВ
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AngularPolicy"); 

app.UseAuthentication(); 
app.UseAuthorization();  

app.MapControllers();

app.Run();