using Dokpod.ControlPlane.Application.Environments;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Application.Auditing;
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
    public async Task AuditAppendFunction_PreservesPreviousAndCurrentSignatures()
    {
        await using var context = CreateContextOrSkip();

        var argumentCounts = await context.Database
            .SqlQueryRaw<int>("""
                SELECT procedure.pronargs::int AS "Value"
                FROM pg_proc procedure
                JOIN pg_namespace namespace ON namespace.oid = procedure.pronamespace
                WHERE namespace.nspname = 'dokpod'
                  AND procedure.proname = 'dokpod_append_audit_event'
                ORDER BY procedure.pronargs
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([10, 11], argumentCounts);
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
        var firstPage = await store.ListAsync(null, 1, TestContext.Current.CancellationToken);
        var secondPage = firstPage.NextCursor is null
            ? new EnvironmentRegistrationPage([], null)
            : await store.ListAsync(firstPage.NextCursor, 100, TestContext.Current.CancellationToken);
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
        Assert.DoesNotContain(
            secondPage.Registrations,
            item => item.EnvironmentId == firstPage.Registrations[0].EnvironmentId);
        Assert.Contains(
            firstPage.Registrations.Concat(secondPage.Registrations),
            item => item.EnvironmentId == registration.EnvironmentId);
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
            var replayWithRenewedFencing = await new PostgresAgentCommandStore(secondContext)
                .EnqueueAsync(
                    command with { Command = command.Command with { FencingToken = 9 } },
                    TestContext.Current.CancellationToken);
            var snapshot = await new PostgresAgentCommandStore(secondContext)
                .GetAsync(environmentId, commandId, TestContext.Current.CancellationToken);
            var persisted = await setupContext.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(1, outcomes.Count(outcome => outcome == AgentCommandEnqueueResult.Created));
            Assert.Equal(1, outcomes.Count(outcome => outcome == AgentCommandEnqueueResult.Duplicate));
            Assert.Equal(AgentCommandEnqueueResult.ConflictingPayload, conflict);
            Assert.Equal(AgentCommandEnqueueResult.ConflictingPayload, envelopeConflict);
            Assert.Equal(AgentCommandEnqueueResult.Duplicate, replayWithRenewedFencing);
            Assert.Equal(new string('A', 64), persisted.PayloadHash);
            Assert.Equal(8, persisted.FencingToken);
            Assert.NotNull(snapshot);
            Assert.Equal(ControlPlaneCommandState.Pending, snapshot.State);
            Assert.Equal(AgentCommandKind.RestartContainer, snapshot.Kind);
            Assert.Null(snapshot.CompletedAtUtc);
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
    public async Task AgentCommandStore_StatusProgressionIsMonotonicAndTerminalReplayIsIdempotent()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var registration = EnvironmentRegistration.Create(
            environmentId,
            "command-status-" + Guid.NewGuid().ToString("N")[..12],
            "lab-host",
            enabled: true,
            [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);
        await new PostgresEnvironmentRegistrationStore(context)
            .CreateAsync(registration, TestContext.Current.CancellationToken);

        var createdAt = new DateTimeOffset(2026, 9, 21, 19, 0, 0, TimeSpan.Zero);
        var command = new PersistedAgentCommand(
            new AgentCommand(
                environmentId,
                commandId,
                AgentCommandKind.RestartContainer,
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "revision-01",
                new string('A', 64),
                createdAt.AddMinutes(1),
                9),
            ControlPlaneCommandState.Pending,
            createdAt,
            createdAt);
        var store = new PostgresAgentCommandStore(context);

        try
        {
            Assert.Equal(
                AgentCommandEnqueueResult.Created,
                await store.EnqueueAsync(command, TestContext.Current.CancellationToken));

            var dispatched = new AgentCommandStatusUpdate(
                environmentId,
                commandId,
                ControlPlaneCommandState.Dispatched,
                null,
                null,
                createdAt.AddSeconds(1));
            var accepted = dispatched with
            {
                State = ControlPlaneCommandState.Accepted,
                UpdatedAtUtc = createdAt.AddSeconds(2),
            };
            var succeeded = accepted with
            {
                State = ControlPlaneCommandState.Succeeded,
                ObservedContainerRevision = "revision-02",
                UpdatedAtUtc = createdAt.AddSeconds(3),
            };

            Assert.Equal(
                AgentCommandStatusUpdateResult.Applied,
                await store.ApplyStatusAsync(dispatched, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.Applied,
                await store.ApplyStatusAsync(accepted, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.Applied,
                await store.ApplyStatusAsync(succeeded, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.Duplicate,
                await store.ApplyStatusAsync(succeeded, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.InvalidTransition,
                await store.ApplyStatusAsync(
                    succeeded with
                    {
                        State = ControlPlaneCommandState.Failed,
                        FailureCode = "ENGINE_FAILURE",
                    },
                    TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.InvalidTransition,
                await store.ApplyStatusAsync(accepted, TestContext.Current.CancellationToken));

            var persisted = await context.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);
            Assert.Equal(ControlPlaneCommandState.Succeeded, persisted.State);
            Assert.Equal("revision-02", persisted.ObservedContainerRevision);
            Assert.Equal(succeeded.UpdatedAtUtc, persisted.CompletedAtUtc);
        }
        finally
        {
            await context.AgentCommands
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await context.EnvironmentRegistrations
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AgentCommandStore_AuditedLifecyclePersistsIntentAndResultOnce()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await CreateEnvironmentAsync(context, environmentId, "command-audit");
        var createdAt = new DateTimeOffset(2026, 9, 21, 19, 30, 0, TimeSpan.Zero);
        var command = CreateCommand(environmentId, commandId, createdAt);
        var intent = CreateCommandAuditEvent(
            environmentId,
            commandId,
            createdAt,
            "container.restart",
            AuditOutcome.Succeeded);
        var completedAt = createdAt.AddSeconds(1);
        var update = new AgentCommandStatusUpdate(
            environmentId,
            commandId,
            ControlPlaneCommandState.Succeeded,
            null,
            "revision-02",
            completedAt);
        var result = CreateCommandAuditEvent(
            environmentId,
            commandId,
            completedAt,
            "container.command.result",
            AuditOutcome.Succeeded);
        var store = new PostgresAgentCommandStore(context);

        try
        {
            Assert.Equal(
                AgentCommandEnqueueResult.Created,
                await store.EnqueueAuditedAsync(command, intent, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandEnqueueResult.Duplicate,
                await store.EnqueueAuditedAsync(command, intent, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.Applied,
                await store.ApplyStatusAuditedAsync(update, result, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.Duplicate,
                await store.ApplyStatusAuditedAsync(update, result, TestContext.Current.CancellationToken));

            var persisted = await context.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);
            var auditEvents = await context.AuditEvents
                .AsNoTracking()
                .Where(item => item.CommandId == commandId)
                .OrderBy(item => item.OccurredAtUtc)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ControlPlaneCommandState.Succeeded, persisted.State);
            Assert.Equal("revision-02", persisted.ObservedContainerRevision);
            Assert.Equal(2, auditEvents.Count);
            Assert.Equal("container.restart", auditEvents[0].Action);
            Assert.Equal("container.command.result", auditEvents[1].Action);
        }
        finally
        {
            await DeleteCommandEnvironmentAsync(context, environmentId);
        }
    }

    [Fact]
    public async Task AgentCommandStore_WhenIntentAuditFails_RollsBackCommand()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await CreateEnvironmentAsync(context, environmentId, "command-intent-rollback");
        var createdAt = new DateTimeOffset(2026, 9, 21, 19, 40, 0, TimeSpan.Zero);
        var command = CreateCommand(environmentId, commandId, createdAt);
        var auditEvent = CreateCommandAuditEvent(
            environmentId,
            commandId,
            createdAt,
            "container.restart",
            AuditOutcome.Succeeded);
        var store = new PostgresAgentCommandStore(context, new ThrowingAuditEventWriter());

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.EnqueueAuditedAsync(command, auditEvent, TestContext.Current.CancellationToken));

            Assert.False(await context.AgentCommands
                .AsNoTracking()
                .AnyAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            await DeleteCommandEnvironmentAsync(context, environmentId);
        }
    }

    [Fact]
    public async Task AgentCommandStore_WhenAuditDoesNotMatchMutation_RejectsBeforePersistence()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await CreateEnvironmentAsync(context, environmentId, "command-audit-mismatch");
        var createdAt = new DateTimeOffset(2026, 9, 21, 19, 45, 0, TimeSpan.Zero);
        var command = CreateCommand(environmentId, commandId, createdAt);
        var mismatchedAuditEvent = CreateCommandAuditEvent(
            environmentId,
            Guid.NewGuid(),
            createdAt,
            "container.restart",
            AuditOutcome.Succeeded);
        var store = new PostgresAgentCommandStore(context);

        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                store.EnqueueAuditedAsync(
                    command,
                    mismatchedAuditEvent,
                    TestContext.Current.CancellationToken));

            Assert.Equal("auditEvent", exception.ParamName);
            Assert.False(await context.AgentCommands
                .AsNoTracking()
                .AnyAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            await DeleteCommandEnvironmentAsync(context, environmentId);
        }
    }

    [Fact]
    public async Task AgentCommandStore_WhenResultAuditFails_RollsBackTerminalStatus()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await CreateEnvironmentAsync(context, environmentId, "command-result-rollback");
        var createdAt = new DateTimeOffset(2026, 9, 21, 19, 50, 0, TimeSpan.Zero);
        var command = CreateCommand(environmentId, commandId, createdAt);
        var setupStore = new PostgresAgentCommandStore(context);

        try
        {
            Assert.Equal(
                AgentCommandEnqueueResult.Created,
                await setupStore.EnqueueAsync(command, TestContext.Current.CancellationToken));
            var update = new AgentCommandStatusUpdate(
                environmentId,
                commandId,
                ControlPlaneCommandState.Succeeded,
                null,
                "revision-02",
                createdAt.AddSeconds(1));
            var auditEvent = CreateCommandAuditEvent(
                environmentId,
                commandId,
                update.UpdatedAtUtc,
                "container.command.result",
                AuditOutcome.Succeeded);
            var store = new PostgresAgentCommandStore(context, new ThrowingAuditEventWriter());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ApplyStatusAuditedAsync(update, auditEvent, TestContext.Current.CancellationToken));

            var persisted = await context.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);
            Assert.Equal(ControlPlaneCommandState.Pending, persisted.State);
            Assert.Null(persisted.CompletedAtUtc);
            Assert.Null(persisted.ObservedContainerRevision);
        }
        finally
        {
            await DeleteCommandEnvironmentAsync(context, environmentId);
        }
    }

    [Fact]
    public async Task AgentCommandStore_WhenExpirationAuditFails_RollsBackExpiration()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await CreateEnvironmentAsync(context, environmentId, "command-expiration-rollback");
        var createdAt = new DateTimeOffset(2026, 9, 21, 19, 55, 0, TimeSpan.Zero);
        var command = CreateCommand(environmentId, commandId, createdAt);
        var setupStore = new PostgresAgentCommandStore(context);

        try
        {
            Assert.Equal(
                AgentCommandEnqueueResult.Created,
                await setupStore.EnqueueAsync(command, TestContext.Current.CancellationToken));
            var store = new PostgresAgentCommandStore(context, new ThrowingAuditEventWriter());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ExpireNonTerminalAsync(command.Command.DeadlineUtc, TestContext.Current.CancellationToken));

            var persisted = await context.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);
            Assert.Equal(ControlPlaneCommandState.Pending, persisted.State);
            Assert.Null(persisted.FailureCode);
            Assert.Null(persisted.CompletedAtUtc);
        }
        finally
        {
            await DeleteCommandEnvironmentAsync(context, environmentId);
        }
    }

    [Fact]
    public async Task AgentCommandStore_WhenExpiredCommandReportsLateResult_ReconcilesTerminalState()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await CreateEnvironmentAsync(context, environmentId, "command-late-result");
        var createdAt = new DateTimeOffset(2026, 9, 21, 19, 58, 0, TimeSpan.Zero);
        var command = CreateCommand(environmentId, commandId, createdAt);
        var store = new PostgresAgentCommandStore(context);

        try
        {
            Assert.Equal(
                AgentCommandEnqueueResult.Created,
                await store.EnqueueAsync(command, TestContext.Current.CancellationToken));
            Assert.Equal(
                AgentCommandStatusUpdateResult.Applied,
                await store.ApplyStatusAsync(
                    new AgentCommandStatusUpdate(
                        environmentId,
                        commandId,
                        ControlPlaneCommandState.Accepted,
                        null,
                        null,
                        createdAt.AddSeconds(1)),
                    TestContext.Current.CancellationToken));
            Assert.Equal(
                1,
                await store.ExpireNonTerminalAsync(
                    command.Command.DeadlineUtc,
                    TestContext.Current.CancellationToken));
            var lateResult = new AgentCommandStatusUpdate(
                environmentId,
                commandId,
                ControlPlaneCommandState.Succeeded,
                null,
                "revision-02",
                command.Command.DeadlineUtc.AddSeconds(-1));
            var resultAudit = CreateCommandAuditEvent(
                environmentId,
                commandId,
                lateResult.UpdatedAtUtc,
                "container.command.result",
                AuditOutcome.Succeeded);

            Assert.Equal(
                AgentCommandStatusUpdateResult.Applied,
                await store.ApplyStatusAuditedAsync(
                    lateResult,
                    resultAudit,
                    TestContext.Current.CancellationToken));

            var persisted = await context.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);
            var outcomes = await context.AuditEvents
                .AsNoTracking()
                .Where(auditEvent =>
                    auditEvent.CommandId == commandId &&
                    auditEvent.Action == "container.command.result")
                .Select(auditEvent => auditEvent.Outcome)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ControlPlaneCommandState.Succeeded, persisted.State);
            Assert.Equal("revision-02", persisted.ObservedContainerRevision);
            Assert.Equal(lateResult.UpdatedAtUtc, persisted.CompletedAtUtc);
            Assert.Equal(command.Command.DeadlineUtc, persisted.UpdatedAtUtc);
            Assert.Equal(2, outcomes.Count);
            Assert.Contains((int)AuditOutcome.Indeterminate, outcomes);
            Assert.Contains((int)AuditOutcome.Succeeded, outcomes);
        }
        finally
        {
            await DeleteCommandEnvironmentAsync(context, environmentId);
        }
    }

    [Fact]
    public async Task AgentCommandStore_ClaimDispatchableRenewsDispatchFencingWithoutChangingOriginal()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var registration = EnvironmentRegistration.Create(
            environmentId,
            "command-claim-" + Guid.NewGuid().ToString("N")[..12],
            "lab-host",
            enabled: true,
            [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);
        await new PostgresEnvironmentRegistrationStore(context)
            .CreateAsync(registration, TestContext.Current.CancellationToken);

        var createdAt = new DateTimeOffset(2026, 9, 21, 20, 0, 0, TimeSpan.Zero);
        const long originalFencingToken = 10;
        const long reconnectedFencingToken = 11;
        var command = new PersistedAgentCommand(
            new AgentCommand(
                environmentId,
                commandId,
                AgentCommandKind.RestartContainer,
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "revision-01",
                new string('A', 64),
                createdAt.AddMinutes(5),
                originalFencingToken),
            ControlPlaneCommandState.Pending,
            createdAt,
            createdAt);
        var store = new PostgresAgentCommandStore(context);

        try
        {
            Assert.Equal(
                AgentCommandEnqueueResult.Created,
                await store.EnqueueAsync(command, TestContext.Current.CancellationToken));

            await using var firstClaimContext = CreateContextOrSkip();
            await using var secondClaimContext = CreateContextOrSkip();
            var concurrentClaims = await Task.WhenAll(
                new PostgresAgentCommandStore(firstClaimContext).ClaimDispatchableAsync(
                    environmentId,
                    originalFencingToken,
                    createdAt,
                    TestContext.Current.CancellationToken),
                new PostgresAgentCommandStore(secondClaimContext).ClaimDispatchableAsync(
                    environmentId,
                    originalFencingToken,
                    createdAt,
                    TestContext.Current.CancellationToken));
            var firstClaim = Assert.Single(concurrentClaims, claim => claim.Count == 1);
            Assert.Equal(1, concurrentClaims.Sum(claim => claim.Count));
            var repeatedClaim = await store.ClaimDispatchableAsync(
                environmentId,
                originalFencingToken,
                createdAt.AddSeconds(1),
                TestContext.Current.CancellationToken);
            Assert.Equal(
                AgentCommandStatusUpdateResult.Applied,
                await store.ApplyStatusAsync(
                    new AgentCommandStatusUpdate(
                        environmentId,
                        commandId,
                        ControlPlaneCommandState.Accepted,
                        null,
                        null,
                        createdAt.AddSeconds(2)),
                    TestContext.Current.CancellationToken));
            var reconnectedClaim = await store.ClaimDispatchableAsync(
                environmentId,
                reconnectedFencingToken,
                createdAt.AddSeconds(3),
                TestContext.Current.CancellationToken);
            var persisted = await context.AgentCommands
                .AsNoTracking()
                .SingleAsync(
                    item => item.EnvironmentId == environmentId && item.CommandId == commandId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(originalFencingToken, Assert.Single(firstClaim).Command.FencingToken);
            Assert.Empty(repeatedClaim);
            Assert.Equal(reconnectedFencingToken, Assert.Single(reconnectedClaim).Command.FencingToken);
            Assert.Equal(ControlPlaneCommandState.Accepted, reconnectedClaim[0].State);
            Assert.Equal(originalFencingToken, persisted.FencingToken);
            Assert.Equal(reconnectedFencingToken, persisted.LastDispatchFencingToken);
        }
        finally
        {
            await context.AgentCommands
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await context.EnvironmentRegistrations
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AgentCommandStore_ClaimDispatchableTerminallyExpiresOverdueCommands()
    {
        await using var context = CreateContextOrSkip();
        var environmentId = Guid.NewGuid();
        var registration = EnvironmentRegistration.Create(
            environmentId,
            "command-expiration-" + Guid.NewGuid().ToString("N")[..12],
            "lab-host",
            enabled: true,
            [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);
        await new PostgresEnvironmentRegistrationStore(context)
            .CreateAsync(registration, TestContext.Current.CancellationToken);

        var createdAt = new DateTimeOffset(2026, 9, 21, 21, 0, 0, TimeSpan.Zero);
        var deadline = createdAt.AddMinutes(1);
        var store = new PostgresAgentCommandStore(context);
        var pendingId = Guid.NewGuid();
        var dispatchedId = Guid.NewGuid();
        var acceptedId = Guid.NewGuid();
        var succeededId = Guid.NewGuid();
        var futureId = Guid.NewGuid();

        try
        {
            foreach (var commandId in new[] { pendingId, dispatchedId, acceptedId, succeededId, futureId })
            {
                var commandDeadline = commandId == futureId ? deadline.AddMinutes(1) : deadline;
                await store.EnqueueAsync(
                    new PersistedAgentCommand(
                        new AgentCommand(
                            environmentId,
                            commandId,
                            AgentCommandKind.RestartContainer,
                            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                            "revision-01",
                            new string('A', 64),
                            commandDeadline,
                            10),
                        ControlPlaneCommandState.Pending,
                        createdAt,
                        createdAt),
                    TestContext.Current.CancellationToken);
            }

            await store.ApplyStatusAsync(
                new AgentCommandStatusUpdate(
                    environmentId,
                    dispatchedId,
                    ControlPlaneCommandState.Dispatched,
                    null,
                    null,
                    createdAt.AddSeconds(10)),
                TestContext.Current.CancellationToken);
            await store.ApplyStatusAsync(
                new AgentCommandStatusUpdate(
                    environmentId,
                    acceptedId,
                    ControlPlaneCommandState.Accepted,
                    null,
                    null,
                    createdAt.AddSeconds(20)),
                TestContext.Current.CancellationToken);
            await store.ApplyStatusAsync(
                new AgentCommandStatusUpdate(
                    environmentId,
                    succeededId,
                    ControlPlaneCommandState.Succeeded,
                    null,
                    "revision-02",
                    createdAt.AddSeconds(30)),
                TestContext.Current.CancellationToken);

            var dispatchable = await store.ClaimDispatchableAsync(
                environmentId,
                11,
                deadline,
                TestContext.Current.CancellationToken);
            var repeatedExpiration = await store.ExpireNonTerminalAsync(
                deadline.AddSeconds(1),
                TestContext.Current.CancellationToken);
            var persisted = await context.AgentCommands
                .AsNoTracking()
                .Where(command => command.EnvironmentId == environmentId)
                .ToDictionaryAsync(command => command.CommandId, TestContext.Current.CancellationToken);
            var expirationEventRows = await context.AuditEvents
                .AsNoTracking()
                .Where(auditEvent =>
                    auditEvent.EnvironmentId == environmentId &&
                    auditEvent.CommandId != null &&
                    auditEvent.Action == "container.command.result" &&
                    auditEvent.FailureCode == "expired_command")
                .ToListAsync(TestContext.Current.CancellationToken);
            var expirationEvents = expirationEventRows.ToDictionary(auditEvent => auditEvent.CommandId!.Value);

            Assert.Equal(futureId, Assert.Single(dispatchable).Command.CommandId);
            Assert.Equal(0, repeatedExpiration);
            Assert.Equal(ControlPlaneCommandState.Failed, persisted[pendingId].State);
            Assert.Equal(ControlPlaneCommandState.Indeterminate, persisted[dispatchedId].State);
            Assert.Equal(ControlPlaneCommandState.Indeterminate, persisted[acceptedId].State);
            Assert.Equal(ControlPlaneCommandState.Succeeded, persisted[succeededId].State);
            Assert.Equal(ControlPlaneCommandState.Pending, persisted[futureId].State);
            Assert.Equal("expired_command", persisted[pendingId].FailureCode);
            Assert.Equal("expired_command", persisted[dispatchedId].FailureCode);
            Assert.Equal("expired_command", persisted[acceptedId].FailureCode);
            Assert.Equal(deadline, persisted[pendingId].CompletedAtUtc);
            Assert.Equal(deadline, persisted[dispatchedId].CompletedAtUtc);
            Assert.Equal(deadline, persisted[acceptedId].CompletedAtUtc);
            Assert.Equal(3, expirationEvents.Count);
            Assert.Equal((int)AuditOutcome.Failed, expirationEvents[pendingId].Outcome);
            Assert.Equal((int)AuditOutcome.Indeterminate, expirationEvents[dispatchedId].Outcome);
            Assert.Equal((int)AuditOutcome.Indeterminate, expirationEvents[acceptedId].Outcome);
        }
        finally
        {
            await context.AgentCommands
                .Where(item => item.EnvironmentId == environmentId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await context.EnvironmentRegistrations
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

    private static async Task CreateEnvironmentAsync(
        ControlPlaneDbContext context,
        Guid environmentId,
        string namePrefix)
    {
        var registration = EnvironmentRegistration.Create(
            environmentId,
            namePrefix + "-" + Guid.NewGuid().ToString("N")[..12],
            "lab-host",
            enabled: true,
            [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);
        await new PostgresEnvironmentRegistrationStore(context)
            .CreateAsync(registration, TestContext.Current.CancellationToken);
    }

    private static PersistedAgentCommand CreateCommand(
        Guid environmentId,
        Guid commandId,
        DateTimeOffset createdAt) =>
        new(
            new AgentCommand(
                environmentId,
                commandId,
                AgentCommandKind.RestartContainer,
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "revision-01",
                new string('A', 64),
                createdAt.AddMinutes(1),
                9),
            ControlPlaneCommandState.Pending,
            createdAt,
            createdAt);

    private static AuditEvent CreateCommandAuditEvent(
        Guid environmentId,
        Guid commandId,
        DateTimeOffset occurredAt,
        string action,
        AuditOutcome outcome) =>
        AuditEvent.Create(
            Guid.NewGuid(),
            occurredAt,
            Guid.NewGuid(),
            action == "container.command.result" ? AuditActorKind.Agent : AuditActorKind.User,
            action == "container.command.result" ? $"agent:{environmentId:D}" : "integration-test",
            action,
            environmentId,
            outcome,
            commandId: commandId);

    private static async Task DeleteCommandEnvironmentAsync(
        ControlPlaneDbContext context,
        Guid environmentId)
    {
        await context.AgentCommands
            .Where(item => item.EnvironmentId == environmentId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await context.EnvironmentRegistrations
            .Where(item => item.EnvironmentId == environmentId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
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

    private sealed class ThrowingAuditEventWriter : IAuditEventWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic audit persistence failure.");
    }
}