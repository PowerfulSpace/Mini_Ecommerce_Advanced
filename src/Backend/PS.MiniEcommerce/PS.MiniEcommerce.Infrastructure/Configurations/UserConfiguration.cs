using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table =>
        {
            table.HasCheckConstraint("CK_Users_Role_Valid", "[Role] IN ('customer', 'admin')");
        });

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        builder.Property(user => user.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(user => user.Role)
            .IsRequired()
            .HasConversion(
                role => role.ToString().ToLowerInvariant(),
                value => Enum.Parse<UserRole>(value, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValueSql("'customer'");

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique();
    }
}
