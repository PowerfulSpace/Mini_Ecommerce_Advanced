using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class ProcessedStripeEventConfiguration : IEntityTypeConfiguration<ProcessedStripeEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedStripeEvent> builder)
    {
        builder.ToTable("ProcessedStripeEvents");

        builder.HasKey(processedEvent => processedEvent.Id);

        builder.Property(processedEvent => processedEvent.Id)
            .ValueGeneratedNever();

        builder.Property(processedEvent => processedEvent.EventId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(processedEvent => processedEvent.Type)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(processedEvent => processedEvent.ProcessedAtUtc)
            .IsRequired();

        builder.HasIndex(processedEvent => processedEvent.EventId)
            .IsUnique();

        builder.HasIndex(processedEvent => processedEvent.ProcessedAtUtc);
    }
}
