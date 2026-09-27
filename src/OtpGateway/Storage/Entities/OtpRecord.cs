using System.Security.Cryptography;

namespace OtpGateway.Storage.Entities;

public class OtpRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string Code { get; set; } = "";
    public string? ChannelUsed { get; set; }
    public string? Ref { get; set; }
    public bool Verified { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public byte Attempts { get; set; }

    public static string GenerateCode(int length = 4)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(length, bytes.ToArray(), (span, b) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = (char)('0' + b[i] % 10);
        });
    }
}
