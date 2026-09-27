using Microsoft.EntityFrameworkCore;
using OtpGateway.Api.Dtos;
using OtpGateway.Channels;
using OtpGateway.Storage;
using OtpGateway.Services;

namespace OtpGateway.Api;

public static class OtpEndpoints
{
    public static void MapOtpEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/otp").RequireAuthorization("ApiKey").RequireRateLimiting("otp");

        group.MapPost("/send", async (SendOtpRequest req, OtpService svc,
            IConfiguration config, IHttpClientFactory httpFactory, ILogger<OtpService> logger,
            CancellationToken ct) =>
        {
            var result = await svc.SendAsync(req.Phone, req.Email, req.PreferredChannel, req.Ref, ct);

            if (result.Success)
            {
                _ = Task.Run(() => FireWebhook(config, httpFactory, logger, "otp.sent", new
                {
                    otp_id = result.OtpId,
                    phone = req.Phone,
                    channel = result.Channel,
                    @ref = req.Ref,
                    timestamp = DateTime.UtcNow
                }), CancellationToken.None);
            }

            if (!result.Success)
                return Results.Json(new SendOtpResponse(false, null, null, result.Error),
                    statusCode: result.Error?.Contains("Cooldown") == true ||
                                result.Error?.Contains("limit") == true ? 429 : 502);

            return Results.Ok(new SendOtpResponse(true, result.OtpId, result.Channel, null));
        });

        group.MapPost("/verify", async (VerifyOtpRequest req, OtpService svc,
            IConfiguration config, IHttpClientFactory httpFactory, ILogger<OtpService> logger,
            CancellationToken ct) =>
        {
            var result = await svc.VerifyAsync(req.OtpId, req.Code, ct);

            if (result.Success)
            {
                _ = Task.Run(() => FireWebhook(config, httpFactory, logger, "otp.verified", new
                {
                    otp_id = req.OtpId,
                    phone = result.Phone,
                    channel = result.Channel,
                    timestamp = DateTime.UtcNow
                }), CancellationToken.None);
            }

            var statusCode = result.Success ? 200 :
                result.Error?.Contains("expired") == true ? 410 :
                result.Error?.Contains("attempts") == true ? 429 : 400;

            return Results.Json(new VerifyOtpResponse(result.Success, result.Error,
                result.Phone, result.Channel), statusCode: statusCode);
        });

