using System.Security.Claims;
using Qwiik.InvoiceManagement.Application.Abstractions;

namespace Qwiik.InvoiceManagement.API.Authentication;

/// <summary>
/// Resolves tenant and user identity exclusively from trusted authenticated ClaimsPrincipal claims.
/// </summary>
public sealed class HttpContextCurrentTenantContext : ICurrentTenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated
        => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true &&
           OrganizationId != Guid.Empty;

    public Guid OrganizationId
    {
        get
        {
            var rawClaim = _httpContextAccessor.HttpContext?.User?.FindFirstValue(TenantClaimTypes.OrganizationId);
            return Guid.TryParse(rawClaim, out var organizationId) ? organizationId : Guid.Empty;
        }
    }

    public string? UserId
        => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
