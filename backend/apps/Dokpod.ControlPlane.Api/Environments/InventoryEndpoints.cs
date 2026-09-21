using System.Security.Claims;
using System.Text;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Inventory;
using Microsoft.AspNetCore.WebUtilities;

namespace Dokpod.ControlPlane.Api.Endpoints;

public static class InventoryEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/environments/{environmentId:guid}/containers", GetAsync)
            .RequireAuthorization();

    private static async Task<IResult> GetAsync(
        Guid environmentId,
        string? cursor,
        int? limit,
        HttpContext context,
        InventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        var subject = context.User.FindFirstValue("sub");
        var accessToken = ExtractBearerToken(context.Request.Headers.Authorization);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(accessToken))
        {
            return Problem(context, StatusCodes.Status401Unauthorized, "authentication_required", "A autenticação do usuário é obrigatória.");
        }

        var pageLimit = limit ?? 50;
        if (pageLimit is < 1 or > 100 || !TryDecodeCursor(cursor, out var afterContainerId))
        {
            return Problem(context, StatusCodes.Status400BadRequest, "invalid_inventory_query", "Os parâmetros da consulta são inválidos.");
        }

        InventoryQueryResult result;
        try
        {
            result = await queryService.GetPageAsync(
                environmentId,
                afterContainerId,
                pageLimit,
                AuthenticatedActor.FromSubject(subject),
                accessToken,
                GetCorrelationId(context),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return Problem(context, StatusCodes.Status503ServiceUnavailable, "persistence_unavailable", "O inventário está indisponível.");
        }

        if (!result.Allowed)
        {
            return Problem(
                context,
                result.AuthorizationOutcome == AuthorizationDecisionOutcome.Indeterminate
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status403Forbidden,
                result.FailureCode ?? "authorization_denied",
                "A consulta do inventário não foi autorizada.");
        }

        if (result.Page is null)
        {
            return Problem(context, StatusCodes.Status404NotFound, "inventory_not_found", "O inventário do ambiente não foi encontrado.");
        }

        var page = result.Page;
        return Results.Ok(new
        {
            environmentId,
            revision = page.Revision,
            observedAt = page.ObservedAtUtc,
            ageSeconds = Math.Max(0, (long)(DateTimeOffset.UtcNow - page.ObservedAtUtc).TotalSeconds),
            containers = page.Containers.Select(container => new
            {
                containerId = container.ContainerId,
                name = container.Name,
                imageReference = container.ImageReference,
                state = container.State,
                revision = container.Revision,
                observedAt = container.ObservedAtUtc,
            }),
            nextCursor = EncodeCursor(page.NextContainerId),
        });
    }

    private static bool TryDecodeCursor(string? cursor, out string? containerId)
    {
        containerId = null;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        try
        {
            containerId = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            return !string.IsNullOrWhiteSpace(containerId) && containerId.Length <= 255;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? EncodeCursor(string? containerId) =>
        containerId is null ? null : WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(containerId));

    private static IResult Problem(HttpContext context, int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: status switch
            {
                StatusCodes.Status401Unauthorized => "Autenticação necessária",
                StatusCodes.Status403Forbidden => "Acesso negado",
                StatusCodes.Status404NotFound => "Inventário não encontrado",
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
        !string.IsNullOrWhiteSpace(authorization)
        && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;

    private static Guid GetCorrelationId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out var correlationId)
            ? correlationId
            : Guid.NewGuid();
}