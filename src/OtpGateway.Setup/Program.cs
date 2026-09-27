using Microsoft.EntityFrameworkCore;
using OtpGateway.Data;
using OtpGateway.Data.Entities;
using TL;
using WTelegram;

var dbPath = args.FirstOrDefault(a => a.StartsWith("--db="))?[5..] ?? "data/otp.db";
var sessionDir = args.FirstOrDefault(a => a.StartsWith("--sessions="))?[11..] ?? "sessions";

Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? "data");
Directory.CreateDirectory(sessionDir);

var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
optionsBuilder.UseSqlite($"Data Source={dbPath}");
using var db = new AppDbContext(optionsBuilder.Options);
await db.Database.EnsureCreatedAsync();

var command = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "help";

switch (command)
{
    case "login":
        await LoginTelegram();
        break;
    case "add-whatsapp":
        await AddWhatsApp();
        break;
    case "list":
        await ListChannels();
        break;
    case "test":
        await TestSend();
        break;
    default:
        PrintHelp();
        break;
}

void PrintHelp()
{
    Console.WriteLine("""
    OTP Gateway Setup Tool

    Commands:
      login           Log in a Telegram account (interactive)
      add-whatsapp    Add a WhatsApp Business number
      list            List all configured channels
      test            Test sending an OTP to a phone number

    Options:
      --db=PATH       Database path (default: data/otp.db)
      --sessions=DIR  Telegram session directory (default: sessions)

    Examples:
      dotnet run -- login
      dotnet run -- add-whatsapp
      dotnet run -- list
      dotnet run -- test
    """);
}

async Task LoginTelegram()
{
    Console.WriteLine("=== Telegram Account Login ===\n");
    Console.WriteLine("You need api_id and api_hash from https://my.telegram.org/apps\n");

    Console.Write("API ID: ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out var apiId))
    {
        Console.WriteLine("Invalid API ID");
        return;
    }

    Console.Write("API Hash: ");
    var apiHash = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(apiHash))
    {
        Console.WriteLine("Invalid API Hash");
        return;
    }

    Console.Write("Phone number (with country code, e.g. +9647701234567): ");
    var phone = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(phone))
    {
        Console.WriteLine("Invalid phone number");
        return;
    }

    var sessionPath = Path.Combine(sessionDir, $"tg_{phone.Replace("+", "")}.session");

    Console.WriteLine($"\nSession will be saved to: {sessionPath}");
    Console.WriteLine("Starting login...\n");

    string? verificationCode = null;
    string? twoFactorPassword = null;

    using var client = new WTelegram.Client(what => what switch
    {
        "api_id" => apiId.ToString(),
        "api_hash" => apiHash,
        "phone_number" => phone,
        "session_pathname" => sessionPath,
        "verification_code" => GetVerificationCode(),
        "password" => GetTwoFactorPassword(),
        _ => null
    });

    string GetVerificationCode()
    {
        if (verificationCode != null) return verificationCode;
        Console.Write("\nEnter the verification code sent to your Telegram: ");
        verificationCode = Console.ReadLine()?.Trim() ?? "";
        return verificationCode;
    }

    string GetTwoFactorPassword()
    {
        if (twoFactorPassword != null) return twoFactorPassword;
        Console.Write("\nEnter your 2FA password: ");
        twoFactorPassword = Console.ReadLine()?.Trim() ?? "";
        return twoFactorPassword;
    }

    try
    {
        var user = await client.LoginUserIfNeeded();
        Console.WriteLine($"\n✓ Logged in as: {user.first_name} {user.last_name} (@{user.MainUsername})");

        var existing = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.Phone == phone);
        if (existing != null)
        {
            existing.ApiId = apiId;
            existing.ApiHash = apiHash;
            existing.SessionPath = sessionPath;
            existing.Enabled = true;
            existing.ConsecutiveFailures = 0;
            Console.WriteLine("✓ Updated existing account in database");
        }
        else
        {
            db.TelegramAccounts.Add(new TelegramAccount
            {
                Phone = phone,
                ApiId = apiId,
                ApiHash = apiHash,
                SessionPath = sessionPath,
                Enabled = true
            });
            Console.WriteLine("✓ Added new account to database");
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"✓ Session saved to {sessionPath}");
        Console.WriteLine("\nThis account is now ready. Start the server to use it.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n✗ Login failed: {ex.Message}");
    }
}

