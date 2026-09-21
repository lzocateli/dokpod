using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Application.Environments;

public interface IEnvironmentRegistrationStore
{
    Task<EnvironmentRegistrationPage> ListAsync(
        Guid? afterEnvironmentId,
        int limit,
        CancellationToken cancellationToken);

    Task<EnvironmentRegistrationStoreResult> CreateAsync(
        EnvironmentRegistration registration,
        CancellationToken cancellationToken);

    Task<EnvironmentRegistration?> GetAsync(
        Guid environmentId,
        CancellationToken cancellationToken);
}

public sealed record EnvironmentRegistrationPage(
    IReadOnlyList<EnvironmentRegistration> Registrations,
    Guid? NextCursor);

public enum EnvironmentRegistrationStoreResult
{
    Created,
    AlreadyExists,
}
