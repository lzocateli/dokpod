using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Environments;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class EnvironmentRegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_WhenAuthorizationIsDenied_DoesNotPersistEnvironment()
    {
        var store = new RecordingEnvironmentRegistrationStore();
        var service = CreateService(
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "scope_denied")),
            store);

        var result = await service.RegisterAsync(
            CreateRegistration(),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.False(result.Created);
        Assert.Equal("scope_denied", result.FailureCode);
        Assert.Null(store.Registration);
    }

    [Fact]
    public async Task RegisterAsync_WhenAuthorized_PersistsEnvironmentAfterAudit()
    {
        var store = new RecordingEnvironmentRegistrationStore();
        var service = CreateService(
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null)),
            store);

        var registration = CreateRegistration();
        var result = await service.RegisterAsync(
            registration,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Allowed);
        Assert.True(result.Created);
        Assert.Equal(registration, store.Registration);
    }

    private static EnvironmentRegistrationService CreateService(
        IEnvironmentAuthorizationDecider authorizationDecider,
        IEnvironmentRegistrationStore store) =>
        new(
            new EnvironmentAccessService(new RecordingAuditEventWriter(), authorizationDecider),
            store);

    private static EnvironmentRegistration CreateRegistration() =>
        EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);

    private sealed class RecordingAuditEventWriter : IAuditEventWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingAuthorizationDecider(EnvironmentAuthorizationDecision decision)
        : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(decision);
    }

    private sealed class RecordingEnvironmentRegistrationStore : IEnvironmentRegistrationStore
    {
        public EnvironmentRegistration? Registration { get; private set; }

        public Task<EnvironmentRegistrationStoreResult> CreateAsync(
            EnvironmentRegistration registration,
            CancellationToken cancellationToken)
        {
            Registration = registration;
            return Task.FromResult(EnvironmentRegistrationStoreResult.Created);
        }
    }
}
