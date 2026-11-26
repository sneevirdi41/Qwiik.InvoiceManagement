# Interview Presentation Guide — Qwiik Invoice Management API

This guide is structured for a 10–15 minute technical walkthrough and interview defense of the **Qwiik Multi-Tenant Invoice Management API**.

---

## 1. Project Introduction

- **What it is:** A multi-tenant B2B Invoice Management REST API built with **C#**, **.NET 10**, **ASP.NET Core Web API**, **Entity Framework Core 10**, and **SQL Server**.
- **Design philosophy:** Production-minded Clean Architecture without overengineering—keeping domain invariants pure, application orchestration explicit, queries SQL-efficient, and tenant isolation enforced across multiple layers.

## 2. Business Problem

B2B SaaS platforms serve multiple customer organizations (`Organization`) on shared infrastructure. An invoice system must:
1. Guarantee strict **data isolation** so Organization A can never view, list, aggregate, or mutate Organization B's financial records.
2. Perform **authoritative monetary calculations** server-side using fixed-point decimal arithmetic (`decimal(18,2)`), never trusting client-supplied totals.
3. Enforce a **deterministic invoice lifecycle** (`Draft` → `Issued` / `Cancelled`; `Issued` → `Paid` / `Cancelled`) so paid or voided invoices cannot be illegally reopened.
4. Provide fast **search, filtering, pagination, and dashboard KPIs** without loading unbounded tables into application memory.

## 3. Architecture Explanation

The solution contains five projects with strict inward dependencies:

```text
Qwiik.InvoiceManagement.API             (HTTP, Auth, ProblemDetails, Swagger, Serilog)
        │                │
        ▼                ▼
Qwiik.InvoiceManagement.Application    Qwiik.InvoiceManagement.Infrastructure
  (InvoiceService, DTOs, Validators,     (AppDbContext, EF Configurations,
   IApplicationDbContext, Abstractions)   SQL Server, DbInitializer)
        │                                        │
        └───────────────┬────────────────────────┘
                        ▼
        Qwiik.InvoiceManagement.Domain
          (Organization, Invoice, InvoiceItem, InvoiceStatus, DomainException)

Qwiik.InvoiceManagement.Tests           (Domain/Validator unit + LocalDB integration tests)
```

- **Why `IApplicationDbContext` instead of Generic Repository + Unit of Work?** EF Core `DbContext` already implements Unit of Work and Repository (`DbSet<T>`). Adding generic repository wrappers hides `IQueryable` projection (`Select`), encourages loading full entity graphs into memory, and adds boilerplate without value.

## 4. Database Relationship Explanation

Three tables in SQL Server configured via `IEntityTypeConfiguration<T>`:
1. **`Organizations`** (`Id` PK, `Name` `nvarchar(200)`, `CreatedAt` `datetime2`)
2. **`Invoices`** (`Id` PK, `OrganizationId` FK, `InvoiceNumber` `nvarchar(50)`, `CustomerName` `nvarchar(200)`, `InvoiceDate` `date`, `DueDate` `date`, `Subtotal`/`TaxAmount`/`TotalAmount` `decimal(18,2)`, `Status` `nvarchar(20)`, `CreatedAt`/`UpdatedAt` `datetime2`)
   - FK to `Organizations` uses `DeleteBehavior.Restrict` so deleting an organization cannot silently cascade-delete financial records.
3. **`InvoiceItems`** (`Id` PK, `InvoiceId` FK, `Description` `nvarchar(500)`, `Quantity`/`UnitPrice`/`LineTotal` `decimal(18,2)`)
   - FK to `Invoices` uses `DeleteBehavior.Cascade` because line items belong strictly to their invoice aggregate.

- **Tenant-scoped uniqueness:** Composite unique index `UX_Invoices_OrganizationId_InvoiceNumber` enforces unique invoice numbers *within* each organization while allowing `INV-001` in both Acme and Globex.

## 5. API Endpoint Explanation

All `/api/invoices` endpoints require authentication (`[Authorize]`):

| Method | Endpoint | Purpose | Key Status Codes |
|---|---|---|---|
| `POST` | `/api/invoices` | Create invoice in `Draft` status; server computes `LineTotal`, `Subtotal`, `TaxAmount`, `TotalAmount` | `201 Created`, `400`, `401`, `403`, `409` |
| `GET` | `/api/invoices` | Paginated listing with `status`, `fromDate`, `toDate`, `search`, `sortBy`, `sortDirection`, `page`, `pageSize` | `200 OK`, `400`, `401` |
| `GET` | `/api/invoices/{id}` | Retrieve invoice header + ordered line items | `200 OK`, `401`, `404` |
| `PATCH` | `/api/invoices/{id}/status` | Transition invoice status (`Draft`/`Issued` rules) | `200 OK`, `400`, `401`, `404`, `409` |
| `GET` | `/api/invoices/dashboard` | Tenant-scoped invoice counts & monetary KPIs | `200 OK`, `401` |
| `GET` | `/health` | Anonymous readiness probe checking SQL Server connectivity | `200 OK`, `503` |

