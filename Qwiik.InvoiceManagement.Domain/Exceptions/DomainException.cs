namespace Qwiik.InvoiceManagement.Domain.Exceptions;

/// <summary>
/// Represents a violation of a domain invariant or business rule.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
