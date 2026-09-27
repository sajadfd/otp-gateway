using MailKit.Net.Smtp;
using MimeKit;

namespace OtpGateway.Channels;

public sealed class EmailChannel : IOtpChannel
{
    public string Name => "email";
    public int Priority => 3;

    private readonly IConfiguration _config;
    private readonly ILogger<EmailChannel> _logger;

    public EmailChannel(IConfiguration config, ILogger<EmailChannel> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<bool> IsAvailableForAsync(string phone, string? email, CancellationToken ct = default)
    {
        var enabled = _config.GetValue("Email:Enabled", false);
        return Task.FromResult(enabled && !string.IsNullOrWhiteSpace(email));
    }

    public async Task<ChannelResult> SendAsync(OtpRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return new ChannelResult(false, Name, "No email provided");

        try
        {
            var host = _config["Email:SmtpHost"] ?? "smtp.gmail.com";
            var port = _config.GetValue("Email:SmtpPort", 587);
            var user = _config["Email:Username"] ?? "";
            var pass = _config["Email:Password"] ?? "";
            var fromName = _config["Email:FromName"] ?? "OTP Gateway";
            var subject = _config["Email:Subject"] ?? "Your Verification Code";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, user));
            message.To.Add(MailboxAddress.Parse(request.Email));
            message.Subject = subject;
            message.Body = new TextPart("html")
            {
                Text = $"""
                <div style="font-family:sans-serif;max-width:400px;margin:0 auto;padding:20px">
                    <h2 style="color:#333">{fromName}</h2>
                    <p>Your verification code is:</p>
                    <div style="font-size:32px;font-weight:bold;letter-spacing:8px;
                                background:#f5f5f5;padding:16px;text-align:center;
                                border-radius:8px;margin:16px 0">{request.Code}</div>
                    <p style="color:#666;font-size:13px">This code expires in 5 minutes. Do not share it.</p>
                </div>
                """
            };

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.StartTls, ct);
            await smtp.AuthenticateAsync(user, pass, ct);
            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(true, ct);

            _logger.LogInformation("OTP sent via Email to {Email}", request.Email);
            return new ChannelResult(true, Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed to {Email}", request.Email);
            return new ChannelResult(false, Name, ex.Message);
        }
    }
}
