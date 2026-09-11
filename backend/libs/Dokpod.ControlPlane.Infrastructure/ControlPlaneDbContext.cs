using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options)
    : DbContext(options)
{
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.EventId);

            entity.Property(x => x.EventId)
                .HasColumnType("uuid")
                .ValueGeneratedNever();

            entity.Property(x => x.CorrelationId)
                .HasColumnType("uuid")
                .IsRequired();

            entity.Property(x => x.EnvironmentId)
                .HasColumnType("uuid")
                .IsRequired();

            entity.Property(x => x.OccurredAtUtc)
                .HasColumnType("timestamptz")
                .IsRequired();

            entity.Property(x => x.ActorKind)
                .HasConversion<int>()
                .HasColumnType("smallint")
                .IsRequired();

            entity.Property(x => x.ActorId)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Action)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Outcome)
                .HasConversion<int>()
                .HasColumnType("smallint")
                .IsRequired();

            entity.Property(x => x.FailureCode)
                .HasMaxLength(128);

            entity.HasIndex(x => x.CorrelationId)
                .HasDatabaseName("ix_audit_events_correlation_id");

            entity.HasIndex(x => new { x.EnvironmentId, x.OccurredAtUtc })
                .HasDatabaseName("ix_audit_events_environment_time");

            entity.HasIndex(x => x.EventId)
                .IsUnique()
                .HasDatabaseName("ux_audit_events_event_id");
        });

        base.OnModelCreating(modelBuilder);
    }
}
