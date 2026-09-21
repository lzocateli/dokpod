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
    public async Task ListAsync_OmitsDeniedEnvironments()
    {
        var allowed = CreateRegistration();
        var denied = CreateRegistration();
        var store = new RecordingEnvironmentRegistrationStore
        {
            PageToRead = new EnvironmentRegistrationPage([allowed, denied], null)
        };
        var service = CreateService(
            new ResourceAuthorizationDecider(allowed.EnvironmentId),
            store);

        var result = await service.ListAsync(
            null,
            20,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Available);
        Assert.Equal([allowed], result.Registrations);
    }

    [Fact]
    public async Task ListAsync_WhenAuthorizationIsIndeterminate_ReturnsNoPartialPage()
    {
        var store = new RecordingEnvironmentRegistrationStore
        {
            PageToRead = new EnvironmentRegistrationPage([CreateRegistration()], null)
        };
        var service = CreateService(
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(
                    AuthorizationDecisionOutcome.Indeterminate,
                    "authorization_timeout")),
            store);

        var result = await service.ListAsync(
            null,
            20,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Available);
        Assert.Empty(result.Registrations);
        Assert.Equal("authorization_timeout", result.FailureCode);
    }

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

    private sealed class ResourceAuthorizationDecider(Guid allowedEnvironmentId)
        : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(resource.EndsWith(allowedEnvironmentId.ToString("D"), StringComparison.Ordinal)
                ? new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null)
                : new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "scope_denied"));
    }

    private sealed class RecordingEnvironmentRegistrationStore : IEnvironmentRegistrationStore
    {
        public EnvironmentRegistration? Registration { get; private set; }
        public EnvironmentRegistration? RegistrationToRead { get; init; }
        public EnvironmentRegistrationPage PageToRead { get; init; } = new([], null);
        public bool WasRead { get; private set; }

        public Task<EnvironmentRegistrationPage> ListAsync(
            Guid? afterEnvironmentId,
            int limit,
            CancellationToken cancellationToken) => Task.FromResult(PageToRead);

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
