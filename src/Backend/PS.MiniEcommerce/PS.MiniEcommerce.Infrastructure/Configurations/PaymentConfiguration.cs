using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount_NonNegative", "[Amount] >= 0");
            table.HasCheckConstraint("CK_Payments_RefundedAmount_NonNegative", "[RefundedAmount] >= 0");
            table.HasCheckConstraint(
                "CK_Payments_RefundedAmount_WithinAmount",
                "[RefundedAmount] <= [Amount]");
            table.HasCheckConstraint(
                "CK_Payments_Status_Valid",
                "[Status] IN ('Pending', 'Confirmed', 'Failed', 'Refunded')");
        });

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .ValueGeneratedNever();

        builder.Property(payment => payment.OrderId)
            .IsRequired();

        builder.Property(payment => payment.Amount)
            .IsRequired()
            .HasConversion(new MoneyValueConverter())
            .HasColumnType("decimal(18,2)");

        builder.Property(payment => payment.RefundedAmount)
            .IsRequired()
            .HasConversion(new MoneyValueConverter())
            .HasColumnType("decimal(18,2)");

        builder.Property(payment => payment.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(payment => payment.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(payment => payment.ProviderPaymentId)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(payment => payment.CreatedAtUtc)
            .IsRequired();

        builder.Property(payment => payment.ConfirmedAtUtc)
            .IsRequired(false);

        builder.Property(payment => payment.RefundedAtUtc)
            .IsRequired(false);

        builder.Property<byte[]>("RowVersion")
            .IsRowVersion()
            .IsRequired();

        builder.HasIndex(payment => payment.OrderId)
            .IsUnique();

        builder.HasIndex(payment => payment.ProviderPaymentId)
            .IsUnique()
            .HasFilter("[Provider] = 'stripe'");

        builder.HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(payment => payment.Refunds)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