        app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
        {
            var channels = app.Services.GetServices<IOtpChannel>();
            var status = new Dictionary<string, object>();
            foreach (var ch in channels)
                status[ch.Name] = new { available = true, priority = ch.Priority };

            var now = DateTime.UtcNow;
            var otpCount24h = await db.Otps.CountAsync(o => o.CreatedAt > now.AddDays(-1), ct);
            var verified24h = await db.Otps.CountAsync(o => o.VerifiedAt > now.AddDays(-1), ct);

            var channelBreakdown = await db.Otps
                .Where(o => o.CreatedAt > now.AddDays(-1) && o.ChannelUsed != null)
                .GroupBy(o => o.ChannelUsed!)
                .Select(g => new { channel = g.Key, count = g.Count() })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                status = "ok",
                channels = status,
                stats = new
                {
                    sent_24h = otpCount24h,
                    verified_24h = verified24h,
                    by_channel = channelBreakdown
                },
                timestamp = now
            });
        });

        var admin = app.MapGroup("/admin").RequireAuthorization("ApiKey");

        admin.MapGet("/channels", async (AppDbContext db, CancellationToken ct) =>
        {
            var tgAccounts = await db.TelegramAccounts.Select(a => new
            {
                a.Id, a.Phone, a.Enabled, a.SentToday,
                a.ConsecutiveFailures, a.LastFailedAt
            }).ToListAsync(ct);

            var waNumbers = await db.WhatsAppNumbers.Select(w => new
            {
                w.Id, w.DisplayPhone, w.Enabled, w.SentToday,
                w.ConsecutiveFailures, w.LastFailedAt
            }).ToListAsync(ct);

            return Results.Ok(new { telegram = tgAccounts, whatsapp = waNumbers });
        });

        admin.MapPost("/channels/whatsapp", async (WhatsAppNumberDto dto, AppDbContext db, CancellationToken ct) =>
        {
            var number = new Storage.Entities.WhatsAppNumber
            {
                PhoneNumberId = dto.PhoneNumberId,
                DisplayPhone = dto.DisplayPhone,
                AccessToken = dto.AccessToken,
                WabaId = dto.WabaId,
                Enabled = dto.Enabled
            };
            db.WhatsAppNumbers.Add(number);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = number.Id });
        });

        admin.MapPost("/channels/telegram", async (TelegramAccountDto dto, AppDbContext db, CancellationToken ct) =>
        {
            var account = new Storage.Entities.TelegramAccount
            {
                Phone = dto.Phone,
                ApiId = dto.ApiId,
                ApiHash = dto.ApiHash,
                SessionPath = dto.SessionPath,
                Enabled = dto.Enabled
            };
            db.TelegramAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = account.Id });
        });

        admin.MapPut("/channels/{type}/{id:int}/toggle",
            async (string type, int id, AppDbContext db, CancellationToken ct) =>
        {
            int updated = type.ToLower() switch
            {
                "whatsapp" => await db.WhatsAppNumbers.Where(w => w.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Enabled, w => !w.Enabled), ct),
                "telegram" => await db.TelegramAccounts.Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Enabled, t => !t.Enabled), ct),
                _ => 0
            };
            return updated > 0 ? Results.Ok() : Results.NotFound();
        });

        admin.MapPost("/channels/{type}/{id:int}/reset",
            async (string type, int id, AppDbContext db, CancellationToken ct) =>
        {
            int updated = type.ToLower() switch
            {
                "whatsapp" => await db.WhatsAppNumbers.Where(w => w.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(w => w.ConsecutiveFailures, 0)
                        .SetProperty(w => w.Enabled, true), ct),
                "telegram" => await db.TelegramAccounts.Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.ConsecutiveFailures, 0)
                        .SetProperty(t => t.Enabled, true), ct),
                _ => 0
            };
            return updated > 0 ? Results.Ok() : Results.NotFound();
        });

        admin.MapGet("/stats", async (AppDbContext db, int? days, CancellationToken ct) =>
        {
            var since = DateTime.UtcNow.AddDays(-(days ?? 7));
            var daily = await db.Otps
                .Where(o => o.CreatedAt > since)
                .GroupBy(o => new { Date = o.CreatedAt.Date, o.ChannelUsed })
                .Select(g => new
                {
                    date = g.Key.Date,
                    channel = g.Key.ChannelUsed,
                    sent = g.Count(),
                    verified = g.Count(o => o.Verified)
                })
                .OrderByDescending(x => x.date)
                .ToListAsync(ct);

            return Results.Ok(daily);
        });
    }

    private static async Task FireWebhook(IConfiguration config, IHttpClientFactory httpFactory,
        ILogger logger, string eventType, object payload)
    {
        var url = config["Webhook:Url"];
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            var client = httpFactory.CreateClient();
            var secret = config["Webhook:Secret"];
            if (!string.IsNullOrEmpty(secret))
                client.DefaultRequestHeaders.Add("X-Webhook-Secret", secret);

            client.DefaultRequestHeaders.Add("X-Event-Type", eventType);

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var resp = await client.PostAsync(url,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("Webhook {Event} to {Url} returned {Status}",
                    eventType, url, resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook {Event} to {Url} failed", eventType, url);
        }
    }
}

public record WhatsAppNumberDto(string PhoneNumberId, string DisplayPhone,
    string AccessToken, string WabaId, bool Enabled = true);

public record TelegramAccountDto(string Phone, int ApiId, string ApiHash,
    string SessionPath, bool Enabled = true);
