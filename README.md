# OTP Gateway

A standalone, multi-channel OTP (One-Time Password) microservice that sends verification codes through the cheapest available channel first, falling back automatically to more expensive ones.

**Save money.** Instead of paying $0.05+ per SMS through Twilio, OTP Gateway sends via Telegram (free) → WhatsApp ($0.008) → Email (free) → SMS (last resort).

## How It Works

```
Your Backend                OTP Gateway              Channels
    │                           │                        │
    ├── POST /otp/send ────────►│                        │
    │   {phone, email?}         │── Try Telegram ───────►│ Free
    │                           │   (userbot)            │
    │                           │   ✗ Not on Telegram    │
    │                           │                        │
    │                           │── Try WhatsApp ───────►│ ~$0.008
    │                           │   (Meta Cloud API)     │
    │                           │   ✓ Sent!              │
    │                           │                        │
    │◄── {otp_id, channel} ─────│                        │
    │                           │                        │
    ├── POST /otp/verify ──────►│                        │
    │   {otp_id, code}          │                        │
    │◄── {verified: true} ──────│                        │
```

### Channel Cascade (Priority Order)

| # | Channel | Cost | How |
|---|---------|------|-----|
| 1 | **Telegram** | Free | Userbot sends message to recipient's Telegram |
| 2 | **WhatsApp** | ~$0.008/msg (Iraq) | Meta Cloud API, no Twilio middleman |
| 3 | **Email** | Free | SMTP (only if email is provided) |
| 4 | **SMS** | $0.05+/msg | Twilio or custom webhook (last resort) |

