using System.Security.Cryptography;
using System.Text;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Commands;
using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Application.Commands;

public enum ContainerCommandSubmissionOutcome
{
    Accepted,
    Duplicate,
    ConflictingPayload,
    AuthorizationDenied,
    AuthorizationUnavailable,
    EnvironmentNotFound,
    EnvironmentUnavailable,
    AgentOffline,
}

public sealed record ContainerCommandSubmissionResult(
    ContainerCommandSubmissionOutcome Outcome,
    Guid CommandId,
    string? FailureCode);

public sealed class ContainerCommandService(
    IEnvironmentAuthorizationDecider authorizationDecider,
    IEnvironmentRegistrationStore registrationStore,
    IAgentSessionStore sessionStore,
    AgentCommandQueueService queueService)
{
    public async Task<ContainerCommandSubmissionResult> SubmitAsync(
        Guid environmentId,
        Guid commandId,
        AgentCommandKind kind,
        string containerId,
        string expectedContainerRevision,
        DateTimeOffset deadlineUtc,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(
            environmentId,
            commandId,
            kind,
            containerId,
            expectedContainerRevision,
            deadlineUtc,
            actor,
            accessToken,
            correlationId);

        var requiredScope = ScopeFor(kind);
        var authorization = await AuthorizeAsync(
            environmentId,
            requiredScope,
            actor,
            accessToken,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        if (!authorization.Allowed)
        {
            return new ContainerCommandSubmissionResult(
                authorization.Outcome == AuthorizationDecisionOutcome.Indeterminate
                    ? ContainerCommandSubmissionOutcome.AuthorizationUnavailable
                    : ContainerCommandSubmissionOutcome.AuthorizationDenied,
                commandId,
                authorization.FailureCode);
        }

        var registration = await registrationStore.GetAsync(environmentId, cancellationToken)
            .ConfigureAwait(false);
        if (registration is null)
        {
            return new ContainerCommandSubmissionResult(
                ContainerCommandSubmissionOutcome.EnvironmentNotFound,
                commandId,
                "environment_not_found");
        }

        var environmentAccess = EnvironmentAccessDecision.Evaluate(registration, requiredScope);
        if (!environmentAccess.Allowed)
        {
            return new ContainerCommandSubmissionResult(
                ContainerCommandSubmissionOutcome.EnvironmentUnavailable,
                commandId,
                environmentAccess.Reason);
        }

        var session = await sessionStore.FindActiveAsync(environmentId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null || session.FencingToken > long.MaxValue)
        {
            return new ContainerCommandSubmissionResult(
                ContainerCommandSubmissionOutcome.AgentOffline,
                commandId,
                "agent_offline");
        }

        var command = new AgentCommand(
            environmentId,
            commandId,
            kind,
            containerId,
            expectedContainerRevision,
            ComputePayloadHash(kind, containerId, expectedContainerRevision),
            deadlineUtc,
            (long)session.FencingToken);
        var enqueueResult = await queueService.EnqueueAsync(command, cancellationToken)
            .ConfigureAwait(false);

        return new ContainerCommandSubmissionResult(
            enqueueResult switch
            {
                AgentCommandEnqueueResult.Created => ContainerCommandSubmissionOutcome.Accepted,
                AgentCommandEnqueueResult.Duplicate => ContainerCommandSubmissionOutcome.Duplicate,
                AgentCommandEnqueueResult.ConflictingPayload => ContainerCommandSubmissionOutcome.ConflictingPayload,
                _ => throw new ArgumentOutOfRangeException(nameof(enqueueResult)),
            },
            commandId,
            enqueueResult == AgentCommandEnqueueResult.ConflictingPayload
                ? "command_id_conflict"
                : null);
    }

    private async Task<EnvironmentAuthorizationDecision> AuthorizeAsync(
        Guid environmentId,
        string requiredScope,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await authorizationDecider.DecideAsync(
                $"urn:dokpod:environment:{environmentId:D}",
                requiredScope,
                actor,
                accessToken,
                correlationId,
                cancellationToken).ConfigureAwait(false);
            return authorization.Allowed || !string.IsNullOrWhiteSpace(authorization.FailureCode)
                ? authorization
                : new EnvironmentAuthorizationDecision(
                    AuthorizationDecisionOutcome.Indeterminate,
                    "authorization_invalid");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new EnvironmentAuthorizationDecision(
                AuthorizationDecisionOutcome.Indeterminate,
                "authorization_unavailable");
        }
    }

    private static string ScopeFor(AgentCommandKind kind) => kind switch
    {
        AgentCommandKind.StartContainer => EnvironmentResourceScopes.StartContainer,
        AgentCommandKind.StopContainer => EnvironmentResourceScopes.StopContainer,
        AgentCommandKind.RestartContainer => EnvironmentResourceScopes.RestartContainer,
        AgentCommandKind.DeleteContainer => EnvironmentResourceScopes.DeleteContainer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ComputePayloadHash(
        AgentCommandKind kind,
        string containerId,
        string expectedContainerRevision)
    {
        var payload = string.Join('\n', "v1", (int)kind, containerId, expectedContainerRevision);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static void ValidateRequest(
        Guid environmentId,
        Guid commandId,
        AgentCommandKind kind,
        string containerId,
        string expectedContainerRevision,
        DateTimeOffset deadlineUtc,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (environmentId == Guid.Empty || commandId == Guid.Empty || correlationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(accessToken) ||
            kind is < AgentCommandKind.StartContainer or > AgentCommandKind.DeleteContainer ||
            containerId?.Length != 64 || !containerId.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(expectedContainerRevision) ||
            expectedContainerRevision.Length > 255 ||
            deadlineUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Container command request is invalid.");
        }
    }
}