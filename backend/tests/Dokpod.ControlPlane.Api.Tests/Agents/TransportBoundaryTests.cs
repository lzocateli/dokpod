using System.Net.Security;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dokpod.Agent.Application.Commands;
using Dokpod.Agent.Application.Engines;
using Dokpod.Agent.Application.Protocol;
using Dokpod.Agent.Contracts.V1;
using Dokpod.Agent.Infrastructure.Commands;
using Dokpod.Agent.Infrastructure.Protocol;
using Dokpod.ControlPlane.Api;
using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Api.Commands;
using Dokpod.ControlPlane.Api.Realtime;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Application.Inventory;
using Dokpod.Domain.Commands;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests.Agents;

public sealed class TransportBoundaryTests
{
    [Fact]
    public void AgentControlClient_RejectsNonHttpsEndpoint()
    {
        using var certificate = CreateCertificate();

        var exception = Assert.Throws<ArgumentException>(() => AgentControlClient.Create(
            new Uri("http://localhost:7443"),
            certificate));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task InMemoryAgentSessionStore_InvalidatesPreviousEnvironmentSession()
    {
        var store = new InMemoryAgentSessionStore();
        var environmentId = Guid.NewGuid();

        var first = await store.ActivateAsync(environmentId, TestContext.Current.CancellationToken);
        var second = await store.ActivateAsync(environmentId, TestContext.Current.CancellationToken);

        Assert.False(store.IsActive(first));
        Assert.True(store.IsActive(second));
        Assert.Equal(second, await store.FindActiveAsync(environmentId, TestContext.Current.CancellationToken));

        await store.DeactivateAsync(first, TestContext.Current.CancellationToken);
        Assert.True(store.IsActive(second));
        await store.DeactivateAsync(second, TestContext.Current.CancellationToken);
        Assert.False(store.IsActive(second));
        Assert.Null(await store.FindActiveAsync(environmentId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InMemoryAgentSessionStore_AllowsOnlyOneActiveSessionUnderConcurrency()
    {
        var store = new InMemoryAgentSessionStore();
        var environmentId = Guid.NewGuid();

        var sessions = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(async () => await store.ActivateAsync(
                environmentId,
                TestContext.Current.CancellationToken).AsTask())));

        Assert.Single(sessions, store.IsActive);
    }

    [Fact]
    public async Task KestrelMtlsHandshake_EstablishesAgentSession()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint);
        var endpoint = GetGrpcEndpoint(app);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);

