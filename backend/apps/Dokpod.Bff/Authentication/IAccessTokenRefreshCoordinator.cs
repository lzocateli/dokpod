namespace Dokpod.Bff.Authentication;

public interface IAccessTokenRefreshCoordinator
{
    Task<AccessTokenRefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}