Each channel is tried in order. First success wins. If a channel fails, it falls through to the next.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Telegram account with `api_id`/`api_hash` from [my.telegram.org](https://my.telegram.org/apps)
- (Optional) Meta Business account for WhatsApp
- (Optional) SMTP credentials for email
- (Optional) Twilio account for SMS fallback

### 1. Clone & Build

```bash
git clone <repo-url> otp-gateway
cd otp-gateway
dotnet build
```

### 2. Set Up Telegram Account

```bash
cd src/OtpGateway.Setup
dotnet run -- login
```

This will:
1. Ask for your `api_id`, `api_hash`, and phone number
2. Send a verification code to your Telegram app
3. Save the session file for the server to use
4. Register the account in the database

> **Important:** Use a dedicated SIM card, not your personal number. Telegram userbot usage violates Telegram's ToS and the account may be banned. Having multiple accounts ready for rotation mitigates this risk.

### 3. Configure

Edit `src/OtpGateway/appsettings.json` or use environment variables:

```bash
# Required
export ApiKey="your-secure-api-key-here"

# WhatsApp (optional - add via setup tool or API)
# Telegram accounts are added via the setup tool (step 2)

# Email (optional)
export Email__Enabled=true
export Email__SmtpHost=smtp.gmail.com
export Email__Username=noreply@example.com
export Email__Password=your-app-password

# SMS fallback (optional)
export Sms__Enabled=true
export Sms__Twilio__AccountSid=AC...
export Sms__Twilio__AuthToken=...
export Sms__Twilio__FromNumber=+1234567890

# Webhook callback (optional - notifies your backend)
export Webhook__Url=https://your-backend.com/otp/callback
export Webhook__Secret=your-webhook-secret
```

### 4. Run

```bash
cd src/OtpGateway
dotnet run
```

Or with Docker:

```bash
docker compose up -d
```

The server starts on port 8080 (configurable via `ASPNETCORE_URLS`).

## API Reference

All endpoints except `/health` require the `X-Api-Key` header.

### Send OTP

```http
POST /otp/send
Content-Type: application/json
X-Api-Key: your-key

{
  "phone": "+9647701234567",
  "email": "user@example.com",
  "preferredChannel": "auto",
  "ref": "registration-42"
}
```

**Response (200):**
```json
{
  "success": true,
  "otpId": "550e8400-e29b-41d4-a716-446655440000",
  "channel": "telegram",
  "error": null
}
```

**Response (429 — rate limited):**
```json
{
  "success": false,
  "otpId": null,
  "channel": null,
  "error": "Cooldown active. Wait 45 seconds."
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `phone` | Yes | Phone with country code |
| `email` | No | Enables email fallback |
| `preferredChannel` | No | Force a specific channel: `telegram`, `whatsapp`, `email`, `sms` |
| `ref` | No | Your reference ID (returned in webhooks) |

### Verify OTP

```http
POST /otp/verify
Content-Type: application/json
X-Api-Key: your-key

{
  "otpId": "550e8400-e29b-41d4-a716-446655440000",
  "code": "7291"
}
```

**Response (200):**
```json
{
  "verified": true,
  "error": null,
  "phone": "+9647701234567",
  "channel": "telegram"
}
```

### Health Check

```http
GET /health
```

No authentication required. Returns channel status and 24h stats.

### Admin: List Channels

```http
GET /admin/channels
X-Api-Key: your-key
```

Returns all Telegram accounts and WhatsApp numbers with their status.

### Admin: Add WhatsApp Number

```http
POST /admin/channels/whatsapp
Content-Type: application/json
X-Api-Key: your-key

{
  "phoneNumberId": "123456789",
  "displayPhone": "+964 770 123 4567",
  "accessToken": "EAA...",
  "wabaId": "987654321"
}
```

### Admin: Toggle Channel

```http
PUT /admin/channels/whatsapp/1/toggle
X-Api-Key: your-key
```

### Admin: Reset Failed Channel

```http
POST /admin/channels/telegram/1/reset
X-Api-Key: your-key
```

### Admin: Stats

```http
GET /admin/stats?days=7
X-Api-Key: your-key
```

Returns daily OTP counts grouped by channel.

## Webhook Callbacks

If `Webhook:Url` is configured, the gateway sends fire-and-forget POST requests on events:

**Event: `otp.sent`**
```json
{
  "otp_id": "...",
  "phone": "+9647701234567",
  "channel": "telegram",
  "ref": "registration-42",
  "timestamp": "2026-09-27T12:00:00Z"
}
```

**Event: `otp.verified`**
```json
{
  "otp_id": "...",
  "phone": "+9647701234567",
  "channel": "telegram",
  "timestamp": "2026-09-27T12:01:30Z"
}
```

Headers:
- `X-Event-Type`: `otp.sent` or `otp.verified`
- `X-Webhook-Secret`: Your configured secret (for verification)

## Multi-Number Rotation

Both Telegram and WhatsApp support multiple numbers with automatic rotation:

- **Round-robin** distributes load across numbers
- **Auto-disable** after 5 consecutive failures
- **Daily counter reset** at midnight UTC
- **Manual reset** via `POST /admin/channels/{type}/{id}/reset`

Add more numbers anytime — via the setup CLI or the admin API.

## Rate Limiting

**Per-user (phone):**
- 60-second cooldown between sends
- 10 OTPs per day per phone number
- 5 verification attempts per OTP

**Per-IP:**
- 20 requests per minute on `/otp/*` endpoints

## Configuration Reference

| Key | Default | Description |
|-----|---------|-------------|
| `ApiKey` | — | Required. API key for all authenticated endpoints |
| `Otp:CodeLength` | 4 | OTP code length |
| `Otp:ExpiryMinutes` | 5 | Code validity duration |
| `Otp:CooldownSeconds` | 60 | Minimum gap between sends for same phone |
| `Otp:DailyLimit` | 10 | Max OTPs per phone per day |
| `Otp:MaxAttempts` | 5 | Max verification tries per OTP |
| `WhatsApp:TemplateName` | `authentication_otp` | Meta-approved template name |
| `WhatsApp:TemplateLanguage` | `ar` | Template language code |
| `Email:Enabled` | `false` | Enable email channel |
| `Email:SmtpHost` | `smtp.gmail.com` | SMTP server |
| `Email:SmtpPort` | `587` | SMTP port |
| `Sms:Enabled` | `false` | Enable SMS fallback |
| `Sms:Provider` | `twilio` | `twilio` or `webhook` |
| `Webhook:Url` | — | Callback URL for OTP events |
| `Webhook:Secret` | — | Secret sent as `X-Webhook-Secret` header |

All settings can be overridden via environment variables using `__` as separator:
`Otp__DailyLimit=20`, `Email__Enabled=true`, etc.

## Integration Example

### C# (ASP.NET Core)

```csharp
public class OtpClient(HttpClient http)
{
    public async Task<(Guid otpId, string channel)?> SendOtp(string phone, string? email = null)
    {
        var resp = await http.PostAsJsonAsync("/otp/send", new { phone, email });
        var result = await resp.Content.ReadFromJsonAsync<SendResult>();
        return result?.Success == true ? (result.OtpId!.Value, result.Channel!) : null;
    }

    public async Task<bool> VerifyOtp(Guid otpId, string code)
    {
        var resp = await http.PostAsJsonAsync("/otp/verify", new { otpId, code });
        var result = await resp.Content.ReadFromJsonAsync<VerifyResult>();
        return result?.Verified == true;
    }

    record SendResult(bool Success, Guid? OtpId, string? Channel, string? Error);
    record VerifyResult(bool Verified, string? Error, string? Phone, string? Channel);
}

// Registration:
// services.AddHttpClient<OtpClient>(c => {
//     c.BaseAddress = new Uri("http://localhost:8080");
//     c.DefaultRequestHeaders.Add("X-Api-Key", "your-key");
// });
```

### Python (requests)

```python
import requests

OTP_URL = "http://localhost:8080"
API_KEY = "your-key"
HEADERS = {"X-Api-Key": API_KEY}

def send_otp(phone, email=None):
    r = requests.post(f"{OTP_URL}/otp/send",
        json={"phone": phone, "email": email}, headers=HEADERS)
    return r.json()

def verify_otp(otp_id, code):
    r = requests.post(f"{OTP_URL}/otp/verify",
        json={"otpId": otp_id, "code": code}, headers=HEADERS)
    return r.json()["verified"]
```

### cURL

```bash
# Send
curl -X POST http://localhost:8080/otp/send \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{"phone": "+9647701234567"}'

# Verify
curl -X POST http://localhost:8080/otp/verify \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{"otpId": "...", "code": "1234"}'
```

## Project Structure

```
otp-gateway/
├── docker-compose.yml
├── OtpGateway.sln
├── src/
│   ├── OtpGateway/                  # Main server
│   │   ├── Program.cs               # Entry point + DI
│   │   ├── Channels/
│   │   │   ├── IOtpChannel.cs       # Channel interface
│   │   │   ├── TelegramChannel.cs   # Userbot (free)
│   │   │   ├── WhatsAppChannel.cs   # Meta Cloud API
│   │   │   ├── EmailChannel.cs      # SMTP
│   │   │   └── SmsChannel.cs        # Twilio / webhook
│   │   ├── Services/
│   │   │   ├── OtpService.cs        # Cascade logic
│   │   │   └── CleanupJob.cs        # Expired OTP cleanup
│   │   ├── Api/
│   │   │   ├── OtpEndpoints.cs      # REST endpoints
│   │   │   ├── ApiKeyAuthHandler.cs # Auth
│   │   │   └── Dtos/
│   │   └── Data/
│   │       ├── AppDbContext.cs
│   │       └── Entities/
│   └── OtpGateway.Setup/           # CLI setup tool
│       └── Program.cs              # Interactive login
```

## Deployment

### Docker

```bash
# Build and run
docker compose up -d

# With environment variables
OTP_API_KEY=your-key docker compose up -d
```

### Standalone

```bash
cd src/OtpGateway
dotnet publish -c Release -o /opt/otp-gateway
cd /opt/otp-gateway
ApiKey=your-key dotnet OtpGateway.dll
```

### Behind a Reverse Proxy

The gateway is designed to sit behind your existing Nginx/Caddy. Only your backend should reach it — do not expose it to the public internet.

```nginx
location /otp-gateway/ {
    proxy_pass http://127.0.0.1:8080/;
    allow 127.0.0.1;
    deny all;
}
```

## Cost Comparison

For 500 OTPs/day (15,000/month), assuming 60% have Telegram, 35% have WhatsApp, 5% email-only:

| Approach | Monthly Cost |
|----------|-------------|
| Twilio SMS only | ~$750 |
| Twilio WhatsApp (current setup) | ~$750 (Twilio charges same) |
| **OTP Gateway** | **~$42** |

Breakdown: 9,000 Telegram (free) + 5,250 WhatsApp × $0.008 ($42) + 750 Email (free) = **$42/month instead of $750.**

## License

MIT
