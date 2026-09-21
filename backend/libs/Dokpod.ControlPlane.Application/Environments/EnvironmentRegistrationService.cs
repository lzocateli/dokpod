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

public sealed record EnvironmentCatalogResult(
    IReadOnlyList<EnvironmentRegistration> Registrations,
    Guid? NextCursor,
    bool Available,
    string? FailureCode);

public sealed class EnvironmentRegistrationService(
    EnvironmentAccessService accessService,
    IEnvironmentRegistrationStore registrationStore,
    IEnvironmentAuthorizationDecider authorizationDecider)
{
    private const int MaxAuthorizationDecisions = 500;

    public async Task<EnvironmentCatalogResult> ListAsync(
        Guid? afterEnvironmentId,
        int limit,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var registrations = new List<EnvironmentRegistration>(limit);
        var cursor = afterEnvironmentId;
        var decisions = 0;

        while (registrations.Count < limit && decisions < MaxAuthorizationDecisions)
        {
            var page = await registrationStore.ListAsync(
                cursor,
                Math.Min(limit - registrations.Count, MaxAuthorizationDecisions - decisions),
                cancellationToken).ConfigureAwait(false);

            foreach (var registration in page.Registrations)
            {
                decisions++;
                var authorization = await AuthorizeReadAsync(
                    registration.EnvironmentId,
                    actor,
                    accessToken,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
                if (authorization.Outcome is AuthorizationDecisionOutcome.Indeterminate)
                {
                    return new EnvironmentCatalogResult([], null, false, authorization.FailureCode);
                }

                if (authorization.Allowed)
                {
                    registrations.Add(registration);
                }
            }

            cursor = page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return new EnvironmentCatalogResult(registrations, cursor, true, null);
    }

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
