# Solution Notes — Qwiik Invoice Management API

This document addresses all 14 architectural, database, security, testing, and Azure deployment considerations for the Qwiik Technical Assessment.

---

## 1. How to Run the Project

1. Ensure .NET 10 SDK (or .NET 10 runtime + compatible SDK) and SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) are installed.
2. Restore NuGet packages and local EF Core tools:
   ```powershell
   dotnet restore Qwiik.InvoiceManagement.slnx
   dotnet tool restore
   ```
3. Generate and apply the initial migration (if running outside auto-migration or before first deployment):
   ```powershell
   dotnet tool run dotnet-ef migrations add InitialCreate `
     --project Qwiik.InvoiceManagement.Infrastructure `
     --startup-project Qwiik.InvoiceManagement.API `
     --output-dir Persistence/Migrations

   dotnet tool run dotnet-ef database update `
     --project Qwiik.InvoiceManagement.Infrastructure `
     --startup-project Qwiik.InvoiceManagement.API
   ```
4. Run the API:
   ```powershell
   dotnet run --project Qwiik.InvoiceManagement.API
   ```
5. Open Swagger UI at `http://localhost:5010/swagger` and authorize with `X-Organization-Id: 11111111-1111-1111-1111-111111111111` (Acme) or `22222222-2222-2222-2222-222222222222` (Globex).
6. Run automated tests:
   ```powershell
   dotnet test Qwiik.InvoiceManagement.slnx
   ```

---

## 2. Assumptions

- **Single tax percentage per invoice:** `CreateInvoiceRequest` accepts a `TaxPercentage` (`0`–`100`) applied to the invoice `Subtotal`, rounded using `MidpointRounding.AwayFromZero` to 2 decimal places.
- **Server-authoritative pricing & dates:** Client requests never supply `Subtotal`, `TaxAmount`, `TotalAmount`, `LineTotal`, `Status`, or `OrganizationId`. All new invoices start in `Draft` status.
- **Dashboard KPI semantics:**
  - `TotalInvoices`: Total count of all invoices belonging to the organization (all statuses).
  - `TotalPaidInvoices`: Count of invoices with `Status == Paid`.
  - `TotalUnpaidInvoices`: Count of invoices with `Status == Issued` (active receivables awaiting payment; `Draft` invoices are unissued and `Cancelled` invoices are void).
  - `TotalOverdueInvoices`: Count of `Issued` invoices whose `DueDate < UtcToday`.
  - `TotalInvoiceAmount`: Sum of `TotalAmount` across non-cancelled invoices (`Status != Cancelled`).
  - `TotalPaidAmount`: Sum of `TotalAmount` for `Paid` invoices.
  - `TotalOutstandingAmount`: Sum of `TotalAmount` for `Issued` invoices.
- **Organization provisioning:** Organizations are pre-existing tenant records (`DbInitializer` seeds two sample organizations in Development). Creating an invoice for an unknown `OrganizationId` returns `403 Forbidden`.
- **Date handling:** `InvoiceDate` and `DueDate` are normalized to UTC calendar dates (`date` column in SQL Server), while `CreatedAt` and `UpdatedAt` use UTC `datetime2`.

---

## 3. Architecture Overview

The solution follows **Clean Architecture** with strict inward dependency flow:

```text
API ──▶ Application ──▶ Domain
 │           ▲
 └──▶ Infrastructure ┘
```

