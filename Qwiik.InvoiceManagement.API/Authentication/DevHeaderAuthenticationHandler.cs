using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Qwiik.InvoiceManagement.API.Authentication;

/// <summary>
/// Development-only authentication handler that reads tenant identity from request headers
/// and issues standard claims so the application remains decoupled from the auth mechanism.
/// </summary>
public sealed class DevHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevHeader";
    public const string OrganizationHeaderName = "X-Organization-Id";
    public const string UserIdHeaderName = "X-User-Id";
    public const string RoleHeaderName = "X-User-Role";

    public DevHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(OrganizationHeaderName, out var orgHeaderValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var rawOrgId = orgHeaderValues.FirstOrDefault()?.Trim();
        if (!Guid.TryParse(rawOrgId, out var organizationId) || organizationId == Guid.Empty)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Header '{OrganizationHeaderName}' must be a valid non-empty GUID."));
        }

        var userId = Request.Headers.TryGetValue(UserIdHeaderName, out var userHeaderValues) &&
                     !string.IsNullOrWhiteSpace(userHeaderValues.FirstOrDefault())
            ? userHeaderValues.FirstOrDefault()!.Trim()
            : "dev-user";

        var role = Request.Headers.TryGetValue(RoleHeaderName, out var roleHeaderValues) &&
                   !string.IsNullOrWhiteSpace(roleHeaderValues.FirstOrDefault())
            ? roleHeaderValues.FirstOrDefault()!.Trim()
            : "Member";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(TenantClaimTypes.OrganizationId, organizationId.ToString()),
            new Claim(ClaimTypes.Role, role)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
