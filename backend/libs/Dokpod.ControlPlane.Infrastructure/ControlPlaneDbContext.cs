using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options)
    : DbContext(options)
{
    public DbSet<AgentIdentityEntity> AgentIdentities => Set<AgentIdentityEntity>();
    public DbSet<AuditEventKeyEntity> AuditEventKeys => Set<AuditEventKeyEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<EnvironmentRegistrationEntity> EnvironmentRegistrations => Set<EnvironmentRegistrationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ControlPlaneDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
