namespace Qwiik.InvoiceManagement.Application.Abstractions;

/// <summary>
/// Abstraction over system clock to support deterministic testing.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
