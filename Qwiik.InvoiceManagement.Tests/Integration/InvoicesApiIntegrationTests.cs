using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Qwiik.InvoiceManagement.Application.DTOs.Invoices;
using Qwiik.InvoiceManagement.Domain.Enums;

namespace Qwiik.InvoiceManagement.Tests.Integration;

public sealed class InvoicesApiIntegrationTests : IClassFixture<InvoiceApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InvoiceApiWebApplicationFactory _factory;

    public InvoicesApiIntegrationTests(InvoiceApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateInvoice_ShouldCalculateTotalsAndReturn201Created()
    {
        using var client = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationAId);

        var request = new CreateInvoiceRequest(
            InvoiceNumber: $"INV-{Guid.NewGuid():N}"[..16],
            CustomerName: "Alpha Retail",
            InvoiceDate: new DateTime(2026, 11, 10),
            DueDate: new DateTime(2026, 12, 10),
            TaxPercentage: 15m,
            Items:
            [
                new CreateInvoiceItemRequest("Implementation Service", 4m, 250m), // 1000.00
                new CreateInvoiceItemRequest("Support Retainer", 2m, 125.50m)     // 251.00
            ]);

        var response = await client.PostAsJsonAsync("/api/invoices", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<InvoiceResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(InvoiceApiWebApplicationFactory.OrganizationAId, created.OrganizationId);
        Assert.Equal(InvoiceStatus.Draft, created.Status);
        Assert.Equal(1251.00m, created.Subtotal);
        Assert.Equal(187.65m, created.TaxAmount);
        Assert.Equal(1438.65m, created.TotalAmount);
        Assert.Equal(2, created.Items.Count);
    }

    [Fact]
    public async Task CreateInvoice_ShouldReturn400BadRequest_WhenPayloadIsInvalid()
    {
        using var client = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationAId);

        var invalidRequest = new CreateInvoiceRequest(
            InvoiceNumber: "",
            CustomerName: "",
            InvoiceDate: new DateTime(2026, 11, 20),
            DueDate: new DateTime(2026, 11, 10), // precedes invoice date
            TaxPercentage: 120m,
            Items: []);

        var response = await client.PostAsJsonAsync("/api/invoices", invalidRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.True(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public async Task CreateInvoice_ShouldReturn409Conflict_WhenDuplicateInvoiceNumberInSameOrganization()
    {
        using var client = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationAId);
        var duplicateNumber = $"DUP-{Guid.NewGuid():N}"[..16];

        var request = BuildValidRequest(duplicateNumber, "Customer One");
        var firstResponse = await client.PostAsJsonAsync("/api/invoices", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await client.PostAsJsonAsync("/api/invoices", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task CreateInvoice_ShouldAllowSameInvoiceNumber_AcrossDifferentOrganizations()
    {
        using var orgAClient = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationAId);
        using var orgBClient = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationBId);
        var sharedNumber = $"SHARED-{Guid.NewGuid():N}"[..16];

        var orgAResponse = await orgAClient.PostAsJsonAsync("/api/invoices", BuildValidRequest(sharedNumber, "OrgA Customer"), JsonOptions);
        var orgBResponse = await orgBClient.PostAsJsonAsync("/api/invoices", BuildValidRequest(sharedNumber, "OrgB Customer"), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, orgAResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, orgBResponse.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_ShouldReturn404AndHideInvoicesFromOtherOrganizations()
    {
        using var orgAClient = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationAId);
        using var orgBClient = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationBId);

        var createResponse = await orgAClient.PostAsJsonAsync(
            "/api/invoices",
            BuildValidRequest($"ISO-{Guid.NewGuid():N}"[..16], "Secret OrgA Customer"),
            JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var orgAInvoice = await createResponse.Content.ReadFromJsonAsync<InvoiceResponse>(JsonOptions);
        Assert.NotNull(orgAInvoice);

        // Org B cannot retrieve Org A's invoice by ID (must return 404, not 403 or 200)
        var getResponse = await orgBClient.GetAsync($"/api/invoices/{orgAInvoice.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // Org B cannot mutate Org A's invoice status (must return 404)
        var patchResponse = await orgBClient.PatchAsJsonAsync(
            $"/api/invoices/{orgAInvoice.Id}/status",
            new UpdateInvoiceStatusRequest(InvoiceStatus.Issued),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);

        // Org B cannot see Org A's invoice in list search
        var listResponse = await orgBClient.GetFromJsonAsync<PagedResponse<InvoiceSummaryResponse>>(
            $"/api/invoices?search={Uri.EscapeDataString(orgAInvoice.InvoiceNumber)}",
            JsonOptions);
        Assert.NotNull(listResponse);
        Assert.DoesNotContain(listResponse.Items, item => item.Id == orgAInvoice.Id);
    }

    [Fact]
    public async Task UnauthenticatedRequest_ShouldReturn401Unauthorized()
    {
        using var unauthenticatedClient = _factory.CreateClient();
        var response = await unauthenticatedClient.GetAsync("/api/invoices");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownOrganization_ShouldReturn403Forbidden_OnCreate()
    {
        using var unknownOrgClient = _factory.CreateClientForOrganization(Guid.Parse("99999999-9999-9999-9999-999999999999"));
        var response = await unknownOrgClient.PostAsJsonAsync(
            "/api/invoices",
            BuildValidRequest($"UNK-{Guid.NewGuid():N}"[..16], "Unknown Org Customer"),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StatusTransitions_ShouldAllowValidAndRejectInvalidTransitions()
    {
        using var client = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationAId);

        var created = await CreateInvoiceAsync(client, BuildValidRequest($"STAT-{Guid.NewGuid():N}"[..16], "Lifecycle Client"));

        // Invalid: Draft -> Paid
        var draftToPaid = await client.PatchAsJsonAsync(
            $"/api/invoices/{created.Id}/status",
            new UpdateInvoiceStatusRequest(InvoiceStatus.Paid),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, draftToPaid.StatusCode);

        // Valid: Draft -> Issued
        var draftToIssued = await client.PatchAsJsonAsync(
            $"/api/invoices/{created.Id}/status",
            new UpdateInvoiceStatusRequest(InvoiceStatus.Issued),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, draftToIssued.StatusCode);

        // Valid: Issued -> Paid
        var issuedToPaid = await client.PatchAsJsonAsync(
            $"/api/invoices/{created.Id}/status",
            new UpdateInvoiceStatusRequest(InvoiceStatus.Paid),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, issuedToPaid.StatusCode);

        // Invalid terminal transition: Paid -> Cancelled
        var paidToCancelled = await client.PatchAsJsonAsync(
            $"/api/invoices/{created.Id}/status",
            new UpdateInvoiceStatusRequest(InvoiceStatus.Cancelled),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, paidToCancelled.StatusCode);
    }

    [Fact]
    public async Task ListAndPagination_ShouldFilterSearchSortAndPaginate()
    {
        using var client = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationAId);
        var batchTag = Guid.NewGuid().ToString("N")[..8];

        var inv1 = await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"PG-{batchTag}-1", $"Zebra {batchTag}", new DateTime(2026, 10, 1), new DateTime(2026, 10, 15), 0m,
            [new CreateInvoiceItemRequest("Item", 1m, 100m)]));

        var inv2 = await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"PG-{batchTag}-2", $"Alpha {batchTag}", new DateTime(2026, 10, 5), new DateTime(2026, 10, 20), 0m,
            [new CreateInvoiceItemRequest("Item", 1m, 300m)]));

        await client.PatchAsJsonAsync($"/api/invoices/{inv2.Id}/status", new UpdateInvoiceStatusRequest(InvoiceStatus.Issued), JsonOptions);

        var inv3 = await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"PG-{batchTag}-3", $"Beta {batchTag}", new DateTime(2026, 10, 10), new DateTime(2026, 10, 25), 0m,
            [new CreateInvoiceItemRequest("Item", 1m, 200m)]));

        // Filter by search + date range + sort by totalAmount desc + page size 1
        var page1 = await client.GetFromJsonAsync<PagedResponse<InvoiceSummaryResponse>>(
            $"/api/invoices?search={batchTag}&fromDate=2026-10-01&toDate=2026-10-10&sortBy=totalAmount&sortDirection=desc&page=1&pageSize=1",
            JsonOptions);

        Assert.NotNull(page1);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.True(page1.HasNextPage);
        Assert.False(page1.HasPreviousPage);
        Assert.Single(page1.Items);
        Assert.Equal(inv2.Id, page1.Items[0].Id);

        // Filter by Issued status
        var issuedOnly = await client.GetFromJsonAsync<PagedResponse<InvoiceSummaryResponse>>(
            $"/api/invoices?search={batchTag}&status=Issued",
            JsonOptions);
        Assert.NotNull(issuedOnly);
        Assert.Single(issuedOnly.Items);
        Assert.Equal(inv2.Id, issuedOnly.Items[0].Id);

        // Verify inv1 and inv3 are also retrievable by ID
        var retrieved1 = await client.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{inv1.Id}", JsonOptions);
        var retrieved3 = await client.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{inv3.Id}", JsonOptions);
        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved3);
        Assert.Single(retrieved1.Items);
    }

    [Fact]
    public async Task Dashboard_ShouldReturnAccurateTenantScopedAggregates()
    {
        // Use a dedicated seeded organization ID or fresh organization via OrganizationB (isolated from OrgA tests)
        using var client = _factory.CreateClientForOrganization(InvoiceApiWebApplicationFactory.OrganizationBId);
        _factory.Clock.UtcNow = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        // Baseline before adding dashboard test invoices
        var baseline = await client.GetFromJsonAsync<DashboardSummaryResponse>("/api/invoices/dashboard", JsonOptions);
        Assert.NotNull(baseline);

        // 1. Draft invoice ($100)
        await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"DASH-{Guid.NewGuid():N}"[..16], "Draft Co", new DateTime(2026, 11, 20), new DateTime(2026, 12, 20), 0m,
            [new CreateInvoiceItemRequest("Draft Item", 1m, 100m)]));

        // 2. Issued & NOT overdue ($200)
        var issuedCurrent = await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"DASH-{Guid.NewGuid():N}"[..16], "Current Co", new DateTime(2026, 11, 20), new DateTime(2026, 12, 1), 0m,
            [new CreateInvoiceItemRequest("Current Item", 1m, 200m)]));
        await TransitionAsync(client, issuedCurrent.Id, InvoiceStatus.Issued);

        // 3. Issued & OVERDUE ($300, due 2026-11-25 < today 2026-11-26)
        var issuedOverdue = await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"DASH-{Guid.NewGuid():N}"[..16], "Overdue Co", new DateTime(2026, 11, 1), new DateTime(2026, 11, 25), 0m,
            [new CreateInvoiceItemRequest("Overdue Item", 1m, 300m)]));
        await TransitionAsync(client, issuedOverdue.Id, InvoiceStatus.Issued);

        // 4. Paid ($400)
        var paid = await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"DASH-{Guid.NewGuid():N}"[..16], "Paid Co", new DateTime(2026, 11, 1), new DateTime(2026, 11, 10), 0m,
            [new CreateInvoiceItemRequest("Paid Item", 1m, 400m)]));
        await TransitionAsync(client, paid.Id, InvoiceStatus.Issued);
        await TransitionAsync(client, paid.Id, InvoiceStatus.Paid);

        // 5. Cancelled ($500 - excluded from TotalInvoiceAmount)
        var cancelled = await CreateInvoiceAsync(client, new CreateInvoiceRequest(
            $"DASH-{Guid.NewGuid():N}"[..16], "Cancelled Co", new DateTime(2026, 11, 1), new DateTime(2026, 11, 10), 0m,
            [new CreateInvoiceItemRequest("Cancelled Item", 1m, 500m)]));
        await TransitionAsync(client, cancelled.Id, InvoiceStatus.Cancelled);

        var after = await client.GetFromJsonAsync<DashboardSummaryResponse>("/api/invoices/dashboard", JsonOptions);
        Assert.NotNull(after);

        Assert.Equal(baseline.TotalInvoices + 5, after.TotalInvoices);
        Assert.Equal(baseline.TotalPaidInvoices + 1, after.TotalPaidInvoices);
        Assert.Equal(baseline.TotalUnpaidInvoices + 2, after.TotalUnpaidInvoices);
        Assert.Equal(baseline.TotalOverdueInvoices + 1, after.TotalOverdueInvoices);
        Assert.Equal(baseline.TotalInvoiceAmount + 1000m, after.TotalInvoiceAmount);
        Assert.Equal(baseline.TotalPaidAmount + 400m, after.TotalPaidAmount);
        Assert.Equal(baseline.TotalOutstandingAmount + 500m, after.TotalOutstandingAmount);
    }

    [Fact]
    public async Task SwaggerAndHealth_ShouldExposeOpenApiSpecAndHealthyStatus()
    {
        using var client = _factory.CreateClient();

        var healthResponse = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        var swaggerResponse = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);

        var openApiJson = await swaggerResponse.Content.ReadAsStringAsync();
        Assert.Contains("/api/invoices", openApiJson);
        Assert.Contains("/api/invoices/{id}", openApiJson);
        Assert.Contains("/api/invoices/{id}/status", openApiJson);
        Assert.Contains("/api/invoices/dashboard", openApiJson);
    }

    private static CreateInvoiceRequest BuildValidRequest(string invoiceNumber, string customerName) =>
        new(
            InvoiceNumber: invoiceNumber,
            CustomerName: customerName,
            InvoiceDate: new DateTime(2026, 11, 15),
            DueDate: new DateTime(2026, 12, 15),
            TaxPercentage: 10m,
            Items: [new CreateInvoiceItemRequest("Standard Service", 2m, 100m)]);

    private static async Task<InvoiceResponse> CreateInvoiceAsync(HttpClient client, CreateInvoiceRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/invoices", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>(JsonOptions))!;
    }

    private static async Task TransitionAsync(HttpClient client, Guid invoiceId, InvoiceStatus status)
    {
        var response = await client.PatchAsJsonAsync(
            $"/api/invoices/{invoiceId}/status",
            new UpdateInvoiceStatusRequest(status),
            JsonOptions);
        response.EnsureSuccessStatusCode();
    }
}
