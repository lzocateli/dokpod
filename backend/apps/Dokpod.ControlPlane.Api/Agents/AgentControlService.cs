using System.Security.Cryptography.X509Certificates;
using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Application.Inventory;
using Dokpod.ControlPlane.Api.Realtime;
using Dokpod.Domain.Inventory;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class AgentControlService(
    AgentSessionNegotiator negotiator,
    IAgentSessionStore sessionStore,
    IAgentCommandDeliveryQueue commandDeliveryQueue,
    IAgentCommandStore commandStore,
    AgentCommandStatusService commandStatusService,
    InventoryProjectionService inventoryProjection,
    IHubContext<ControlPlaneHub> hubContext,
    ILogger<AgentControlService> logger) : AgentControl.AgentControlBase
{
    private const int MaximumMessageBytes = 1_048_576;
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(90);

    public override async Task Connect(
        IAsyncStreamReader<AgentMessage> requestStream,
        IServerStreamWriter<ControlPlaneMessage> responseStream,
        ServerCallContext context)
    {
        logger.LogInformation("Agent control connection received");
        if (!await requestStream.MoveNext(context.CancellationToken) ||
            requestStream.Current.PayloadCase != AgentMessage.PayloadOneofCase.Hello)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "agent_hello_required"));
        }

        var certificate = await context.GetHttpContext().Connection.GetClientCertificateAsync(
            context.CancellationToken);
        if (certificate is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "client_certificate_required"));
        }

        AgentSession session;
        try
        {
            session = await negotiator.NegotiateAsync(
                certificate,
                requestStream.Current.Hello,
                context.CancellationToken);
        }
        catch (AgentSessionRejectedException exception)
        {
            logger.LogWarning("Agent control negotiation rejected with {FailureCode}", exception.FailureCode);
            throw new RpcException(new Status(StatusCode.PermissionDenied, exception.FailureCode));
        }

        logger.LogInformation(
            "Agent control session negotiated for environment {EnvironmentId}",
            session.EnvironmentId);

        var invalidated = sessionStore.WaitUntilInactiveAsync(session, CancellationToken.None);
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        try
        {
            if (!sessionStore.IsActive(session))
            {
                throw new RpcException(new Status(StatusCode.Aborted, "agent_session_fenced"));
            }

            await responseStream.WriteAsync(new ControlPlaneMessage
            {
                Metadata = new MessageMetadata
                {
                    ProtocolVersion = AgentSessionNegotiator.ProtocolVersion,
                    SessionId = session.SessionId.ToString("D"),
                    FencingToken = session.FencingToken,
                    Sequence = 1,
                    OccurredAt = Timestamp.FromDateTime(DateTime.UtcNow),
                    CorrelationId = Guid.NewGuid().ToString("D"),
                },
                SessionEstablished = new SessionEstablished
                {
                    SelectedProtocolVersion = AgentSessionNegotiator.ProtocolVersion,
                    SessionId = session.SessionId.ToString("D"),
                    FencingToken = session.FencingToken,
                    HeartbeatIntervalSeconds = 30,
                    MaximumMessageBytes = MaximumMessageBytes,
                },
            });

            ulong lastSequence = 0;
            ulong serverSequence = 1;
            var snapshotAccumulator = new InventorySnapshotAccumulator(session.EnvironmentId);
            var recoveredCommands = new Queue<PersistedAgentCommand>(
                await commandStore.ClaimDispatchableAsync(
                    session.EnvironmentId,
                    (long)session.FencingToken,
                    DateTimeOffset.UtcNow,
                    context.CancellationToken));
            using var heartbeatTimeout = new CancellationTokenSource(HeartbeatTimeout);
            var heartbeatExpired = Task.Delay(Timeout.InfiniteTimeSpan, heartbeatTimeout.Token);
            var nextMessage = requestStream.MoveNext(sessionCancellation.Token);
            Task<PersistedAgentCommand>? nextCommand = null;
            while (true)
            {
                nextCommand ??= recoveredCommands.TryDequeue(out var recoveredCommand)
                    ? Task.FromResult(recoveredCommand)
                    : commandDeliveryQueue
                        .DequeueAsync(session.EnvironmentId, sessionCancellation.Token)
                        .AsTask();
                var completed = await Task.WhenAny(nextMessage, nextCommand, invalidated, heartbeatExpired);
                if (completed == invalidated)
                {
                    throw new RpcException(new Status(StatusCode.Aborted, "agent_session_fenced"));
                }

                if (completed == heartbeatExpired)
                {
                    throw new RpcException(new Status(StatusCode.DeadlineExceeded, "agent_heartbeat_timeout"));
                }

                if (completed == nextCommand)
                {
                    var command = await nextCommand;
                    nextCommand = null;
                    var nowUtc = DateTimeOffset.UtcNow;
                    if (command.Command.DeadlineUtc <= nowUtc)
                    {
                        await commandStore.ExpireNonTerminalAsync(nowUtc, context.CancellationToken);
                        continue;
                    }

                    if (command.Command.EnvironmentId != session.EnvironmentId ||
                        command.Command.FencingToken != (long)session.FencingToken)
                    {
                        continue;
                    }

                    if (command.State != ControlPlaneCommandState.Accepted)
                    {
                        await RequireStatusAppliedAsync(commandStatusService.MarkDispatchedAsync(
                            session.EnvironmentId,
                            command.Command.CommandId,
                            DateTimeOffset.UtcNow,
                            context.CancellationToken));
                    }
                    await responseStream.WriteAsync(new ControlPlaneMessage
                    {
                        Metadata = CreateServerMetadata(session, ++serverSequence),
                        Command = MapCommand(command.Command),
                    });
                    continue;
                }

                if (!await nextMessage)
                {
                    break;
                }

                heartbeatTimeout.CancelAfter(HeartbeatTimeout);

                if (!sessionStore.IsActive(session))
                {
                    throw new RpcException(new Status(StatusCode.Aborted, "agent_session_fenced"));
                }

                var metadata = requestStream.Current.Metadata;
                if (metadata is null ||
                    metadata.ProtocolVersion != AgentSessionNegotiator.ProtocolVersion ||
                    metadata.SessionId != session.SessionId.ToString("D") ||
                    metadata.FencingToken != session.FencingToken ||
                    lastSequence == ulong.MaxValue ||
                    metadata.Sequence != lastSequence + 1 ||
                    string.IsNullOrWhiteSpace(metadata.CorrelationId) ||
                    metadata.CorrelationId.Length > 128 ||
                    !IsValidTimestamp(metadata.OccurredAt))
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "agent_message_metadata_invalid"));
                }

                lastSequence = requestStream.Current.Metadata.Sequence;

                switch (requestStream.Current.PayloadCase)
                {
                    case AgentMessage.PayloadOneofCase.Heartbeat:
                        break;

                    case AgentMessage.PayloadOneofCase.InventoryDelta:
                        var result = await inventoryProjection.ApplyDeltaAsync(
                            session.EnvironmentId,
                            MapDelta(requestStream.Current.InventoryDelta),
                            context.CancellationToken);
                        if (result.Outcome is InventoryReconciliationOutcome.StaleBase or InventoryReconciliationOutcome.SequenceGap)
                        {
                            await responseStream.WriteAsync(new ControlPlaneMessage
                            {
                                Metadata = CreateServerMetadata(session, ++serverSequence),
                                SnapshotRequest = new SnapshotRequest { ReasonCode = result.FailureCode ?? "inventory_reconciliation_required" },
                            });
                        }
                        else if (result.Outcome == InventoryReconciliationOutcome.InvalidDelta)
                        {
                            throw new RpcException(new Status(StatusCode.InvalidArgument, result.FailureCode ?? "inventory_delta_invalid"));
                        }
                        else
                        {
                            await NotifyInventoryChangedAsync(
                                session.EnvironmentId,
                                result.Snapshot.Revision,
                                context.CancellationToken);
                        }

                        break;

                    case AgentMessage.PayloadOneofCase.SnapshotPage:
                        var pageResult = snapshotAccumulator.AddPage(MapSnapshotPage(requestStream.Current.SnapshotPage));
                        if (pageResult.Outcome == InventorySnapshotPageOutcome.Invalid)
                        {
                            throw new RpcException(new Status(
                                StatusCode.InvalidArgument,
                                pageResult.FailureCode ?? "inventory_snapshot_invalid"));
                        }

                        if (pageResult.Outcome == InventorySnapshotPageOutcome.Completed)
                        {
                            var snapshotResult = await inventoryProjection.ReplaceSnapshotAsync(
                                pageResult.Snapshot!,
                                context.CancellationToken);
                            if (snapshotResult.Outcome != InventoryReconciliationOutcome.Accepted)
                            {
                                throw new RpcException(new Status(
                                    StatusCode.FailedPrecondition,
                                    snapshotResult.FailureCode ?? "inventory_snapshot_stale"));
                            }

                            await NotifyInventoryChangedAsync(
                                session.EnvironmentId,
                                snapshotResult.Snapshot.Revision,
                                context.CancellationToken);
                        }

                        break;

                    case AgentMessage.PayloadOneofCase.CommandAccepted:
                        await HandleCommandAcceptedAsync(
                            session.EnvironmentId,
                            requestStream.Current.CommandAccepted,
                            metadata.OccurredAt.ToDateTimeOffset(),
                            context.CancellationToken);
                        break;

                    case AgentMessage.PayloadOneofCase.CommandResult:
                        await HandleCommandResultAsync(
                            session.EnvironmentId,
                            requestStream.Current.CommandResult,
                            context.CancellationToken);
                        break;

                    default:
                        throw new RpcException(new Status(StatusCode.Unimplemented, "agent_payload_not_supported"));
                }

                nextMessage = requestStream.MoveNext(sessionCancellation.Token);
            }
        }
        finally
        {
            await sessionCancellation.CancelAsync();
            await sessionStore.DeactivateAsync(session, CancellationToken.None);
        }
    }

    private static bool IsValidTimestamp(Timestamp? timestamp)
        => timestamp is not null &&
            timestamp.Seconds >= -62_135_596_800 &&
            timestamp.Seconds <= 253_402_300_799 &&
            timestamp.Nanos is >= 0 and <= 999_999_999;

    private static MessageMetadata CreateServerMetadata(AgentSession session, ulong sequence) => new()
    {
        ProtocolVersion = AgentSessionNegotiator.ProtocolVersion,
        SessionId = session.SessionId.ToString("D"),
        FencingToken = session.FencingToken,
        Sequence = sequence,
        OccurredAt = Timestamp.FromDateTime(DateTime.UtcNow),
        CorrelationId = Guid.NewGuid().ToString("D"),
    };

    private static Dokpod.Domain.Inventory.InventoryDelta MapDelta(
        Dokpod.Agent.Contracts.V1.InventoryDelta message)
    {
        if (message.InventoryRevision == 0)
        {
            return new Dokpod.Domain.Inventory.InventoryDelta(message.BaseRevision, message.InventoryRevision, []);
        }

        var changes = message.Changes.Select(change =>
        {
            var kind = change.Kind switch
            {
                ChangeKind.Upsert => InventoryChangeKind.Updated,
                ChangeKind.Remove => InventoryChangeKind.Removed,
                _ => (InventoryChangeKind?)null,
            };
            if (kind is null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "inventory_change_kind_invalid"));
            }

            var containerId = string.IsNullOrWhiteSpace(change.ContainerId)
                ? change.Container?.ContainerId
                : change.ContainerId;
            if (string.IsNullOrWhiteSpace(containerId) ||
                (kind == InventoryChangeKind.Updated && change.Container is null))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "inventory_change_invalid"));
            }

            var container = change.Container is null
                ? null
                : new ContainerInventory(
                    containerId,
                    change.Container.Name,
                    change.Container.ImageReference,
                    change.Container.State.ToString(),
                    change.Container.Revision,
                    change.Container.ObservedAt.ToDateTimeOffset());
            return new InventoryChange(kind.Value, containerId, container);
        }).ToArray();

        return new Dokpod.Domain.Inventory.InventoryDelta(message.BaseRevision, message.InventoryRevision, changes);
    }

    private static Dokpod.Agent.Contracts.V1.AgentCommand MapCommand(
        Dokpod.Domain.Commands.AgentCommand command) => new()
        {
            CommandId = command.CommandId.ToString("D"),
            Kind = command.Kind switch
            {
                Dokpod.Domain.Commands.AgentCommandKind.StartContainer => CommandKind.StartContainer,
                Dokpod.Domain.Commands.AgentCommandKind.StopContainer => CommandKind.StopContainer,
                Dokpod.Domain.Commands.AgentCommandKind.RestartContainer => CommandKind.RestartContainer,
                Dokpod.Domain.Commands.AgentCommandKind.DeleteContainer => CommandKind.DeleteContainer,
                _ => CommandKind.Unspecified,
            },
            ContainerId = command.ContainerId,
            ExpectedContainerRevision = command.ExpectedContainerRevision,
            PayloadHash = ByteString.CopyFrom(Convert.FromHexString(command.PayloadHash)),
            Deadline = Timestamp.FromDateTimeOffset(command.DeadlineUtc),
        };

    private async Task HandleCommandAcceptedAsync(
        Guid environmentId,
        CommandAccepted message,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(message.CommandId, out var commandId) ||
            message.Acceptance is CommandAcceptance.Unspecified ||
            !System.Enum.IsDefined(message.Acceptance))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "command_acceptance_invalid"));
        }

        var accepted = message.Acceptance is CommandAcceptance.Accepted or CommandAcceptance.Duplicate;
        try
        {
            await RequireStatusAppliedAsync(commandStatusService.RecordAcceptanceAsync(
                environmentId,
                commandId,
                accepted,
                message.FailureCode,
                occurredAtUtc,
                cancellationToken));
        }
        catch (ArgumentException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "command_acceptance_invalid"));
        }
    }

    private async Task HandleCommandResultAsync(
        Guid environmentId,
        CommandResult message,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(message.CommandId, out var commandId) ||
            !IsValidTimestamp(message.CompletedAt))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "command_result_invalid"));
        }

        var state = message.State switch
        {
            CommandResultState.Succeeded => ControlPlaneCommandState.Succeeded,
            CommandResultState.Failed => ControlPlaneCommandState.Failed,
            CommandResultState.Indeterminate => ControlPlaneCommandState.Indeterminate,
            _ => (ControlPlaneCommandState?)null,
        };
        if (state is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "command_result_invalid"));
        }

        try
        {
            await RequireStatusAppliedAsync(commandStatusService.RecordResultAsync(
                environmentId,
                commandId,
                state.Value,
                message.FailureCode,
                message.ObservedContainerRevision,
                message.CompletedAt.ToDateTimeOffset(),
                cancellationToken));
        }
        catch (ArgumentException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "command_result_invalid"));
        }
    }

    private static async Task RequireStatusAppliedAsync(
        Task<AgentCommandStatusUpdateResult> update)
    {
        var result = await update;
        if (result is AgentCommandStatusUpdateResult.NotFound or AgentCommandStatusUpdateResult.InvalidTransition)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "command_status_rejected"));
        }
    }

    private static Dokpod.ControlPlane.Application.Inventory.InventorySnapshotPage MapSnapshotPage(
        Dokpod.Agent.Contracts.V1.InventorySnapshotPage message) =>
        new(
            message.SnapshotId,
            message.InventoryRevision,
            message.PageNumber,
            message.IsLastPage,
            message.Containers.Select(MapContainer).ToArray());

    private static ContainerInventory MapContainer(ContainerState container)
    {
        if (string.IsNullOrWhiteSpace(container.ContainerId) || !IsValidTimestamp(container.ObservedAt))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "inventory_container_invalid"));
        }

        return new ContainerInventory(
            container.ContainerId,
            container.Name,
            container.ImageReference,
            container.State.ToString(),
            container.Revision,
            container.ObservedAt.ToDateTimeOffset());
    }

    private Task NotifyInventoryChangedAsync(
        Guid environmentId,
        ulong revision,
        CancellationToken cancellationToken) =>
        hubContext.Clients
            .Group(ControlPlaneHub.GroupFor(environmentId.ToString("D")))
            .SendAsync(
                "inventoryChanged",
                new InventoryChangedNotification(environmentId, revision),
                cancellationToken);
}