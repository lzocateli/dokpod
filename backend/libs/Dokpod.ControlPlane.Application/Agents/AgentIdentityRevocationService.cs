using Dokpod.ControlPlane.Application.Environments;
using Dokpod.ControlPlane.Application.Authorization;

namespace Dokpod.ControlPlane.Application.Agents;

public sealed record AgentIdentityRevocationResult(
    bool Allowed,
    bool Revoked,
    string? FailureCode,
    AuthorizationDecisionOutcome AuthorizationOutcome);

public sealed class AgentIdentityRevocationService(
    EnvironmentAccessService accessService,
    IAgentIdentityRegistry identityRegistry,
    IAgentSessionStore sessionStore)
{
    public async Task<AgentIdentityRevocationResult> RevokeAsync(
        Guid environmentId,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var access = await accessService.RevokeAsync(
            environmentId,
            actor,
            accessToken,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        if (!access.Allowed)
        {
            return new AgentIdentityRevocationResult(
                false,
                false,
                access.FailureCode,
                access.Outcome);
        }

        var revoked = await identityRegistry.RevokeEnvironmentAsync(
            environmentId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (revoked)
        {
            await sessionStore.InvalidateEnvironmentAsync(
                environmentId,
                cancellationToken).ConfigureAwait(false);
        }

        return new AgentIdentityRevocationResult(
            true,
            revoked,
            revoked ? null : "agent_identity_not_found",
            AuthorizationDecisionOutcome.Allowed);
    }
}