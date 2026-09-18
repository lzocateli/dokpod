using Dokpod.ControlPlane.Api.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dokpod.ControlPlane.Api.Health;

public sealed class KeycloakReadinessHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakAuthorizationOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{options.Value.Authority.TrimEnd('/')}/.well-known/openid-configuration");
            using var response = await httpClientFactory
                .CreateClient(nameof(KeycloakReadinessHealthCheck))
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Keycloak indisponível.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Keycloak indisponível.");
        }
    }
}