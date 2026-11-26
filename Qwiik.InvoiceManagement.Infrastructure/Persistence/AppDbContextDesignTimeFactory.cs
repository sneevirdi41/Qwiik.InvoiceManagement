using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qwiik.InvoiceManagement.Application.Abstractions;

namespace Qwiik.InvoiceManagement.Infrastructure.Persistence;

/// <summary>
/// Provides an AppDbContext instance for EF Core design-time CLI tooling (migrations).
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QWIIK_DESIGNTIME_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=QwiikInvoiceManagementDev;Trusted_Connection=True;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ICurrentTenantContext
    {
        public Guid OrganizationId => Guid.Empty;
        public string? UserId => "ef-design-time";
        public bool IsAuthenticated => false;
    }
}
