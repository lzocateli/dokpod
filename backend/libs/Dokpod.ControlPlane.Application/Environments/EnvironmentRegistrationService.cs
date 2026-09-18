using Dokpod.Domain.Auditing;
using Dokpod.Domain.Environments;
using Dokpod.ControlPlane.Application.Authorization;

namespace Dokpod.ControlPlane.Application.Environments;

public sealed record EnvironmentRegistrationResult(
    bool Created,
    bool Allowed,
    string? FailureCode,
    AuthorizationDecisionOutcome AuthorizationOutcome);

public sealed record EnvironmentStateResult(
    EnvironmentRegistration? Registration,
    bool Allowed,
    string? FailureCode,
    AuthorizationDecisionOutcome AuthorizationOutcome);

public sealed class EnvironmentRegistrationService(
    EnvironmentAccessService accessService,
    IEnvironmentRegistrationStore registrationStore,
    IEnvironmentAuthorizationDecider authorizationDecider)
{
    public async Task<EnvironmentRegistrationResult> RegisterAsync(
        EnvironmentRegistration registration,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var access = await accessService.RegisterAsync(
            registration,
            EnvironmentResourceScopes.Manage,
            actor,
            accessToken,
            AuditActorKind.User,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        if (!access.Allowed)
        {
            return new EnvironmentRegistrationResult(false, false, access.FailureCode, access.Outcome);
        }

        var persisted = await registrationStore.CreateAsync(registration, cancellationToken).ConfigureAwait(false);
        return persisted is EnvironmentRegistrationStoreResult.Created
            ? new EnvironmentRegistrationResult(true, true, null, AuthorizationDecisionOutcome.Allowed)
            : new EnvironmentRegistrationResult(false, true, "environment_already_exists", AuthorizationDecisionOutcome.Allowed);
    }

    public async Task<EnvironmentStateResult> GetAsync(
        Guid environmentId,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (environmentId == Guid.Empty)
        {
            throw new ArgumentException("Environment ID is required.", nameof(environmentId));
        }

        var authorization = await AuthorizeReadAsync(
            environmentId,
            actor,
            accessToken,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        if (!authorization.Allowed)
        {
            return new EnvironmentStateResult(null, false, authorization.FailureCode, authorization.Outcome);
        }

        var registration = await registrationStore.GetAsync(environmentId, cancellationToken).ConfigureAwait(false);
        return new EnvironmentStateResult(registration, true, null, AuthorizationDecisionOutcome.Allowed);
    }

    private async Task<EnvironmentAuthorizationDecision> AuthorizeReadAsync(
        Guid environmentId,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await authorizationDecider.DecideAsync(
                $"urn:dokpod:environment:{environmentId:D}",
                EnvironmentResourceScopes.Read,
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
}
