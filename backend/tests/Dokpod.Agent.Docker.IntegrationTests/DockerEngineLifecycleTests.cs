using System.Net;
using System.Text;
using System.Text.Json;
using Dokpod.Agent.Infrastructure.Engines.Docker;
using Dokpod.Domain.Commands;
using Xunit;

namespace Dokpod.Agent.Docker.IntegrationTests;

public sealed class DockerEngineLifecycleTests
{
    private const string FixtureImage = "lzocateli/dotnet-sdk:10.0.400-noble";

    [Fact]
    public async Task Adapter_ExecutesSupportedLifecycleAgainstRealDockerEngine()
    {
        using var httpClient = DockerEngineClient.CreateUnixSocketClient(
            "/var/run/docker.sock",
            TimeSpan.FromSeconds(30));
        var engine = new DockerEngineClient(httpClient);
        var descriptor = await engine.InspectAsync(TestContext.Current.CancellationToken);
        var containerId = await CreateFixtureAsync(httpClient, descriptor.ApiVersion, TestContext.Current.CancellationToken);

        try
        {
            Assert.True((await engine.ExecuteAsync(
                AgentCommandKind.StartContainer,
                containerId,
                TestContext.Current.CancellationToken)).Succeeded);

            var containers = await engine.ListContainersAsync(TestContext.Current.CancellationToken);
            Assert.Contains(containers, container => container.ContainerId == containerId && container.State == "running");

            Assert.True((await engine.ExecuteAsync(
                AgentCommandKind.RestartContainer,
                containerId,
                TestContext.Current.CancellationToken)).Succeeded);
            Assert.True((await engine.ExecuteAsync(
                AgentCommandKind.StopContainer,
                containerId,
                TestContext.Current.CancellationToken)).Succeeded);
            Assert.True((await engine.ExecuteAsync(
                AgentCommandKind.DeleteContainer,
                containerId,
                TestContext.Current.CancellationToken)).Succeeded);

            containerId = string.Empty;
        }
        finally
        {
            if (containerId.Length > 0)
            {
                await DeleteFixtureAsync(httpClient, descriptor.ApiVersion, containerId);
            }
        }
    }

    private static async Task<string> CreateFixtureAsync(
        HttpClient httpClient,
        string apiVersion,
        CancellationToken cancellationToken)
    {
        var name = $"dokpod-integration-{Guid.NewGuid():N}";
        using var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                Image = FixtureImage,
                Cmd = new[] { "/bin/sh", "-c", "sleep 300" },
            }),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.PostAsync(
            $"v{apiVersion}/containers/create?name={name}",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("Id").GetString()
            ?? throw new InvalidDataException("Docker returned an empty fixture ID.");
    }

    private static async Task DeleteFixtureAsync(HttpClient httpClient, string apiVersion, string containerId)
    {
        using var response = await httpClient.DeleteAsync(
            $"v{apiVersion}/containers/{containerId}?v=0&force=1",
            CancellationToken.None);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }
}