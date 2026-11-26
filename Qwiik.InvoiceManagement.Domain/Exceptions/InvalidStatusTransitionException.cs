using Qwiik.InvoiceManagement.Domain.Enums;

namespace Qwiik.InvoiceManagement.Domain.Exceptions;

/// <summary>
/// Raised when an invoice status transition violates lifecycle rules.
/// </summary>
public sealed class InvalidStatusTransitionException : DomainException
{
    public InvoiceStatus CurrentStatus { get; }
    public InvoiceStatus TargetStatus { get; }

    public InvalidStatusTransitionException(InvoiceStatus currentStatus, InvoiceStatus targetStatus)
        : base($"Cannot transition invoice status from '{currentStatus}' to '{targetStatus}'.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }
}
