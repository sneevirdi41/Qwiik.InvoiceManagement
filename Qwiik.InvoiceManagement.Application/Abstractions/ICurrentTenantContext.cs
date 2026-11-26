namespace Qwiik.InvoiceManagement.Application.Abstractions;

/// <summary>
/// Provides the authenticated tenant (organization) and user identity for the current request.
/// </summary>
public interface ICurrentTenantContext
{
    Guid OrganizationId { get; }
    string? UserId { get; }
    bool IsAuthenticated { get; }
}
