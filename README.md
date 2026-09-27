# OTP Gateway

A standalone, multi-channel OTP (One-Time Password) microservice that sends verification codes through the cheapest available channel first, falling back automatically to more expensive ones.

**Save money.** Instead of paying $0.05+ per SMS, OTP Gateway sends via Telegram (free) → WhatsApp ($0.008) → Email (free) → SMS (last resort).

---

**خدمة مستقلة لإرسال رموز التحقق (OTP) عبر عدة قنوات، تبدأ بالأرخص تلقائياً.**

**وفّر فلوسك.** بدال ما تدفع $0.05+ لكل SMS، الخدمة تبعث عبر تلكرام (مجاني) ← واتساب ($0.008) ← إيميل (مجاني) ← SMS (آخر خيار).

---

## How It Works / كيف تعمل

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

### Channel Cascade / ترتيب القنوات

| # | Channel / القناة | Cost / التكلفة | How / الطريقة |
|---|---------|------|-----|
| 1 | **Telegram** | Free / مجاني | Userbot sends message to recipient's Telegram |
| 2 | **WhatsApp** | ~$0.008/msg | Meta Cloud API direct, no middleman |
| 3 | **Email** | Free / مجاني | SMTP (only if email is provided) |
| 4 | **SMS** | $0.05+/msg | Twilio or custom webhook (last resort) |

Each channel is tried in order. First success wins. If a channel fails, it falls through to the next.

كل قناة تُجرب بالترتيب. أول نجاح يفوز. إذا فشلت قناة، ينتقل للتالية تلقائياً.

## Quick Start / البداية السريعة

