using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Qwiik.InvoiceManagement.API.Authentication;
using Qwiik.InvoiceManagement.API.Middleware;
using Qwiik.InvoiceManagement.Application;
using Qwiik.InvoiceManagement.Application.Abstractions;
using Qwiik.InvoiceManagement.Infrastructure;
using Qwiik.InvoiceManagement.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentTenantContext, HttpContextCurrentTenantContext>();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    ConfigureAuthentication(builder);
    builder.Services.AddAuthorization();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database");

    ConfigureSwagger(builder);

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            var orgId = httpContext.User.FindFirst(TenantClaimTypes.OrganizationId)?.Value;
            if (!string.IsNullOrWhiteSpace(orgId))
            {
                diagnosticContext.Set("OrganizationId", orgId);
            }

            diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        };
    });

    var swaggerEnabled = app.Environment.IsDevelopment() ||
                         app.Configuration.GetValue<bool>("Swagger:Enabled");
    if (swaggerEnabled)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Qwiik Invoice Management API v1");
            options.RoutePrefix = "swagger";
        });
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapControllers();

    await InitializeDatabaseIfConfiguredAsync(app);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static void ConfigureAuthentication(WebApplicationBuilder builder)
{
    var authMode = builder.Configuration["Authentication:Mode"] ?? "Jwt";

    if (authMode.Equals("DevHeader", StringComparison.OrdinalIgnoreCase))
    {
        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "DevHeader authentication mode is only permitted in the Development environment. Configure JWT authentication for Staging and Production.");
        }

        builder.Services
            .AddAuthentication(DevHeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(
                DevHeaderAuthenticationHandler.SchemeName,
                _ => { });

        return;
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Authentication:Jwt:Authority"];
            options.Audience = builder.Configuration["Authentication:Jwt:Audience"];

            var validIssuer = builder.Configuration["Authentication:Jwt:ValidIssuer"];
            if (!string.IsNullOrWhiteSpace(validIssuer))
            {
                options.TokenValidationParameters.ValidIssuer = validIssuer;
            }
        });
}

static void ConfigureSwagger(WebApplicationBuilder builder)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Qwiik Multi-Tenant Invoice Management API",
            Version = "v1",
            Description = """
                Production-minded multi-tenant Invoice Management API built with .NET 10, Clean Architecture, EF Core 10, and SQL Server.

                **Development Authentication:** Supply `X-Organization-Id` header using one of the seeded organizations:
                - Acme Corporation: `11111111-1111-1111-1111-111111111111`
                - Globex Industries: `22222222-2222-2222-2222-222222222222`
                """
        });

        options.AddSecurityDefinition("DevOrganizationHeader", new OpenApiSecurityScheme
        {
            Name = DevHeaderAuthenticationHandler.OrganizationHeaderName,
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Description = "Development tenant header. Example: 11111111-1111-1111-1111-111111111111"
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Production JWT Bearer token containing an 'organization_id' claim."
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("DevOrganizationHeader", document)] = []
        });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    });
}

static async Task InitializeDatabaseIfConfiguredAsync(WebApplication app)
{
    if (!app.Configuration.GetValue<bool>("Database:MigrateAndSeedOnStartup"))
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await dbContext.Database.MigrateAsync();
    await DbInitializer.SeedOrganizationsAsync(dbContext, logger);
}

public partial class Program;
