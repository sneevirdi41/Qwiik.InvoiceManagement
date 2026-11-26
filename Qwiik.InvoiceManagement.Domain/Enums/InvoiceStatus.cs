namespace Qwiik.InvoiceManagement.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of an invoice.
/// </summary>
public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Paid = 2,
    Cancelled = 3
}
