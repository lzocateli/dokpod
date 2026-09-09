using System.Security.Cryptography.X509Certificates;
using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Application.Agents;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class AgentControlService(
    AgentSessionNegotiator negotiator,
    IAgentSessionStore sessionStore) : AgentControl.AgentControlBase
{
    private const int MaximumMessageBytes = 1_048_576;
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(90);

    public override async Task Connect(
        IAsyncStreamReader<AgentMessage> requestStream,
        IServerStreamWriter<ControlPlaneMessage> responseStream,
        ServerCallContext context)
    {
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
            throw new RpcException(new Status(StatusCode.PermissionDenied, exception.FailureCode));
        }

        var invalidated = sessionStore.WaitUntilInactiveAsync(session, CancellationToken.None);
        try
        {
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
            while (true)
            {
                using var heartbeatTimeout = new CancellationTokenSource(HeartbeatTimeout);
                var heartbeatExpired = Task.Delay(Timeout.InfiniteTimeSpan, heartbeatTimeout.Token);
                var nextMessage = requestStream.MoveNext(context.CancellationToken);
                var completed = await Task.WhenAny(nextMessage, invalidated, heartbeatExpired);
                if (completed == invalidated)
                {
                    throw new RpcException(new Status(StatusCode.Aborted, "agent_session_fenced"));
                }

                if (completed == heartbeatExpired)
                {
                    throw new RpcException(new Status(StatusCode.DeadlineExceeded, "agent_heartbeat_timeout"));
                }

                heartbeatTimeout.Cancel();

                if (!await nextMessage)
                {
                    break;
                }

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

                if (requestStream.Current.PayloadCase != AgentMessage.PayloadOneofCase.Heartbeat)
                {
                    throw new RpcException(new Status(StatusCode.Unimplemented, "agent_payload_not_supported"));
                }
            }
        }
        finally
        {
            await sessionStore.DeactivateAsync(session, CancellationToken.None);
        }
    }

    private static bool IsValidTimestamp(Timestamp? timestamp)
        => timestamp is not null &&
            timestamp.Seconds >= -62_135_596_800 &&
            timestamp.Seconds <= 253_402_300_799 &&
            timestamp.Nanos is >= 0 and <= 999_999_999;
}