        await call.RequestStream.WriteAsync(new AgentMessage
        {
            Hello = new AgentHello
            {
                Engine = EngineKind.Docker,
                OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
                Architecture = Architecture.Amd64,
                Capabilities = { Capability.Inventory },
                SupportedProtocolVersions = { "1" },
            },
        }, TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ControlPlaneMessage.PayloadOneofCase.SessionEstablished, call.ResponseStream.Current.PayloadCase);
        Assert.NotEqual(string.Empty, call.ResponseStream.Current.SessionEstablished.SessionId);
        Assert.NotEqual(0UL, call.ResponseStream.Current.SessionEstablished.FencingToken);
    }

    [Fact]
    public async Task KestrelSession_WhenCommandIsQueued_DeliversItWithoutWaitingForAgentMessage()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        var deliveryQueue = new InMemoryAgentCommandDeliveryQueue();
        var commandStore = new RecordingAgentCommandStore();
        await using var app = await StartApiAsync(
            certificates,
            environmentId,
            fingerprint,
            commandDeliveryQueue: deliveryQueue,
            commandStore: commandStore);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(
            GetGrpcEndpoint(app),
            new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call, Capability.ContainerRestart);
        var command = new PersistedAgentCommand(
            new Dokpod.Domain.Commands.AgentCommand(
                environmentId,
                Guid.NewGuid(),
                AgentCommandKind.RestartContainer,
                new string('A', 64),
                "revision-01",
                new string('B', 64),
                DateTimeOffset.UtcNow.AddMinutes(1),
                (long)session.FencingToken),
            ControlPlaneCommandState.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await deliveryQueue.EnqueueAsync(command, TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var delivered = call.ResponseStream.Current;
        Assert.Equal(ControlPlaneMessage.PayloadOneofCase.Command, delivered.PayloadCase);
        Assert.Equal(2UL, delivered.Metadata.Sequence);
        Assert.Equal(session.SessionId, delivered.Metadata.SessionId);
        Assert.Equal(session.FencingToken, delivered.Metadata.FencingToken);
        Assert.Equal(command.Command.CommandId.ToString("D"), delivered.Command.CommandId);
        Assert.Equal(CommandKind.RestartContainer, delivered.Command.Kind);
        Assert.Equal(command.Command.ContainerId, delivered.Command.ContainerId);
        Assert.Equal(command.Command.ExpectedContainerRevision, delivered.Command.ExpectedContainerRevision);
        Assert.Equal(command.Command.PayloadHash, Convert.ToHexString(delivered.Command.PayloadHash.Span));

        var accepted = CreateHeartbeat(session, 1);
        accepted.Heartbeat = null;
        accepted.CommandAccepted = new CommandAccepted
        {
            CommandId = command.Command.CommandId.ToString("D"),
            Acceptance = CommandAcceptance.Accepted,
        };
        await call.RequestStream.WriteAsync(accepted, TestContext.Current.CancellationToken);

        var completedAt = DateTimeOffset.UtcNow;
        var result = CreateHeartbeat(session, 2);
        result.Heartbeat = null;
        result.CommandResult = new CommandResult
        {
            CommandId = command.Command.CommandId.ToString("D"),
            State = CommandResultState.Succeeded,
            ObservedContainerRevision = "revision-02",
            CompletedAt = Timestamp.FromDateTimeOffset(completedAt),
        };
        await call.RequestStream.WriteAsync(result, TestContext.Current.CancellationToken);
        await call.RequestStream.CompleteAsync();
        Assert.False(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Collection(
            commandStore.Updates,
            update => Assert.Equal(ControlPlaneCommandState.Dispatched, update.State),
            update => Assert.Equal(ControlPlaneCommandState.Accepted, update.State),
            update =>
            {
                Assert.Equal(ControlPlaneCommandState.Succeeded, update.State);
                Assert.Equal("revision-02", update.ObservedContainerRevision);
                Assert.Equal(completedAt, update.UpdatedAtUtc);
            });
        Assert.All(commandStore.Updates, update =>
        {
            Assert.Equal(environmentId, update.EnvironmentId);
            Assert.Equal(command.Command.CommandId, update.CommandId);
        });
    }

    [Fact]
    public async Task KestrelSession_WhenAcceptedCommandExists_RedeliversWithActiveFencing()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        var command = new PersistedAgentCommand(
            new Dokpod.Domain.Commands.AgentCommand(
                environmentId,
                Guid.NewGuid(),
                AgentCommandKind.RestartContainer,
                new string('A', 64),
                "revision-01",
                new string('B', 64),
                DateTimeOffset.UtcNow.AddMinutes(1),
                1),
            ControlPlaneCommandState.Accepted,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var commandStore = new RecordingAgentCommandStore
        {
            DispatchableCommands = [command],
        };
        await using var app = await StartApiAsync(
            certificates,
            environmentId,
            fingerprint,
            commandStore: commandStore);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(
            GetGrpcEndpoint(app),
            new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call, Capability.ContainerRestart);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var delivered = call.ResponseStream.Current;
        Assert.Equal(ControlPlaneMessage.PayloadOneofCase.Command, delivered.PayloadCase);
        Assert.Equal(command.Command.CommandId.ToString("D"), delivered.Command.CommandId);
        Assert.Equal(session.FencingToken, delivered.Metadata.FencingToken);
        Assert.Equal((long)session.FencingToken, Assert.Single(commandStore.ClaimFencingTokens));
        Assert.Empty(commandStore.Updates);
    }

    [Fact]
    public async Task AgentControlWorker_ProcessesRecoveredCommandAndReportsTerminalResult()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"dokpod-agent-control-{Guid.NewGuid():N}");
        var command = new PersistedAgentCommand(
            new Dokpod.Domain.Commands.AgentCommand(
                environmentId,
                Guid.NewGuid(),
                AgentCommandKind.RestartContainer,
                new string('A', 64),
                "revision-01",
                new string('B', 64),
                DateTimeOffset.UtcNow.AddMinutes(1),
                1),
            ControlPlaneCommandState.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var commandStore = new RecordingAgentCommandStore
        {
            DispatchableCommands = [command],
        };

        try
        {
            await using var app = await StartApiAsync(
                certificates,
                environmentId,
                fingerprint,
                commandStore: commandStore);
            using var handler = CreateHttpHandler(certificates);
            using var channel = GrpcChannel.ForAddress(
                GetGrpcEndpoint(app),
                new GrpcChannelOptions { HttpHandler = handler });
            var client = new AgentControl.AgentControlClient(channel);
            var journal = new FileCommandJournal(dataDirectory);
            var engine = new CommandRecordingEngine(command.Command.ContainerId);
            var timeProvider = TimeProvider.System;
            var processor = new AgentCommandProcessor(
                new AgentCommandGate(journal, timeProvider),
                journal,
                engine,
                timeProvider);
            var options = new global::Dokpod.Agent.AgentOptions(
                dataDirectory,
                Path.Combine(dataDirectory, "docker.sock"),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                false,
                null,
                environmentId,
                Path.Combine(dataDirectory, "identity", "agent.pfx"),
                null);
            var worker = new global::Dokpod.Agent.AgentControlWorker(
                options,
                engine,
                new AgentCommandProtocolHandler(processor),
                NullLogger<global::Dokpod.Agent.AgentControlWorker>.Instance);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromSeconds(15));
            var session = worker.RunSessionAsync(
                token => client.Connect(cancellationToken: token),
                environmentId,
                cancellation.Token);

            await commandStore.TerminalResult.Task.WaitAsync(cancellation.Token);

            Assert.Equal(1, engine.ExecutionCount);
            Assert.Collection(
                commandStore.Updates,
                update => Assert.Equal(ControlPlaneCommandState.Dispatched, update.State),
                update => Assert.Equal(ControlPlaneCommandState.Accepted, update.State),
                update => Assert.Equal(ControlPlaneCommandState.Succeeded, update.State));

            await app.Services.GetRequiredService<IAgentSessionStore>()
                .InvalidateEnvironmentAsync(environmentId, cancellation.Token);
            var exception = await Assert.ThrowsAsync<RpcException>(async () => await session);
            Assert.Equal(StatusCode.Aborted, exception.StatusCode);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task KestrelMtlsHandshake_RejectsUnknownClientCertificate()
    {
        using var certificates = TestCertificates.Create();
        using var unknownAgent = TestCertificates.CreateAgentCertificate(certificates.CertificateAuthority);
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), "unknown");
        using var handler = CreateHttpHandler(certificates, unknownAgent);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await call.RequestStream.WriteAsync(new AgentMessage
            {
                Hello = new AgentHello
                {
                    Engine = EngineKind.Docker,
                    OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
                    Architecture = Architecture.Amd64,
                    Capabilities = { Capability.Inventory },
                    SupportedProtocolVersions = { "1" },
                },
            }, TestContext.Current.CancellationToken);
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        });
        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task KestrelMtlsHandshake_RejectsClientOutsideTrustedAuthority()
    {
        using var certificates = TestCertificates.Create();
        using var untrustedAgent = TestCertificates.CreateUntrustedAgentCertificate();
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), "unused");
        using var handler = CreateHttpHandler(certificates, untrustedAgent);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await call.RequestStream.WriteAsync(new AgentMessage
            {
                Hello = new AgentHello
                {
                    Engine = EngineKind.Docker,
                    OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
                    Architecture = Architecture.Amd64,
                    Capabilities = { Capability.Inventory },
                    SupportedProtocolVersions = { "1" },
                },
            }, TestContext.Current.CancellationToken);
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task KestrelSession_ReconnectFencesPreviousStream()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(
            GetGrpcEndpoint(app),
            new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var firstCall = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var firstSession = await EstablishSessionAsync(firstCall);
        using var secondCall = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var secondSession = await EstablishSessionAsync(secondCall);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await firstCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Aborted, exception.StatusCode);
        Assert.Equal("agent_session_fenced", exception.Status.Detail);
        Assert.NotEqual(firstSession.FencingToken, secondSession.FencingToken);
    }

    [Fact]
    public async Task KestrelSession_ActiveInvalidationFencesStream()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(
            GetGrpcEndpoint(app),
            new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        await EstablishSessionAsync(call);

        await app.Services.GetRequiredService<IAgentSessionStore>()
            .InvalidateEnvironmentAsync(environmentId, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Aborted, exception.StatusCode);
        Assert.Equal("agent_session_fenced", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_RejectsReplayedSequence()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(
            GetGrpcEndpoint(app),
            new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);

        var heartbeat = CreateHeartbeat(session, 1);
        await call.RequestStream.WriteAsync(heartbeat, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(heartbeat, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal("agent_message_metadata_invalid", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_RejectsSequenceGap()
    {
        using var certificates = TestCertificates.Create();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);

        await call.RequestStream.WriteAsync(CreateHeartbeat(session, 1), TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(CreateHeartbeat(session, 3), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal("agent_message_metadata_invalid", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_RejectsMissingMetadata()
    {
        using var certificates = TestCertificates.Create();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        await EstablishSessionAsync(call);

        await call.RequestStream.WriteAsync(
            new AgentMessage { Heartbeat = new Heartbeat() },
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal("agent_message_metadata_invalid", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_RejectsUnsupportedPayload()
    {
        using var certificates = TestCertificates.Create();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);
        var message = CreateHeartbeat(session, 1);
        message.Heartbeat = null;

        await call.RequestStream.WriteAsync(message, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Equal("agent_payload_not_supported", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_WhenInventoryHasSequenceGap_RequestsSnapshot()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        var inventoryStore = new FixedInventoryProjectionStore(
            Dokpod.Domain.Inventory.InventoryReconciliationOutcome.SequenceGap,
            "sequence_gap");
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint, inventoryStore);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);
        var message = CreateHeartbeat(session, 1);
        message.Heartbeat = null;
        message.InventoryDelta = new InventoryDelta
        {
            BaseRevision = 1,
            InventoryRevision = 3,
        };

        await call.RequestStream.WriteAsync(message, TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ControlPlaneMessage.PayloadOneofCase.SnapshotRequest, call.ResponseStream.Current.PayloadCase);
        Assert.Equal("sequence_gap", call.ResponseStream.Current.SnapshotRequest.ReasonCode);
        Assert.Equal(environmentId, inventoryStore.EnvironmentId);
    }

    [Fact]
    public async Task KestrelSession_WhenSnapshotCompletes_PersistsAndNotifiesEnvironmentGroup()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        var inventoryStore = new FixedInventoryProjectionStore(
            Dokpod.Domain.Inventory.InventoryReconciliationOutcome.Accepted,
            string.Empty);
        var hubContext = new RecordingHubContext();
        await using var app = await StartApiAsync(
            certificates,
            environmentId,
            fingerprint,
            inventoryStore,
            hubContext);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);
        var message = CreateHeartbeat(session, 1);
        message.Heartbeat = null;
        message.SnapshotPage = new Agent.Contracts.V1.InventorySnapshotPage
        {
            SnapshotId = Guid.NewGuid().ToString("D"),
            InventoryRevision = 7,
            PageNumber = 0,
            IsLastPage = true,
            Containers =
            {
                new ContainerState
                {
                    ContainerId = "container-1",
                    Name = "api",
                    ImageReference = "dokpod/api:test",
                    State = ContainerLifecycleState.Running,
                    Revision = "revision-1",
                    ObservedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            },
        };

        await call.RequestStream.WriteAsync(message, TestContext.Current.CancellationToken);
        await call.RequestStream.CompleteAsync();
        Assert.False(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(environmentId, inventoryStore.EnvironmentId);
        Assert.Equal(7UL, inventoryStore.Snapshot?.Revision);
        Assert.Equal(ControlPlaneHub.GroupFor(environmentId.ToString("D")), hubContext.GroupName);
        Assert.Equal("inventoryChanged", hubContext.Method);
        var notification = Assert.IsType<InventoryChangedNotification>(Assert.Single(hubContext.Arguments!));
        Assert.Equal(environmentId, notification.EnvironmentId);
        Assert.Equal(7UL, notification.Revision);
    }

    private static AgentMessage CreateHeartbeat(SessionEstablished session, ulong sequence)
        => new()
        {
            Metadata = new MessageMetadata
            {
                ProtocolVersion = "1",
                SessionId = session.SessionId,
                FencingToken = session.FencingToken,
                Sequence = sequence,
                OccurredAt = Timestamp.FromDateTime(DateTime.UtcNow),
                CorrelationId = Guid.NewGuid().ToString("D"),
            },
            Heartbeat = new Heartbeat(),
        };

    private static async Task<SessionEstablished> EstablishSessionAsync(
        AsyncDuplexStreamingCall<AgentMessage, ControlPlaneMessage> call,
        params Capability[] capabilities)
    {
        var hello = new AgentHello
        {
            Engine = EngineKind.Docker,
            OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
            Architecture = Architecture.Amd64,
            SupportedProtocolVersions = { "1" },
        };
        hello.Capabilities.Add(capabilities.Length == 0 ? [Capability.Inventory] : capabilities);
        await call.RequestStream.WriteAsync(new AgentMessage
        {
            Hello = hello,
        }, TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        return call.ResponseStream.Current.SessionEstablished;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=synthetic-agent",
            key,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private static async Task<WebApplication> StartApiAsync(
        TestCertificates certificates,
        Guid environmentId,
        string fingerprint,
        IInventoryProjectionStore? inventoryStore = null,
        IHubContext<ControlPlaneHub>? hubContext = null,
        IAgentCommandDeliveryQueue? commandDeliveryQueue = null,
        IAgentCommandStore? commandStore = null)
    {
        var builder = ApiHost.CreateBuilder(
            [],
            new ApiHostOptions(
                0,
                0,
                certificates.Server,
                certificates.ValidateClient,
                LoopbackOnly: true,
                ConfigureServices: services =>
                {
                    services.AddSingleton<IAgentIdentityRegistry>(
                        new FixedAgentIdentityRegistry(environmentId, fingerprint));
                    if (inventoryStore is not null)
                    {
                        services.AddSingleton(inventoryStore);
                    }

                    if (hubContext is not null)
                    {
                        services.AddSingleton(hubContext);
                    }

                    if (commandDeliveryQueue is not null)
                    {
                        services.AddSingleton(commandDeliveryQueue);
                    }

                    if (commandStore is not null)
                    {
                        services.AddSingleton(commandStore);
                    }
                }));
        var app = builder.Build();
        ApiHost.MapEndpoints(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class FixedAgentIdentityRegistry(Guid environmentId, string fingerprint) : IAgentIdentityRegistry
    {
        public ValueTask<AgentIdentity?> FindByFingerprintAsync(
            string certificateFingerprint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<AgentIdentity?>(
                string.Equals(certificateFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                    ? new AgentIdentity(environmentId, fingerprint)
                    : null);
        }
    }

    private sealed class FixedInventoryProjectionStore(
        Dokpod.Domain.Inventory.InventoryReconciliationOutcome outcome,
        string failureCode) : IInventoryProjectionStore
    {
        public Guid? EnvironmentId { get; private set; }
        public Dokpod.Domain.Inventory.InventorySnapshot? Snapshot { get; private set; }

        public Task<Dokpod.Domain.Inventory.InventoryReconciliationResult> ApplyDeltaAsync(
            Guid environmentId,
            Dokpod.Domain.Inventory.InventoryDelta delta,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnvironmentId = environmentId;
            return Task.FromResult(new Dokpod.Domain.Inventory.InventoryReconciliationResult(
                Dokpod.Domain.Inventory.InventorySnapshot.Empty(environmentId, delta.BaseRevision),
                outcome,
                failureCode));
        }

        public Task<Dokpod.Domain.Inventory.InventoryReconciliationResult> ReplaceSnapshotAsync(
            Dokpod.Domain.Inventory.InventorySnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnvironmentId = snapshot.EnvironmentId;
            Snapshot = snapshot;
            return Task.FromResult(new Dokpod.Domain.Inventory.InventoryReconciliationResult(
                snapshot,
                outcome,
                failureCode));
        }

        public Task<InventoryProjectionPage?> GetPageAsync(
            Guid environmentId,
            string? afterContainerId,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<InventoryProjectionPage?>(null);
        }
    }

    private sealed class RecordingAgentCommandStore : IAgentCommandStore
    {
        public List<AgentCommandStatusUpdate> Updates { get; } = [];
        public List<long> ClaimFencingTokens { get; } = [];
        public IReadOnlyList<PersistedAgentCommand> DispatchableCommands { get; init; } = [];
        public TaskCompletionSource TerminalResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentCommandEnqueueResult> EnqueueAsync(
            PersistedAgentCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(AgentCommandEnqueueResult.Created);

        public Task<AgentCommandStatusUpdateResult> ApplyStatusAsync(
            AgentCommandStatusUpdate update,
            CancellationToken cancellationToken)
        {
            Updates.Add(update);
            if (update.State is ControlPlaneCommandState.Succeeded or
                ControlPlaneCommandState.Failed or
                ControlPlaneCommandState.Indeterminate)
            {
                TerminalResult.TrySetResult();
            }

            return Task.FromResult(AgentCommandStatusUpdateResult.Applied);
        }

        public Task<int> ExpireNonTerminalAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<PersistedAgentCommand>> ClaimDispatchableAsync(
            Guid environmentId,
            long activeFencingToken,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            ClaimFencingTokens.Add(activeFencingToken);
            return Task.FromResult<IReadOnlyList<PersistedAgentCommand>>(
                DispatchableCommands
                    .Where(command => command.Command.EnvironmentId == environmentId)
                    .Select(command => command with
                    {
                        Command = command.Command with { FencingToken = activeFencingToken },
                    })
                    .ToArray());
        }
    }

    private sealed class CommandRecordingEngine(string containerId) : IContainerEngine
    {
        public int ExecutionCount { get; private set; }

        public Task<EngineDescriptor> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new EngineDescriptor("test", "1.47"));

        public Task<IReadOnlyList<EngineContainer>> ListContainersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EngineContainer>>([]);

        public Task<EngineContainer?> InspectContainerAsync(
            string requestedContainerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<EngineContainer?>(
                requestedContainerId == containerId
                    ? new EngineContainer(containerId, "fixture", "fixture:latest", "running", "revision-01")
                    : null);

        public Task<ContainerMutationResult> ExecuteAsync(
            AgentCommandKind commandKind,
            string requestedContainerId,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ContainerMutationResult(true, null));
        }
    }

    private sealed class RecordingHubContext : IHubContext<ControlPlaneHub>
    {
        private readonly RecordingClientProxy proxy;

        public RecordingHubContext()
        {
            proxy = new RecordingClientProxy(this);
            Clients = new RecordingHubClients(this, proxy);
        }

        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = new NoOpGroupManager();
        public string? GroupName { get; set; }
        public string? Method { get; set; }
        public object?[]? Arguments { get; set; }

        private sealed class RecordingHubClients(RecordingHubContext context, IClientProxy proxy) : IHubClients
        {
            public IClientProxy All => proxy;
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => proxy;
            public IClientProxy Client(string connectionId) => proxy;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;
            public IClientProxy Group(string groupName)
            {
                context.GroupName = groupName;
                return proxy;
            }
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => proxy;
            public IClientProxy User(string userId) => proxy;
            public IClientProxy Users(IReadOnlyList<string> userIds) => proxy;
        }

        private sealed class RecordingClientProxy(RecordingHubContext context) : IClientProxy
        {
            public Task SendCoreAsync(
                string method,
                object?[] args,
                CancellationToken cancellationToken = default)
            {
                context.Method = method;
                context.Arguments = args;
                return Task.CompletedTask;
            }
        }

        private sealed class NoOpGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }

    private static Uri GetGrpcEndpoint(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        var address = addresses.Single(address => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        return new Uri(address);
    }

    private static HttpClientHandler CreateHttpHandler(TestCertificates certificates, X509Certificate2? agent = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && certificates.ValidateServer(certificate),
        };
        handler.ClientCertificates.Add(agent ?? certificates.Agent);
        return handler;
    }

    private sealed class TestCertificates : IDisposable
    {
        private TestCertificates(X509Certificate2 certificateAuthority, X509Certificate2 server, X509Certificate2 agent)
        {
            CertificateAuthority = certificateAuthority;
            Server = server;
            Agent = agent;
        }

        public X509Certificate2 CertificateAuthority { get; }
        public X509Certificate2 Server { get; }
        public X509Certificate2 Agent { get; }
        public Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool> ValidateClient => ValidateClientCertificate;

        public static TestCertificates Create()
        {
            var authorityKey = RSA.Create(2048);
            var authorityRequest = new CertificateRequest("CN=Dokpod test CA", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 1, true));
            authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            var authority = authorityRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30));
            return new TestCertificates(authority, CreateSignedCertificate(authority, "localhost", false), CreateAgentCertificate(authority));
        }

        public static X509Certificate2 CreateAgentCertificate(X509Certificate2 authority)
            => CreateSignedCertificate(authority, "dokpod-agent", true);

        public static X509Certificate2 CreateUntrustedAgentCertificate()
        {
            var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=untrusted-agent",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") },
                true));
            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5));
        }

        public bool ValidateServer(X509Certificate certificate)
            => BuildChain(certificate, X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.1");

        private bool ValidateClientCertificate(X509Certificate2 certificate, X509Chain? _, SslPolicyErrors __)
            => BuildChain(certificate, X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.2");

        private bool BuildChain(X509Certificate certificate, X509KeyUsageFlags _, string eku)
        {
            using var chain = new X509Chain();
            using var presentedCertificate = new X509Certificate2(certificate);
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(CertificateAuthority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            return chain.Build(presentedCertificate) &&
                presentedCertificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                    .Single().EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == eku);
        }

        private static X509Certificate2 CreateSignedCertificate(X509Certificate2 authority, string commonName, bool client)
        {
            var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var usages = new OidCollection { new Oid(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                client
                    ? X509KeyUsageFlags.DigitalSignature
                    : X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            if (!client)
            {
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost");
                names.AddIpAddress(IPAddress.Loopback);
                names.AddIpAddress(IPAddress.IPv6Loopback);
                request.CertificateExtensions.Add(names.Build());
            }

            using var certificate = request.Create(
                authority,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                RandomNumberGenerator.GetBytes(16));
            var withPrivateKey = certificate.CopyWithPrivateKey(key);
            var pfxBytes = withPrivateKey.Export(X509ContentType.Pfx, string.Empty);
            return X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                string.Empty,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
        }

        public void Dispose()
        {
            CertificateAuthority.Dispose();
            Server.Dispose();
            Agent.Dispose();
        }
    }
}