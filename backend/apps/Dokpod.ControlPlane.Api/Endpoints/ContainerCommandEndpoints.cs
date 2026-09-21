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
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/environments/{environmentId:guid}/containers/{containerId}/commands",
                SubmitAsync)
            .RequireAuthorization();

        endpoints.MapGet(
                "/api/v1/environments/{environmentId:guid}/commands/{commandId:guid}",
                GetAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetAsync(
        Guid environmentId,
        Guid commandId,
        HttpContext context,
        ContainerCommandQueryService queryService,
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

        ContainerCommandQueryResult result;
        try
        {
            result = await queryService.GetAsync(
                environmentId,
                commandId,
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
                "invalid_command_query",
                "A consulta do comando é inválida.");
        }
        catch (InvalidOperationException)
        {
            return Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "persistence_unavailable",
                "O estado do comando está indisponível.");
        }

        if (result.Outcome == ContainerCommandQueryOutcome.AuthorizationDenied)
        {
            return Problem(
                context,
                StatusCodes.Status403Forbidden,
                result.FailureCode ?? "authorization_denied",
                "A consulta do comando não foi autorizada.");
        }

        if (result.Outcome == ContainerCommandQueryOutcome.AuthorizationUnavailable)
        {
            return Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                result.FailureCode ?? "authorization_unavailable",
                "A autorização da consulta está indisponível.");
        }

        if (result.Outcome == ContainerCommandQueryOutcome.NotFound || result.Command is null)
        {
            return Problem(
                context,
                StatusCodes.Status404NotFound,
                result.FailureCode ?? "command_not_found",
                "O comando não foi encontrado.");
        }

        var command = result.Command;
        return Results.Ok(new
        {
            command.EnvironmentId,
            command.CommandId,
            action = MapKind(command.Kind),
            command.ContainerId,
            command.ExpectedContainerRevision,
            state = MapState(command.State),
            command.FailureCode,
            command.ObservedContainerRevision,
            command.DeadlineUtc,
            command.CreatedAtUtc,
            command.UpdatedAtUtc,
            command.CompletedAtUtc,
        });
    }

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

    private static string MapKind(AgentCommandKind kind) => kind switch
    {
        AgentCommandKind.StartContainer => "start",
        AgentCommandKind.StopContainer => "stop",
        AgentCommandKind.RestartContainer => "restart",
        AgentCommandKind.DeleteContainer => "delete",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string MapState(ControlPlaneCommandState state) => state switch
    {
        ControlPlaneCommandState.Pending => "pending",
        ControlPlaneCommandState.Dispatched => "dispatched",
        ControlPlaneCommandState.Accepted => "accepted",
        ControlPlaneCommandState.Succeeded => "succeeded",
        ControlPlaneCommandState.Failed => "failed",
        ControlPlaneCommandState.Indeterminate => "indeterminate",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

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