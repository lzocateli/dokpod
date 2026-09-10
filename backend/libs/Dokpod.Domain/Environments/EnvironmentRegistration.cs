using System.Collections.Frozen;

namespace Dokpod.Domain.Environments;

public static class EnvironmentResourceScopes
{
    public const string Read = "environment:read";
    public const string Manage = "environment:manage";
    public const string StartContainer = "container:start";
    public const string StopContainer = "container:stop";
    public const string RestartContainer = "container:restart";
    public const string DeleteContainer = "container:delete";
    public const string AuditRead = "audit:read";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
    [
        Read,
        Manage,
        StartContainer,
        StopContainer,
        RestartContainer,
        DeleteContainer,
        AuditRead,
    ], StringComparer.Ordinal).ToFrozenSet(StringComparer.Ordinal);
}

public sealed record EnvironmentRegistration(
    Guid EnvironmentId,
    string Name,
    string Host,
    bool Enabled,
    IReadOnlySet<string> Scopes)
{
    public static EnvironmentRegistration Create(
        Guid environmentId,
        string name,
        string host,
        bool enabled,
        IEnumerable<string> scopes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Environment name is required.", nameof(name));
        }

        var resolvedScopes = new HashSet<string>(
            scopes ?? throw new ArgumentNullException(nameof(scopes)),
            StringComparer.Ordinal);

        if (resolvedScopes.Any(scope => !EnvironmentResourceScopes.Supported.Contains(scope)))
        {
            throw new ArgumentException("Unsupported environment scope.", nameof(scopes));
        }

        return new EnvironmentRegistration(
            environmentId,
            name.Trim(),
            host.Trim(),
            enabled,
            resolvedScopes.ToFrozenSet(StringComparer.Ordinal));
    }
}

public sealed record EnvironmentAccessDecision(bool Allowed, string? Reason)
{
    public static EnvironmentAccessDecision Evaluate(
        EnvironmentRegistration registration,
        string requiredScope)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!EnvironmentResourceScopes.Supported.Contains(requiredScope))
        {
            throw new ArgumentException("Unsupported environment scope.", nameof(requiredScope));
        }

        if (!registration.Enabled)
        {
            return new EnvironmentAccessDecision(false, "environment_disabled");
        }

        return registration.Scopes.Contains(requiredScope)
            ? new EnvironmentAccessDecision(true, null)
            : new EnvironmentAccessDecision(false, "scope_missing");
    }
}
