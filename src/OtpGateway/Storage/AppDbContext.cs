using Microsoft.EntityFrameworkCore;
using OtpGateway.Storage.Entities;

namespace OtpGateway.Storage;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<OtpRecord> Otps => Set<OtpRecord>();
    public DbSet<TelegramLink> TelegramLinks => Set<TelegramLink>();
    public DbSet<TelegramAccount> TelegramAccounts => Set<TelegramAccount>();
    public DbSet<WhatsAppNumber> WhatsAppNumbers => Set<WhatsAppNumber>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<OtpRecord>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasIndex(o => o.Phone);
            e.HasIndex(o => o.CreatedAt);
        });

        m.Entity<TelegramLink>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Phone).IsUnique();
            e.HasIndex(t => t.TelegramUserId).IsUnique();
        });

        m.Entity<TelegramAccount>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Phone).IsUnique();
        });

        m.Entity<WhatsAppNumber>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => w.PhoneNumberId).IsUnique();
        });
    }
}
