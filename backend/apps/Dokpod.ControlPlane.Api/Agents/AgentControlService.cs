using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Application.Agents;
using Grpc.Core;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class AgentControlService(AgentSessionNegotiator sessionNegotiator)
    : AgentControl.AgentControlBase
{
    public override async Task Connect(
        IAsyncStreamReader<AgentMessage> requestStream,
        IServerStreamWriter<ControlPlaneMessage> responseStream,
        ServerCallContext context)
    {
        var clientCertificate = context.GetHttpContext().Connection.ClientCertificate
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "client_certificate_required"));

        if (!await requestStream.MoveNext(context.CancellationToken) || requestStream.Current.Hello is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "agent_hello_required"));
        }

        AgentSession session;
        try
        {
            session = await sessionNegotiator.NegotiateAsync(
                clientCertificate,
                requestStream.Current.Hello,
                context.CancellationToken);
        }
        catch (AgentSessionRejectedException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "agent_session_rejected"));
        }

        await responseStream.WriteAsync(new ControlPlaneMessage
        {
            Metadata = new MessageMetadata
            {
                ProtocolVersion = AgentSessionNegotiator.ProtocolVersion,
                SessionId = session.SessionId.ToString("D"),
                FencingToken = session.FencingToken,
                Sequence = 1,
                CorrelationId = context.GetHttpContext().TraceIdentifier,
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
        }
    }
}