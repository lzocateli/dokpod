using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Application.Inventory;

public sealed record InventoryQueryResult(
    InventoryProjectionPage? Page,
    bool Allowed,
    string? FailureCode,
    AuthorizationDecisionOutcome AuthorizationOutcome);

public sealed class InventoryQueryService(
    IInventoryProjectionStore store,
    IEnvironmentAuthorizationDecider authorizationDecider)
{
    public async Task<InventoryQueryResult> GetPageAsync(
        Guid environmentId,
        string? afterContainerId,
        int limit,
        AuthenticatedActor actor,
        string accessToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (environmentId == Guid.Empty)
        {
            throw new ArgumentException("Environment ID is required.", nameof(environmentId));
        }

        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        EnvironmentAuthorizationDecision authorization;
        try
        {
            authorization = await authorizationDecider.DecideAsync(
                $"urn:dokpod:environment:{environmentId:D}",
                EnvironmentResourceScopes.Read,
                actor,
                accessToken,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new InventoryQueryResult(
                null,
                false,
                "authorization_unavailable",
                AuthorizationDecisionOutcome.Indeterminate);
        }

        if (!authorization.Allowed)
        {
            return new InventoryQueryResult(
                null,
                false,
                authorization.FailureCode ?? "authorization_invalid",
                authorization.Outcome);
        }

        var page = await store.GetPageAsync(
            environmentId,
            afterContainerId,
            limit,
            cancellationToken).ConfigureAwait(false);
        return new InventoryQueryResult(page, true, null, AuthorizationDecisionOutcome.Allowed);
    }
}