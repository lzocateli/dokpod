using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dokpod.ControlPlane.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventEntity> entity)
    {
        entity.ToTable("audit_events");
        entity.HasKey(x => new { x.EventId, x.OccurredAtUtc });

        entity.Property(x => x.EventId)
            .HasColumnName("event_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        entity.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasColumnType("uuid")
            .IsRequired();

        entity.Property(x => x.EnvironmentId)
            .HasColumnName("environment_id")
            .HasColumnType("uuid")
            .IsRequired();

        entity.Property(x => x.CommandId)
            .HasColumnName("command_id")
            .HasColumnType("uuid");

        entity.Property(x => x.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        entity.Property(x => x.ActorKind)
            .HasColumnName("actor_kind")
            .HasConversion<short>()
            .HasColumnType("smallint")
            .IsRequired();

        entity.Property(x => x.ActorId)
            .HasColumnName("actor_id")
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Action)
            .HasColumnName("action")
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Outcome)
            .HasColumnName("outcome")
            .HasConversion<short>()
            .HasColumnType("smallint")
            .IsRequired();

        entity.Property(x => x.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(128);

        entity.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("ix_audit_events_correlation_id");

        entity.HasIndex(x => new { x.EnvironmentId, x.OccurredAtUtc })
            .HasDatabaseName("ix_audit_events_environment_time");

        entity.HasIndex(x => new { x.EnvironmentId, x.CommandId, x.OccurredAtUtc })
            .HasDatabaseName("ix_audit_events_environment_command_time");
    }
}