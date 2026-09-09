using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dokpod.Agent.Contracts.V1;

namespace Dokpod.ControlPlane.Application.Agents;

public sealed record AgentIdentity(Guid EnvironmentId, string CertificateFingerprint);

public sealed record AgentSession(Guid EnvironmentId, Guid SessionId, ulong FencingToken);

public interface IAgentIdentityRegistry
{
    ValueTask<AgentIdentity?> FindByFingerprintAsync(
        string certificateFingerprint,
        CancellationToken cancellationToken);
}

public interface IAgentSessionStore
{
    ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken);

    bool IsActive(AgentSession session);
}

public sealed class AgentSessionNegotiator(
    IAgentIdentityRegistry identityRegistry,
    IAgentSessionStore sessionStore)
{
    public const string ProtocolVersion = "1";

    public async ValueTask<AgentSession> NegotiateAsync(
        X509Certificate2 clientCertificate,
        AgentHello hello,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientCertificate);
        ArgumentNullException.ThrowIfNull(hello);

        if (!HasClientAuthenticationEku(clientCertificate))
        {
            throw new AgentSessionRejectedException("client_certificate_eku_invalid");
        }

        if (!hello.SupportedProtocolVersions.Contains(ProtocolVersion))
        {
            throw new AgentSessionRejectedException("protocol_version_unsupported");
        }

        if (hello.Engine == EngineKind.Unspecified ||
            hello.OperatingSystem == Agent.Contracts.V1.OperatingSystem.Unspecified ||
            hello.Architecture == Architecture.Unspecified ||
            hello.Capabilities.Count == 0 ||
            hello.Capabilities.Contains(Capability.Unspecified))
        {
            throw new AgentSessionRejectedException("agent_hello_invalid");
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(clientCertificate.RawData));
        var identity = await identityRegistry.FindByFingerprintAsync(fingerprint, cancellationToken)
            ?? throw new AgentSessionRejectedException("agent_certificate_unknown");

        return await sessionStore.ActivateAsync(identity.EnvironmentId, cancellationToken);
    }

    private static bool HasClientAuthenticationEku(X509Certificate2 certificate)
    {
        const string clientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
        var ekuExtension = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        return ekuExtension is not null && ekuExtension.EnhancedKeyUsages.Cast<Oid>()
            .Any(oid => string.Equals(oid.Value, clientAuthenticationOid, StringComparison.Ordinal));
    }
}

public sealed class AgentSessionRejectedException(string failureCode) : Exception(failureCode)
{
    public string FailureCode { get; } = failureCode;
}