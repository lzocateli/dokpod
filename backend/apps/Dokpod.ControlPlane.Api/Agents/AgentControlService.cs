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
                MaximumMessageBytes = 1_048_576,
            },
        });

        while (await requestStream.MoveNext(context.CancellationToken))
        {
            if (!sessionStore.IsActive(session) ||
                requestStream.Current.Metadata.SessionId != session.SessionId.ToString("D") ||
                requestStream.Current.Metadata.FencingToken != session.FencingToken)
            {
                throw new RpcException(new Status(StatusCode.Aborted, "agent_session_fenced"));
            }

            if (requestStream.Current.PayloadCase == AgentMessage.PayloadOneofCase.Heartbeat)
            {
                continue;
            }
        }
    }
}