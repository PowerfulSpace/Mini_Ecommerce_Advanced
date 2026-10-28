using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds", table =>
        {
            table.HasCheckConstraint("CK_Refunds_Amount_Positive", "[Amount] > 0");
        });

        builder.HasKey(refund => refund.Id);

        builder.Property(refund => refund.Id)
            .ValueGeneratedNever();

        builder.Property(refund => refund.PaymentId)
            .IsRequired();

        builder.Property(refund => refund.Amount)
            .IsRequired()
            .HasConversion(new MoneyValueConverter())
            .HasColumnType("decimal(18,2)");

        builder.Property(refund => refund.RefundedAtUtc)
            .IsRequired();

        builder.Property(refund => refund.Reason)
            .IsRequired(false)
            .HasMaxLength(500);
    }
}
