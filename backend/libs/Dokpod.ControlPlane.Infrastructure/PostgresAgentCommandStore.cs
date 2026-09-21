using Dokpod.ControlPlane.Application.Commands;
using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class PostgresAgentCommandStore(ControlPlaneDbContext dbContext) : IAgentCommandStore
{
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
            ON CONFLICT (environment_id, command_id) DO NOTHING
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
            persisted.DeadlineUtc == agentCommand.DeadlineUtc &&
            persisted.FencingToken == agentCommand.FencingToken;

        return sameEnvelope
            ? AgentCommandEnqueueResult.Duplicate
            : AgentCommandEnqueueResult.ConflictingPayload;
    }
}