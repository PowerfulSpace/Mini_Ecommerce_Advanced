using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", table =>
        {
            table.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0");
        });

        builder.HasKey(cartItem => cartItem.Id);

        builder.Property(cartItem => cartItem.Id)
            .ValueGeneratedNever();

        builder.Property(cartItem => cartItem.CartId)
            .IsRequired();

        builder.Property(cartItem => cartItem.ProductId)
            .IsRequired();

        builder.Property(cartItem => cartItem.Quantity)
            .IsRequired();

        builder.Property(cartItem => cartItem.CreatedAtUtc)
            .IsRequired();

        builder.Property(cartItem => cartItem.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(cartItem => new { cartItem.CartId, cartItem.ProductId })
            .IsUnique();

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(cartItem => cartItem.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
