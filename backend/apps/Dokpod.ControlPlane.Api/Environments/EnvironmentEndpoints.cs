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
        endpoints.MapGet("/api/v1/environments/{environmentId:guid}", GetAsync)
            .RequireAuthorization();
        endpoints.MapPost("/api/v1/environments", RegisterAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetAsync(
        Guid environmentId,
        HttpContext context,
        EnvironmentRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var subject = context.User.FindFirstValue("sub");
        var accessToken = ExtractBearerToken(context.Request.Headers.Authorization);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(accessToken))
        {
            return Problem(context,
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "A autenticação do usuário é obrigatória.");
        }

        EnvironmentStateResult result;
        try
        {
            result = await registrationService.GetAsync(
                environmentId,
                AuthenticatedActor.FromSubject(subject),
                accessToken,
                GetCorrelationId(context),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return Problem(context,
                StatusCodes.Status400BadRequest,
                "invalid_environment_id",
                "O identificador do ambiente é inválido.");
        }
        catch (InvalidOperationException)
        {
            return Problem(context,
                StatusCodes.Status503ServiceUnavailable,
                "persistence_unavailable",
                "O estado do ambiente está indisponível.");
        }

        if (!result.Allowed)
        {
            return Problem(context,
                result.AuthorizationOutcome is AuthorizationDecisionOutcome.Indeterminate
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status403Forbidden,
                result.FailureCode ?? "authorization_denied",
                "O estado do ambiente não foi autorizado.");
        }

        if (result.Registration is null)
        {
            return Problem(context,
                StatusCodes.Status404NotFound,
                "environment_not_found",
                "O ambiente não foi encontrado.");
        }

        return Results.Ok(new
        {
            environmentId = result.Registration.EnvironmentId,
            name = result.Registration.Name,
            host = result.Registration.Host,
            enabled = result.Registration.Enabled,
            scopes = result.Registration.Scopes.Order(StringComparer.Ordinal),
        });
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
            return Problem(context,
                StatusCodes.Status400BadRequest,
                "invalid_environment_registration",
                "O cadastro do ambiente é inválido.");
        }

        var subject = context.User.FindFirstValue("sub");
        var accessToken = ExtractBearerToken(context.Request.Headers.Authorization);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(accessToken))
        {
            return Problem(context,
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "A autenticação do usuário é obrigatória.");
        }

        EnvironmentRegistration registration;
        try
        {
            registration = EnvironmentRegistration.Create(
                request.EnvironmentId,
                request.Name,
                request.Host,
                request.Enabled,
                request.Scopes ?? [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);
        }
        catch (ArgumentException)
        {
            return Problem(context,
                StatusCodes.Status400BadRequest,
                "invalid_environment_registration",
                "O cadastro do ambiente é inválido.");
        }
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
            return Problem(context,
                StatusCodes.Status503ServiceUnavailable,
                "persistence_unavailable",
                "O cadastro do ambiente está indisponível.");
        }

        if (!result.Allowed)
        {
            return Problem(context,
                result.AuthorizationOutcome is AuthorizationDecisionOutcome.Indeterminate
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status403Forbidden,
                result.FailureCode ?? "authorization_denied",
                "O cadastro do ambiente não foi autorizado.");
        }

        if (!result.Created)
        {
            return Problem(context,
                StatusCodes.Status409Conflict,
                result.FailureCode ?? "environment_conflict",
                "O ambiente já está cadastrado.");
        }

        return Results.Created($"/api/v1/environments/{registration.EnvironmentId:D}", new
        {
            environmentId = registration.EnvironmentId,
        });
    }

    private static IResult Problem(HttpContext context, int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: status switch
            {
                StatusCodes.Status401Unauthorized => "Autenticação necessária",
                StatusCodes.Status403Forbidden => "Acesso negado",
                StatusCodes.Status404NotFound => "Ambiente não encontrado",
                StatusCodes.Status409Conflict => "Conflito de ambiente",
                StatusCodes.Status503ServiceUnavailable => "Autorização indisponível",
                _ => "Requisição inválida",
            },
            detail: detail,
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = context.TraceIdentifier,
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
