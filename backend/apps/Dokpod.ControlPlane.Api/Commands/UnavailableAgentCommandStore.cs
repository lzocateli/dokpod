using Dokpod.ControlPlane.Application.Commands;
using Dokpod.Domain.Auditing;

namespace Dokpod.ControlPlane.Api.Commands;

public sealed class UnavailableAgentCommandStore : IAgentCommandStore
{
    public Task<AgentCommandStatusSnapshot?> GetAsync(
        Guid environmentId,
        Guid commandId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Agent command storage is not configured.");

    public Task<AgentCommandEnqueueResult> EnqueueAsync(
        PersistedAgentCommand command,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Agent command storage is not configured.");

    public Task<AgentCommandEnqueueResult> EnqueueAuditedAsync(
        PersistedAgentCommand command,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Agent command storage is not configured.");

    public Task<AgentCommandStatusUpdateResult> ApplyStatusAsync(
        AgentCommandStatusUpdate update,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Agent command storage is not configured.");

    public Task<AgentCommandStatusUpdateResult> ApplyStatusAuditedAsync(
        AgentCommandStatusUpdate update,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Agent command storage is not configured.");

    public Task<int> ExpireNonTerminalAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<PersistedAgentCommand>> ClaimDispatchableAsync(
        Guid environmentId,
        long activeFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PersistedAgentCommand>>([]);
}