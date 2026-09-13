namespace Dokpod.Bff.Authentication;

public static class KeycloakHttpMessageHandlerFactory
{
    public static HttpClientHandler Create(string authority)
    {
        var authorityUri = new Uri(authority, UriKind.Absolute);
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = !authorityUri.IsLoopback,
        };
    }
}