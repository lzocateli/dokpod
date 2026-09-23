using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Dokpod.Agent.Application.Engines;
using Dokpod.Agent.Application.Protocol;
using Dokpod.Agent.Contracts.V1;
using AgentGrpcClient = Dokpod.Agent.Infrastructure.Protocol.AgentControlClient;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dokpod.Agent;

public sealed class AgentControlWorker(
    AgentOptions options,
    IContainerEngine engine,
    AgentCommandProtocolHandler commandHandler,
    ILogger<AgentControlWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = options.ControlPlaneEndpoint
            ?? throw new InvalidOperationException("Control plane endpoint is required.");
        var environmentId = options.EnvironmentId
            ?? throw new InvalidOperationException("Agent environment ID is required.");
        if (!File.Exists(options.ClientCertificatePath))
        {
            throw new InvalidOperationException("Agent client certificate was not found.");
        }

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            options.ClientCertificatePath,
            options.ClientCertificatePassword,
            System.OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet
                : X509KeyStorageFlags.EphemeralKeySet);
        using var serverCaCertificate = options.ServerCaCertificatePath is null
            ? null
            : X509CertificateLoader.LoadCertificateFromFile(options.ServerCaCertificatePath);
        await using var client = AgentGrpcClient.Create(endpoint, certificate, serverCaCertificate);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(client.Connect, environmentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is RpcException or HttpRequestException or IOException)
            {
                logger.LogWarning(
                    exception,
                    "Agent control session disconnected with status {Status}",
                    GetStatus(exception));
            }

            await Task.Delay(ReconnectDelay, stoppingToken);
        }
    }

    internal async Task RunSessionAsync(
        Func<CancellationToken, AsyncDuplexStreamingCall<AgentMessage, ControlPlaneMessage>> connect,
        Guid environmentId,
        CancellationToken stoppingToken)
    {
        var descriptor = await engine.InspectAsync(stoppingToken);
        using var call = connect(stoppingToken);
        await call.RequestStream.WriteAsync(CreateHello(descriptor), stoppingToken);
        if (!await call.ResponseStream.MoveNext(stoppingToken) ||
            call.ResponseStream.Current.PayloadCase != ControlPlaneMessage.PayloadOneofCase.SessionEstablished)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "agent_session_not_established"));
        }

        var establishedMessage = call.ResponseStream.Current;
        var established = establishedMessage.SessionEstablished;
        if (!Guid.TryParse(established.SessionId, out var sessionId) ||
            established.FencingToken == 0 ||
            established.FencingToken > long.MaxValue ||
            established.HeartbeatIntervalSeconds == 0 ||
            !IsValidServerMetadata(establishedMessage.Metadata, established.SessionId, established.FencingToken, 1))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "control_plane_session_invalid"));
        }

        logger.LogInformation("Agent control session established");
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var outbound = Channel.CreateBounded<AgentMessage>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var writer = WriteMessagesAsync(
            call.RequestStream,
            outbound.Reader,
            sessionId,
            established.FencingToken,
            sessionCancellation.Token);
        var heartbeat = SendHeartbeatsAsync(
            outbound.Writer,
            TimeSpan.FromSeconds(established.HeartbeatIntervalSeconds),
            sessionCancellation.Token);

        ulong serverSequence = 1;
        try
        {
            while (await call.ResponseStream.MoveNext(sessionCancellation.Token))
            {
                var message = call.ResponseStream.Current;
                if (!IsValidServerMetadata(
                        message.Metadata,
                        established.SessionId,
                        established.FencingToken,
                        ++serverSequence))
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "control_plane_metadata_invalid"));
                }

                if (message.PayloadCase == ControlPlaneMessage.PayloadOneofCase.Command)
                {
                    await commandHandler.HandleAsync(
                        environmentId,
                        message.Metadata,
                        message.Command,
                        (acceptance, cancellationToken) => outbound.Writer.WriteAsync(
                            new AgentMessage { CommandAccepted = acceptance },
                            cancellationToken).AsTask(),
                        (result, cancellationToken) => outbound.Writer.WriteAsync(
                            new AgentMessage { CommandResult = result },
                            cancellationToken).AsTask(),
                        sessionCancellation.Token);
                }
            }
        }
        finally
        {
            await sessionCancellation.CancelAsync();
            outbound.Writer.TryComplete();
            await IgnoreCancellationAsync(writer);
            await IgnoreCancellationAsync(heartbeat);
        }
    }

    private static async Task WriteMessagesAsync(
        IClientStreamWriter<AgentMessage> requestStream,
        ChannelReader<AgentMessage> messages,
        Guid sessionId,
        ulong fencingToken,
        CancellationToken cancellationToken)
    {
        ulong sequence = 0;
        await foreach (var message in messages.ReadAllAsync(cancellationToken))
        {
            message.Metadata = new MessageMetadata
            {
                ProtocolVersion = "1",
                SessionId = sessionId.ToString("D"),
                FencingToken = fencingToken,
                Sequence = ++sequence,
                OccurredAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                CorrelationId = Guid.NewGuid().ToString("D"),
            };
            await requestStream.WriteAsync(message, cancellationToken);
        }
    }

    private static async Task SendHeartbeatsAsync(
        ChannelWriter<AgentMessage> messages,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await messages.WriteAsync(new AgentMessage { Heartbeat = new Heartbeat() }, cancellationToken);
        }
    }

    private static AgentMessage CreateHello(EngineDescriptor descriptor)
    {
        var hello = new AgentHello
        {
            AgentVersion = typeof(AgentControlWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Engine = EngineKind.Docker,
            EngineVersion = descriptor.EngineVersion,
            OperatingSystem = System.OperatingSystem.IsWindows()
                ? Dokpod.Agent.Contracts.V1.OperatingSystem.Windows
                : Dokpod.Agent.Contracts.V1.OperatingSystem.Linux,
            OperatingSystemVersion = System.Environment.OSVersion.VersionString,
            Architecture = RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? Dokpod.Agent.Contracts.V1.Architecture.Arm64
                : Dokpod.Agent.Contracts.V1.Architecture.Amd64,
            Capabilities =
            {
                Capability.Inventory,
                Capability.ContainerStart,
                Capability.ContainerStop,
                Capability.ContainerRestart,
                Capability.ContainerDelete,
            },
            SupportedProtocolVersions = { "1" },
        };
        return new AgentMessage { Hello = hello };
    }

    private static bool IsValidServerMetadata(
        MessageMetadata? metadata,
        string sessionId,
        ulong fencingToken,
        ulong expectedSequence) =>
        metadata is not null &&
        metadata.ProtocolVersion == "1" &&
        metadata.SessionId == sessionId &&
        metadata.FencingToken == fencingToken &&
        metadata.Sequence == expectedSequence &&
        metadata.OccurredAt is not null;

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string GetStatus(Exception exception) => exception is RpcException rpcException
        ? rpcException.StatusCode.ToString()
        : exception.GetType().Name;
}