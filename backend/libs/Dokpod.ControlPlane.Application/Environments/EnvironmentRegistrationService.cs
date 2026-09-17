using Dokpod.Domain.Auditing;
using Dokpod.Domain.Environments;
using Dokpod.ControlPlane.Application.Authorization;

namespace Dokpod.ControlPlane.Application.Environments;

public sealed record EnvironmentRegistrationResult(
    bool Created,
    bool Allowed,
    string? FailureCode);

public sealed class EnvironmentRegistrationService(
    EnvironmentAccessService accessService,
    IEnvironmentRegistrationStore registrationStore)
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
            return new EnvironmentRegistrationResult(false, false, access.FailureCode);
        }

        var persisted = await registrationStore.CreateAsync(registration, cancellationToken).ConfigureAwait(false);
        return persisted is EnvironmentRegistrationStoreResult.Created
            ? new EnvironmentRegistrationResult(true, true, null)
            : new EnvironmentRegistrationResult(false, true, "environment_already_exists");
    }
}
