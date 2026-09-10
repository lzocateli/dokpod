using Dokpod.Domain.Auditing;
using Xunit;

namespace Dokpod.Domain.Tests.Auditing;

public sealed class AuditEventTests
{
    [Fact]
    public void Create_RequiresEnvironmentAndCorrelationIdentifiers()
    {
        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            AuditActorKind.User,
            "user-123",
            "environment.register",
            Guid.Empty,
            AuditOutcome.Succeeded));

        Assert.Equal("environmentId", exception.ParamName);
    }

    [Fact]
    public void Create_RejectsNonUtcTimestamp()
    {
        var localTime = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.FromHours(-3));

        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            Guid.NewGuid(),
            localTime,
            Guid.NewGuid(),
            AuditActorKind.User,
            "user-123",
            "environment.register",
            Guid.NewGuid(),
            AuditOutcome.Succeeded));

        Assert.Equal("occurredAtUtc", exception.ParamName);
    }

    [Fact]
    public void Create_RequiresFailureCodeForFailedOutcome()
    {
        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            AuditActorKind.System,
            "control-plane",
            "environment.register",
            Guid.NewGuid(),
            AuditOutcome.Failed));

        Assert.Equal("failureCode", exception.ParamName);
    }

    [Fact]
    public void Create_RejectsActionsOutsideTheAllowlist()
    {
        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            AuditActorKind.Agent,
            "agent-123",
            "container.exec",
            Guid.NewGuid(),
            AuditOutcome.Denied));

        Assert.Equal("action", exception.ParamName);
    }

    [Theory]
    [InlineData(AuditOutcome.Failed)]
    [InlineData(AuditOutcome.Denied)]
    [InlineData(AuditOutcome.Indeterminate)]
    public void Create_RequiresFailureCodeForUnsuccessfulOutcomes(AuditOutcome outcome)
    {
        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            AuditActorKind.System,
            "control-plane",
            "environment.register",
            Guid.NewGuid(),
            outcome));

        Assert.Equal("failureCode", exception.ParamName);
    }

    [Fact]
    public void Create_RejectsControlCharactersAndOversizedActorIds()
    {
        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            AuditActorKind.User,
            $"user-{Environment.NewLine}",
            "environment.register",
            Guid.NewGuid(),
            AuditOutcome.Succeeded));

        Assert.Equal("actorId", exception.ParamName);

        exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            AuditActorKind.User,
            new string('x', AuditEvent.MaxActorIdLength + 1),
            "environment.register",
            Guid.NewGuid(),
            AuditOutcome.Succeeded));

        Assert.Equal("actorId", exception.ParamName);
    }

    [Fact]
    public void Create_TrimsNonSensitiveIdentifiersWithoutStoringPayload()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();

        var auditEvent = AuditEvent.Create(
            eventId,
            DateTimeOffset.UtcNow,
            correlationId,
            AuditActorKind.User,
            " user-123 ",
            "environment.register",
            environmentId,
            AuditOutcome.Succeeded);

        Assert.Equal(eventId, auditEvent.EventId);
        Assert.Equal(correlationId, auditEvent.CorrelationId);
        Assert.Equal(environmentId, auditEvent.EnvironmentId);
        Assert.Equal("user-123", auditEvent.ActorId);
        Assert.Null(auditEvent.FailureCode);
    }
}
