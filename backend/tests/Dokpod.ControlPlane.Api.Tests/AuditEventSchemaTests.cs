using Dokpod.ControlPlane.Infrastructure;
using Dokpod.ControlPlane.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class AuditEventSchemaTests
{
    [Fact]
    public void MigrationSqlScriptLoader_LoadsEmbeddedPostgreSqlAndRejectsMissingResource()
    {
        var script = MigrationSqlScriptLoader.Load(
            "202609220001_PartitionAgentCommands/Up/01-PartitionAgentCommands.sql");

        Assert.Contains("dokpod_reserve_agent_command_key", script, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            MigrationSqlScriptLoader.Load("missing/Up/01-Missing.sql"));
    }

    [Fact]
    public void ControlPlaneDbContext_MapsAgentCommandPartitionKey()
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new ControlPlaneDbContext(options);

        var entity = context.Model.FindEntityType(typeof(AgentCommandEntity));
        Assert.NotNull(entity);
        Assert.Equal("agent_commands", entity!.GetTableName());
        Assert.Equal(
            [
                nameof(AgentCommandEntity.EnvironmentId),
                nameof(AgentCommandEntity.CommandId),
                nameof(AgentCommandEntity.CreatedAtUtc),
            ],
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void ControlPlaneDbContext_MapsAuditEventTableWithAppendOnlyConstraints()
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new ControlPlaneDbContext(options);

        var entity = context.Model.FindEntityType(typeof(AuditEventEntity));
        Assert.NotNull(entity);
        Assert.Equal("audit_events", entity!.GetTableName());
        Assert.NotNull(entity.FindPrimaryKey());

        var eventIdProperty = entity.FindProperty(nameof(AuditEventEntity.EventId));
        var environmentIdProperty = entity.FindProperty(nameof(AuditEventEntity.EnvironmentId));
        var commandIdProperty = entity.FindProperty(nameof(AuditEventEntity.CommandId));
        var occurredAtUtcProperty = entity.FindProperty(nameof(AuditEventEntity.OccurredAtUtc));
        var actorKindProperty = entity.FindProperty(nameof(AuditEventEntity.ActorKind));
        var actorIdProperty = entity.FindProperty(nameof(AuditEventEntity.ActorId));
        var actionProperty = entity.FindProperty(nameof(AuditEventEntity.Action));
        var outcomeProperty = entity.FindProperty(nameof(AuditEventEntity.Outcome));

        Assert.NotNull(eventIdProperty);
        Assert.NotNull(environmentIdProperty);
        Assert.NotNull(commandIdProperty);
        Assert.NotNull(occurredAtUtcProperty);
        Assert.NotNull(actorKindProperty);
        Assert.NotNull(actorIdProperty);
        Assert.NotNull(actionProperty);
        Assert.NotNull(outcomeProperty);

        Assert.Equal(ValueGenerated.Never, eventIdProperty!.ValueGenerated);
        Assert.False(environmentIdProperty!.IsNullable);
        Assert.True(commandIdProperty!.IsNullable);
        Assert.False(occurredAtUtcProperty!.IsNullable);
        Assert.False(actorKindProperty!.IsNullable);
        Assert.Equal(128, actorIdProperty!.GetMaxLength());
        Assert.Equal(128, actionProperty!.GetMaxLength());
        Assert.False(outcomeProperty!.IsNullable);
        Assert.Equal(128, entity.FindProperty(nameof(AuditEventEntity.FailureCode))!.GetMaxLength());
    }
}
