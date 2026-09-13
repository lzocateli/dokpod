using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dokpod.ControlPlane.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventKeyConfiguration : IEntityTypeConfiguration<AuditEventKeyEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventKeyEntity> entity)
    {
        entity.ToTable("audit_event_keys");
        entity.HasKey(x => x.EventId);

        entity.Property(x => x.EventId)
            .HasColumnName("event_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        entity.Property(x => x.PayloadHash)
            .HasColumnName("payload_hash")
            .HasColumnType("bytea")
            .IsRequired();
    }
}