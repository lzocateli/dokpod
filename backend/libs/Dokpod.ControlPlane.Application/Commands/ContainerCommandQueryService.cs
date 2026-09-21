using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Application.Commands;

public enum ContainerCommandQueryOutcome
{
    Found,
    NotFound,
    AuthorizationDenied,
    AuthorizationUnavailable,
}

public sealed record ContainerCommandQueryResult(
    ContainerCommandQueryOutcome Outcome,
    AgentCommandStatusSnapshot? Command,
    string? FailureCode);

public sealed class ContainerCommandQueryService(
    IAgentCommandStore commandStore,
    IEnvironmentAuthorizationDecider authorizationDecider)
{
    public async Task<ContainerCommandQueryResult> GetAsync(
        Guid environmentId,
        Guid commandId,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (environmentId == Guid.Empty || commandId == Guid.Empty ||
            correlationId == Guid.Empty || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Command query is invalid.");
        }

        EnvironmentAuthorizationDecision authorization;
        try
        {
            authorization = await authorizationDecider.DecideAsync(
                $"urn:dokpod:environment:{environmentId:D}",
                EnvironmentResourceScopes.Read,
                actor,
                accessToken,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new ContainerCommandQueryResult(
                ContainerCommandQueryOutcome.AuthorizationUnavailable,
                null,
                "authorization_unavailable");
        }

        if (!Enum.IsDefined(authorization.Outcome) ||
            (!authorization.Allowed && string.IsNullOrWhiteSpace(authorization.FailureCode)))
        {
            return new ContainerCommandQueryResult(
                ContainerCommandQueryOutcome.AuthorizationUnavailable,
                null,
                "authorization_invalid");
        }

        if (!authorization.Allowed)
        {
            return new ContainerCommandQueryResult(
                authorization.Outcome == AuthorizationDecisionOutcome.Indeterminate
                    ? ContainerCommandQueryOutcome.AuthorizationUnavailable
                    : ContainerCommandQueryOutcome.AuthorizationDenied,
                null,
                authorization.FailureCode ?? "authorization_invalid");
        }

        var command = await commandStore.GetAsync(environmentId, commandId, cancellationToken)
            .ConfigureAwait(false);
        return new ContainerCommandQueryResult(
            command is null ? ContainerCommandQueryOutcome.NotFound : ContainerCommandQueryOutcome.Found,
            command,
            command is null ? "command_not_found" : null);
    }
}