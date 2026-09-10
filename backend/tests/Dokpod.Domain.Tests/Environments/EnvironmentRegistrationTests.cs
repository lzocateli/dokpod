using Dokpod.Domain.Environments;
using Xunit;

namespace Dokpod.Domain.Tests.Environments;

public sealed class EnvironmentRegistrationTests
{
    [Fact]
    public void Create_RejectsEmptyEnvironmentName()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            EnvironmentRegistration.Create(
                Guid.NewGuid(),
                "   ",
                "lab",
                enabled: true,
                scopes: [EnvironmentResourceScopes.Read]));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Create_RejectsUnsupportedScope()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            EnvironmentRegistration.Create(
                Guid.NewGuid(),
                "prod-lab",
                "lab",
                enabled: true,
                scopes: ["environment:unknown"]));

        Assert.Equal("scopes", exception.ParamName);
    }

    [Fact]
    public void Evaluate_RequiresExactScopeToAllowAccess()
    {
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes: [EnvironmentResourceScopes.Read, EnvironmentResourceScopes.Manage]);

        var allowed = EnvironmentAccessDecision.Evaluate(registration, EnvironmentResourceScopes.Read);
        var denied = EnvironmentAccessDecision.Evaluate(registration, EnvironmentResourceScopes.StartContainer);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
        Assert.Equal("scope_missing", denied.Reason);
    }

    [Fact]
    public void Create_CopiesScopesToPreventExternalMutation()
    {
        var scopes = new HashSet<string>([EnvironmentResourceScopes.Read], StringComparer.Ordinal);
        var registration = EnvironmentRegistration.Create(
            Guid.NewGuid(),
            "prod-lab",
            "lab",
            enabled: true,
            scopes);

        scopes.Add(EnvironmentResourceScopes.Manage);

        Assert.DoesNotContain(EnvironmentResourceScopes.Manage, registration.Scopes);
    }
}
