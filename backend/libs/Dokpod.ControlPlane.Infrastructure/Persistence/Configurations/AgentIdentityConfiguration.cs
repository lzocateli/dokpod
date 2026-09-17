using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dokpod.ControlPlane.Infrastructure.Persistence.Configurations;

internal sealed class AgentIdentityConfiguration : IEntityTypeConfiguration<AgentIdentityEntity>
{
    public void Configure(EntityTypeBuilder<AgentIdentityEntity> entity)
    {
        entity.ToTable("agent_identities");
        entity.HasKey(identity => identity.CertificateFingerprint);
        entity.Property(identity => identity.CertificateFingerprint)
            .HasColumnName("certificate_fingerprint")
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(identity => identity.EnvironmentId)
            .HasColumnName("environment_id")
            .IsRequired();
        entity.Property(identity => identity.RevokedAtUtc)
            .HasColumnName("revoked_at_utc");
        entity.HasIndex(identity => identity.EnvironmentId);
    }
}