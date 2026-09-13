namespace Dokpod.Bff.Authentication;

public sealed class TokenRefreshRejectedException(int statusCode)
    : InvalidOperationException($"O Keycloak recusou a renovação do token com HTTP {statusCode}.");