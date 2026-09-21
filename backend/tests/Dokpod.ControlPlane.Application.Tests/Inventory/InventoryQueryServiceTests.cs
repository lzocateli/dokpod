using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Inventory;
using Dokpod.Domain.Inventory;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Inventory;

public sealed class InventoryQueryServiceTests
{
    [Fact]
    public async Task GetPageAsync_WhenAuthorizationIsDenied_DoesNotReadProjection()
    {
        var store = new RecordingStore();
        var service = new InventoryQueryService(
            store,
            new FixedAuthorizationDecider(AuthorizationDecisionOutcome.Denied));

        var result = await service.GetPageAsync(
            Guid.NewGuid(),
            null,
            50,
            AuthenticatedActor.FromSubject("user-1"),
            "token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.False(store.WasRead);
    }

    [Fact]
    public async Task GetPageAsync_WhenAuthorizationIsAllowed_ReadsRequestedPage()
    {
        var store = new RecordingStore();
        var service = new InventoryQueryService(
            store,
            new FixedAuthorizationDecider(AuthorizationDecisionOutcome.Allowed));
        var environmentId = Guid.NewGuid();

        var result = await service.GetPageAsync(
            environmentId,
            "container-1",
            25,
            AuthenticatedActor.FromSubject("user-1"),
            "token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Allowed);
        Assert.True(store.WasRead);
        Assert.Equal(environmentId, store.EnvironmentId);
        Assert.Equal("container-1", store.AfterContainerId);
        Assert.Equal(25, store.Limit);
    }

    private sealed class FixedAuthorizationDecider(AuthorizationDecisionOutcome outcome)
        : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EnvironmentAuthorizationDecision(outcome, "authorization_denied"));
    }

    private sealed class RecordingStore : IInventoryProjectionStore
    {
        public bool WasRead { get; private set; }
        public Guid? EnvironmentId { get; private set; }
        public string? AfterContainerId { get; private set; }
        public int? Limit { get; private set; }

        public Task<InventoryProjectionPage?> GetPageAsync(
            Guid environmentId,
            string? afterContainerId,
            int limit,
            CancellationToken cancellationToken)
        {
            WasRead = true;
            EnvironmentId = environmentId;
            AfterContainerId = afterContainerId;
            Limit = limit;
            return Task.FromResult<InventoryProjectionPage?>(new(
                1,
                DateTimeOffset.UtcNow,
                [],
                null));
        }

        public Task<InventoryReconciliationResult> ApplyDeltaAsync(
            Guid environmentId,
            InventoryDelta delta,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InventoryReconciliationResult> ReplaceSnapshotAsync(
            InventorySnapshot snapshot,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