## 6. Multi-Tenant Implementation

Defense-in-depth across four layers:
1. **Claim-Based Identity:** `HttpContextCurrentTenantContext` (`ICurrentTenantContext`) reads `organization_id` exclusively from `HttpContext.User` (`ClaimsPrincipal`). Request DTOs (`CreateInvoiceRequest`, `GetInvoicesQuery`) do not contain `OrganizationId`.
2. **EF Core Global Query Filter:** `AppDbContext` configures `HasQueryFilter(i => i.OrganizationId == CurrentOrganizationId)`, where `CurrentOrganizationId` is evaluated per query execution as a SQL parameter.
3. **Explicit Service Filters:** `InvoiceService` also explicitly filters `i.OrganizationId == organizationId` in LINQ queries.
4. **404 Masking:** Accessing or updating an invoice owned by another organization throws `NotFoundException` (`404 Not Found`), preventing cross-tenant resource enumeration.

## 7. Security Decisions

- **Swappable Auth Boundary:** `Program.cs` selects `DevHeaderAuthenticationHandler` (local dev/Swagger) or `JwtBearerDefaults` (`Authentication:Mode = "Jwt"`). Both populate identical `ClaimsPrincipal` claims (`organization_id`, `NameIdentifier`, `Role`).
- **Fail-Safe Production Guard:** `Program.cs` throws `InvalidOperationException` at startup if `Authentication:Mode == "DevHeader"` while `ASPNETCORE_ENVIRONMENT == "Production"`.
- **Safe Logging & Secrets:** No hardcoded production credentials; Serilog logs `TraceId` and non-sensitive `OrganizationId` without logging `Authorization` headers or request payloads.
- **SQL Injection Protection:** 100% parameterized EF Core LINQ queries.

## 8. Error Handling

`GlobalExceptionHandler` (`IExceptionHandler`) + `AddProblemDetails()` + `UseStatusCodePages()` return uniform RFC 7807 `ProblemDetails` JSON with `traceId`:
- `AppValidationException` / `DomainException` → `400 Bad Request` (`errors` dictionary for validation)
- `UnauthorizedAccessException` / auth challenge → `401 Unauthorized`
- `ForbiddenAccessException` (e.g., unknown organization claim on create) → `403 Forbidden`
- `NotFoundException` (missing or cross-tenant ID) → `404 Not Found`
- `ConflictException` / `InvalidStatusTransitionException` → `409 Conflict`
- Unhandled `Exception` → `500 Internal Server Error` (internal details suppressed outside `Development`)

## 9. Pagination and Performance

- **SQL Projection:** `GetPagedAsync` uses `AsNoTracking()` and `.Select(i => new InvoiceSummaryResponse(...))`, never fetching child `InvoiceItems` on list queries.
- **Single-Query Dashboard:** `GetDashboardAsync` runs one SQL `GroupBy(_ => 1)` aggregate query with `CASE WHEN` sums—zero invoice rows loaded into memory.
- **Targeted Indexes:**
  - `UX_Invoices_OrganizationId_InvoiceNumber` (unique lookup & duplicate guard)
  - `IX_Invoices_OrganizationId_Status` (status filtering & dashboard)
  - `IX_Invoices_OrganizationId_InvoiceDate` (date range filter & default sorting)
  - `IX_InvoiceItems_InvoiceId` (detail join)

## 10. Testing Strategy

- **Domain Unit Tests (`InvoiceDomainTests`):** Arithmetic rounding (`AwayFromZero`), all 14 `CanTransition` combinations, invalid transition exception, and `DueDate < InvoiceDate` guard.
- **Validator Unit Tests (`InvoiceValidatorTests`):** FluentValidation rules for creation requests and query bounds/sorting whitelist.
- **Integration Tests (`InvoicesApiIntegrationTests`):** `WebApplicationFactory<Program>` wired to isolated per-fixture SQL Server LocalDB databases (`QwiikInvoiceTests_<guid>`) testing all 5 endpoints, Swagger/Health, tenant isolation, duplicate invoice numbers, status lifecycle, pagination/search/sorting, and dashboard aggregates.

