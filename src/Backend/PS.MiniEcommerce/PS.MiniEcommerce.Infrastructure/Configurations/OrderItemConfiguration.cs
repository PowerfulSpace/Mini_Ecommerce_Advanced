using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;

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
            .HasColumnType("decimal(18,2)");

        builder.Property(orderItem => orderItem.Quantity)
            .IsRequired();

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(orderItem => orderItem.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
