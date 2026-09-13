namespace Dokpod.Bff.Authentication;

public sealed record AccessTokenRefreshResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);