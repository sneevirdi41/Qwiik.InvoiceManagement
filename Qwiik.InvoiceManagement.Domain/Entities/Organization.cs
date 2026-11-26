namespace Qwiik.InvoiceManagement.Domain.Entities;

/// <summary>
/// Represents a tenant organization that owns invoices.
/// </summary>
public sealed class Organization
{
    private readonly List<Invoice> _invoices = [];

    private Organization()
    {
    }

    public Organization(Guid id, string name, DateTime createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Organization ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name is required.", nameof(name));
        }

        Id = id;
        Name = name.Trim();
        CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyCollection<Invoice> Invoices => _invoices.AsReadOnly();
}
