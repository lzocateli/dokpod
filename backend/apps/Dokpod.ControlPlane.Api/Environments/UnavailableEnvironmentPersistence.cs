using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Api.Endpoints;

public sealed class UnavailableAuditEventWriter : IAuditEventWriter
{
    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        Task.FromException(
            new InvalidOperationException("Control Plane persistence is unavailable."));
}

public sealed class UnavailableEnvironmentRegistrationStore : IEnvironmentRegistrationStore
{
    public Task<EnvironmentRegistration?> GetAsync(
        Guid environmentId,
        CancellationToken cancellationToken) =>
        Task.FromException<EnvironmentRegistration?>(
            new InvalidOperationException("Control Plane persistence is unavailable."));

    public Task<EnvironmentRegistrationStoreResult> CreateAsync(
        EnvironmentRegistration registration,
        CancellationToken cancellationToken) =>
        Task.FromException<EnvironmentRegistrationStoreResult>(
            new InvalidOperationException("Control Plane persistence is unavailable."));
}
