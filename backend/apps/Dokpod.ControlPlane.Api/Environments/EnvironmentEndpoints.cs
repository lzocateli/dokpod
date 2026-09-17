using System.Security.Claims;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Api.Endpoints;

public sealed record RegisterEnvironmentRequest(
    Guid EnvironmentId,
    string Name,
    string Host,
    bool Enabled = true,
    IReadOnlyCollection<string>? Scopes = null);

public static class EnvironmentEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/environments", RegisterAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterEnvironmentRequest request,
        HttpContext context,
        EnvironmentRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        if (request.EnvironmentId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Host))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_environment_registration",
                "O cadastro do ambiente é inválido.");
        }

        var subject = context.User.FindFirstValue("sub");
        var accessToken = ExtractBearerToken(context.Request.Headers.Authorization);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(accessToken))
        {
            return Problem(
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "A autenticação do usuário é obrigatória.");
        }

        var registration = EnvironmentRegistration.Create(
            request.EnvironmentId,
            request.Name,
            request.Host,
            request.Enabled,
            request.Scopes ?? [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);
        EnvironmentRegistrationResult result;
        try
        {
            result = await registrationService.RegisterAsync(
                registration,
                AuthenticatedActor.FromSubject(subject),
                accessToken,
                GetCorrelationId(context),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "persistence_unavailable",
                "O cadastro do ambiente está indisponível.");
        }

        if (!result.Allowed)
        {
            return Problem(
                result.FailureCode == "authorization_unavailable"
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status403Forbidden,
                result.FailureCode ?? "authorization_denied",
                "O cadastro do ambiente não foi autorizado.");
        }

        if (!result.Created)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                result.FailureCode ?? "environment_conflict",
                "O ambiente já está cadastrado.");
        }

        return Results.Created($"/api/v1/environments/{registration.EnvironmentId:D}", new
        {
            environmentId = registration.EnvironmentId,
        });
    }

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: status switch
            {
                StatusCodes.Status401Unauthorized => "Autenticação necessária",
                StatusCodes.Status403Forbidden => "Acesso negado",
                StatusCodes.Status409Conflict => "Conflito de ambiente",
                StatusCodes.Status503ServiceUnavailable => "Autorização indisponível",
                _ => "Requisição inválida",
            },
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            });

    private static string? ExtractBearerToken(string? authorization) =>
        !string.IsNullOrWhiteSpace(authorization)
        && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;

    private static Guid GetCorrelationId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out var correlationId)
            ? correlationId
            : Guid.NewGuid();
}
