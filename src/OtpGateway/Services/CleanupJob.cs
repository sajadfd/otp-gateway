using Microsoft.EntityFrameworkCore;
using OtpGateway.Data;

namespace OtpGateway.Services;

public class CleanupJob(IServiceScopeFactory scopeFactory, ILogger<CleanupJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var cutoff = DateTime.UtcNow.AddDays(-1);
                var deleted = await db.Otps
                    .Where(o => o.ExpiresAt < cutoff)
                    .ExecuteDeleteAsync(ct);

                if (deleted > 0)
                    logger.LogInformation("Cleaned up {Count} expired OTP records", deleted);

                var today = DateTime.UtcNow.Date;
                await db.TelegramAccounts
                    .Where(a => a.LastResetUtc < today)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.SentToday, 0)
                        .SetProperty(a => a.LastResetUtc, today), ct);

                await db.WhatsAppNumbers
                    .Where(w => w.LastResetUtc < today)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(w => w.SentToday, 0)
                        .SetProperty(w => w.LastResetUtc, today), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Cleanup job failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
