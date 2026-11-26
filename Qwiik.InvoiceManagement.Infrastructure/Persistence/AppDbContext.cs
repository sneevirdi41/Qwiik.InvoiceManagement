using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Qwiik.InvoiceManagement.Application.Abstractions;
using Qwiik.InvoiceManagement.Application.Exceptions;
using Qwiik.InvoiceManagement.Domain.Entities;

namespace Qwiik.InvoiceManagement.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentTenantContext _currentTenantContext;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentTenantContext currentTenantContext)
        : base(options)
    {
        _currentTenantContext = currentTenantContext;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();

    // Evaluated per query execution by EF Core so global query filter binds the active tenant as a SQL parameter.
    private Guid CurrentOrganizationId => _currentTenantContext.OrganizationId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Invoice>()
            .HasQueryFilter(i => i.OrganizationId == CurrentOrganizationId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("A record with the same unique key already exists for this organization.");
        }
    }
}
