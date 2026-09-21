using Dokpod.ControlPlane.Application.Environments;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Infrastructure;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Commands;
using Dokpod.Domain.Environments;
using Dokpod.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresAuditSchemaIntegrationTests
{
    [Fact]
    public async Task AuditEvents_RouteRowsToMonthlyAndDefaultPartitions()
    {
        await using var context = CreateContextOrSkip();
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var monthlyEventId = Guid.NewGuid();
        var defaultEventId = Guid.NewGuid();
        await InsertAsync(context, monthlyEventId, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));
        await InsertAsync(context, defaultEventId, new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var monthlyPartition = await GetPartitionAsync(context, monthlyEventId);
        var defaultPartition = await GetPartitionAsync(context, defaultEventId);

        Assert.Equal("dokpod.audit_events_2026_09", monthlyPartition);
        Assert.Equal("dokpod.audit_events_default", defaultPartition);
    }

    [Fact]
    public async Task AuditEvents_RejectMutation()
    {
        await using var context = CreateContextOrSkip();
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var eventId = Guid.NewGuid();
        await InsertAsync(context, eventId, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));
        const string changedActor = "changed";

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE dokpod.audit_events SET actor_id = {changedActor} WHERE event_id = {eventId}", TestContext.Current.CancellationToken));

        Assert.Contains("append-only", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writer_WithConcurrentSameEvent_IsIdempotent()
    {
        await using var firstContext = CreateContextOrSkip();
        await using var secondContext = CreateContextOrSkip();
        var auditEvent = CreateAuditEvent();
        var firstTask = new PostgresAuditEventWriter(firstContext)
            .AppendAsync(auditEvent, TestContext.Current.CancellationToken);
        var secondTask = new PostgresAuditEventWriter(secondContext)
            .AppendAsync(auditEvent, TestContext.Current.CancellationToken);

        await Task.WhenAll(firstTask, secondTask);

        var count = await firstContext.AuditEvents
            .CountAsync(x => x.EventId == auditEvent.EventId, TestContext.Current.CancellationToken);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Writer_WithConcurrentConflictingPayload_RejectsOneWriter()
    {
        await using var firstContext = CreateContextOrSkip();
        await using var secondContext = CreateContextOrSkip();
        var original = CreateAuditEvent();
        var conflicting = AuditEvent.Create(
            original.EventId,
            original.OccurredAtUtc,
            original.CorrelationId,
            original.ActorKind,
            original.ActorId,
            "environment.approve",
            original.EnvironmentId,
            original.Outcome,
            "AUTHZ_DENIED");
        var tasks = new[]
        {
            new PostgresAuditEventWriter(firstContext).AppendAsync(original, TestContext.Current.CancellationToken),
            new PostgresAuditEventWriter(secondContext).AppendAsync(conflicting, TestContext.Current.CancellationToken),
        };

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Equal(1, tasks.Count(task => task.IsCompletedSuccessfully));
        Assert.Contains(tasks, task => task.Exception?.GetBaseException() is InvalidOperationException);
    }

    [Fact]
    public async Task EnvironmentRegistrationStore_RoundTripsThroughDokpodSchema()
    {
        await using var context = CreateContextOrSkip();
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var store = new PostgresEnvironmentRegistrationStore(context);
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "integration-" + Guid.NewGuid().ToString("N")[..12],
            "lab-host",
            enabled: true,
            [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);

        var created = await store.CreateAsync(registration, TestContext.Current.CancellationToken);
        var persisted = await store.GetAsync(registration.EnvironmentId, TestContext.Current.CancellationToken);
        var schemaRows = await context.Database
            .SqlQueryRaw<string>("""
                SELECT namespace.nspname AS "Value"
                FROM dokpod.environments environments
                JOIN pg_class relation ON relation.oid = environments.tableoid
                JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE environments.environment_id = {0}
                """, registration.EnvironmentId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EnvironmentRegistrationStoreResult.Created, created);
        Assert.NotNull(persisted);
        Assert.Equal(registration.EnvironmentId, persisted.EnvironmentId);
        Assert.Equal(registration.Name, persisted.Name);
        Assert.Equal(registration.Host, persisted.Host);
        Assert.Equal(registration.Enabled, persisted.Enabled);
        Assert.True(registration.Scopes.SetEquals(persisted.Scopes));
        Assert.Equal("dokpod", Assert.Single(schemaRows));
    }

    [Fact]
    public async Task AgentCommandStore_ConcurrentReplayIsIdempotentAndConflictingHashIsRejected()
    {
        await using var setupContext = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var registration = EnvironmentRegistration.Create(
            environmentId,
            "command-test-" + Guid.NewGuid().ToString("N")[..12],
            "lab-host",
            enabled: true,
            [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);
        await new PostgresEnvironmentRegistrationStore(setupContext)
            .CreateAsync(registration, TestContext.Current.CancellationToken);

        var createdAt = new DateTimeOffset(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
        var command = new PersistedAgentCommand(
            new AgentCommand(
                environmentId,
                commandId,
                AgentCommandKind.RestartContainer,
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "revision-01",
                new string('A', 64),
                createdAt.AddMinutes(1),
                8),
            ControlPlaneCommandState.Pending,
            createdAt,
            createdAt);

        try
        {
            await using var firstContext = CreateContextOrSkip();
            await using var secondContext = CreateContextOrSkip();
            var outcomes = await Task.WhenAll(
                new PostgresAgentCommandStore(firstContext)
                    .EnqueueAsync(command, TestContext.Current.CancellationToken),
                new PostgresAgentCommandStore(secondContext)
                    .EnqueueAsync(command, TestContext.Current.CancellationToken));

            var conflict = await new PostgresAgentCommandStore(secondContext)
                .EnqueueAsync(
                    command with { Command = command.Command with { PayloadHash = new string('B', 64) } },
                    TestContext.Current.CancellationToken);
            var envelopeConflict = await new PostgresAgentCommandStore(secondContext)
                .EnqueueAsync(
                    command with { Command = command.Command with { Kind = AgentCommandKind.StopContainer } },
                    TestContext.Current.CancellationToken);
            var persisted = await setupContext.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(1, outcomes.Count(outcome => outcome == AgentCommandEnqueueResult.Created));
            Assert.Equal(1, outcomes.Count(outcome => outcome == AgentCommandEnqueueResult.Duplicate));
            Assert.Equal(AgentCommandEnqueueResult.ConflictingPayload, conflict);
            Assert.Equal(AgentCommandEnqueueResult.ConflictingPayload, envelopeConflict);
            Assert.Equal(new string('A', 64), persisted.PayloadHash);
        }
        finally
        {
            await setupContext.AgentCommands
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await setupContext.EnvironmentRegistrations
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task InventoryProjectionStore_AppliesDeltaAndPersistsCurrentState()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var observedAt = new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
        var store = new PostgresInventoryProjectionStore(context);
        var delta = new InventoryDelta(
            0,
            1,
            [
                new InventoryChange(
                    InventoryChangeKind.Added,
                    "container-1",
                    new ContainerInventory(
                        "container-1",
                        "api",
                        "dokpod/api:test",
                        "Running",
                        "revision-1",
                        observedAt)),
            ]);

        try
        {
            var result = await store.ApplyDeltaAsync(
                environmentId,
                delta,
                TestContext.Current.CancellationToken);
            var projection = await context.InventoryProjections
                .AsNoTracking()
                .Include(item => item.Containers)
                .SingleAsync(item => item.EnvironmentId == environmentId, TestContext.Current.CancellationToken);

            Assert.Equal(InventoryReconciliationOutcome.Accepted, result.Outcome);
            Assert.Equal(1, projection.Revision);
            var container = Assert.Single(projection.Containers);
            Assert.Equal("container-1", container.ContainerId);
            Assert.Equal("revision-1", container.Revision);
            Assert.Equal(observedAt, projection.ObservedAtUtc);
        }
        finally
        {
            await context.InventoryProjections
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task InventoryProjectionStore_ReplacesSnapshotAndReadsCursorPages()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var observedAt = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        var store = new PostgresInventoryProjectionStore(context);

        try
        {
            await store.ApplyDeltaAsync(
                environmentId,
                new InventoryDelta(
                    0,
                    1,
                    [new InventoryChange(
                        InventoryChangeKind.Added,
                        "container-1",
                        new ContainerInventory("container-1", "old", "fixture:old", "Stopped", "revision-1", observedAt))]),
                TestContext.Current.CancellationToken);

            var replacement = new InventorySnapshot(
                environmentId,
                2,
                new Dictionary<string, ContainerInventory>(StringComparer.Ordinal)
                {
                    ["container-1"] = new("container-1", "api", "fixture:new", "Running", "revision-2", observedAt.AddMinutes(1)),
                    ["container-2"] = new("container-2", "worker", "fixture:new", "Running", "revision-1", observedAt.AddMinutes(1)),
                });
            var result = await store.ReplaceSnapshotAsync(replacement, TestContext.Current.CancellationToken);
            var firstPage = await store.GetPageAsync(environmentId, null, 1, TestContext.Current.CancellationToken);
            var secondPage = await store.GetPageAsync(
                environmentId,
                firstPage!.NextContainerId,
                1,
                TestContext.Current.CancellationToken);

            Assert.Equal(InventoryReconciliationOutcome.Accepted, result.Outcome);
            Assert.Equal(2UL, firstPage.Revision);
            Assert.Equal("container-1", Assert.Single(firstPage.Containers).ContainerId);
            Assert.Equal("api", firstPage.Containers[0].Name);
            Assert.Equal("container-1", firstPage.NextContainerId);
            Assert.Equal("container-2", Assert.Single(secondPage!.Containers).ContainerId);
            Assert.Null(secondPage.NextContainerId);
        }
        finally
        {
            await context.InventoryProjections
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
    }

    private static ControlPlaneDbContext CreateContextOrSkip()
    {
        var connectionString = Environment.GetEnvironmentVariable("DOKPOD_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("DOKPOD_TEST_POSTGRES_CONNECTION is not configured.");
        }

        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ControlPlaneDbContext(options);
    }

    private static async Task InsertAsync(
        ControlPlaneDbContext context,
        Guid eventId,
        DateTimeOffset occurredAtUtc)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dokpod.audit_events (
                event_id,
                occurred_at_utc,
                correlation_id,
                actor_kind,
                actor_id,
                action,
                environment_id,
                outcome)
            VALUES (
                {eventId},
                {occurredAtUtc},
                {Guid.NewGuid()},
                {1},
                {"integration-test"},
                {"environment.register"},
                {Guid.NewGuid()},
                {1})
            """, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetPartitionAsync(ControlPlaneDbContext context, Guid eventId)
    {
        var rows = await context.Database
            .SqlQueryRaw<string>("""
                SELECT namespace.nspname || '.' || relation.relname AS "Value"
                FROM dokpod.audit_events audit_events
                JOIN pg_class relation ON relation.oid = audit_events.tableoid
                JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE audit_events.event_id = {0}
                """, eventId)
            .ToListAsync(TestContext.Current.CancellationToken);

        return Assert.Single(rows);
    }

    private static AuditEvent CreateAuditEvent()
    {
        return AuditEvent.Create(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            AuditActorKind.User,
            "integration-test",
            "environment.register",
            Guid.NewGuid(),
            AuditOutcome.Succeeded);
    }
}