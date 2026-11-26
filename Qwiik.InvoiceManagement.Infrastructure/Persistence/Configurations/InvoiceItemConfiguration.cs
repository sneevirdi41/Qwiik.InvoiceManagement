using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwiik.InvoiceManagement.Domain.Entities;

namespace Qwiik.InvoiceManagement.Infrastructure.Persistence.Configurations;

public sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("InvoiceItems");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .ValueGeneratedNever();

        builder.Property(item => item.InvoiceId)
            .IsRequired();

        builder.Property(item => item.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(item => item.Quantity)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(item => item.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(item => item.LineTotal)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.HasIndex(item => item.InvoiceId)
            .HasDatabaseName("IX_InvoiceItems_InvoiceId");
    }
}
