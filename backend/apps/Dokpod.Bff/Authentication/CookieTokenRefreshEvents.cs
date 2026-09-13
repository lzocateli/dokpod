using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Dokpod.Bff.Authentication;

public sealed class CookieTokenRefreshEvents(
    IAccessTokenRefreshCoordinator refreshCoordinator,
    TimeProvider timeProvider,
    ILogger<CookieTokenRefreshEvents> logger) : CookieAuthenticationEvents
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(1);

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var expiresAtValue = context.Properties.GetTokenValue("expires_at");
        var hasExpiration = DateTimeOffset.TryParse(
            expiresAtValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var expiresAt);
        if (hasExpiration && expiresAt > timeProvider.GetUtcNow().Add(RefreshWindow))
            return;

        var refreshToken = context.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await RejectSessionAsync(context);
            return;
        }

        try
        {
            var refreshed = await refreshCoordinator.RefreshAsync(refreshToken, context.HttpContext.RequestAborted);
            context.Properties.UpdateTokenValue("access_token", refreshed.AccessToken);
            context.Properties.UpdateTokenValue("refresh_token", refreshed.RefreshToken);
            context.Properties.UpdateTokenValue("expires_at", refreshed.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
            context.ShouldRenew = true;
        }
        catch (TokenRefreshRejectedException exception)
        {
            logger.LogWarning(exception, "A renovação da sessão no Keycloak foi rejeitada.");
            await RejectSessionAsync(context);
        }
        catch (OperationCanceledException exception) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "A renovação da sessão no Keycloak excedeu o timeout.");
            if (!hasExpiration || expiresAt <= timeProvider.GetUtcNow())
                await RejectSessionAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "A renovação da sessão no Keycloak falhou.");
            if (!hasExpiration || expiresAt <= timeProvider.GetUtcNow())
                context.RejectPrincipal();
        }
    }

    private static async Task RejectSessionAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        context.HttpContext.Items["Dokpod.AuthenticationFailure"] = "session_token_refresh_failed";
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}