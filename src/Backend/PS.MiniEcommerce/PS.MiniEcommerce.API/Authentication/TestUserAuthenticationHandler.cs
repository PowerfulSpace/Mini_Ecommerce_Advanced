using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PS.MiniEcommerce.Infrastructure;

namespace PS.MiniEcommerce.API.Authentication;

public class TestUserAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "X-Test-User";
    public const string HeaderName = "X-Test-User";

    private static readonly Dictionary<string, string> UserEmailsByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alice"] = "alice@example.com",
        ["bob"] = "bob@example.com"
    };

    private readonly AppDbContext _dbContext;

    public TestUserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext dbContext)
        : base(options, logger, encoder)
    {
        _dbContext = dbContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var key = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(key) || !UserEmailsByKey.TryGetValue(key, out var email))
        {
            return AuthenticateResult.Fail($"Unknown test user '{key}'.");
        }

        var userId = await _dbContext.Users
            .Where(user => user.Email == email)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync();

        if (userId is null)
        {
            return AuthenticateResult.Fail($"Seeded test user '{key}' not found.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()),
            new Claim("sub", userId.Value.ToString())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
