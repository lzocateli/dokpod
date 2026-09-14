using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Environments;

namespace Dokpod.ControlPlane.Application.Environments;

public sealed record EnvironmentAccessDecisionResult(
    bool Allowed,
    string? FailureCode);

public sealed class EnvironmentAccessService(
    IAuditEventWriter auditEventWriter,
    IEnvironmentAuthorizationDecider authorizationDecider)
{
    public Task<EnvironmentAccessDecisionResult> RegisterAsync(
        EnvironmentRegistration registration,
        string requiredScope,
        AuthenticatedActor actor,
        AuditActorKind actorKind,
        Guid correlationId,
        CancellationToken cancellationToken) => RegisterAsync(
            registration,
            requiredScope,
            actor,
            string.Empty,
            actorKind,
            correlationId,
            cancellationToken);

    public async Task<EnvironmentAccessDecisionResult> RegisterAsync(
        EnvironmentRegistration registration,
        string requiredScope,
        AuthenticatedActor actor,
        string accessToken,
        AuditActorKind actorKind,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(actor);

        EnvironmentAuthorizationDecision authorization;
        try
        {
            authorization = await authorizationDecider.DecideAsync(
                $"urn:dokpod:environment:{registration.EnvironmentId:D}",
                requiredScope,
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
            authorization = new EnvironmentAuthorizationDecision(
                AuthorizationDecisionOutcome.Indeterminate,
                "authorization_unavailable");
        }

        if (!Enum.IsDefined(authorization.Outcome) ||
            (!authorization.Allowed && string.IsNullOrWhiteSpace(authorization.FailureCode)))
        {
            authorization = new EnvironmentAuthorizationDecision(
                AuthorizationDecisionOutcome.Indeterminate,
                "authorization_invalid");
        }

        var outcome = authorization.Outcome switch
        {
            AuthorizationDecisionOutcome.Allowed => AuditOutcome.Succeeded,
            AuthorizationDecisionOutcome.Denied => AuditOutcome.Denied,
            AuthorizationDecisionOutcome.Indeterminate => AuditOutcome.Indeterminate,
            _ => throw new ArgumentOutOfRangeException(),
        };
        var failureCode = authorization.Allowed ? null : authorization.FailureCode;

        var auditEvent = AuditEvent.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            correlationId,
            actorKind,
            actor.Subject,
            "environment.register",
            registration.EnvironmentId,
            outcome,
            failureCode);

        await auditEventWriter.AppendAsync(auditEvent, cancellationToken).ConfigureAwait(false);

        return new EnvironmentAccessDecisionResult(authorization.Allowed, failureCode);
    }
}
