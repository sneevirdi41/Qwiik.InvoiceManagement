using Microsoft.EntityFrameworkCore;
using Qwiik.InvoiceManagement.Domain.Entities;

namespace Qwiik.InvoiceManagement.Application.Abstractions;

/// <summary>
/// Persistence abstraction exposing EF Core DbSets for application use cases.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceItem> InvoiceItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
