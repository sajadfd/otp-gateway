namespace OtpGateway.Storage.Entities;

public class WhatsAppNumber
{
    public int Id { get; set; }
    public string PhoneNumberId { get; set; } = "";
    public string DisplayPhone { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string WabaId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int SentToday { get; set; }
    public DateTime LastResetUtc { get; set; } = DateTime.UtcNow.Date;
    public DateTime? LastFailedAt { get; set; }
    public int ConsecutiveFailures { get; set; }
}
