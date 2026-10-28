using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_Price_NonNegative", "[Price] >= 0");
            table.HasCheckConstraint("CK_Products_Available_NonNegative", "[Available] >= 0");
            table.HasCheckConstraint("CK_Products_Reserved_NonNegative", "[Reserved] >= 0");
        });

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Id)
            .ValueGeneratedNever();

        builder.Property(product => product.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(product => product.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(product => product.Price)
            .IsRequired()
            .HasConversion(new MoneyValueConverter())
            .HasColumnType("decimal(18,2)");

        builder.Property(product => product.Available)
            .IsRequired();

        builder.Property(product => product.Reserved)
            .IsRequired();

        builder.Property(product => product.IsActive)
            .IsRequired();

        builder.Property(product => product.CreatedAtUtc)
            .IsRequired();

        builder.Property<byte[]>("RowVersion")
            .IsRowVersion()
            .IsRequired();

        builder.Ignore(product => product.DomainEvents);
    }
}
