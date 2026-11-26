using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qwiik.InvoiceManagement.API.Authentication;
using Qwiik.InvoiceManagement.Application.Abstractions;
using Qwiik.InvoiceManagement.Infrastructure.Persistence;

namespace Qwiik.InvoiceManagement.Tests.Integration;

/// <summary>
/// WebApplicationFactory backed by a dedicated SQL Server LocalDB database per test fixture.
/// </summary>
public sealed class InvoiceApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid OrganizationAId = DbInitializer.AcmeOrganizationId;
    public static readonly Guid OrganizationBId = DbInitializer.GlobexOrganizationId;

    private readonly string _databaseName = $"QwiikInvoiceTests_{Guid.NewGuid():N}";
    private string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;";

    public MutableDateTimeProvider Clock { get; } = new(new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Authentication:Mode"] = "DevHeader",
                ["Database:MigrateAndSeedOnStartup"] = "false",
                ["Swagger:Enabled"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(Clock);
        });
    }

    public HttpClient CreateClientForOrganization(Guid organizationId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationHandler.OrganizationHeaderName, organizationId.ToString());
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationHandler.UserIdHeaderName, $"test-user-{organizationId:N}");
        return client;
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.Database.EnsureCreatedAsync();
        await DbInitializer.SeedOrganizationsAsync(dbContext);
    }

    public new async Task DisposeAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public sealed class MutableDateTimeProvider : IDateTimeProvider
    {
        public MutableDateTimeProvider(DateTime initialUtcNow) => UtcNow = initialUtcNow;
        public DateTime UtcNow { get; set; }
    }
}
