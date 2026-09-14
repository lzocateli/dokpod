using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Dokpod.ControlPlane.Application.Authorization;
using System.Security.Claims;

namespace Dokpod.ControlPlane.Api.Realtime;

[Authorize]
public sealed class ControlPlaneHub(IEnvironmentAuthorizationDecider authorizationDecider) : Hub
{
    public async Task JoinEnvironmentAsync(string environmentId)
    {
        if (string.IsNullOrWhiteSpace(environmentId)
            || environmentId.Length > 64
            || environmentId.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw new HubException("Identificador de ambiente inválido.");
        }

        var httpContext = Context.GetHttpContext();
        var accessToken = ExtractAccessToken(httpContext);
        var subject = Context.User?.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(subject))
        {
            throw new HubException("Acesso negado.");
        }

        if (!Guid.TryParse(environmentId, out var parsedEnvironmentId))
        {
            throw new HubException("Identificador de ambiente inválido.");
        }

        var decision = await authorizationDecider.DecideAsync(
            $"urn:dokpod:environment:{parsedEnvironmentId:D}",
            Dokpod.Domain.Environments.EnvironmentResourceScopes.Read,
            AuthenticatedActor.FromSubject(subject),
            accessToken,
            GetCorrelationId(httpContext),
            Context.ConnectionAborted);
        if (!decision.Allowed)
        {
            throw new HubException("Acesso negado.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GroupFor(environmentId),
            Context.ConnectionAborted);
    }

    public static string GroupFor(string environmentId) => $"environment-{environmentId}";

    private static string? ExtractAccessToken(HttpContext? context)
    {
        var header = context?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(header)
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        return null;
    }

    private static Guid GetCorrelationId(HttpContext? context) =>
        Guid.TryParse(context?.Request.Headers["X-Correlation-Id"], out var correlationId)
            ? correlationId
            : Guid.NewGuid();
}
