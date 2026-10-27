using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class OrderItemCancellationConfiguration : IEntityTypeConfiguration<OrderItemCancellation>
{
    public void Configure(EntityTypeBuilder<OrderItemCancellation> builder)
    {
        builder.ToTable("OrderItemCancellations", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderItemCancellations_Quantity_Positive",
                "[Quantity] > 0");
        });

        builder.HasKey(cancellation => cancellation.Id);

        builder.Property(cancellation => cancellation.Id)
            .ValueGeneratedNever();

        builder.Property(cancellation => cancellation.OrderItemId)
            .IsRequired();

        builder.Property(cancellation => cancellation.Quantity)
            .IsRequired();

        builder.Property(cancellation => cancellation.CancelledAtUtc)
            .IsRequired();

        builder.Property(cancellation => cancellation.Reason)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.HasIndex(cancellation => cancellation.OrderItemId);
    }
}
