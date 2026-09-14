using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Environments;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class EnvironmentAccessServiceTests
{
    [Fact]
    public async Task RegisterAsync_WhenScopeIsMissing_WritesDeniedAuditEvent()
    {
        var writer = new RecordingAuditEventWriter();
        var authorization = new RecordingAuthorizationDecider(
            new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "scope_missing"));
        var service = new EnvironmentAccessService(writer, authorization);
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes: [EnvironmentResourceScopes.Read]);

        var result = await service.RegisterAsync(
            registration,
            requiredScope: EnvironmentResourceScopes.Manage,
            actor: AuthenticatedActor.FromSubject("user-42"),
            actorKind: AuditActorKind.User,
            correlationId: Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.Equal("scope_missing", result.FailureCode);
        Assert.NotNull(writer.Event);
        Assert.Equal("environment.register", writer.Event!.Action);
        Assert.Equal(AuditOutcome.Denied, writer.Event.Outcome);
    }

    [Fact]
    public async Task RegisterAsync_WhenScopeIsAllowed_WritesSucceededAuditEvent()
    {
        var writer = new RecordingAuditEventWriter();
        var authorization = new RecordingAuthorizationDecider(
            new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null));
        var service = new EnvironmentAccessService(writer, authorization);
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes: [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);

        var result = await service.RegisterAsync(
            registration,
            requiredScope: EnvironmentResourceScopes.Manage,
            actor: AuthenticatedActor.FromSubject("user-42"),
            actorKind: AuditActorKind.User,
            correlationId: Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Allowed);
        Assert.Null(result.FailureCode);
        Assert.NotNull(writer.Event);
        Assert.Equal("environment.register", writer.Event!.Action);
        Assert.Equal(AuditOutcome.Succeeded, writer.Event.Outcome);
        Assert.Equal("urn:dokpod:environment:" + registration.EnvironmentId.ToString("D"), authorization.Resource);
        Assert.Equal(EnvironmentResourceScopes.Manage, authorization.Scope);
        Assert.Equal("user-42", authorization.Actor?.Subject);
        Assert.Equal(writer.Event.CorrelationId, authorization.CorrelationId);
        Assert.DoesNotContain("prod-lab", authorization.Resource!, StringComparison.Ordinal);
        Assert.DoesNotContain("lab", authorization.Resource!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterAsync_WhenExternalDecisionIsDenied_DoesNotUseLocalScope()
    {
        var writer = new RecordingAuditEventWriter();
        var authorization = new RecordingAuthorizationDecider(
            new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "keycloak_denied"));
        var service = new EnvironmentAccessService(writer, authorization);
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes: [EnvironmentResourceScopes.Manage]);

        var result = await service.RegisterAsync(
            registration,
            requiredScope: EnvironmentResourceScopes.Manage,
            actor: AuthenticatedActor.FromSubject("user-42"),
            actorKind: AuditActorKind.User,
            correlationId: Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.Equal("keycloak_denied", result.FailureCode);
        Assert.Equal(AuditOutcome.Denied, writer.Event!.Outcome);
    }

    [Fact]
    public async Task RegisterAsync_WhenExternalDecisionIsIndeterminate_FailsClosed()
    {
        var writer = new RecordingAuditEventWriter();
        var service = new EnvironmentAccessService(
            writer,
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Indeterminate, "keycloak_unavailable")));
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes: [EnvironmentResourceScopes.Manage]);

        var result = await service.RegisterAsync(
            registration,
            requiredScope: EnvironmentResourceScopes.Manage,
            actor: AuthenticatedActor.FromSubject("user-42"),
            actorKind: AuditActorKind.User,
            correlationId: Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.Equal("keycloak_unavailable", result.FailureCode);
        Assert.Equal(AuditOutcome.Indeterminate, writer.Event!.Outcome);
    }

    [Fact]
    public async Task RegisterAsync_WhenAuditPersistenceFails_DoesNotConfirmRegistration()
    {
        var service = new EnvironmentAccessService(
            new FailingAuditEventWriter(),
            new RecordingAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null)));
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes: [EnvironmentResourceScopes.Manage]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterAsync(
                registration,
                requiredScope: EnvironmentResourceScopes.Manage,
                actor: AuthenticatedActor.FromSubject("user-42"),
                actorKind: AuditActorKind.User,
                correlationId: Guid.NewGuid(),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_WhenAuthorizationThrows_AuditsIndeterminateAndFailsClosed()
    {
        var writer = new RecordingAuditEventWriter();
        var service = new EnvironmentAccessService(writer, new ThrowingAuthorizationDecider());
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes: [EnvironmentResourceScopes.Manage]);

        var result = await service.RegisterAsync(
            registration,
            requiredScope: EnvironmentResourceScopes.Manage,
            actor: AuthenticatedActor.FromSubject("user-42"),
            actorKind: AuditActorKind.User,
            correlationId: Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.Equal("authorization_unavailable", result.FailureCode);
        Assert.Equal(AuditOutcome.Indeterminate, writer.Event!.Outcome);
    }

    [Theory]
    [InlineData(AuthorizationDecisionOutcome.Denied)]
    [InlineData(AuthorizationDecisionOutcome.Indeterminate)]
    public async Task RegisterAsync_WhenAuthorizationDecisionIsInvalid_AuditsControlledIndeterminate(
        AuthorizationDecisionOutcome outcome)
    {
        var writer = new RecordingAuditEventWriter();
        var service = new EnvironmentAccessService(
            writer,
            new RecordingAuthorizationDecider(new EnvironmentAuthorizationDecision(outcome, null)));
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(), "prod-lab", "lab", true, [EnvironmentResourceScopes.Manage]);

        var result = await service.RegisterAsync(
            registration,
            EnvironmentResourceScopes.Manage,
            AuthenticatedActor.FromSubject("user-42"),
            AuditActorKind.User,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.Equal("authorization_invalid", result.FailureCode);
        Assert.Equal(AuditOutcome.Indeterminate, writer.Event!.Outcome);
    }

    [Fact]
    public async Task RegisterAsync_WhenAuthorizationIsCanceled_PropagatesCancellationWithoutAudit()
    {
        var writer = new RecordingAuditEventWriter();
        var service = new EnvironmentAccessService(writer, new CanceledAuthorizationDecider());
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(), "prod-lab", "lab", true, [EnvironmentResourceScopes.Manage]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RegisterAsync(
                registration,
                EnvironmentResourceScopes.Manage,
                AuthenticatedActor.FromSubject("user-42"),
                AuditActorKind.User,
                Guid.NewGuid(),
                cancellation.Token));

        Assert.Null(writer.Event);
    }

    private sealed class RecordingAuditEventWriter : IAuditEventWriter
    {
        public AuditEvent? Event { get; private set; }

        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Event = auditEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingAuditEventWriter : IAuditEventWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Audit persistence unavailable.");
        }
    }

    private sealed class RecordingAuthorizationDecider(EnvironmentAuthorizationDecision decision)
        : IEnvironmentAuthorizationDecider
    {
        public string? Resource { get; private set; }
        public string? Scope { get; private set; }
        public AuthenticatedActor? Actor { get; private set; }
        public Guid CorrelationId { get; private set; }

        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            Resource = resource;
            Scope = scope;
            Actor = actor;
            CorrelationId = correlationId;
            return Task.FromResult(decision);
        }
    }

    private sealed class ThrowingAuthorizationDecider : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Authorization provider unavailable.");
        }
    }

    private sealed class CanceledAuthorizationDecider : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
