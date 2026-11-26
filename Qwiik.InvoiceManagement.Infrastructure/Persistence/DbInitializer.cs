using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qwiik.InvoiceManagement.Domain.Entities;

namespace Qwiik.InvoiceManagement.Infrastructure.Persistence;

/// <summary>
/// Seeds baseline organizations for local development and evaluation.
/// </summary>
public static class DbInitializer
{
    public static readonly Guid AcmeOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid GlobexOrganizationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task SeedOrganizationsAsync(
        AppDbContext dbContext,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var seedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var defaultOrganizations = new[]
        {
            new Organization(AcmeOrganizationId, "Acme Corporation", seedTime),
            new Organization(GlobexOrganizationId, "Globex Industries", seedTime)
        };

        var existingIds = await dbContext.Organizations
            .AsNoTracking()
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var addedCount = 0;
        foreach (var organization in defaultOrganizations)
        {
            if (!existingIds.Contains(organization.Id))
            {
                dbContext.Organizations.Add(organization);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger?.LogInformation("Seeded {Count} sample organizations for development.", addedCount);
        }
    }
}
