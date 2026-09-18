using System.Diagnostics.Metrics;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class AuditEventMetrics : IDisposable
{
    public const string MeterName = "Dokpod.ControlPlane.Auditing";

    private readonly Meter meter = new(MeterName, "1.0.0");
    private readonly Counter<long> appendAttempts;
    private readonly Counter<long> appendFailures;
    private readonly Counter<long> idempotencyConflicts;
    private readonly Histogram<double> appendDuration;

    public AuditEventMetrics()
    {
        appendAttempts = meter.CreateCounter<long>("dokpod.audit.append.attempts");
        appendFailures = meter.CreateCounter<long>("dokpod.audit.append.failures");
        idempotencyConflicts = meter.CreateCounter<long>("dokpod.audit.idempotency.conflicts");
        appendDuration = meter.CreateHistogram<double>("dokpod.audit.append.duration", "ms");
    }

    public void RecordAttempt() => appendAttempts.Add(1);

    public void RecordFailure() => appendFailures.Add(1);

    public void RecordConflict() => idempotencyConflicts.Add(1);

    public void RecordDuration(TimeSpan duration) => appendDuration.Record(duration.TotalMilliseconds);

    public void Dispose() => meter.Dispose();
}

