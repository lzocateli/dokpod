using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dokpod.ControlPlane.Infrastructure.Persistence.Configurations;

public sealed class EnvironmentRegistrationConfiguration : IEntityTypeConfiguration<EnvironmentRegistrationEntity>
{
    public void Configure(EntityTypeBuilder<EnvironmentRegistrationEntity> entity)
    {
        entity.ToTable("environments");
        entity.HasKey(environment => environment.EnvironmentId);
        entity.Property(environment => environment.EnvironmentId).HasColumnName("environment_id");
        entity.Property(environment => environment.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        entity.Property(environment => environment.Host).HasColumnName("host").HasMaxLength(255).IsRequired();
        entity.Property(environment => environment.Enabled).HasColumnName("enabled").IsRequired();
        entity.Property(environment => environment.Scopes).HasColumnName("scopes").HasMaxLength(2048).IsRequired();
        entity.Property(environment => environment.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(environment => environment.Name).IsUnique();
    }
}
