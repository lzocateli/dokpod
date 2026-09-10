namespace Dokpod.Domain.Auditing;

public enum AuditActorKind
{
    User,
    Agent,
    System,
}

public enum AuditOutcome
{
    Succeeded,
    Failed,
    Denied,
    Indeterminate,
}

public sealed record AuditEvent
{
    public const int MaxActorIdLength = 128;
    public const int MaxFailureCodeLength = 128;

    private static readonly HashSet<string> SupportedActions =
    [
        "environment.register",
        "environment.approve",
        "environment.suspend",
        "environment.revoke",
    ];

    private AuditEvent(
        Guid eventId,
        DateTimeOffset occurredAtUtc,
        Guid correlationId,
        AuditActorKind actorKind,
        string actorId,
        string action,
        Guid environmentId,
        AuditOutcome outcome,
        string? failureCode)
    {
        EventId = eventId;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        ActorKind = actorKind;
        ActorId = actorId;
        Action = action;
        EnvironmentId = environmentId;
        Outcome = outcome;
        FailureCode = failureCode;
    }

    public Guid EventId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public Guid CorrelationId { get; }
    public AuditActorKind ActorKind { get; }
    public string ActorId { get; }
    public string Action { get; }
    public Guid EnvironmentId { get; }
    public AuditOutcome Outcome { get; }
    public string? FailureCode { get; }

    public static AuditEvent Create(
        Guid eventId,
        DateTimeOffset occurredAtUtc,
        Guid correlationId,
        AuditActorKind actorKind,
        string actorId,
        string action,
        Guid environmentId,
        AuditOutcome outcome,
        string? failureCode = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Audit event ID is required.", nameof(eventId));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit timestamp must be UTC.", nameof(occurredAtUtc));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        if (!Enum.IsDefined(actorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind));
        }

        var normalizedActorId = NormalizeIdentifier(actorId, nameof(actorId), MaxActorIdLength);
        if (normalizedActorId is null)
        {
            throw new ArgumentException("Audit actor ID is required.", nameof(actorId));
        }

        if (!SupportedActions.Contains(action))
        {
            throw new ArgumentException("Unsupported audit action.", nameof(action));
        }

        if (environmentId == Guid.Empty)
        {
            throw new ArgumentException("Environment ID is required.", nameof(environmentId));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        var normalizedFailureCode = NormalizeIdentifier(failureCode, nameof(failureCode), MaxFailureCodeLength);
        if (outcome is AuditOutcome.Failed or AuditOutcome.Denied or AuditOutcome.Indeterminate &&
            normalizedFailureCode is null)
        {
            throw new ArgumentException("Unsuccessful audit events require a failure code.", nameof(failureCode));
        }

        return new AuditEvent(
            eventId,
            occurredAtUtc,
            correlationId,
            actorKind,
            normalizedActorId,
            action,
            environmentId,
            outcome,
            normalizedFailureCode);
    }

    private static string? NormalizeIdentifier(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Audit identifier has an invalid format.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException("Audit identifier has an invalid format.", parameterName);
        }

        return normalized;
    }
}
