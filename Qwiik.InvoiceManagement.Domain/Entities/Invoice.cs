using Qwiik.InvoiceManagement.Domain.Enums;
using Qwiik.InvoiceManagement.Domain.Exceptions;

namespace Qwiik.InvoiceManagement.Domain.Entities;

/// <summary>
/// Represents a tenant-scoped invoice aggregate root.
/// </summary>
public sealed class Invoice
{
    private static readonly IReadOnlyDictionary<InvoiceStatus, IReadOnlySet<InvoiceStatus>> AllowedTransitions =
        new Dictionary<InvoiceStatus, IReadOnlySet<InvoiceStatus>>
        {
            [InvoiceStatus.Draft] = new HashSet<InvoiceStatus> { InvoiceStatus.Issued, InvoiceStatus.Cancelled },
            [InvoiceStatus.Issued] = new HashSet<InvoiceStatus> { InvoiceStatus.Paid, InvoiceStatus.Cancelled },
            [InvoiceStatus.Paid] = new HashSet<InvoiceStatus>(),
            [InvoiceStatus.Cancelled] = new HashSet<InvoiceStatus>()
        };

    private readonly List<InvoiceItem> _items = [];

    private Invoice()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public DateTime InvoiceDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Organization? Organization { get; private set; }
    public IReadOnlyCollection<InvoiceItem> Items => _items.AsReadOnly();

    /// <summary>
    /// Creates a new invoice in Draft status with server-calculated totals.
    /// </summary>
    public static Invoice Create(
        Guid organizationId,
        string invoiceNumber,
        string customerName,
        DateTime invoiceDate,
        DateTime dueDate,
        decimal taxPercentage,
        IEnumerable<(string Description, decimal Quantity, decimal UnitPrice)> items,
        DateTime utcNow)
    {
        if (organizationId == Guid.Empty)
        {
            throw new DomainException("OrganizationId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            throw new DomainException("Invoice number is required.");
        }

        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new DomainException("Customer name is required.");
        }

        var normalizedInvoiceDate = DateTime.SpecifyKind(invoiceDate.Date, DateTimeKind.Utc);
        var normalizedDueDate = DateTime.SpecifyKind(dueDate.Date, DateTimeKind.Utc);

        if (normalizedDueDate < normalizedInvoiceDate)
        {
            throw new DomainException("Due date cannot precede invoice date.");
        }

        if (taxPercentage is < 0 or > 100)
        {
            throw new DomainException("Tax percentage must be between 0 and 100.");
        }

        var itemInputs = items?.ToList() ?? [];
        if (itemInputs.Count == 0)
        {
            throw new DomainException("An invoice must contain at least one item.");
        }

        var invoiceId = Guid.NewGuid();
        var utcTimestamp = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        var invoice = new Invoice
        {
            Id = invoiceId,
            OrganizationId = organizationId,
            InvoiceNumber = invoiceNumber.Trim(),
            CustomerName = customerName.Trim(),
            InvoiceDate = normalizedInvoiceDate,
            DueDate = normalizedDueDate,
            Status = InvoiceStatus.Draft,
            CreatedAt = utcTimestamp,
            UpdatedAt = utcTimestamp
        };

        foreach (var itemInput in itemInputs)
        {
            invoice._items.Add(new InvoiceItem(
                Guid.NewGuid(),
                invoiceId,
                itemInput.Description,
                itemInput.Quantity,
                itemInput.UnitPrice));
        }

        invoice.Subtotal = decimal.Round(invoice._items.Sum(x => x.LineTotal), 2, MidpointRounding.AwayFromZero);
        invoice.TaxAmount = decimal.Round(invoice.Subtotal * (taxPercentage / 100m), 2, MidpointRounding.AwayFromZero);
        invoice.TotalAmount = decimal.Round(invoice.Subtotal + invoice.TaxAmount, 2, MidpointRounding.AwayFromZero);

        return invoice;
    }

    /// <summary>
    /// Determines whether transitioning from <paramref name="from"/> to <paramref name="to"/> is permitted.
    /// </summary>
    public static bool CanTransition(InvoiceStatus from, InvoiceStatus to)
        => AllowedTransitions.TryGetValue(from, out var allowedTargets) && allowedTargets.Contains(to);

    /// <summary>
    /// Transitions the invoice to a new lifecycle status.
    /// </summary>
    public void TransitionStatus(InvoiceStatus newStatus, DateTime utcNow)
    {
        if (!CanTransition(Status, newStatus))
        {
            throw new InvalidStatusTransitionException(Status, newStatus);
        }

        Status = newStatus;
        UpdatedAt = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }
}
