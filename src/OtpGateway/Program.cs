using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OtpGateway.Api;
using OtpGateway.Channels;
using OtpGateway.Storage;
using OtpGateway.Services;

var builder = WebApplication.CreateBuilder(args);

var configConnStr = builder.Configuration.GetConnectionString("Default");
string connStr;
if (!string.IsNullOrEmpty(configConnStr))
{
    connStr = configConnStr;
}
else
{
    var root = FindSolutionRoot(builder.Environment.ContentRootPath);
    var dbPath = Path.GetFullPath(Path.Combine(root, "data", "otp.db"));
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    connStr = $"Data Source={dbPath}";
}

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(connStr));

builder.Services.AddHttpClient();

builder.Services.AddSingleton<TelegramChannel>();
builder.Services.AddSingleton<IOtpChannel>(sp => sp.GetRequiredService<TelegramChannel>());

builder.Services.AddSingleton<WhatsAppChannel>();
builder.Services.AddSingleton<IOtpChannel>(sp => sp.GetRequiredService<WhatsAppChannel>());

builder.Services.AddSingleton<EmailChannel>();
builder.Services.AddSingleton<IOtpChannel>(sp => sp.GetRequiredService<EmailChannel>());

builder.Services.AddSingleton<SmsChannel>();
builder.Services.AddSingleton<IOtpChannel>(sp => sp.GetRequiredService<SmsChannel>());

builder.Services.AddScoped<OtpService>();
builder.Services.AddHostedService<CleanupJob>();

builder.Services.AddAuthentication("ApiKey")
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("ApiKey", null);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiKey", p => p.RequireAuthenticatedUser());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("otp", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOtpEndpoints();

app.Run();

static string FindSolutionRoot(string start)
{
    var dir = start;
    while (dir != null)
    {
        if (Directory.GetFiles(dir, "*.sln*").Length > 0) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return start;
}
