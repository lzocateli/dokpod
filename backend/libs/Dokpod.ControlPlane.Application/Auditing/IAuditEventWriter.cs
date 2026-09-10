using Dokpod.Domain.Auditing;

namespace Dokpod.ControlPlane.Application.Auditing;

public interface IAuditEventWriter
{
    Task AppendAsync(
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