## 11. Azure Deployment Architecture

- **Azure App Service (Linux, Premium v3):** Hosts stateless .NET 10 API with staging deployment slot for zero-downtime swaps and instant rollbacks.
- **Azure SQL Database:** Multi-tenant DB with Private Endpoint, zone redundancy, PITR + Long-Term Retention backups, and EF Core `EnableRetryOnFailure`.
- **Azure Key Vault:** Stores SQL connection string and JWT authority/keys, accessed via System-Assigned Managed Identity.
- **Application Insights & Azure Monitor:** Distributed tracing, SQL dependency metrics, `OrganizationId` custom dimensions, and alerts on 5xx spikes / P95 latency / `/health` failures.
- **GitHub Actions:** Build → Test on LocalDB → EF Migration Bundle → Deploy to staging slot → Smoke test → Swap to production.

## 12. Known Limitations

1. Development auth uses `X-Organization-Id` headers (must be replaced by JWT in production).
2. No `PUT` endpoint to edit `Draft` line items in v1.
3. Single `TaxPercentage` per invoice and single implicit currency (no `CurrencyCode`).
4. No soft-delete or `InvoiceStatusHistory` audit table yet.
5. In the authoring Windows environment, Smart App Control (`0x800711C7`) blocked running unsigned local binaries (`dotnet ef`, `dotnet test`), so CI or a non-SAC host is needed to generate `InitialCreate` and execute tests.

## 13. Future Improvements

- JWT integration tests using ephemeral RSA signing keys.
- Draft editing (`PUT /api/invoices/{id}`) and partial payments tracking.
- Multi-currency (`CurrencyCode`) and per-line tax rates.
- Outbox pattern for asynchronous events (`InvoiceIssued`, `InvoicePaid`).
- Azure SQL Row-Level Security (`SECURITY POLICY`) as a database-enforced tenant backstop.

---

## 20 Likely Technical Interview Questions & Answers

1. **Why did you use Clean Architecture for a relatively focused API?**
   To isolate domain rules (`Invoice.Create`, `Invoice.TransitionStatus`) from HTTP and EF Core concerns. By keeping Domain dependency-free and avoiding unnecessary abstractions like MediatR or generic repositories, we get testability and clear boundaries without ceremony.

2. **Why did you expose `DbSet<T>` via `IApplicationDbContext` instead of creating `IInvoiceRepository`?**
   EF Core's `DbContext` already implements the Unit of Work and Repository patterns. Exposing `DbSet<T>` lets `InvoiceService` compose `AsNoTracking()` queries with SQL `Select(...)` projections and `GroupBy` aggregations directly, avoiding N+1 queries or over-fetching.

3. **How do you prevent a malicious client from spoofing `OrganizationId`?**
   `CreateInvoiceRequest` and `GetInvoicesQuery` do not accept `OrganizationId`. `HttpContextCurrentTenantContext` reads `organization_id` exclusively from `HttpContext.User` claims populated by the authentication handler.

4. **Why return `404 Not Found` instead of `403 Forbidden` when Organization B requests Organization A's invoice ID?**
   Returning `403` confirms that the GUID exists in another tenant (resource enumeration / IDOR oracle). Returning `404` hides whether the invoice exists at all.

5. **How does EF Core's Global Query Filter work per request without caching the first tenant's ID?**
   In `AppDbContext`, the filter references an instance property `CurrentOrganizationId` backed by scoped `ICurrentTenantContext.OrganizationId` (`i => i.OrganizationId == CurrentOrganizationId`). EF Core parameterizes `CurrentOrganizationId` on each query execution rather than baking a constant into the model.

6. **Is the Global Query Filter alone enough for multi-tenant safety?**
   No—we treat it as defense-in-depth alongside explicit `.Where(i => i.OrganizationId == organizationId)` predicates in `InvoiceService` and claim checks (`RequireTenantOrganizationId()`). That protects against accidental `.IgnoreQueryFilters()` usage.

7. **How is invoice number uniqueness enforced across tenants?**
   By a composite unique index `UX_Invoices_OrganizationId_InvoiceNumber` on `(OrganizationId, InvoiceNumber)`. Same-tenant duplicates are caught proactively in `InvoiceService.CreateAsync` and also at the database boundary in `AppDbContext.SaveChangesAsync` (`SqlException` 2601/2627 → `ConflictException` → `409 Conflict`).

