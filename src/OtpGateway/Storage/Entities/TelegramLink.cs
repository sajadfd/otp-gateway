namespace OtpGateway.Storage.Entities;

public class TelegramLink
{
    public long Id { get; set; }
    public string Phone { get; set; } = "";
    public long TelegramUserId { get; set; }
    public long ChatId { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
