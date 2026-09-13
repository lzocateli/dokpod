using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.ControlPlane.Infrastructure;
using Dokpod.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class AuditEventWriterTests
{
    [Fact]
    public async Task AppendAsync_WithNewEvent_PersistsAuditEvent()
    {
        await using var context = CreateContext();
        var writer = new PostgresAuditEventWriter(context);
        var auditEvent = CreateAuditEvent();

        await writer.AppendAsync(auditEvent, TestContext.Current.CancellationToken);

        var persisted = await context.AuditEvents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(auditEvent.EventId, persisted.EventId);
        Assert.Equal(auditEvent.CorrelationId, persisted.CorrelationId);
        Assert.Equal(auditEvent.EnvironmentId, persisted.EnvironmentId);
        Assert.Equal((int)auditEvent.ActorKind, persisted.ActorKind);
        Assert.Equal(auditEvent.Action, persisted.Action);
        Assert.Equal((int)auditEvent.Outcome, persisted.Outcome);
    }

    [Fact]
    public async Task AppendAsync_WithSameEventIdAndSamePayload_IsIdempotent()
    {
        await using var context = CreateContext();
        var writer = new PostgresAuditEventWriter(context);
        var auditEvent = CreateAuditEvent();

        await writer.AppendAsync(auditEvent, TestContext.Current.CancellationToken);
        await writer.AppendAsync(auditEvent, TestContext.Current.CancellationToken);

        Assert.Equal(1, await context.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AppendAsync_WithSameEventIdAndDifferentPayload_Throws()
    {
        await using var context = CreateContext();
        var writer = new PostgresAuditEventWriter(context);
        var original = CreateAuditEvent();
        var conflicting = AuditEvent.Create(
            original.EventId,
            original.OccurredAtUtc,
            original.CorrelationId,
            original.ActorKind,
            original.ActorId,
            "environment.approve",
            original.EnvironmentId,
            original.Outcome,
            "AUTHZ_DENIED");

        await writer.AppendAsync(original, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.AppendAsync(conflicting, TestContext.Current.CancellationToken));

        Assert.Equal(1, await context.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AppendAsync_WithCanceledToken_ThrowsOperationCanceledException()
    {
        await using var context = CreateContext();
        var writer = new PostgresAuditEventWriter(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.AppendAsync(CreateAuditEvent(), cancellation.Token));
    }

    private static ControlPlaneDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ControlPlaneDbContext(options);
    }

    private static AuditEvent CreateAuditEvent()
    {
        return AuditEvent.Create(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            AuditActorKind.User,
            "user-123",
            "environment.register",
            Guid.NewGuid(),
            AuditOutcome.Succeeded);
    }
}