async Task AddWhatsApp()
{
    Console.WriteLine("=== Add WhatsApp Business Number ===\n");
    Console.WriteLine("You need these from Meta Business Suite → WhatsApp → API Setup\n");

    Console.Write("Phone Number ID: ");
    var phoneNumberId = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(phoneNumberId)) { Console.WriteLine("Required"); return; }

    Console.Write("Display phone (e.g. +964 770 123 4567): ");
    var displayPhone = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(displayPhone)) { Console.WriteLine("Required"); return; }

    Console.Write("WABA ID: ");
    var wabaId = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(wabaId)) { Console.WriteLine("Required"); return; }

    Console.Write("Permanent Access Token: ");
    var accessToken = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(accessToken)) { Console.WriteLine("Required"); return; }

    var existing = await db.WhatsAppNumbers.FirstOrDefaultAsync(w => w.PhoneNumberId == phoneNumberId);
    if (existing != null)
    {
        existing.DisplayPhone = displayPhone;
        existing.WabaId = wabaId;
        existing.AccessToken = accessToken;
        existing.Enabled = true;
        existing.ConsecutiveFailures = 0;
        Console.WriteLine("✓ Updated existing number");
    }
    else
    {
        db.WhatsAppNumbers.Add(new WhatsAppNumber
        {
            PhoneNumberId = phoneNumberId,
            DisplayPhone = displayPhone,
            WabaId = wabaId,
            AccessToken = accessToken,
            Enabled = true
        });
        Console.WriteLine("✓ Added new WhatsApp number");
    }

    await db.SaveChangesAsync();
    Console.WriteLine("Done. Restart the server to pick up the change.");
}

async Task ListChannels()
{
    Console.WriteLine("=== Configured Channels ===\n");

    var tgAccounts = await db.TelegramAccounts.ToListAsync();
    Console.WriteLine($"Telegram Accounts ({tgAccounts.Count}):");
    if (tgAccounts.Count == 0) Console.WriteLine("  (none)");
    foreach (var a in tgAccounts)
    {
        var status = a.Enabled ? "✓" : "✗";
        var session = File.Exists(a.SessionPath) ? "session OK" : "NO SESSION";
        Console.WriteLine($"  {status} [{a.Id}] {a.Phone} — sent today: {a.SentToday}, " +
                          $"failures: {a.ConsecutiveFailures}, {session}");
    }

    Console.WriteLine();

    var waNumbers = await db.WhatsAppNumbers.ToListAsync();
    Console.WriteLine($"WhatsApp Numbers ({waNumbers.Count}):");
    if (waNumbers.Count == 0) Console.WriteLine("  (none)");
    foreach (var w in waNumbers)
    {
        var status = w.Enabled ? "✓" : "✗";
        Console.WriteLine($"  {status} [{w.Id}] {w.DisplayPhone} — sent today: {w.SentToday}, " +
                          $"failures: {w.ConsecutiveFailures}");
    }
}

async Task TestSend()
{
    Console.WriteLine("=== Test OTP Send ===\n");

    Console.Write("Target phone number (e.g. +9647701234567): ");
    var phone = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(phone)) { Console.WriteLine("Required"); return; }

    Console.Write("Channel to test (telegram/whatsapp/email): ");
    var channel = Console.ReadLine()?.Trim()?.ToLower();

    var code = OtpRecord.GenerateCode();
    Console.WriteLine($"\nGenerated code: {code}");

    if (channel == "telegram")
    {
        var accounts = await db.TelegramAccounts.Where(a => a.Enabled).ToListAsync();
        if (accounts.Count == 0) { Console.WriteLine("No Telegram accounts configured"); return; }

        var account = accounts[0];
        Console.WriteLine($"Using account: {account.Phone}");

        try
        {
            using var client = new WTelegram.Client(what => what switch
            {
                "api_id" => account.ApiId.ToString(),
                "api_hash" => account.ApiHash,
                "phone_number" => account.Phone,
                "session_pathname" => account.SessionPath,
                _ => null
            });

            var user = await client.LoginUserIfNeeded();
            Console.WriteLine($"Logged in as {user.first_name}");

            var normalizedPhone = new string(phone.Where(char.IsDigit).ToArray());
            var contacts = await client.Contacts_ImportContacts([
                new TL.InputPhoneContact { phone = normalizedPhone, first_name = "Test", last_name = "OTP" }
            ]);

            if (contacts.users.Count == 0)
            {
                Console.WriteLine("✗ Phone not found on Telegram");
                return;
            }

            var target = contacts.users.Values.First();
            await client.SendMessageAsync(
                new TL.InputPeerUser(target.id, target.access_hash),
                $"🔐 Test verification code: *{code}*\n\nThis is a test from OTP Gateway.");

            await client.Contacts_DeleteContacts([new TL.InputUser(target.id, target.access_hash)]);
            Console.WriteLine($"✓ Message sent to {phone} via Telegram");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("Only 'telegram' test is supported in the CLI. " +
                          "Use the API /otp/send endpoint to test WhatsApp and Email.");
    }
}