### Prerequisites / المتطلبات

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Telegram `api_id` & `api_hash` from [my.telegram.org](https://my.telegram.org/apps)
- (Optional) Meta Business account for WhatsApp
- (Optional) SMTP credentials for email
- (Optional) Twilio account for SMS fallback

### 1. Clone & Build / استنساخ وبناء

```bash
git clone https://github.com/sajadfd/otp-gateway.git
cd otp-gateway
dotnet build
```

### 2. Set Up Telegram / إعداد التلكرام

```bash
dotnet run --project src/OtpGateway.Setup -- login
```

This will ask for your `api_id`, `api_hash`, phone number, then send a verification code to your Telegram.

هذا الأمر يسألك عن `api_id` و `api_hash` ورقم الهاتف، ثم يرسل رمز تحقق لتلكرامك.

> **Important / مهم:** Use a dedicated SIM card. Telegram userbot usage may violate Telegram's ToS. Having multiple accounts for rotation mitigates risk.
>
> استخدم شريحة مخصصة. استخدام userbot قد يخالف شروط تلكرام. وجود عدة حسابات للتدوير يقلل المخاطر.

### 3. Configure / الإعدادات

Edit `src/OtpGateway/appsettings.json` or use environment variables:

```bash
# Required / مطلوب
export ApiKey="your-secure-api-key-here"

# Email (optional / اختياري)
export Email__Enabled=true
export Email__SmtpHost=smtp.gmail.com
export Email__Username=noreply@example.com
export Email__Password=your-app-password

# SMS fallback (optional / اختياري)
export Sms__Enabled=true
export Sms__Twilio__AccountSid=AC...
export Sms__Twilio__AuthToken=...
export Sms__Twilio__FromNumber=+1234567890

# Webhook callback (optional / اختياري)
export Webhook__Url=https://your-backend.com/otp/callback
export Webhook__Secret=your-webhook-secret
```

### 4. Run / التشغيل

```bash
dotnet run --project src/OtpGateway
```

Or with Docker:

```bash
docker compose up -d
```

## API Reference / مرجع الـ API

All endpoints except `/health` require the `X-Api-Key` header.

جميع النقاط ما عدا `/health` تتطلب هيدر `X-Api-Key`.

### Send OTP / إرسال رمز التحقق

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

| Field | Required | Description |
|-------|----------|-------------|
| `phone` | Yes | Phone with country code |
| `email` | No | Enables email fallback |
| `preferredChannel` | No | Force a channel: `telegram`, `whatsapp`, `email`, `sms` |
| `ref` | No | Your reference ID (returned in webhooks) |

### Verify OTP / التحقق من الرمز

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

### Health Check / فحص الحالة

```http
GET /health
```

No authentication required. Returns channel status and 24h stats.

### Admin Endpoints / نقاط الإدارة

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/admin/channels` | List all configured channels |
| `POST` | `/admin/channels/whatsapp` | Add a WhatsApp number |
| `POST` | `/admin/channels/telegram` | Add a Telegram account |
| `PUT` | `/admin/channels/{type}/{id}/toggle` | Enable/disable a channel |
| `POST` | `/admin/channels/{type}/{id}/reset` | Reset failure counter |
| `GET` | `/admin/stats?days=7` | Daily OTP stats by channel |

## Message Templates / قوالب الرسائل

Customize messages per channel in `appsettings.json`:

خصّص رسالة كل قناة من `appsettings.json`:

```json
{
  "Telegram": {
    "MessageTemplate": "🔐 Your code: *{code}*\nValid for 5 minutes."
  },
  "Sms": {
    "MessageTemplate": "Your verification code: {code}"
  }
}
```

**Available variables / المتغيرات المتاحة:** `{code}`, `{phone}`, `{ref}`

> WhatsApp uses Meta-approved templates configured in Meta Business Suite.
>
> واتساب يستخدم قوالب معتمدة من Meta تُعدّ من Meta Business Suite.

## Webhook Callbacks

If `Webhook:Url` is configured, the gateway sends fire-and-forget POST requests:

| Event | When |
|-------|------|
| `otp.sent` | After successful send |
| `otp.verified` | After successful verification |

Headers: `X-Event-Type` and `X-Webhook-Secret`.

## Multi-Number Rotation / تدوير الأرقام

Both Telegram and WhatsApp support multiple numbers:

- **Round-robin** distributes load across numbers
- **Auto-disable** after 5 consecutive failures
- **Daily counter reset** at midnight UTC
- **Manual reset** via `POST /admin/channels/{type}/{id}/reset`

Add more numbers anytime via the setup CLI or the admin API.

أضف أرقام بأي وقت عبر أداة الإعداد أو الـ API.

## Rate Limiting / حدود الاستخدام

**Per phone / لكل رقم:**
- 60s cooldown between sends
- 10 OTPs per day
- 5 verification attempts per OTP

**Per IP:**
- 20 requests/minute on `/otp/*`

## Configuration Reference / مرجع الإعدادات

| Key | Default | Description |
|-----|---------|-------------|
| `ApiKey` | — | API key for authenticated endpoints |
| `Otp:CodeLength` | 4 | OTP code length |
| `Otp:ExpiryMinutes` | 5 | Code validity duration |
| `Otp:CooldownSeconds` | 60 | Min gap between sends per phone |
| `Otp:DailyLimit` | 10 | Max OTPs per phone per day |
| `Otp:MaxAttempts` | 5 | Max verification tries per OTP |
| `Telegram:MessageTemplate` | (built-in) | Telegram message template |
| `WhatsApp:TemplateName` | `authentication_otp` | Meta-approved template name |
| `WhatsApp:TemplateLanguage` | `ar` | Template language code |
| `Email:Enabled` | `false` | Enable email channel |
| `Sms:Enabled` | `false` | Enable SMS fallback |
| `Sms:MessageTemplate` | (built-in) | SMS message template |
| `Webhook:Url` | — | Callback URL for OTP events |
| `Webhook:Secret` | — | Webhook verification secret |

Override via environment variables: `Otp__DailyLimit=20`, `Email__Enabled=true`, etc.

## Integration Examples / أمثلة الربط

### C#

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
```

### Python

```python
import requests

OTP_URL = "http://localhost:5299"
HEADERS = {"X-Api-Key": "your-key"}

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
curl -X POST http://localhost:5299/otp/send \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{"phone": "+9647701234567"}'

# Verify
curl -X POST http://localhost:5299/otp/verify \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{"otpId": "...", "code": "1234"}'
```

## Project Structure / هيكل المشروع

```
otp-gateway/
├── docker-compose.yml
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
│   │   │   ├── ApiKeyAuthHandler.cs # Auth handler
│   │   │   └── Dtos/
│   │   └── Storage/
│   │       ├── AppDbContext.cs       # EF Core context
│   │       └── Entities/
│   └── OtpGateway.Setup/            # CLI setup tool
│       └── Program.cs               # Interactive login
├── data/                             # SQLite database (auto-created)
└── sessions/                         # Telegram sessions (auto-created)
```

## Deployment / النشر

### Docker

```bash
docker compose up -d
```

### Standalone

```bash
dotnet publish src/OtpGateway -c Release -o ./publish
cd publish
ApiKey=your-key dotnet OtpGateway.dll
```

### Behind Reverse Proxy / خلف بروكسي

Only your backend should reach OTP Gateway — do not expose it publicly.

فقط الباك إند مالتك يوصل للخدمة — لا تعرضها للإنترنت مباشرة.

```nginx
location /otp-gateway/ {
    proxy_pass http://127.0.0.1:5299/;
    allow 127.0.0.1;
    deny all;
}
```

## Cost Comparison / مقارنة التكلفة

For 500 OTPs/day (15,000/month), assuming 60% Telegram, 35% WhatsApp, 5% email:

لـ 500 OTP/يوم (15,000/شهر)، بافتراض 60% تلكرام، 35% واتساب، 5% إيميل:

| Approach | Monthly Cost |
|----------|-------------|
| Twilio SMS only | ~$750 |
| Twilio WhatsApp | ~$750 |
| **OTP Gateway** | **~$42** |

9,000 Telegram (free) + 5,250 WhatsApp × $0.008 ($42) + 750 Email (free) = **$42/month instead of $750.**

## License

MIT
