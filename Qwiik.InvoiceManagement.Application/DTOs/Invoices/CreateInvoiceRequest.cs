namespace Qwiik.InvoiceManagement.Application.DTOs.Invoices;

/// <summary>
/// Line item payload used when creating an invoice.
/// </summary>
public sealed record CreateInvoiceItemRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice);

/// <summary>
/// Request payload for creating a new invoice in the authenticated organization.
/// </summary>
public sealed record CreateInvoiceRequest(
    string InvoiceNumber,
    string CustomerName,
    DateTime InvoiceDate,
    DateTime DueDate,
    decimal TaxPercentage,
    IReadOnlyList<CreateInvoiceItemRequest> Items);
