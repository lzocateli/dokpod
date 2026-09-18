using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task ControlPlaneContract_DocumentsEnvironmentEndpointsAndProblemResponses()
    {
        var contract = await File.ReadAllTextAsync(
            FindContractPath(),
            TestContext.Current.CancellationToken);

        Assert.Contains("openapi: 3.1.0", contract, StringComparison.Ordinal);
        Assert.Contains("/api/v1/environments/{environmentId}:", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: getEnvironment", contract, StringComparison.Ordinal);
        Assert.Contains("/api/v1/environments:", contract, StringComparison.Ordinal);
        Assert.Contains("operationId: registerEnvironment", contract, StringComparison.Ordinal);
        Assert.Contains("application/problem+json:", contract, StringComparison.Ordinal);
        Assert.Contains("'201':", contract, StringComparison.Ordinal);
        Assert.Contains("'400':", contract, StringComparison.Ordinal);
        Assert.Contains("'401':", contract, StringComparison.Ordinal);
        Assert.Contains("'403':", contract, StringComparison.Ordinal);
        Assert.Contains("'404':", contract, StringComparison.Ordinal);
        Assert.Contains("'409':", contract, StringComparison.Ordinal);
        Assert.Contains("'503':", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditEventEntity", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("EnvironmentRegistrationEntity", contract, StringComparison.Ordinal);
    }

    private static string FindContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "contracts",
                "openapi",
                "dokpod-control-plane.v1.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("OpenAPI contract was not found.");
    }
}
