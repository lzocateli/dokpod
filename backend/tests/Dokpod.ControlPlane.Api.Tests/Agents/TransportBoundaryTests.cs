using System.Security.Cryptography.X509Certificates;
using Dokpod.Agent.Infrastructure.Protocol;
using Dokpod.ControlPlane.Api.Agents;
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
}