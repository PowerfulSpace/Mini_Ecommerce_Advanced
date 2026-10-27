using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_TotalAmount_NonNegative", "[TotalAmount] >= 0");
            table.HasCheckConstraint(
                "CK_Orders_Status_Valid",
                "[Status] IN ('Created', 'Paid', 'Shipped', 'Delivered', 'Cancelled', 'Returned')");
        });

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .ValueGeneratedNever();

        builder.Property(order => order.UserId)
            .IsRequired();

        builder.Property(order => order.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(order => order.PaymentId)
            .IsRequired(false);

        builder.Property(order => order.CreatedAtUtc)
            .IsRequired();

        builder.Property(order => order.TotalAmount)
            .IsRequired()
            .HasConversion(new MoneyValueConverter())
            .HasColumnType("decimal(18,2)");

        builder.Property<byte[]>("RowVersion")
            .IsRowVersion()
            .IsRequired();

        builder.HasIndex(order => new { order.UserId, order.CreatedAtUtc })
            .IsDescending(false, true);

        builder.Ignore(order => order.DomainEvents);

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(orderItem => orderItem.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
