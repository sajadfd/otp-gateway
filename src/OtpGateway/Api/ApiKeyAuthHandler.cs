using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OtpGateway.Api;

public class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration config)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedKey = config["ApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
            return Task.FromResult(AuthenticateResult.Fail("ApiKey not configured"));

        if (!Request.Headers.TryGetValue("X-Api-Key", out var providedKey))
            return Task.FromResult(AuthenticateResult.Fail("Missing X-Api-Key header"));

        if (!string.Equals(expectedKey, providedKey.ToString(), StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var identity = new ClaimsIdentity("ApiKey");
        identity.AddClaim(new Claim(ClaimTypes.Name, "api-client"));
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "ApiKey");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
