using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Application.Environments;

public interface IEnvironmentRegistrationStore
{
    Task<EnvironmentRegistrationStoreResult> CreateAsync(
        EnvironmentRegistration registration,
        CancellationToken cancellationToken);

    Task<EnvironmentRegistration?> GetAsync(
        Guid environmentId,
        CancellationToken cancellationToken);
}

public enum EnvironmentRegistrationStoreResult
{
    Created,
    AlreadyExists,
}
