using Qwiik.InvoiceManagement.Application.DTOs.Invoices;
using Qwiik.InvoiceManagement.Application.Validators;

namespace Qwiik.InvoiceManagement.Tests.Application;

public sealed class InvoiceValidatorTests
{
    private readonly CreateInvoiceRequestValidator _createValidator = new();
    private readonly GetInvoicesQueryValidator _queryValidator = new();

    [Fact]
    public void CreateValidator_ShouldFail_WhenDueDatePrecedesInvoiceDate()
    {
        var request = new CreateInvoiceRequest(
            "INV-100",
            "Acme Client",
            new DateTime(2026, 11, 20),
            new DateTime(2026, 11, 19),
            10m,
            [new CreateInvoiceItemRequest("Work", 1m, 100m)]);

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateInvoiceRequest.DueDate));
    }

    [Fact]
    public void CreateValidator_ShouldFail_WhenItemQuantityIsZeroOrUnitPriceNegative()
    {
        var request = new CreateInvoiceRequest(
            "INV-101",
            "Acme Client",
            new DateTime(2026, 11, 20),
            new DateTime(2026, 11, 30),
            10m,
            [new CreateInvoiceItemRequest("Work", 0m, -5m)]);

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Quantity"));
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("UnitPrice"));
    }

    [Fact]
    public void QueryValidator_ShouldFail_WhenSortByOrPageSizeIsInvalid()
    {
        var query = new GetInvoicesQuery
        {
            Page = 0,
            PageSize = 500,
            SortBy = "dropTable",
            SortDirection = "sideways"
        };

        var result = _queryValidator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInvoicesQuery.Page));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInvoicesQuery.PageSize));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInvoicesQuery.SortBy));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInvoicesQuery.SortDirection));
    }
}
