using Qwiik.InvoiceManagement.Domain.Enums;

namespace Qwiik.InvoiceManagement.Application.DTOs.Invoices;

public sealed record InvoiceItemResponse(
    Guid Id,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record InvoiceResponse(
    Guid Id,
    Guid OrganizationId,
    string InvoiceNumber,
    string CustomerName,
    DateTime InvoiceDate,
    DateTime DueDate,
    decimal Subtotal,
    decimal TaxAmount,
    decimal TotalAmount,
    InvoiceStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<InvoiceItemResponse> Items);

public sealed record InvoiceSummaryResponse(
    Guid Id,
    Guid OrganizationId,
    string InvoiceNumber,
    string CustomerName,
    DateTime InvoiceDate,
    DateTime DueDate,
    decimal Subtotal,
    decimal TaxAmount,
    decimal TotalAmount,
    InvoiceStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

/// <summary>
/// Aggregated invoice KPIs scoped to the authenticated organization.
/// </summary>
public sealed record DashboardSummaryResponse(
    int TotalInvoices,
    int TotalPaidInvoices,
    int TotalUnpaidInvoices,
    int TotalOverdueInvoices,
    decimal TotalInvoiceAmount,
    decimal TotalPaidAmount,
    decimal TotalOutstandingAmount);
