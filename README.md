# Qwiik Invoice Management API

A production-minded, multi-tenant Invoice Management REST API built with **.NET 10**, **Clean Architecture**, **Entity Framework Core 10**, and **SQL Server**.

## Project Overview

Multiple organizations share the same application and SQL Server database while remaining strictly isolated at the authentication boundary, application layer, and EF Core query layer.

### Key Capabilities
- **Multi-Tenant Isolation:** Tenant identity (`OrganizationId`) is extracted exclusively from trusted `ClaimsPrincipal` claims (`organization_id`), never from request payloads. Cross-tenant resource access returns `404 Not Found`.
- **Server-Side Calculations:** Line totals, subtotal, tax amount, and invoice total are computed server-side using `decimal(18,2)` arithmetic.
- **Invoice Status Lifecycle:** Enforced state machine (`Draft` → `Issued` / `Cancelled`; `Issued` → `Paid` / `Cancelled`; `Paid` and `Cancelled` are terminal).
- **Querying & Dashboard:** Paginated invoice listing with status/date filters, search, sorting, and single-query SQL dashboard KPIs.
- **Standardized Errors & Logging:** RFC 7807 `ProblemDetails` exception handling (`400`, `401`, `403`, `404`, `409`, `500`) and structured Serilog logging.

## Technology Stack

- C# / .NET 10 (`net10.0`)
- ASP.NET Core Web API
- Entity Framework Core 10 & SQL Server
- FluentValidation
- Swashbuckle (Swagger / OpenAPI)
- Serilog (`Serilog.AspNetCore`)
- xUnit & `Microsoft.AspNetCore.Mvc.Testing`
- Docker & GitHub Actions

## Solution Structure

```text
Qwiik.InvoiceManagement/
├── Qwiik.InvoiceManagement.Domain/          # Entities, enums, domain exceptions & lifecycle rules
├── Qwiik.InvoiceManagement.Application/     # Use-case service, DTOs, FluentValidation, abstractions
├── Qwiik.InvoiceManagement.Infrastructure/  # EF Core AppDbContext, entity configs, seeder
├── Qwiik.InvoiceManagement.API/             # Controllers, auth handlers, ProblemDetails, Swagger, Serilog
├── Qwiik.InvoiceManagement.Tests/           # Domain unit tests, validator tests, SQL Server integration tests
├── Dockerfile
├── docker-compose.yml
└── .github/workflows/ci.yml
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or newer SDK with .NET 10 runtime)
- SQL Server LocalDB (included with Visual Studio / SQL Server Express LocalDB) or SQL Server 2022+ / Docker
- `dotnet-ef` CLI tool (included via `dotnet-tools.json` local tool manifest)

## Installation & SQL Server Configuration

1. Navigate to the solution directory:
   ```powershell
   cd D:\Qwikk\Qwiik.InvoiceManagement
   ```
2. Restore packages and local tools:
   ```powershell
   dotnet restore Qwiik.InvoiceManagement.slnx
   dotnet tool restore
   ```
3. Configure the SQL Server connection string in `Qwiik.InvoiceManagement.API/appsettings.Development.json` (defaults to LocalDB):
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=QwiikInvoiceManagementDev;Trusted_Connection=True;TrustServerCertificate=True;"
   }
   ```
   For production or CI, provide `ConnectionStrings__DefaultConnection` via environment variables or Azure Key Vault—never hardcode production credentials.

## Database Migration Commands

Ensure the `MSSQLLocalDB` instance is created and started (one-time local setup):
```powershell
sqllocaldb create MSSQLLocalDB -s
```

Generate the initial EF Core migration (not yet committed if generated on a Smart App Control-enforced host) and apply it to SQL Server:

```powershell
dotnet tool run dotnet-ef migrations add InitialCreate `
  --project Qwiik.InvoiceManagement.Infrastructure `
  --startup-project Qwiik.InvoiceManagement.API `
  --output-dir Persistence/Migrations

dotnet tool run dotnet-ef database update `
  --project Qwiik.InvoiceManagement.Infrastructure `
  --startup-project Qwiik.InvoiceManagement.API
```

> **Note:** In `Development` mode (`Database:MigrateAndSeedOnStartup = true`), starting the API calls `MigrateAsync()` and seeds two sample organizations. Ensure `Persistence/Migrations` contains `InitialCreate` before starting the API or running `docker compose up --build`.

## Running the Project

```powershell
dotnet run --project Qwiik.InvoiceManagement.API
```

### Swagger / OpenAPI URL
- HTTP: `http://localhost:5010/swagger`
- HTTPS: `https://localhost:7053/swagger`
- Health Check: `http://localhost:5010/health`

### Development Authentication Headers

In `Development` (`Authentication:Mode = "DevHeader"`), pass the `X-Organization-Id` header using one of the seeded tenant IDs:
- **Acme Corporation:** `11111111-1111-1111-1111-111111111111`
- **Globex Industries:** `22222222-2222-2222-2222-222222222222`

Optional headers: `X-User-Id` and `X-User-Role`.

> **Important:** `DevHeader` mode is explicitly blocked at startup if `ASPNETCORE_ENVIRONMENT=Production`. Production deployments must use `Authentication:Mode = "Jwt"`.

## Running Tests

Ensure SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) is running, then run:

```powershell
dotnet test Qwiik.InvoiceManagement.slnx
```

## Docker Usage

```powershell
docker compose up --build
```
The API will be available at `http://localhost:8080/swagger`.
