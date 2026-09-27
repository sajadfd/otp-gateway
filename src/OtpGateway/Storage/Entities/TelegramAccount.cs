namespace OtpGateway.Storage.Entities;

public class TelegramAccount
{
    public int Id { get; set; }
    public string Phone { get; set; } = "";
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = "";
    public string SessionPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int SentToday { get; set; }
    public DateTime LastResetUtc { get; set; } = DateTime.UtcNow.Date;
    public DateTime? LastFailedAt { get; set; }
    public int ConsecutiveFailures { get; set; }
}
