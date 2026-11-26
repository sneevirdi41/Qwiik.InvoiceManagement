using Qwiik.InvoiceManagement.Domain.Enums;

namespace Qwiik.InvoiceManagement.Application.DTOs.Invoices;

/// <summary>
/// Request payload for transitioning an invoice's status.
/// </summary>
public sealed record UpdateInvoiceStatusRequest(InvoiceStatus Status);
