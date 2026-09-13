using System.Xml.Linq;
using Xunit;

namespace Dokpod.Architecture.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ControlPlaneApplication_ReferencesDomainAndNotInfrastructure()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../libs/Dokpod.ControlPlane.Application/Dokpod.ControlPlane.Application.csproj");

        var includes = GetProjectReferences(projectPath);

        Assert.Contains(includes, value => value.Contains("Dokpod.Domain/Dokpod.Domain.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includes, value => value.Contains("Dokpod.Agent.Infrastructure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includes, value => value.Contains("Dokpod.ControlPlane.Api", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AgentApplication_DoesNotDependOnControlPlaneContractsOrApi()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../libs/Dokpod.Agent.Application/Dokpod.Agent.Application.csproj");

        var includes = GetProjectReferences(projectPath);

        Assert.DoesNotContain(includes, value => value.Contains("Dokpod.ControlPlane.Application", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includes, value => value.Contains("Dokpod.ControlPlane.Api", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ControlPlaneInfrastructure_ReferencesApplicationAndDomainOnly()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../libs/Dokpod.ControlPlane.Infrastructure/Dokpod.ControlPlane.Infrastructure.csproj");

        var includes = GetProjectReferences(projectPath);

        Assert.Contains(includes, value => value.Contains("Dokpod.ControlPlane.Application/Dokpod.ControlPlane.Application.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(includes, value => value.Contains("Dokpod.Domain/Dokpod.Domain.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includes, value => value.Contains("Dokpod.ControlPlane.Api", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DomainAndApplication_DoNotReferenceEntityFrameworkCore()
    {
        var backendRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var protectedProjects = new[]
        {
            Path.Combine(backendRoot, "libs/Dokpod.Domain"),
            Path.Combine(backendRoot, "libs/Dokpod.ControlPlane.Application"),
            Path.Combine(backendRoot, "libs/Dokpod.Agent.Application"),
        };

        foreach (var projectDirectory in protectedProjects)
        {
            var projectFiles = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
            foreach (var projectFile in projectFiles)
            {
                var projectText = File.ReadAllText(projectFile);
                Assert.DoesNotContain("EntityFrameworkCore", projectText, StringComparison.OrdinalIgnoreCase);
            }

            var sourceFiles = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
            foreach (var sourceFile in sourceFiles)
            {
                var sourceText = File.ReadAllText(sourceFile);
                Assert.DoesNotContain("Microsoft.EntityFrameworkCore", sourceText, StringComparison.Ordinal);
            }
        }
    }

    private static IReadOnlyList<string> GetProjectReferences(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var document = XDocument.Load(fullPath);

        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}
