using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .ValueGeneratedNever();

        builder.Property(message => message.EventType)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(message => message.Payload)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(message => message.CreatedAtUtc)
            .IsRequired();

        builder.Property(message => message.ProcessedAtUtc)
            .IsRequired(false);

        builder.Property(message => message.LastAttemptAtUtc)
            .IsRequired(false);

        builder.Property(message => message.Attempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(message => message.LastError)
            .IsRequired(false)
            .HasMaxLength(2000);

        builder.HasIndex(message => new { message.ProcessedAtUtc, message.CreatedAtUtc });
    }
}
