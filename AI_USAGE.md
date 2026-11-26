# AI Usage Documentation

This file transparently documents how AI was used during the development of the Qwiik Invoice Management API, what was verified, and what remains unexecuted due to local environment constraints.

## AI Tools Used
- **Antigravity AI Coding Assistant (Gemini)**

## What AI Was Used For
- Scaffolding the 5-project .NET 10 Clean Architecture solution (`Domain`, `Application`, `Infrastructure`, `API`, `Tests`).
- Implementing domain entities (`Organization`, `Invoice`, `InvoiceItem`), status transition state machine, and server-side monetary calculations.
- Implementing `InvoiceService`, DTOs, FluentValidation validators, `AppDbContext` with tenant global query filters, EF Core entity configurations, and `DbInitializer`.
- Implementing `InvoicesController`, `DevHeaderAuthenticationHandler` + JWT Bearer configuration boundary, `HttpContextCurrentTenantContext`, `GlobalExceptionHandler` (`ProblemDetails`), Serilog configuration, and Swashbuckle Swagger setup.
- Authoring domain unit tests, validator unit tests, and SQL Server LocalDB `WebApplicationFactory` integration tests.
- Authoring `README.md`, `SOLUTION_NOTES.md`, `Dockerfile`, `docker-compose.yml`, and `.github/workflows/ci.yml`.

## Issues Identified and Corrected During Development
1. **OpenAPI Package Version Conflict (`NU1107`):**
   - `Microsoft.AspNetCore.OpenApi` (`10.0.15`, added by the default template) required `Microsoft.OpenApi` `< 3.0.0`, while `Swashbuckle.AspNetCore` (`11.0.1`) required `Microsoft.OpenApi` `>= 3.11.0`.
   - **Resolution:** Removed `Microsoft.AspNetCore.OpenApi` from `Qwiik.InvoiceManagement.API.csproj` and standardized on `Swashbuckle.AspNetCore 11.0.1` with updated `Microsoft.OpenApi` v3 types (`OpenApiSecuritySchemeReference`).
2. **PowerShell Command Chaining:**
   - Replaced `&&` with PowerShell-compatible exit code checks (`; if ($LASTEXITCODE -eq 0) { ... }`) for Windows PowerShell 5.1.
3. **EF Core `GroupBy` Dashboard Query Translation:**
   - Replaced conditional `.Where(...).Sum(...)` inside `GroupBy(_ => 1)` with `Sum(i => condition ? value : 0m)` to ensure reliable SQL `CASE WHEN` translation across EF Core SQL Server providers.

## Verification Status (Honest Reporting)
- **Verified:**
  - `dotnet restore` and `dotnet build Qwiik.InvoiceManagement.slnx` — compiles with 0 warnings and 0 errors across all 5 projects targeting `net10.0`.
  - SQL Server LocalDB (`MSSQLLocalDB`, Microsoft SQL Server 2025 `17.0.4025.3`) created, started, and verified via `System.Data.SqlClient.SqlConnection` (`SELECT @@VERSION`).
  - Root cause of `0x800711C7` confirmed via `HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy` (`VerifiedAndReputablePolicyState = 1`) and `Microsoft-Windows-CodeIntegrity/Operational` Event IDs `3118`, `3077`, `3033`: Windows 11 Smart App Control is Enforced and blocks unsigned locally compiled assemblies.
- **Not Executed / Blocked by Host Environment:**
  - `dotnet ef migrations add InitialCreate`, `dotnet ef database update`, `dotnet test`, and `dotnet run` could **not** execute on this host because Windows Smart App Control (`0x800711C7`) blocks loading unsigned compiled `.dll` files (`Qwiik.InvoiceManagement.Infrastructure.dll`, `Qwiik.InvoiceManagement.Tests.dll`, `Qwiik.InvoiceManagement.API.dll`), and neither Docker nor WSL nor an enterprise code-signing certificate is available on this machine.
  - Per engineering integrity guidelines, no EF Core migration files were manually fabricated and no tests are claimed as passing at runtime until executed in a supported environment (e.g., Linux Docker container, GitHub Actions CI, or a host with trusted signing / non-enforced Smart App Control).
