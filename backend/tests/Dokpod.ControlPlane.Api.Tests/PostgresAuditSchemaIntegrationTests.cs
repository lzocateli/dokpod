using Dokpod.ControlPlane.Infrastructure;
using Dokpod.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresAuditSchemaIntegrationTests
{
    [Fact]
    public async Task AuditEvents_RouteRowsToMonthlyAndDefaultPartitions()
    {
        await using var context = CreateContextOrSkip();
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var monthlyEventId = Guid.NewGuid();
        var defaultEventId = Guid.NewGuid();
        await InsertAsync(context, monthlyEventId, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));
        await InsertAsync(context, defaultEventId, new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var monthlyPartition = await GetPartitionAsync(context, monthlyEventId);
        var defaultPartition = await GetPartitionAsync(context, defaultEventId);

        Assert.Equal("audit_events_2026_09", monthlyPartition);
        Assert.Equal("audit_events_default", defaultPartition);
    }

    [Fact]
    public async Task AuditEvents_RejectMutation()
    {
        await using var context = CreateContextOrSkip();
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var eventId = Guid.NewGuid();
        await InsertAsync(context, eventId, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));
        const string changedActor = "changed";

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit_events SET actor_id = {changedActor} WHERE event_id = {eventId}", TestContext.Current.CancellationToken));

        Assert.Contains("append-only", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writer_WithConcurrentSameEvent_IsIdempotent()
    {
        await using var firstContext = CreateContextOrSkip();
        await using var secondContext = CreateContextOrSkip();
        var auditEvent = CreateAuditEvent();
        var firstTask = new PostgresAuditEventWriter(firstContext)
            .AppendAsync(auditEvent, TestContext.Current.CancellationToken);
        var secondTask = new PostgresAuditEventWriter(secondContext)
            .AppendAsync(auditEvent, TestContext.Current.CancellationToken);

        await Task.WhenAll(firstTask, secondTask);

        var count = await firstContext.AuditEvents
            .CountAsync(x => x.EventId == auditEvent.EventId, TestContext.Current.CancellationToken);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Writer_WithConcurrentConflictingPayload_RejectsOneWriter()
    {
        await using var firstContext = CreateContextOrSkip();
        await using var secondContext = CreateContextOrSkip();
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
        var tasks = new[]
        {
            new PostgresAuditEventWriter(firstContext).AppendAsync(original, TestContext.Current.CancellationToken),
            new PostgresAuditEventWriter(secondContext).AppendAsync(conflicting, TestContext.Current.CancellationToken),
        };

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Equal(1, tasks.Count(task => task.IsCompletedSuccessfully));
        Assert.Contains(tasks, task => task.Exception?.GetBaseException() is InvalidOperationException);
    }

    private static ControlPlaneDbContext CreateContextOrSkip()
    {
        var connectionString = Environment.GetEnvironmentVariable("DOKPOD_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("DOKPOD_TEST_POSTGRES_CONNECTION is not configured.");
        }

        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ControlPlaneDbContext(options);
    }

    private static async Task InsertAsync(
        ControlPlaneDbContext context,
        Guid eventId,
        DateTimeOffset occurredAtUtc)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_events (
                event_id,
                occurred_at_utc,
                correlation_id,
                actor_kind,
                actor_id,
                action,
                environment_id,
                outcome)
            VALUES (
                {eventId},
                {occurredAtUtc},
                {Guid.NewGuid()},
                {1},
                {"integration-test"},
                {"environment.register"},
                {Guid.NewGuid()},
                {1})
            """, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetPartitionAsync(ControlPlaneDbContext context, Guid eventId)
    {
        var rows = await context.Database
            .SqlQueryRaw<string>("SELECT tableoid::regclass::text AS \"Value\" FROM audit_events WHERE event_id = {0}", eventId)
            .ToListAsync(TestContext.Current.CancellationToken);

        return Assert.Single(rows);
    }

    private static AuditEvent CreateAuditEvent()
    {
        return AuditEvent.Create(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            AuditActorKind.User,
            "integration-test",
            "environment.register",
            Guid.NewGuid(),
            AuditOutcome.Succeeded);
    }
}