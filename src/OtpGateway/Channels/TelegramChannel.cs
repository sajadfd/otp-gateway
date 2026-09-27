using Microsoft.EntityFrameworkCore;
using OtpGateway.Storage;
using OtpGateway.Storage.Entities;
using TL;
using WTelegram;

namespace OtpGateway.Channels;

public sealed class TelegramChannel : IOtpChannel, IAsyncDisposable
{
    public string Name => "telegram";
    public int Priority => 1;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramChannel> _logger;
    private readonly List<ManagedClient> _clients = [];
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private int _roundRobin;

    public TelegramChannel(IServiceScopeFactory scopeFactory, ILogger<TelegramChannel> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accounts = await db.TelegramAccounts.Where(a => a.Enabled).ToListAsync(ct);

            foreach (var account in accounts)
            {
                try
                {
                    var client = new WTelegram.Client(what => what switch
                    {
                        "api_id" => account.ApiId.ToString(),
                        "api_hash" => account.ApiHash,
                        "phone_number" => account.Phone,
                        "session_pathname" => account.SessionPath,
                        _ => null
                    });

                    var user = await client.LoginUserIfNeeded();
                    _clients.Add(new ManagedClient(client, account.Id, account.Phone));
                    _logger.LogInformation("Telegram account {Phone} logged in as {Name}",
                        account.Phone, user.first_name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Telegram account {Phone}. " +
                        "Run the setup tool first: dotnet run --project src/OtpGateway.Setup -- login", account.Phone);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<bool> IsAvailableForAsync(string phone, string? email, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        return _clients.Count > 0;
    }

    public async Task<ChannelResult> SendAsync(OtpRequest request, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        if (_clients.Count == 0)
            return new ChannelResult(false, Name, "No Telegram accounts available");

        var normalizedPhone = NormalizePhone(request.Phone);
        var tried = 0;

        while (tried < _clients.Count)
        {
            var idx = Interlocked.Increment(ref _roundRobin) % _clients.Count;
            var managed = _clients[idx];
            tried++;

            try
            {
                var contacts = await managed.Client.Contacts_ImportContacts([
                    new InputPhoneContact { phone = normalizedPhone, first_name = "OTP", last_name = "Verify" }
                ]);

                if (contacts.users.Count == 0)
                    return new ChannelResult(false, Name, "Phone not on Telegram");

                var targetUser = contacts.users.Values.First();
                var peer = new InputPeerUser(targetUser.id, targetUser.access_hash);

                await managed.Client.SendMessageAsync(peer,
                    $"🔐 Your verification code: *{request.Code}*\n\nDo not share this code with anyone.\nValid for 5 minutes.");

                // Clean up the imported contact
                await managed.Client.Contacts_DeleteContacts([new InputUser(targetUser.id, targetUser.access_hash)]);

                await UpdateSentCount(managed.AccountId);
                _logger.LogInformation("OTP sent via Telegram account {Sender} to {Phone}",
                    managed.Phone, request.Phone);
                return new ChannelResult(true, Name);
            }
            catch (RpcException ex) when (ex.Code is 400 or 401)
            {
                _logger.LogWarning("Telegram error on account {Phone}: {Msg}", managed.Phone, ex.Message);
                await RecordFailure(managed.AccountId);

                if (ex.Message.Contains("PHONE") || ex.Message.Contains("CONTACT"))
                    return new ChannelResult(false, Name, "Phone not on Telegram");

                continue;
            }
            catch (RpcException ex) when (ex.Code == 420)
            {
                _logger.LogWarning("Telegram flood wait on account {Phone}: {Msg}",
                    managed.Phone, ex.Message);
                await RecordFailure(managed.AccountId);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram send failed on account {Phone}", managed.Phone);
                await RecordFailure(managed.AccountId);
                continue;
            }
        }

        return new ChannelResult(false, Name, "All Telegram accounts exhausted");
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.StartsWith('0') ? digits[1..] : digits;
    }

    private async Task UpdateSentCount(int accountId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.TelegramAccounts.Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.SentToday, a => a.SentToday + 1)
                .SetProperty(a => a.ConsecutiveFailures, 0));
    }

    private async Task RecordFailure(int accountId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.TelegramAccounts.Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.ConsecutiveFailures, a => a.ConsecutiveFailures + 1)
                .SetProperty(a => a.LastFailedAt, DateTime.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients)
            c.Client.Dispose();
        _clients.Clear();
    }

    private record ManagedClient(Client Client, int AccountId, string Phone);
}
