using Qwiik.InvoiceManagement.Domain.Exceptions;

namespace Qwiik.InvoiceManagement.Domain.Entities;

/// <summary>
/// Represents an individual line item belonging to an invoice.
/// </summary>
public sealed class InvoiceItem
{
    private InvoiceItem()
    {
    }

    internal InvoiceItem(Guid id, Guid invoiceId, string description, decimal quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("Invoice item description is required.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Invoice item quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("Invoice item unit price cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        InvoiceId = invoiceId;
        Description = description.Trim();
        Quantity = decimal.Round(quantity, 2, MidpointRounding.AwayFromZero);
        UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero);
        LineTotal = decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
}
