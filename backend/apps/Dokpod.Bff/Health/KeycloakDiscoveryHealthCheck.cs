using System.Text.Json;
using Dokpod.Bff.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dokpod.Bff.Health;

public sealed class KeycloakDiscoveryHealthCheck(IHttpClientFactory httpClientFactory,
    IOptions<KeycloakOptions> keycloakOptions) : IHealthCheck
{
    public const string HttpClientName = "Dokpod.KeycloakDiscovery";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var authority = keycloakOptions.Value.Authority.TrimEnd('/');
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .GetAsync($"{authority}/.well-known/openid-configuration", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy("O discovery OIDC do Keycloak não respondeu com sucesso.");
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var discovery = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            var issuer = discovery.RootElement.GetProperty("issuer").GetString()?.TrimEnd('/');
            return string.Equals(issuer, authority, StringComparison.Ordinal)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("O issuer do discovery OIDC diverge da configuração.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException)
        {
            return HealthCheckResult.Unhealthy("O discovery OIDC do Keycloak está indisponível ou inválido.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("O discovery OIDC do Keycloak excedeu o timeout.");
        }
    }
}