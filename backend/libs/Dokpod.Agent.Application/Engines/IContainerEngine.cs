using Dokpod.Domain.Commands;

namespace Dokpod.Agent.Application.Engines;

public sealed record EngineDescriptor(string EngineVersion, string ApiVersion);

public sealed record EngineContainer(
    string ContainerId,
    string Name,
    string ImageReference,
    string State,
    string Revision);

public sealed record ContainerMutationResult(bool Succeeded, string? FailureCode);

public interface IContainerEngine
{
    Task<EngineDescriptor> InspectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EngineContainer>> ListContainersAsync(CancellationToken cancellationToken);

    Task<EngineContainer?> InspectContainerAsync(string containerId, CancellationToken cancellationToken);

    Task<ContainerMutationResult> ExecuteAsync(
        AgentCommandKind commandKind,
        string containerId,
        CancellationToken cancellationToken);
}