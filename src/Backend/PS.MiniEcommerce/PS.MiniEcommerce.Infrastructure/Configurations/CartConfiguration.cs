using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");

        builder.HasKey(cart => cart.Id);

        builder.Property(cart => cart.Id)
            .ValueGeneratedNever();

        builder.Property(cart => cart.UserId)
            .IsRequired();

        builder.Property(cart => cart.CreatedAtUtc)
            .IsRequired();

        builder.Property(cart => cart.UpdatedAtUtc)
            .IsRequired();

        builder.Property<byte[]>("RowVersion")
            .IsRowVersion();

        builder.HasIndex(cart => cart.UserId)
            .IsUnique();

        builder.HasMany(cart => cart.Items)
            .WithOne()
            .HasForeignKey(cartItem => cartItem.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(cart => cart.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(cart => cart.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