- **Domain (`Qwiik.InvoiceManagement.Domain`):** Zero external dependencies. Contains `Organization`, `Invoice`, `InvoiceItem`, `InvoiceStatus`, `DomainException`, and `InvalidStatusTransitionException`. All core invariants (line/invoice total calculation and status state machine) live on the `Invoice` aggregate root.
- **Application (`Qwiik.InvoiceManagement.Application`):** Contains `IApplicationDbContext`, `ICurrentTenantContext`, `IDateTimeProvider`, DTOs, FluentValidation validators, application exceptions (`NotFoundException`, `ConflictException`, `ForbiddenAccessException`, `AppValidationException`), and `InvoiceService`.
- **Infrastructure (`Qwiik.InvoiceManagement.Infrastructure`):** Implements `AppDbContext` (EF Core 10 + SQL Server), `IEntityTypeConfiguration<T>` mappings, unique constraint translation to `ConflictException`, `SystemDateTimeProvider`, and `DbInitializer`.
- **API (`Qwiik.InvoiceManagement.API`):** Thin `InvoicesController`, authentication handlers (`DevHeaderAuthenticationHandler` and `JwtBearer` configuration), `HttpContextCurrentTenantContext`, `GlobalExceptionHandler` (`ProblemDetails`), Serilog request logging, health checks, and Swashbuckle Swagger.
- **Intentional anti-overengineering choice:** Rather than wrapping EF Core in generic repository/unit-of-work interfaces or adding MediatR boilerplate, `IApplicationDbContext` exposes `DbSet<T>` directly to `InvoiceService`. This preserves full async LINQ projection, avoids N+1 loading, and keeps the codebase concise.

---

## 4. Domain Model Explanation

- **`Organization`:** `Id` (`Guid`), `Name` (`string(200)`), `CreatedAt` (`datetime2`).
- **`Invoice`:** Aggregate root owning `_items`. Created via `Invoice.Create(...)`, which enforces invariants, calculates each `InvoiceItem.LineTotal` (`decimal(18,2)`), `Subtotal`, `TaxAmount`, and `TotalAmount`, and initializes `Status = Draft`.
  - `Invoice.TransitionStatus(newStatus, utcNow)` enforces:
    - `Draft → Issued`, `Draft → Cancelled`
    - `Issued → Paid`, `Issued → Cancelled`
    - `Paid` and `Cancelled` are terminal states.
- **`InvoiceItem`:** Child entity with `Description`, `Quantity` (`decimal(18,2)`), `UnitPrice` (`decimal(18,2)`), and `LineTotal` (`decimal(18,2)`).

---

## 5. Database Design Explanation

