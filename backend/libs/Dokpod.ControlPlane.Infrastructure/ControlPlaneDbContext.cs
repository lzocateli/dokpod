using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options)
    : DbContext(options)
{
    public DbSet<AgentIdentityEntity> AgentIdentities => Set<AgentIdentityEntity>();
    public DbSet<AuditEventKeyEntity> AuditEventKeys => Set<AuditEventKeyEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<AgentCommandEntity> AgentCommands => Set<AgentCommandEntity>();
    public DbSet<EnvironmentRegistrationEntity> EnvironmentRegistrations => Set<EnvironmentRegistrationEntity>();
    public DbSet<InventoryProjectionEntity> InventoryProjections => Set<InventoryProjectionEntity>();
    public DbSet<InventoryContainerEntity> InventoryContainers => Set<InventoryContainerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ControlPlaneSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ControlPlaneDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
