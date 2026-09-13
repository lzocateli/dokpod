using System.Security.Claims;

namespace Dokpod.Bff.Endpoints;

public sealed record BffSessionResponse(bool Authenticated, string? Subject, string? Name)
{
    public static BffSessionResponse FromPrincipal(ClaimsPrincipal principal)
    {
        var authenticated = principal.Identity?.IsAuthenticated == true;
        return new(authenticated, authenticated ? principal.FindFirstValue("sub") : null,
            authenticated ? principal.Identity?.Name : null);
    }
}