8. **Why catch `SqlException` 2601/2627 in `AppDbContext.SaveChangesAsync` if `InvoiceService` already checks `AnyAsync`?**
   To handle race conditions: two concurrent requests with the same `InvoiceNumber` in the same organization can both pass `AnyAsync` before either commits. The unique index is the authoritative concurrency lock, and translating 2601/2627 turns a `500` crash into a clean `409 Conflict`.

9. **Why use `decimal(18,2)` instead of `double` or `float` for money?**
   Binary floating-point types (`float`/`double`) cannot represent base-10 fractions like `0.10` or `0.01` accurately, causing penny drift in financial totals. `decimal(18,2)` stores exact base-10 values up to 18 digits with 2 decimal places.

10. **How did you handle rounding in invoice calculations?**
    `Invoice.Create` rounds `Quantity`, `UnitPrice`, each `LineTotal`, `Subtotal`, `TaxAmount`, and `TotalAmount` using `MidpointRounding.AwayFromZero` to 2 decimal places, matching standard commercial accounting expectations.

11. **Why store `Invoice.Status` as `nvarchar(20)` instead of `int` in SQL Server?**
    String conversion (`HasConversion<string>()`) makes ad-hoc SQL queries, Azure SQL troubleshooting, and BI exports immediately readable without joining lookup tables, while remaining compact (`<= 20` chars) and indexable via `IX_Invoices_OrganizationId_Status`.

12. **How are invalid status transitions handled, and why return `409 Conflict` instead of `400 Bad Request`?**
    FluentValidation rejects malformed payloads or unknown enum values with `400 Bad Request`. Attempting a disallowed transition like `Paid → Cancelled` is a valid payload that conflicts with the resource's current persisted state, which maps semantically to `409 Conflict` (`InvalidStatusTransitionException`).

13. **How does `GET /api/invoices/dashboard` avoid loading all invoices into memory?**
    `InvoiceService.GetDashboardAsync` translates all 7 metrics into a single SQL `GroupBy(_ => 1)` aggregate query using conditional `Sum(i => condition ? value : 0)` (`COUNT` and `SUM(CASE WHEN ...)` in T-SQL), returning one summary row from SQL Server.

14. **How do you define "Unpaid" and "Overdue" invoices on the dashboard?**
    `TotalUnpaidInvoices` and `TotalOutstandingAmount` count `Issued` invoices (awaiting payment). `Draft` invoices have not been issued to customers yet, and `Cancelled` invoices are voided (and excluded from `TotalInvoiceAmount`). `TotalOverdueInvoices` counts `Issued` invoices where `DueDate < todayUtc`.

15. **How would you switch from `DevHeader` authentication to production JWT without rewriting the app?**
    Set `Authentication:Mode` to `"Jwt"` and configure `Authentication:Jwt:Authority`, `Audience`, and `ValidIssuer` (via Key Vault/env vars). Because `HttpContextCurrentTenantContext` only reads `ClaimsPrincipal` claims (`organization_id`), controllers, services, and `AppDbContext` require zero code changes.

16. **How do you prevent `DevHeader` authentication from ever running in production?**
    `appsettings.json` defaults `Authentication:Mode` to `"Jwt"`, and `Program.cs` explicitly throws `InvalidOperationException` on startup if `Authentication:Mode == "DevHeader"` when `builder.Environment.IsProduction()`.

17. **Why use `DeleteBehavior.Restrict` between `Organization` and `Invoice`, and `Cascade` for `InvoiceItem`?**
    Deleting an `Organization` should never accidentally wipe out audited financial invoices (`Restrict`). Deleting an `Invoice` removes its tightly owned `InvoiceItem` rows (`Cascade`), though in production we only cancel invoices rather than hard-deleting them.

18. **How do you ensure `GlobalExceptionHandler` doesn't leak sensitive details?**
    Only in `Development` does `500 Internal Server Error` include `exception.Message`; in non-Development environments it returns a generic message with `traceId`. Serilog request logging enriches `OrganizationId` and `TraceId` without logging `Authorization` headers or bodies.

19. **Why test with SQL Server LocalDB instead of EF Core InMemory?**
    EF Core InMemory does not enforce SQL constraints (such as `UX_Invoices_OrganizationId_InvoiceNumber`), foreign key behaviors, `decimal(18,2)` truncation, or T-SQL `GroupBy`/`CASE` translations. Testing against LocalDB catches real relational and SQL translation bugs.

20. **How would you handle zero-downtime database migrations and rollbacks in Azure?**
    In GitHub Actions, build an idempotent `efbundle` and run forward-compatible (expand-and-contract) migrations before swapping the App Service staging slot to production. If a regression occurs, swap the App Service slot back immediately without breaking the older code against the updated schema.
