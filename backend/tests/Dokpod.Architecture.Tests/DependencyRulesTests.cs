using System.Reflection;
using Dokpod.Agent;
using Dokpod.Agent.Application.Commands;
using Dokpod.Agent.Contracts.V1;
using Dokpod.Agent.Infrastructure.Engines.Docker;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.Domain.Commands;
using Xunit;

namespace Dokpod.Architecture.Tests;

public sealed class DependencyRulesTests
{
    [Theory]
    [MemberData(nameof(ModuleDependencies))]
    public void Module_ReferencesOnlyAllowedDokpodAssemblies(Assembly assembly, string[] allowedDependencies)
    {
        var actualDependencies = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("Dokpod.", StringComparison.Ordinal))
            .Cast<string>()
            .Order()
            .ToArray();

        Assert.Equal(allowedDependencies.Order(), actualDependencies);
    }

    public static TheoryData<Assembly, string[]> ModuleDependencies => new()
    {
        { typeof(Dokpod.Domain.Commands.AgentCommand).Assembly, [] },
        { typeof(AgentHello).Assembly, [] },
        { typeof(AgentCommandProcessor).Assembly, ["Dokpod.Domain"] },
        { typeof(DockerEngineClient).Assembly, ["Dokpod.Agent.Application", "Dokpod.Domain"] },
        { typeof(AgentSessionNegotiator).Assembly, ["Dokpod.Agent.Contracts"] },
        { typeof(AgentOptions).Assembly, ["Dokpod.Agent.Application", "Dokpod.Agent.Infrastructure", "Dokpod.Domain"] },
        { Assembly.Load("Dokpod.Bff"), [] },
        { Assembly.Load("Dokpod.ControlPlane.Api"), ["Dokpod.Agent.Contracts", "Dokpod.ControlPlane.Application"] },
    };
}