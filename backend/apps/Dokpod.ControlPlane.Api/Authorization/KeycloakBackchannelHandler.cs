namespace Dokpod.ControlPlane.Api.Authorization;

public sealed class KeycloakBackchannelHandler(
    Uri publicAuthority,
    Uri backchannelAuthority) : DelegatingHandler(new SocketsHttpHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } requestUri
            && HasSameOrigin(requestUri, publicAuthority)
            && requestUri.AbsolutePath.StartsWith(publicAuthority.AbsolutePath, StringComparison.Ordinal))
        {
            request.RequestUri = new UriBuilder(requestUri)
            {
                Scheme = backchannelAuthority.Scheme,
                Host = backchannelAuthority.Host,
                Port = backchannelAuthority.Port
            }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool HasSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;
}