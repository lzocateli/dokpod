using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.Domain.Environments;
using Microsoft.Extensions.Options;

namespace Dokpod.ControlPlane.Api.Authorization;

public sealed class KeycloakAuthorizationDecisionService(
    HttpClient httpClient,
    IOptions<KeycloakAuthorizationOptions> options,
    ILogger<KeycloakAuthorizationDecisionService> logger) : IEnvironmentAuthorizationDecider
{
    public async Task<EnvironmentAuthorizationDecision> DecideAsync(
        string resource,
        string scope,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(actor);
        if (!IsSupportedPermission(resource, scope))
        {
            throw new ArgumentException("Recurso ou scope de autorização inválido.");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return LogDecision(
                new EnvironmentAuthorizationDecision(
                    AuthorizationDecisionOutcome.Indeterminate,
                    "authorization_unavailable"),
                resource,
                scope,
                correlationId,
                Stopwatch.StartNew());
        }

        var stopwatch = Stopwatch.StartNew();
        var authorizationOptions = options.Value;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{authorizationOptions.EffectiveBackchannelAuthority.TrimEnd('/')}/protocol/openid-connect/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:uma-ticket",
            ["audience"] = authorizationOptions.Audience,
            ["permission"] = $"{resource}#{scope}",
            ["response_mode"] = "decision"
        });

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return LogDecision(
                    new EnvironmentAuthorizationDecision(
                        AuthorizationDecisionOutcome.Denied,
                        $"keycloak_http_{(int)response.StatusCode}"),
                    resource,
                    scope,
                    correlationId,
                    stopwatch);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("result", out var result)
                || result.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                return LogDecision(
                    new EnvironmentAuthorizationDecision(
                        AuthorizationDecisionOutcome.Indeterminate,
                        "authorization_invalid"),
                    resource,
                    scope,
                    correlationId,
                    stopwatch);
            }

            return LogDecision(
                new EnvironmentAuthorizationDecision(
                    result.GetBoolean()
                        ? AuthorizationDecisionOutcome.Allowed
                        : AuthorizationDecisionOutcome.Denied,
                    result.GetBoolean() ? null : "scope_denied"),
                resource,
                scope,
                correlationId,
                stopwatch);
        }
        catch (HttpRequestException)
        {
            return LogDecision(
                new EnvironmentAuthorizationDecision(
                    AuthorizationDecisionOutcome.Indeterminate,
                    "authorization_unavailable"),
                resource,
                scope,
                correlationId,
                stopwatch);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LogDecision(
                new EnvironmentAuthorizationDecision(
                    AuthorizationDecisionOutcome.Indeterminate,
                    "authorization_timeout"),
                resource,
                scope,
                correlationId,
                stopwatch);
        }
        catch (JsonException)
        {
            return LogDecision(
                new EnvironmentAuthorizationDecision(
                    AuthorizationDecisionOutcome.Indeterminate,
                    "authorization_invalid"),
                resource,
                scope,
                correlationId,
                stopwatch);
        }
    }

    private static bool IsSupportedPermission(string resource, string scope) =>
        resource.StartsWith("urn:dokpod:environment:", StringComparison.Ordinal)
        && Guid.TryParse(resource["urn:dokpod:environment:".Length..], out _)
        && EnvironmentResourceScopes.Supported.Contains(scope);

    private EnvironmentAuthorizationDecision LogDecision(
        EnvironmentAuthorizationDecision decision,
        string resource,
        string scope,
        Guid correlationId,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        logger.LogInformation(
            "Keycloak authorization decision outcome={Outcome} available={Available} resource={Resource} scope={Scope} correlation_id={CorrelationId} duration_ms={DurationMs}",
            decision.Outcome,
            decision.Outcome is not AuthorizationDecisionOutcome.Indeterminate,
            resource,
            scope,
            correlationId,
            stopwatch.Elapsed.TotalMilliseconds);
        return decision;
    }
}
