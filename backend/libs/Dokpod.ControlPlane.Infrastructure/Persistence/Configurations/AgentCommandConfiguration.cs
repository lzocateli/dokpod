using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dokpod.ControlPlane.Infrastructure.Persistence.Configurations;

public sealed class AgentCommandConfiguration : IEntityTypeConfiguration<AgentCommandEntity>
{
    public void Configure(EntityTypeBuilder<AgentCommandEntity> entity)
    {
        entity.ToTable("agent_commands");
        entity.HasKey(command => new { command.EnvironmentId, command.CommandId, command.CreatedAtUtc });
        entity.Property(command => command.EnvironmentId).HasColumnName("environment_id");
        entity.Property(command => command.CommandId).HasColumnName("command_id");
        entity.Property(command => command.Kind).HasColumnName("kind").HasConversion<short>();
        entity.Property(command => command.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
        entity.Property(command => command.ExpectedContainerRevision).HasColumnName("expected_container_revision").HasMaxLength(255).IsRequired();
        entity.Property(command => command.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        entity.Property(command => command.DeadlineUtc).HasColumnName("deadline_utc").IsRequired();
        entity.Property(command => command.FencingToken).HasColumnName("fencing_token").IsRequired();
        entity.Property(command => command.LastDispatchFencingToken).HasColumnName("last_dispatch_fencing_token");
        entity.Property(command => command.State).HasColumnName("state").HasConversion<short>();
        entity.Property(command => command.FailureCode).HasColumnName("failure_code").HasMaxLength(128);
        entity.Property(command => command.ObservedContainerRevision).HasColumnName("observed_container_revision").HasMaxLength(255);
        entity.Property(command => command.CompletedAtUtc).HasColumnName("completed_at_utc");
        entity.Property(command => command.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(command => command.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        entity.HasOne<EnvironmentRegistrationEntity>()
            .WithMany()
            .HasForeignKey(command => command.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(command => new { command.EnvironmentId, command.State, command.CreatedAtUtc });
        entity.HasIndex(command => new { command.State, command.DeadlineUtc });
    }
}