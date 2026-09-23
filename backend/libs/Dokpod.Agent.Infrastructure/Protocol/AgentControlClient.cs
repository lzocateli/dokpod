using Dokpod.Agent.Contracts.V1;
using Grpc.Core;
using Grpc.Net.Client;
using System.Security.Cryptography.X509Certificates;

namespace Dokpod.Agent.Infrastructure.Protocol;

public sealed class AgentControlClient : IAsyncDisposable
{
    private readonly GrpcChannel channel;
    private readonly AgentControl.AgentControlClient client;

    private AgentControlClient(GrpcChannel channel)
    {
        this.channel = channel;
        client = new AgentControl.AgentControlClient(channel);
    }

    public static AgentControlClient Create(
        Uri endpoint,
        X509Certificate2 clientCertificate,
        X509Certificate2? serverCaCertificate = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(clientCertificate);
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The agent control endpoint must use HTTPS.", nameof(endpoint));
        }

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(clientCertificate);
        if (serverCaCertificate is not null)
        {
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is not X509Certificate2 serverCertificate)
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(serverCaCertificate);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(serverCertificate);
            };
        }

        return new AgentControlClient(GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            HttpHandler = handler,
        }));
    }

    public AsyncDuplexStreamingCall<AgentMessage, ControlPlaneMessage> Connect(
        CancellationToken cancellationToken) => client.Connect(cancellationToken: cancellationToken);

    public ValueTask DisposeAsync()
    {
        channel.Dispose();
        return ValueTask.CompletedTask;
    }
}