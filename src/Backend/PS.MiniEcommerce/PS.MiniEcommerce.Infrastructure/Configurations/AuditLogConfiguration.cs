using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(auditLog => auditLog.Id);

        builder.Property(auditLog => auditLog.Id)
            .ValueGeneratedNever();

        builder.Property(auditLog => auditLog.OccurredAtUtc)
            .IsRequired();

        builder.Property(auditLog => auditLog.ActorUserId)
            .IsRequired(false);

        builder.Property(auditLog => auditLog.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(auditLog => auditLog.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(auditLog => auditLog.EntityId)
            .IsRequired();

        builder.Property(auditLog => auditLog.Payload)
            .IsRequired(false)
            .HasColumnType("nvarchar(max)");

        builder.Property(auditLog => auditLog.CorrelationId)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.HasIndex(auditLog => auditLog.OccurredAtUtc);

        builder.HasIndex(auditLog => new { auditLog.EntityType, auditLog.EntityId });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(auditLog => auditLog.ActorUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
