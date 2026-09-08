using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dokpod.Agent.Application.Engines;
using Dokpod.Domain.Commands;

namespace Dokpod.Agent.Infrastructure.Engines.Docker;

public sealed class DockerEngineClient(HttpClient httpClient) : IContainerEngine
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly Version MaximumApiVersion = new(1, 47);
    private readonly SemaphoreSlim negotiationLock = new(1, 1);
    private string? apiVersion;
    private string? engineVersion;

    public static HttpClient CreateUnixSocketClient(string socketPath, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        if (!Path.IsPathFullyQualified(socketPath))
        {
            throw new ArgumentException("The Docker socket path must be absolute.", nameof(socketPath));
        }

        var normalizedSocketPath = Path.GetFullPath(socketPath);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(normalizedSocketPath), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://docker/", UriKind.Absolute),
            Timeout = timeout,
        };
    }

    public async Task<EngineDescriptor> InspectAsync(CancellationToken cancellationToken)
    {
        await EnsureNegotiatedAsync(cancellationToken);
        return new EngineDescriptor(engineVersion!, apiVersion!);
    }

    public async Task<IReadOnlyList<EngineContainer>> ListContainersAsync(CancellationToken cancellationToken)
    {
        await EnsureNegotiatedAsync(cancellationToken);
        using var response = await httpClient.GetAsync(
            $"v{apiVersion}/containers/json?all=1",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await ReadLimitedAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var containers = new List<EngineContainer>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("Id").GetString() ?? throw new InvalidDataException("Docker returned an empty container ID.");
            var name = item.TryGetProperty("Names", out var names) && names.GetArrayLength() > 0
                ? names[0].GetString()?.TrimStart('/') ?? string.Empty
                : string.Empty;
            var image = item.GetProperty("Image").GetString() ?? string.Empty;
            var state = item.GetProperty("State").GetString() ?? "unknown";
            var created = item.GetProperty("Created").GetInt64();
            containers.Add(new EngineContainer(id, name, image, state, $"{id}:{created}"));
        }

        return containers;
    }

    public async Task<EngineContainer?> InspectContainerAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        ValidateContainerId(containerId);
        await EnsureNegotiatedAsync(cancellationToken);
        using var response = await httpClient.GetAsync(
            $"v{apiVersion}/containers/{containerId}/json",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var content = await ReadLimitedAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var id = root.GetProperty("Id").GetString() ?? throw new InvalidDataException("Docker returned an empty container ID.");
        var name = root.GetProperty("Name").GetString()?.TrimStart('/') ?? string.Empty;
        var image = root.GetProperty("Config").GetProperty("Image").GetString() ?? string.Empty;
        var state = root.GetProperty("State").GetProperty("Status").GetString() ?? "unknown";
        var created = DateTimeOffset.Parse(root.GetProperty("Created").GetString()!).ToUnixTimeSeconds();
        return new EngineContainer(id, name, image, state, $"{id}:{created}");
    }

    public async Task<ContainerMutationResult> ExecuteAsync(
        AgentCommandKind commandKind,
        string containerId,
        CancellationToken cancellationToken)
    {
        ValidateContainerId(containerId);
        await EnsureNegotiatedAsync(cancellationToken);

        var request = commandKind switch
        {
            AgentCommandKind.StartContainer => new HttpRequestMessage(HttpMethod.Post, $"v{apiVersion}/containers/{containerId}/start"),
            AgentCommandKind.StopContainer => new HttpRequestMessage(HttpMethod.Post, $"v{apiVersion}/containers/{containerId}/stop"),
            AgentCommandKind.RestartContainer => new HttpRequestMessage(HttpMethod.Post, $"v{apiVersion}/containers/{containerId}/restart"),
            AgentCommandKind.DeleteContainer => new HttpRequestMessage(HttpMethod.Delete, $"v{apiVersion}/containers/{containerId}?v=0&force=0"),
            _ => throw new ArgumentOutOfRangeException(nameof(commandKind), commandKind, "Unsupported Docker command."),
        };

        using (request)
        using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
            {
                return new ContainerMutationResult(true, null);
            }

            return new ContainerMutationResult(
                false,
                response.StatusCode == HttpStatusCode.NotFound ? "target_not_found" : "operation_failed");
        }
    }

    private async Task EnsureNegotiatedAsync(CancellationToken cancellationToken)
    {
        if (apiVersion is not null)
        {
            return;
        }

        await negotiationLock.WaitAsync(cancellationToken);
        try
        {
            if (apiVersion is not null)
            {
                return;
            }

            using var response = await httpClient.GetAsync("version", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var content = await ReadLimitedAsync(response, cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

            var reportedApiVersion = Version.Parse(document.RootElement.GetProperty("ApiVersion").GetString()!);
            var minimumApiVersion = Version.Parse(document.RootElement.GetProperty("MinAPIVersion").GetString()!);
            if (minimumApiVersion > MaximumApiVersion)
            {
                throw new NotSupportedException($"Docker requires API {minimumApiVersion}, above supported {MaximumApiVersion}.");
            }

            var selectedVersion = reportedApiVersion < MaximumApiVersion ? reportedApiVersion : MaximumApiVersion;
            engineVersion = document.RootElement.GetProperty("Version").GetString() ?? "unknown";
            apiVersion = $"{selectedVersion.Major}.{selectedVersion.Minor}";
        }
        finally
        {
            negotiationLock.Release();
        }
    }

    private static void ValidateContainerId(string containerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        if (containerId.Length != 64 || containerId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Docker container IDs must contain exactly 64 hexadecimal characters.", nameof(containerId));
        }
    }

    private static async Task<MemoryStream> ReadLimitedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new InvalidDataException("Docker response exceeded the configured limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                destination.Position = 0;
                return destination;
            }

            total += read;
            if (total > MaximumResponseBytes)
            {
                await destination.DisposeAsync();
                throw new InvalidDataException("Docker response exceeded the configured limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}