using Qwiik.InvoiceManagement.Domain.Enums;

namespace Qwiik.InvoiceManagement.Application.DTOs.Invoices;

/// <summary>
/// Filter, search, sort, and pagination parameters for listing invoices.
/// </summary>
public sealed class GetInvoicesQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public InvoiceStatus? Status { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; } = "invoiceDate";
    public string? SortDirection { get; init; } = "desc";
}
