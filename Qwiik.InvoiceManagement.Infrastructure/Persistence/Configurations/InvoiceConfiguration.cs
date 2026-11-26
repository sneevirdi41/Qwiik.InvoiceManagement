using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwiik.InvoiceManagement.Domain.Entities;

namespace Qwiik.InvoiceManagement.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.OrganizationId)
            .IsRequired();

        builder.Property(i => i.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.CustomerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.InvoiceDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(i => i.DueDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(i => i.Subtotal)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.TaxAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.TotalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.CreatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.HasOne(i => i.Organization)
            .WithMany(o => o.Invoices)
            .HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Items)
            .WithOne()
            .HasForeignKey(item => item.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Tenant-scoped unique invoice number + recommended performance indexes
        builder.HasIndex(i => new { i.OrganizationId, i.InvoiceNumber })
            .IsUnique()
            .HasDatabaseName("UX_Invoices_OrganizationId_InvoiceNumber");

        builder.HasIndex(i => new { i.OrganizationId, i.Status })
            .HasDatabaseName("IX_Invoices_OrganizationId_Status");

        builder.HasIndex(i => new { i.OrganizationId, i.InvoiceDate })
            .HasDatabaseName("IX_Invoices_OrganizationId_InvoiceDate");
    }
}
