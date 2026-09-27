namespace OtpGateway.Channels;

public interface IOtpChannel
{
    string Name { get; }
    int Priority { get; }
    Task<ChannelResult> SendAsync(OtpRequest request, CancellationToken ct = default);
    Task<bool> IsAvailableForAsync(string phone, string? email, CancellationToken ct = default);
}

public record OtpRequest(string Phone, string? Email, string Code, string? Ref);

public record ChannelResult(bool Success, string Channel, string? Error = null);
