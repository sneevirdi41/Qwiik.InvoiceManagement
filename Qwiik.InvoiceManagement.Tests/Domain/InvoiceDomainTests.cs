using Qwiik.InvoiceManagement.Domain.Entities;
using Qwiik.InvoiceManagement.Domain.Enums;
using Qwiik.InvoiceManagement.Domain.Exceptions;

namespace Qwiik.InvoiceManagement.Tests.Domain;

public sealed class InvoiceDomainTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime NowUtc = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ShouldCalculateLineTotalsSubtotalTaxAndTotal_WithAwayFromZeroRounding()
    {
        var invoice = Invoice.Create(
            OrgId,
            "INV-2026-001",
            "Northwind Traders",
            new DateTime(2026, 11, 1),
            new DateTime(2026, 11, 30),
            taxPercentage: 10m,
            items:
            [
                ("Consulting Hours", 2.5m, 125.55m), // 313.875 -> 313.88
                ("Cloud Hosting", 1m, 49.99m)        // 49.99
            ],
            NowUtc);

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(2, invoice.Items.Count);
        Assert.Equal(363.87m, invoice.Subtotal);
        Assert.Equal(36.39m, invoice.TaxAmount);   // 36.387 -> 36.39
        Assert.Equal(400.26m, invoice.TotalAmount);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Issued, true)]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Cancelled, true)]
    [InlineData(InvoiceStatus.Issued, InvoiceStatus.Paid, true)]
    [InlineData(InvoiceStatus.Issued, InvoiceStatus.Cancelled, true)]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Issued, InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Issued, InvoiceStatus.Issued, false)]
    [InlineData(InvoiceStatus.Paid, InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Paid, InvoiceStatus.Issued, false)]
    [InlineData(InvoiceStatus.Paid, InvoiceStatus.Cancelled, false)]
    [InlineData(InvoiceStatus.Cancelled, InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Cancelled, InvoiceStatus.Issued, false)]
    [InlineData(InvoiceStatus.Cancelled, InvoiceStatus.Paid, false)]
    public void CanTransition_ShouldEnforceInvoiceLifecycleRules(
        InvoiceStatus from,
        InvoiceStatus to,
        bool expectedAllowed)
    {
        Assert.Equal(expectedAllowed, Invoice.CanTransition(from, to));
    }

    [Fact]
    public void TransitionStatus_ShouldThrowInvalidStatusTransitionException_WhenTransitionIsDisallowed()
    {
        var invoice = Invoice.Create(
            OrgId,
            "INV-2026-002",
            "Contoso Ltd",
            new DateTime(2026, 11, 1),
            new DateTime(2026, 11, 15),
            0m,
            [("License", 1m, 100m)],
            NowUtc);

        var ex = Assert.Throws<InvalidStatusTransitionException>(
            () => invoice.TransitionStatus(InvoiceStatus.Paid, NowUtc.AddMinutes(5)));

        Assert.Equal(InvoiceStatus.Draft, ex.CurrentStatus);
        Assert.Equal(InvoiceStatus.Paid, ex.TargetStatus);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
    }

    [Fact]
    public void Create_ShouldThrowDomainException_WhenDueDatePrecedesInvoiceDate()
    {
        Assert.Throws<DomainException>(() => Invoice.Create(
            OrgId,
            "INV-2026-003",
            "Contoso Ltd",
            new DateTime(2026, 11, 10),
            new DateTime(2026, 11, 9),
            10m,
            [("Item", 1m, 50m)],
            NowUtc));
    }
}
