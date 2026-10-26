using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class CheckoutAttemptConfiguration : IEntityTypeConfiguration<CheckoutAttempt>
{
    public void Configure(EntityTypeBuilder<CheckoutAttempt> builder)
    {
        builder.ToTable("CheckoutAttempts", table =>
        {
            table.HasCheckConstraint(
                "CK_CheckoutAttempts_Status_Valid",
                "[Status] IN ('InProgress', 'Completed')");
        });

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .ValueGeneratedNever();

        builder.Property(attempt => attempt.UserId)
            .IsRequired();

        builder.Property(attempt => attempt.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(attempt => attempt.RequestFingerprint)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(attempt => attempt.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(attempt => attempt.OrderId)
            .IsRequired(false);

        builder.Property(attempt => attempt.ResponseStatusCode)
            .IsRequired(false);

        builder.Property(attempt => attempt.ResponsePayload)
            .IsRequired(false);

        builder.Property(attempt => attempt.LeaseExpiresAtUtc)
            .IsRequired();

        builder.Property(attempt => attempt.CreatedAtUtc)
            .IsRequired();

        builder.Property(attempt => attempt.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(attempt => new { attempt.UserId, attempt.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(attempt => attempt.LeaseExpiresAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(attempt => attempt.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(attempt => attempt.OrderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
