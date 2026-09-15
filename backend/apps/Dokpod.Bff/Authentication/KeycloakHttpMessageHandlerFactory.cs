namespace Dokpod.Bff.Authentication;

public static class KeycloakHttpMessageHandlerFactory
{
    public static HttpClientHandler Create(string authority, bool allowDevelopmentCertificate)
    {
        var authorityUri = new Uri(authority, UriKind.Absolute);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = !authorityUri.IsLoopback,
        };
        if (allowDevelopmentCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return handler;
    }
}