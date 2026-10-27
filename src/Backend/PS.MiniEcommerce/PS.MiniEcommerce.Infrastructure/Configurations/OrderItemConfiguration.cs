using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0");
            table.HasCheckConstraint("CK_OrderItems_UnitPrice_NonNegative", "[UnitPrice] >= 0");
        });

        builder.HasKey(orderItem => orderItem.Id);

        builder.Property(orderItem => orderItem.Id)
            .ValueGeneratedNever();

        builder.Property(orderItem => orderItem.OrderId)
            .IsRequired();

        builder.Property(orderItem => orderItem.ProductId)
            .IsRequired();

        builder.Property(orderItem => orderItem.ProductName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(orderItem => orderItem.UnitPrice)
            .IsRequired()
            .HasConversion(new MoneyValueConverter())
            .HasColumnType("decimal(18,2)");

        builder.Property(orderItem => orderItem.Quantity)
            .IsRequired();

        builder.Ignore(orderItem => orderItem.CancelledQuantity);

        builder.HasMany(orderItem => orderItem.Cancellations)
            .WithOne()
            .HasForeignKey(cancellation => cancellation.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(orderItem => orderItem.Cancellations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(orderItem => orderItem.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