- **Provider:** SQL Server via EF Core 10.
- **Monetary Precision:** All money columns (`Subtotal`, `TaxAmount`, `TotalAmount`, `UnitPrice`, `LineTotal`) and `Quantity` use `decimal(18,2)` (`HasPrecision(18, 2)`), avoiding binary floating-point rounding drift.
- **Enum Persistence:** `Invoice.Status` is stored as `nvarchar(20)` via `HasConversion<string>()` for operational readability in SQL queries without losing indexability.
- **Relationships & Delete Behavior:**
  - `Organization` 1 — * `Invoice` with `DeleteBehavior.Restrict` (prevents accidental cascading deletion of an organization's financial history).
  - `Invoice` 1 — * `InvoiceItem` with `DeleteBehavior.Cascade` (items belong strictly to their parent invoice aggregate).
- **Concurrency & Uniqueness:** Unique composite index `UX_Invoices_OrganizationId_InvoiceNumber` guarantees invoice numbers are unique per tenant while permitting identical invoice numbers across different organizations. `AppDbContext.SaveChangesAsync` translates SQL Server `2601`/`2627` violations into `ConflictException` (`409`).

---

## 6. API Design Explanation

| Method | Route | Purpose | Success | Error Codes |
|---|---|---|---|---|
| `POST` | `/api/invoices` | Create invoice & compute totals | `201 Created` (`Location` header) | `400`, `401`, `403`, `409` |
| `GET` | `/api/invoices` | Paginated list, filter, search, sort | `200 OK` | `400`, `401` |
| `GET` | `/api/invoices/{id}` | Invoice details + line items | `200 OK` | `401`, `404` |
| `PATCH` | `/api/invoices/{id}/status` | Transition invoice status | `200 OK` | `400`, `401`, `404`, `409` |
| `GET` | `/api/invoices/dashboard` | Tenant-scoped invoice KPIs | `200 OK` | `401` |
| `GET` | `/health` | Readiness/DB health probe | `200 OK` | `503` |

All error responses use RFC 7807 `ProblemDetails` (`application/problem+json`) with `traceId`.

---

## 7. Validation Approach

Validation operates in three complementary layers:
1. **Model Binding / JSON:** Rejects malformed JSON or invalid enum values at the HTTP boundary (`400`).
2. **FluentValidation (`Application/Validators`):** Executed inside `InvoiceService` before persistence (`CreateInvoiceRequestValidator`, `UpdateInvoiceStatusRequestValidator`, `GetInvoicesQueryValidator`). Validates required strings, max lengths, `DueDate >= InvoiceDate`, non-empty items, `Quantity > 0`, `UnitPrice >= 0`, `TaxPercentage` in `[0, 100]`, pagination bounds, and allowed sort fields. Failures throw `AppValidationException` mapped to `400 Bad Request` with field-level `errors`.
3. **Domain Invariants (`Invoice` & `InvoiceItem`):** Defensive checks ensure invalid state cannot be constructed even if called outside the API.

---

## 8. Tenant Isolation Approach

Tenant isolation uses **defense-in-depth**:
1. **Authentication Boundary:** `DevHeaderAuthenticationHandler` (dev) or `JwtBearer` (prod) validates credentials and populates a `ClaimsPrincipal` with `organization_id`.
2. **Trusted Context:** `HttpContextCurrentTenantContext` reads `ICurrentTenantContext.OrganizationId` strictly from `HttpContext.User` claims. Client DTOs have no `OrganizationId` property.
3. **EF Core Global Query Filter:** `AppDbContext` applies `HasQueryFilter(i => i.OrganizationId == CurrentOrganizationId)` evaluated per query execution as a parameterized SQL predicate.
4. **Explicit Application Predicates:** Service queries also filter explicitly by `i.OrganizationId == organizationId` as a second safety net.
5. **Resource Enumeration Protection:** Retrieving or updating an invoice owned by another organization returns `404 Not Found` (never `403`), preventing cross-tenant ID probing.

---

## 9. Indexing and Performance Strategy

- **Composite Indexes on `Invoices`:**
  - `UX_Invoices_OrganizationId_InvoiceNumber` (`UNIQUE`) — accelerates duplicate checks and invoice number lookups per tenant.
  - `IX_Invoices_OrganizationId_Status` — accelerates status filtering and dashboard aggregations.
  - `IX_Invoices_OrganizationId_InvoiceDate` — accelerates default descending date sorting and date-range filters.
- **Foreign Key Index on `InvoiceItems`:**
  - `IX_InvoiceItems_InvoiceId` — accelerates detail retrieval joins.
- **Query Efficiency:**
  - Read queries use `AsNoTracking()` and SQL `Select(...)` projections so `GET /api/invoices` never fetches child `InvoiceItems` or unused columns.
  - `GET /api/invoices/dashboard` computes all 7 KPIs in a single `GROUP BY` SQL aggregate query without loading invoice rows into application memory.

---

## 10. Testing Approach

- **Domain Unit Tests (`InvoiceDomainTests`):** Verify server-side line/subtotal/tax/total rounding (`AwayFromZero`), all valid and invalid status transitions, and domain guard exceptions.
- **Validator Unit Tests (`InvoiceValidatorTests`):** Verify FluentValidation rules on creation requests and query parameters.
- **Integration Tests (`InvoicesApiIntegrationTests`):** Uses `WebApplicationFactory<Program>` backed by real SQL Server LocalDB (`QwiikInvoiceTests_<guid>` per test fixture) to verify full HTTP pipeline, EF Core SQL translation, unique constraints, tenant isolation (`404` across tenants), `401`/`403` auth boundaries, pagination/filtering/sorting, status lifecycle transitions, and dashboard calculations.

---

## 11. Azure Deployment and Monitoring Considerations

### Target Azure Architecture

```text
Clients ─▶ Azure App Service (.NET 10 Web API, Managed Identity)
              ├──▶ Azure Key Vault (JWT secrets, SQL connection string)
              ├──▶ Azure SQL Database (Multi-tenant DB, Geo-redundant backups)
              └──▶ Application Insights + Azure Monitor (Logs, metrics, alerts)

CI/CD: GitHub Actions ─▶ Build & Test ─▶ EF Migration Bundle ─▶ Staging Slot ─▶ Swap to Prod
```

- **Azure App Service:** Host the containerized or framework-dependent .NET 10 API on Linux App Service Plan (Premium v3 for deployment slots and VNet integration). Enable System-Assigned Managed Identity.
- **Azure SQL Database:** Use General Purpose or Hyperscale tier with Zone Redundancy in production, Private Endpoint / VNet integration, and EF Core connection resiliency (`EnableRetryOnFailure`).
- **Azure Key Vault:** Store `ConnectionStrings__DefaultConnection` and JWT signing/issuer configuration in Key Vault, accessed via Managed Identity and App Service Key Vault references (zero secrets in `appsettings.json` or repo).
- **Application Insights & Azure Monitor:** Wire Serilog's Application Insights sink and OpenTelemetry/App Insights SDK to collect request latency, SQL dependency timings, `OrganizationId` dimensions, and 5xx rates. Configure Azure Monitor alerts for HTTP 5xx spikes, P95 latency > 500ms, SQL DTU/CPU > 80%, and `/health` probe failures.
- **GitHub Actions CI/CD:**
  - Build, run unit/integration tests, generate an idempotent EF Core migration bundle (`dotnet ef migrations bundle`), apply migrations to staging, deploy to the App Service **staging slot**, run smoke checks against `/health`, and **swap slots** to production.
- **Scaling:** App Service scales horizontally (stateless API) using autoscale rules on CPU/HTTP queue length; Azure SQL scales vertically (vCores) or uses read-scale out for heavy reporting queries.
- **Rollback Strategy:** Instant App Service slot swap back to the previous slot for application regressions; pair with forward-compatible (expand-and-contract) database migrations so schema changes do not break the prior slot during rollback.
- **Backups:** Azure SQL automated Point-in-Time Restore (PITR, 7–35 days) plus Long-Term Retention (LTR) for financial compliance.

---

## 12. Security Considerations

- **Development Auth Boundary vs Production JWT:**
  - `DevHeaderAuthenticationHandler` trusts `X-Organization-Id` headers and is **strictly limited to the Development environment** (`Program.cs` throws at startup if `DevHeader` is configured outside `Development`).
  - Staging and Production must use `Authentication:Mode = "Jwt"` with an OIDC/OAuth2 provider (e.g., Microsoft Entra ID or Auth0) issuing signed tokens containing a verified `organization_id` claim.
- **Zero Hardcoded Secrets:** `appsettings.json` contains no production passwords or signing keys.
- **SQL Injection Prevention:** All EF Core queries use parameterized LINQ/SQL.
- **Safe Logging & Error Masking:** Serilog logs `TraceId` and non-sensitive `OrganizationId` without logging `Authorization` headers or request bodies. `GlobalExceptionHandler` suppresses internal exception details in Production.

---

## 13. Known Limitations

1. **Development Header Auth:** Convenient for local testing/Swagger, unsuitable for production because headers are client-supplied.
2. **No Edit-Draft Endpoint:** Initial v1 supports creating invoices and transitioning statuses; editing line items of a `Draft` invoice is out of scope.
3. **Single Currency & Single Tax Rate:** Invoices do not track ISO currency codes or per-line tax rates.
4. **Local Environment Verification Caveat:** In the build environment used to author this project, Windows Application Control (`0x800711C7`) blocked loading unsigned locally compiled `.dll` files during `dotnet ef` and `dotnet test` execution, so runtime execution and migration generation must be run in an environment allowing local unsigned .NET assemblies (see `AI_USAGE.md`).

---

## 14. Future Improvements

- JWT token issuance / Entra ID integration tests with mock OIDC keys.
- `PUT /api/invoices/{id}` for editing `Draft` invoices and `POST /api/invoices/{id}/payments` for partial payments.
- Multi-currency support (`CurrencyCode` ISO-4217) and per-line tax/discount rules.
- Outbox pattern / domain events (`InvoiceIssued`, `InvoicePaid`) for email notifications and webhook delivery.
- Soft delete / audit trail table (`InvoiceStatusHistory`) for financial compliance.
- Row-Level Security (RLS) in Azure SQL Database as an additional database-layer tenant enforcement mechanism.
