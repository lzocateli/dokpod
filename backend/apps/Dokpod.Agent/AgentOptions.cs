namespace Dokpod.Agent;

public sealed record AgentOptions(
    string DataDirectory,
    string DockerSocketPath,
    TimeSpan EngineTimeout,
    TimeSpan InventoryInterval,
    bool RunOnce)
{
    public static AgentOptions FromEnvironment()
    {
        var defaultDataDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Dokpod", "Agent")
            : "/var/lib/dokpod-agent";

        return new AgentOptions(
            GetAbsolutePath("DOKPOD_AGENT_DATA_DIRECTORY", defaultDataDirectory),
            GetAbsolutePath("DOKPOD_AGENT_DOCKER_SOCKET", "/run/docker.sock"),
            TimeSpan.FromSeconds(GetBoundedInteger("DOKPOD_AGENT_ENGINE_TIMEOUT_SECONDS", 30, 1, 300)),
            TimeSpan.FromSeconds(GetBoundedInteger("DOKPOD_AGENT_INVENTORY_INTERVAL_SECONDS", 30, 5, 900)),
            string.Equals(Environment.GetEnvironmentVariable("DOKPOD_AGENT_RUN_ONCE"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetAbsolutePath(string variableName, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variableName) ?? defaultValue;
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException($"{variableName} must be an absolute path.");
        }

        return Path.GetFullPath(value);
    }

    private static int GetBoundedInteger(string variableName, int defaultValue, int minimum, int maximum)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException($"{variableName} must be between {minimum} and {maximum}.");
        }

        return parsed;
    }
}