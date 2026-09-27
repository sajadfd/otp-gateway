using Microsoft.EntityFrameworkCore;
using OtpGateway.Channels;
using OtpGateway.Data;
using OtpGateway.Data.Entities;

namespace OtpGateway.Services;

public class OtpService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<IOtpChannel> _channels;
    private readonly ILogger<OtpService> _logger;
    private readonly IConfiguration _config;

    public OtpService(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IOtpChannel> channels,
        ILogger<OtpService> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _channels = channels.OrderBy(c => c.Priority);
        _logger = logger;
        _config = config;
    }

    public async Task<SendResult> SendAsync(string phone, string? email, string? preferredChannel,
        string? reference, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cooldownSec = _config.GetValue("Otp:CooldownSeconds", 60);
        var dailyLimit = _config.GetValue("Otp:DailyLimit", 10);
        var expiryMin = _config.GetValue("Otp:ExpiryMinutes", 5);
        var codeLength = _config.GetValue("Otp:CodeLength", 4);

        var cutoff = DateTime.UtcNow.AddSeconds(-cooldownSec);
        var recentOtp = await db.Otps
            .Where(o => o.Phone == phone && o.CreatedAt > cutoff && !o.Verified)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (recentOtp is not null)
        {
            var wait = cooldownSec - (int)(DateTime.UtcNow - recentOtp.CreatedAt).TotalSeconds;
            return new SendResult(false, null, null, recentOtp.Id,
                $"Cooldown active. Wait {wait} seconds.");
        }

        var since = DateTime.UtcNow.AddDays(-1);
        var sentLast24h = await db.Otps.CountAsync(o => o.Phone == phone && o.CreatedAt > since, ct);
        if (sentLast24h >= dailyLimit)
            return new SendResult(false, null, null, null, "Daily OTP limit reached.");

        var code = OtpRecord.GenerateCode(codeLength);
        var record = new OtpRecord
        {
            Phone = phone,
            Email = email,
            Code = code,
            Ref = reference,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMin)
        };

        var request = new OtpRequest(phone, email, code, reference);
        var errors = new List<string>();

        var orderedChannels = preferredChannel is not null
            ? _channels.OrderByDescending(c =>
                c.Name.Equals(preferredChannel, StringComparison.OrdinalIgnoreCase))
                .ThenBy(c => c.Priority)
            : _channels;

        foreach (var channel in orderedChannels)
        {
            if (!await channel.IsAvailableForAsync(phone, email, ct))
            {
                _logger.LogDebug("Channel {Channel} not available for {Phone}", channel.Name, phone);
                continue;
            }

            var result = await channel.SendAsync(request, ct);
            if (result.Success)
            {
                record.ChannelUsed = result.Channel;
                db.Otps.Add(record);
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("OTP {Id} sent via {Channel} to {Phone}",
                    record.Id, result.Channel, phone);
                return new SendResult(true, record.Id, result.Channel, record.Id, null);
            }

            errors.Add($"{channel.Name}: {result.Error}");
            _logger.LogWarning("Channel {Channel} failed for {Phone}: {Error}",
                channel.Name, phone, result.Error);
        }

        return new SendResult(false, null, null, null,
            $"All channels failed. {string.Join("; ", errors)}");
    }

    public async Task<VerifyResult> VerifyAsync(Guid otpId, string code, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var maxAttempts = _config.GetValue("Otp:MaxAttempts", 5);

        var otp = await db.Otps.FindAsync([otpId], ct);
        if (otp is null)
            return new VerifyResult(false, "OTP not found.");

        if (otp.Verified)
            return new VerifyResult(false, "OTP already used.");

        if (otp.ExpiresAt < DateTime.UtcNow)
            return new VerifyResult(false, "OTP expired.");

        if (otp.Attempts >= maxAttempts)
            return new VerifyResult(false, "Too many attempts.");

        if (otp.Code != code)
        {
            otp.Attempts++;
            await db.SaveChangesAsync(ct);
            return new VerifyResult(false, $"Invalid code. {maxAttempts - otp.Attempts} attempts remaining.");
        }

        otp.Verified = true;
        otp.VerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new VerifyResult(true, null, otp.Phone, otp.ChannelUsed);
    }
}

public record SendResult(bool Success, Guid? OtpId, string? Channel, Guid? ExistingId, string? Error);
public record VerifyResult(bool Success, string? Error, string? Phone = null, string? Channel = null);
