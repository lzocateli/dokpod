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
    public async Task RegisterAsync_WhenAuthorizationIsIndeterminate_PreservesOutcomeAndDoesNotPersistEnvironment()
    {
        var store = new RecordingEnvironmentRegistrationStore();
        var service = CreateService(
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(
                    AuthorizationDecisionOutcome.Indeterminate,
                    "authorization_timeout")),
            store);

        var result = await service.RegisterAsync(
            CreateRegistration(),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.False(result.Created);
        Assert.Equal(AuthorizationDecisionOutcome.Indeterminate, result.AuthorizationOutcome);
        Assert.Equal("authorization_timeout", result.FailureCode);
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

    [Fact]
    public async Task GetAsync_WhenAuthorizationIsDenied_DoesNotReadEnvironment()
    {
        var store = new RecordingEnvironmentRegistrationStore();
        var service = CreateService(
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "scope_denied")),
            store);

        var result = await service.GetAsync(
            Guid.NewGuid(),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.Equal("scope_denied", result.FailureCode);
        Assert.Equal(AuthorizationDecisionOutcome.Denied, result.AuthorizationOutcome);
        Assert.False(store.WasRead);
    }

    [Fact]
    public async Task GetAsync_WhenAuthorized_ReturnsEnvironment()
    {
        var registration = CreateRegistration();
        var store = new RecordingEnvironmentRegistrationStore { RegistrationToRead = registration };
        var service = CreateService(
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null)),
            store);

        var result = await service.GetAsync(
            registration.EnvironmentId,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Allowed);
        Assert.Equal(registration, result.Registration);
        Assert.True(store.WasRead);
    }

    private static EnvironmentRegistrationService CreateService(
        IEnvironmentAuthorizationDecider authorizationDecider,
        IEnvironmentRegistrationStore store) =>
        new(
            new EnvironmentAccessService(new RecordingAuditEventWriter(), authorizationDecider),
            store,
            authorizationDecider);

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
        public EnvironmentRegistration? RegistrationToRead { get; init; }
        public bool WasRead { get; private set; }

        public Task<EnvironmentRegistration?> GetAsync(
            Guid environmentId,
            CancellationToken cancellationToken)
        {
            WasRead = true;
            return Task.FromResult(RegistrationToRead);
        }

        public Task<EnvironmentRegistrationStoreResult> CreateAsync(
            EnvironmentRegistration registration,
            CancellationToken cancellationToken)
        {
            Registration = registration;
            return Task.FromResult(EnvironmentRegistrationStoreResult.Created);
        }
    }
}
