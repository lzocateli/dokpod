using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Commands;
using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class PostgresAgentCommandStore(
    ControlPlaneDbContext dbContext,
    IAuditEventWriter auditEventWriter) : IAgentCommandStore
{
    private const int MaximumDispatchBatchSize = 100;
    private const int MaximumExpirationBatchSize = 100;

    public PostgresAgentCommandStore(ControlPlaneDbContext dbContext)
        : this(dbContext, new PostgresAuditEventWriter(dbContext))
    {
    }

    public Task<AgentCommandStatusSnapshot?> GetAsync(
        Guid environmentId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        if (environmentId == Guid.Empty || commandId == Guid.Empty)
        {
            throw new ArgumentException("Command identity is required.");
        }

        return dbContext.AgentCommands
            .AsNoTracking()
            .Where(command =>
                command.EnvironmentId == environmentId &&
                command.CommandId == commandId)
            .Select(command => new AgentCommandStatusSnapshot(
                command.EnvironmentId,
                command.CommandId,
                command.Kind,
                command.ContainerId,
                command.ExpectedContainerRevision,
                command.State,
                command.FailureCode,
                command.ObservedContainerRevision,
                command.DeadlineUtc,
                command.CreatedAtUtc,
                command.UpdatedAtUtc,
                command.CompletedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AgentCommandEnqueueResult> EnqueueAsync(
        PersistedAgentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var agentCommand = command.Command;
        var kind = (short)agentCommand.Kind;
        var state = (short)command.State;
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dokpod.agent_commands (
                environment_id,
                command_id,
                kind,
                container_id,
                expected_container_revision,
                payload_hash,
                deadline_utc,
                fencing_token,
                state,
                created_at_utc,
                updated_at_utc)
            VALUES (
                {agentCommand.EnvironmentId},
                {agentCommand.CommandId},
                {kind},
                {agentCommand.ContainerId},
                {agentCommand.ExpectedContainerRevision},
                {agentCommand.PayloadHash},
                {agentCommand.DeadlineUtc},
                {agentCommand.FencingToken},
                {state},
                {command.CreatedAtUtc},
                {command.UpdatedAtUtc})
            ON CONFLICT (environment_id, command_id, created_at_utc) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        if (inserted == 1)
        {
            return AgentCommandEnqueueResult.Created;
        }

        var persisted = await dbContext.AgentCommands
            .AsNoTracking()
            .SingleAsync(existing =>
                existing.EnvironmentId == agentCommand.EnvironmentId &&
                existing.CommandId == agentCommand.CommandId,
                cancellationToken)
            .ConfigureAwait(false);

        var sameEnvelope = persisted.Kind == agentCommand.Kind &&
            string.Equals(persisted.ContainerId, agentCommand.ContainerId, StringComparison.Ordinal) &&
            string.Equals(
                persisted.ExpectedContainerRevision,
                agentCommand.ExpectedContainerRevision,
                StringComparison.Ordinal) &&
            string.Equals(persisted.PayloadHash, agentCommand.PayloadHash, StringComparison.Ordinal) &&
            persisted.DeadlineUtc == agentCommand.DeadlineUtc;

        return sameEnvelope
            ? AgentCommandEnqueueResult.Duplicate
            : AgentCommandEnqueueResult.ConflictingPayload;
    }

    public async Task<AgentCommandEnqueueResult> EnqueueAuditedAsync(
        PersistedAgentCommand command,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ValidateAuditEvent(
            auditEvent,
            command.Command.EnvironmentId,
            command.Command.CommandId,
            GetCommandAction(command.Command.Kind));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = await EnqueueAsync(command, cancellationToken).ConfigureAwait(false);
        if (result == AgentCommandEnqueueResult.Created)
        {
            await auditEventWriter.AppendAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<AgentCommandStatusUpdateResult> ApplyStatusAsync(
        AgentCommandStatusUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var terminal = update.State is ControlPlaneCommandState.Succeeded or
            ControlPlaneCommandState.Failed or
            ControlPlaneCommandState.Indeterminate;
        var reconcilesExpiredCommand = update.State is ControlPlaneCommandState.Succeeded or
            ControlPlaneCommandState.Failed;
        var completedAtUtc = terminal ? update.UpdatedAtUtc : (DateTimeOffset?)null;
        var updated = await dbContext.AgentCommands
            .Where(command =>
                command.EnvironmentId == update.EnvironmentId &&
                command.CommandId == update.CommandId &&
                ((command.State <= ControlPlaneCommandState.Accepted &&
                    command.State < update.State) ||
                 (reconcilesExpiredCommand &&
                    command.State == ControlPlaneCommandState.Indeterminate &&
                    command.FailureCode == "expired_command")))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.State, update.State)
                    .SetProperty(command => command.FailureCode, update.FailureCode)
                    .SetProperty(command => command.ObservedContainerRevision, update.ObservedContainerRevision)
                    .SetProperty(command => command.CompletedAtUtc, completedAtUtc)
                    .SetProperty(
                        command => command.UpdatedAtUtc,
                        command => command.UpdatedAtUtc > update.UpdatedAtUtc
                            ? command.UpdatedAtUtc
                            : update.UpdatedAtUtc),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated == 1)
        {
            return AgentCommandStatusUpdateResult.Applied;
        }

        var existing = await dbContext.AgentCommands
            .AsNoTracking()
            .SingleOrDefaultAsync(
                command =>
                    command.EnvironmentId == update.EnvironmentId &&
                    command.CommandId == update.CommandId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return AgentCommandStatusUpdateResult.NotFound;
        }

        var sameStatus = existing.State == update.State &&
            string.Equals(existing.FailureCode, update.FailureCode, StringComparison.Ordinal) &&
            string.Equals(
                existing.ObservedContainerRevision,
                update.ObservedContainerRevision,
                StringComparison.Ordinal);
        return sameStatus && (!terminal || existing.CompletedAtUtc == completedAtUtc)
                ? AgentCommandStatusUpdateResult.Duplicate
                : AgentCommandStatusUpdateResult.InvalidTransition;
    }

    public async Task<AgentCommandStatusUpdateResult> ApplyStatusAuditedAsync(
        AgentCommandStatusUpdate update,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ValidateAuditEvent(
            auditEvent,
            update.EnvironmentId,
            update.CommandId,
            "container.command.result");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = await ApplyStatusAsync(update, cancellationToken).ConfigureAwait(false);
        if (result == AgentCommandStatusUpdateResult.Applied)
        {
            await auditEventWriter.AppendAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<PersistedAgentCommand>> ClaimDispatchableAsync(
        Guid environmentId,
        long activeFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (environmentId == Guid.Empty)
        {
            throw new ArgumentException("Environment ID is required.", nameof(environmentId));
        }

        if (activeFencingToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeFencingToken));
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Current time must be UTC.", nameof(nowUtc));
        }

        await ExpireNonTerminalAsync(nowUtc, cancellationToken).ConfigureAwait(false);

        var candidates = await dbContext.AgentCommands
            .AsNoTracking()
            .Where(command =>
                command.EnvironmentId == environmentId &&
                command.State <= ControlPlaneCommandState.Accepted &&
                command.DeadlineUtc > nowUtc &&
                command.LastDispatchFencingToken != activeFencingToken)
            .OrderBy(command => command.CreatedAtUtc)
            .ThenBy(command => command.CommandId)
            .Select(command => new
            {
                command.CommandId,
                command.LastDispatchFencingToken,
            })
            .Take(MaximumDispatchBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return [];
        }

        var claimedIds = new List<Guid>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var updated = await dbContext.AgentCommands
                .Where(command =>
                    command.EnvironmentId == environmentId &&
                    command.CommandId == candidate.CommandId &&
                    command.State <= ControlPlaneCommandState.Accepted &&
                    command.DeadlineUtc > nowUtc &&
                    command.LastDispatchFencingToken == candidate.LastDispatchFencingToken)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(command => command.LastDispatchFencingToken, activeFencingToken)
                        .SetProperty(command => command.UpdatedAtUtc, nowUtc),
                    cancellationToken)
                .ConfigureAwait(false);
            if (updated == 1)
            {
                claimedIds.Add(candidate.CommandId);
            }
        }

        if (claimedIds.Count == 0)
        {
            return [];
        }

        var claimed = await dbContext.AgentCommands
            .AsNoTracking()
            .Where(command =>
                command.EnvironmentId == environmentId &&
                claimedIds.Contains(command.CommandId) &&
                command.State <= ControlPlaneCommandState.Accepted &&
                command.LastDispatchFencingToken == activeFencingToken)
            .OrderBy(command => command.CreatedAtUtc)
            .ThenBy(command => command.CommandId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return claimed.Select(command => new PersistedAgentCommand(
                new Dokpod.Domain.Commands.AgentCommand(
                    command.EnvironmentId,
                    command.CommandId,
                    command.Kind,
                    command.ContainerId,
                    command.ExpectedContainerRevision,
                    command.PayloadHash,
                    command.DeadlineUtc,
                    activeFencingToken),
                command.State,
                command.CreatedAtUtc,
                command.UpdatedAtUtc))
            .ToArray();
    }

    public async Task<int> ExpireNonTerminalAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Current time must be UTC.", nameof(nowUtc));
        }

        var totalExpired = 0;
        while (true)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            var candidates = await dbContext.AgentCommands
                .AsNoTracking()
                .Where(command =>
                    command.State <= ControlPlaneCommandState.Accepted &&
                    command.DeadlineUtc <= nowUtc)
                .OrderBy(command => command.DeadlineUtc)
                .ThenBy(command => command.CommandId)
                .Select(command => new ExpirationCandidate(
                    command.EnvironmentId,
                    command.CommandId,
                    command.State,
                    command.LastDispatchFencingToken))
                .Take(MaximumExpirationBatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var expiredInBatch = 0;
            foreach (var candidate in candidates)
            {
                var state = candidate.State == ControlPlaneCommandState.Pending &&
                    candidate.LastDispatchFencingToken is null
                        ? ControlPlaneCommandState.Failed
                        : ControlPlaneCommandState.Indeterminate;
                var updated = await dbContext.AgentCommands
                    .Where(command =>
                        command.EnvironmentId == candidate.EnvironmentId &&
                        command.CommandId == candidate.CommandId &&
                        command.State == candidate.State &&
                        command.LastDispatchFencingToken == candidate.LastDispatchFencingToken &&
                        command.DeadlineUtc <= nowUtc)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(command => command.State, state)
                            .SetProperty(command => command.FailureCode, "expired_command")
                            .SetProperty(command => command.CompletedAtUtc, nowUtc)
                            .SetProperty(command => command.UpdatedAtUtc, nowUtc),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated == 0)
                {
                    continue;
                }

                var outcome = state == ControlPlaneCommandState.Failed
                    ? AuditOutcome.Failed
                    : AuditOutcome.Indeterminate;
                var auditEvent = AuditEvent.Create(
                    Guid.NewGuid(),
                    nowUtc,
                    Guid.NewGuid(),
                    AuditActorKind.System,
                    "control-plane",
                    "container.command.result",
                    candidate.EnvironmentId,
                    outcome,
                    "expired_command",
                    candidate.CommandId);
                await auditEventWriter.AppendAsync(auditEvent, cancellationToken).ConfigureAwait(false);
                expiredInBatch++;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            totalExpired += expiredInBatch;
            if (candidates.Count < MaximumExpirationBatchSize || expiredInBatch == 0)
            {
                return totalExpired;
            }
        }
    }

    private sealed record ExpirationCandidate(
        Guid EnvironmentId,
        Guid CommandId,
        ControlPlaneCommandState State,
        long? LastDispatchFencingToken);

    private static void ValidateAuditEvent(
        AuditEvent auditEvent,
        Guid environmentId,
        Guid commandId,
        string action)
    {
        if (auditEvent.EnvironmentId != environmentId ||
            auditEvent.CommandId != commandId ||
            !string.Equals(auditEvent.Action, action, StringComparison.Ordinal))
        {
            throw new ArgumentException("Audit event does not match the command mutation.", nameof(auditEvent));
        }
    }

    private static string GetCommandAction(AgentCommandKind kind) =>
        kind switch
        {
            AgentCommandKind.StartContainer => "container.start",
            AgentCommandKind.StopContainer => "container.stop",
            AgentCommandKind.RestartContainer => "container.restart",
            AgentCommandKind.DeleteContainer => "container.delete",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}