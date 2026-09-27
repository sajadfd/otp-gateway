using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OtpGateway.Data;
using OtpGateway.Data.Entities;

namespace OtpGateway.Channels;

public sealed class WhatsAppChannel : IOtpChannel
{
    public string Name => "whatsapp";
    public int Priority => 2;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WhatsAppChannel> _logger;
    private readonly IConfiguration _config;
    private int _roundRobin;

    private const string GraphApi = "https://graph.facebook.com/v21.0";

    public WhatsAppChannel(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        ILogger<WhatsAppChannel> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
        _config = config;
    }

    public async Task<bool> IsAvailableForAsync(string phone, string? email, CancellationToken ct = default)
    {
        var number = await PickNumberAsync(ct);
        return number is not null;
    }

    public async Task<ChannelResult> SendAsync(OtpRequest request, CancellationToken ct = default)
    {
        var templateName = _config["WhatsApp:TemplateName"] ?? "authentication_otp";
        var templateLang = _config["WhatsApp:TemplateLanguage"] ?? "en";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var number = await PickNumberAsync(ct);
            if (number is null)
                return new ChannelResult(false, Name, "No WhatsApp numbers available");

            try
            {
                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = request.Phone.TrimStart('+'),
                    type = "template",
                    template = new
                    {
                        name = templateName,
                        language = new { code = templateLang },
                        components = new object[]
                        {
                            new
                            {
                                type = "body",
                                parameters = new object[]
                                {
                                    new { type = "text", text = request.Code }
                                }
                            },
                            new
                            {
                                type = "button",
                                sub_type = "url",
                                index = 0,
                                parameters = new object[]
                                {
                                    new { type = "text", text = request.Code }
                                }
                            }
                        }
                    }
                };

                var client = _httpFactory.CreateClient();
                var json = JsonSerializer.Serialize(payload);
                var req = new HttpRequestMessage(HttpMethod.Post,
                    $"{GraphApi}/{number.PhoneNumberId}/messages")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", number.AccessToken);

                var resp = await client.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if (resp.IsSuccessStatusCode)
                {
                    await UpdateSentCount(number.Id);
                    _logger.LogInformation("OTP sent via WhatsApp number {Phone} to {To}",
                        number.DisplayPhone, request.Phone);
                    return new ChannelResult(true, Name);
                }

                _logger.LogWarning("WhatsApp API error on {Phone}: {Status} {Body}",
                    number.DisplayPhone, resp.StatusCode, body);
                await RecordFailure(number.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp send failed on number {Phone}", number.DisplayPhone);
                await RecordFailure(number.Id);
            }
        }

        return new ChannelResult(false, Name, "All WhatsApp numbers failed");
    }

    private async Task<WhatsAppNumber?> PickNumberAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var today = DateTime.UtcNow.Date;
        await db.WhatsAppNumbers
            .Where(w => w.LastResetUtc < today)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.SentToday, 0)
                .SetProperty(w => w.LastResetUtc, today), ct);

        var numbers = await db.WhatsAppNumbers
            .Where(w => w.Enabled && w.ConsecutiveFailures < 5)
            .OrderBy(w => w.SentToday)
            .ToListAsync(ct);

        if (numbers.Count == 0) return null;

        var idx = Interlocked.Increment(ref _roundRobin) % numbers.Count;
        return numbers[idx];
    }

    private async Task UpdateSentCount(int numberId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.WhatsAppNumbers.Where(w => w.Id == numberId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.SentToday, w => w.SentToday + 1)
                .SetProperty(w => w.ConsecutiveFailures, 0));
    }

    private async Task RecordFailure(int numberId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.WhatsAppNumbers.Where(w => w.Id == numberId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.ConsecutiveFailures, w => w.ConsecutiveFailures + 1)
                .SetProperty(w => w.LastFailedAt, DateTime.UtcNow));
    }
}
