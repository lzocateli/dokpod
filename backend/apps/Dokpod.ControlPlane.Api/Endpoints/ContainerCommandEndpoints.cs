using System.Security.Claims;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.Domain.Commands;

namespace Dokpod.ControlPlane.Api.Endpoints;

/// <summary>Payload para solicitar uma ação de ciclo de vida em um container.</summary>
public sealed record SubmitContainerCommandRequest(
    string Action,
    string ExpectedContainerRevision,
    DateTimeOffset DeadlineUtc);

public static class ContainerCommandEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(
                "/api/v1/environments/{environmentId:guid}/containers/{containerId}/commands",
                SubmitAsync)
            .RequireAuthorization();

    private static async Task<IResult> SubmitAsync(
        Guid environmentId,
        string containerId,
        SubmitContainerCommandRequest request,
        HttpContext context,
        ContainerCommandService commandService,
        CancellationToken cancellationToken)
    {
        var subject = context.User.FindFirstValue("sub");
        var accessToken = ExtractBearerToken(context.Request.Headers.Authorization);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(accessToken))
        {
            return Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "A autenticação do usuário é obrigatória.");
        }

        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var commandId) ||
            commandId == Guid.Empty ||
            !TryMapKind(request.Action, out var kind))
        {
            return Problem(
                context,
                StatusCodes.Status400BadRequest,
                "invalid_container_command",
                "A solicitação do comando é inválida.");
        }

        ContainerCommandSubmissionResult result;
        try
        {
            result = await commandService.SubmitAsync(
                environmentId,
                commandId,
                kind,
                containerId,
                request.ExpectedContainerRevision,
                request.DeadlineUtc,
                AuthenticatedActor.FromSubject(subject),
                accessToken,
                GetCorrelationId(context),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return Problem(
                context,
                StatusCodes.Status400BadRequest,
                "invalid_container_command",
                "A solicitação do comando é inválida.");
        }
        catch (InvalidOperationException)
        {
            return Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "persistence_unavailable",
                "O comando não pôde ser persistido.");
        }

        return result.Outcome switch
        {
            ContainerCommandSubmissionOutcome.Accepted or
                ContainerCommandSubmissionOutcome.Duplicate => Results.Accepted(value: new
                {
                    commandId = result.CommandId,
                }),
            ContainerCommandSubmissionOutcome.AuthorizationDenied => Problem(
                context,
                StatusCodes.Status403Forbidden,
                result.FailureCode ?? "authorization_denied",
                "O comando não foi autorizado."),
            ContainerCommandSubmissionOutcome.AuthorizationUnavailable => Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                result.FailureCode ?? "authorization_unavailable",
                "A autorização do comando está indisponível."),
            ContainerCommandSubmissionOutcome.EnvironmentNotFound => Problem(
                context,
                StatusCodes.Status404NotFound,
                result.FailureCode ?? "environment_not_found",
                "O ambiente não foi encontrado."),
            ContainerCommandSubmissionOutcome.EnvironmentUnavailable or
                ContainerCommandSubmissionOutcome.AgentOffline => Problem(
                context,
                StatusCodes.Status409Conflict,
                result.FailureCode ?? "command_unavailable",
                "O ambiente não está disponível para executar o comando."),
            ContainerCommandSubmissionOutcome.ConflictingPayload => Problem(
                context,
                StatusCodes.Status409Conflict,
                result.FailureCode ?? "command_id_conflict",
                "A chave idempotente já identifica outro comando."),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private static bool TryMapKind(string? action, out AgentCommandKind kind)
    {
        kind = action switch
        {
            "start" => AgentCommandKind.StartContainer,
            "stop" => AgentCommandKind.StopContainer,
            "restart" => AgentCommandKind.RestartContainer,
            "delete" => AgentCommandKind.DeleteContainer,
            _ => default,
        };
        return action is "start" or "stop" or "restart" or "delete";
    }

    private static IResult Problem(HttpContext context, int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: status switch
            {
                StatusCodes.Status401Unauthorized => "Autenticação necessária",
                StatusCodes.Status403Forbidden => "Acesso negado",
                StatusCodes.Status404NotFound => "Ambiente não encontrado",
                StatusCodes.Status409Conflict => "Comando indisponível",
                StatusCodes.Status503ServiceUnavailable => "Dependência indisponível",
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
        !string.IsNullOrWhiteSpace(authorization) &&
        authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;

    private static Guid GetCorrelationId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out var correlationId)
            ? correlationId
            : Guid.NewGuid();
}