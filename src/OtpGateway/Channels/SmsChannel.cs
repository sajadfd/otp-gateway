using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OtpGateway.Channels;

public sealed class SmsChannel : IOtpChannel
{
    public string Name => "sms";
    public int Priority => 4;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SmsChannel> _logger;

    public SmsChannel(IHttpClientFactory httpFactory, IConfiguration config, ILogger<SmsChannel> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public Task<bool> IsAvailableForAsync(string phone, string? email, CancellationToken ct = default)
    {
        var enabled = _config.GetValue("Sms:Enabled", false);
        return Task.FromResult(enabled && !string.IsNullOrWhiteSpace(phone));
    }

    public async Task<ChannelResult> SendAsync(OtpRequest request, CancellationToken ct = default)
    {
        var provider = _config["Sms:Provider"]?.ToLower() ?? "twilio";

        return provider switch
        {
            "twilio" => await SendViaTwilio(request, ct),
            "webhook" => await SendViaWebhook(request, ct),
            _ => new ChannelResult(false, Name, $"Unknown SMS provider: {provider}")
        };
    }

    private async Task<ChannelResult> SendViaTwilio(OtpRequest request, CancellationToken ct)
    {
        var accountSid = _config["Sms:Twilio:AccountSid"] ?? "";
        var authToken = _config["Sms:Twilio:AuthToken"] ?? "";
        var fromNumber = _config["Sms:Twilio:FromNumber"] ?? "";

        if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken))
            return new ChannelResult(false, Name, "Twilio credentials not configured");

        try
        {
            var client = _httpFactory.CreateClient();
            var authBytes = Encoding.ASCII.GetBytes($"{accountSid}:{authToken}");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = request.Phone,
                ["From"] = fromNumber,
                ["Body"] = (_config["Sms:MessageTemplate"] ?? "Your verification code: {code}\n\nDo not share this code.")
                    .Replace("{code}", request.Code).Replace("{phone}", request.Phone).Replace("{ref}", request.Ref ?? "")
            });

            var resp = await client.PostAsync(
                $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json",
                content, ct);

            var body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("OTP sent via SMS (Twilio) to {Phone}", request.Phone);
                return new ChannelResult(true, Name);
            }

            _logger.LogWarning("Twilio SMS failed: {Status} {Body}", resp.StatusCode, body);
            return new ChannelResult(false, Name, $"Twilio error: {resp.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS send failed to {Phone}", request.Phone);
            return new ChannelResult(false, Name, ex.Message);
        }
    }

    private async Task<ChannelResult> SendViaWebhook(OtpRequest request, CancellationToken ct)
    {
        var url = _config["Sms:Webhook:Url"] ?? "";
        var headerName = _config["Sms:Webhook:AuthHeader"] ?? "X-Api-Key";
        var headerValue = _config["Sms:Webhook:AuthValue"] ?? "";

        if (string.IsNullOrEmpty(url))
            return new ChannelResult(false, Name, "SMS webhook URL not configured");

        try
        {
            var client = _httpFactory.CreateClient();
            if (!string.IsNullOrEmpty(headerValue))
                client.DefaultRequestHeaders.Add(headerName, headerValue);

            var payload = JsonSerializer.Serialize(new
            {
                phone = request.Phone,
                code = request.Code,
                message = (_config["Sms:MessageTemplate"] ?? "Your verification code: {code}")
                    .Replace("{code}", request.Code).Replace("{phone}", request.Phone).Replace("{ref}", request.Ref ?? "")
            });

            var resp = await client.PostAsync(url,
                new StringContent(payload, Encoding.UTF8, "application/json"), ct);

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("OTP sent via SMS webhook to {Phone}", request.Phone);
                return new ChannelResult(true, Name);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("SMS webhook failed: {Status} {Body}", resp.StatusCode, body);
            return new ChannelResult(false, Name, $"Webhook error: {resp.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS webhook failed to {Phone}", request.Phone);
            return new ChannelResult(false, Name, ex.Message);
        }
    }
}
