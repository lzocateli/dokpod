namespace Dokpod.Agent;

public sealed record AgentOptions(
    string DataDirectory,
    string DockerSocketPath,
    TimeSpan EngineTimeout,
    TimeSpan InventoryInterval,
    bool RunOnce,
    Uri? ControlPlaneEndpoint,
    Guid? EnvironmentId,
    string ClientCertificatePath,
    string? ClientCertificatePassword,
    string? ServerCaCertificatePath)
{
    public static AgentOptions FromEnvironment()
    {
        var defaultDataDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Dokpod", "Agent")
            : "/var/lib/dokpod-agent";
        var dataDirectory = GetAbsolutePath("DOKPOD_AGENT_DATA_DIRECTORY", defaultDataDirectory);

        var controlPlaneEndpoint = GetOptionalHttpsUri("DOKPOD_AGENT_CONTROL_PLANE_ENDPOINT");
        var environmentId = GetOptionalGuid("DOKPOD_AGENT_ENVIRONMENT_ID");
        if (controlPlaneEndpoint is not null && environmentId is null)
        {
            throw new InvalidOperationException(
                "DOKPOD_AGENT_ENVIRONMENT_ID is required when the control plane endpoint is configured.");
        }

        return new AgentOptions(
            dataDirectory,
            GetDockerSocketPath(),
            TimeSpan.FromSeconds(GetBoundedInteger("DOKPOD_AGENT_ENGINE_TIMEOUT_SECONDS", 30, 1, 300)),
            TimeSpan.FromSeconds(GetBoundedInteger("DOKPOD_AGENT_INVENTORY_INTERVAL_SECONDS", 30, 5, 900)),
            string.Equals(Environment.GetEnvironmentVariable("DOKPOD_AGENT_RUN_ONCE"), "true", StringComparison.OrdinalIgnoreCase),
            controlPlaneEndpoint,
            environmentId,
            GetAbsolutePath(
                "DOKPOD_AGENT_CLIENT_CERTIFICATE_PATH",
                Path.Combine(dataDirectory, "identity", "agent.pfx")),
            Environment.GetEnvironmentVariable("DOKPOD_AGENT_CLIENT_CERTIFICATE_PASSWORD"),
            GetOptionalAbsolutePath("DOKPOD_AGENT_SERVER_CA_CERTIFICATE_PATH"));
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

    private static string GetDockerSocketPath() =>
        Environment.GetEnvironmentVariable("DOKPOD_AGENT_DOCKER_SOCKET")
        ?? (OperatingSystem.IsWindows() ? "docker_engine" : "/run/docker.sock");

    private static string? GetOptionalAbsolutePath(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? null : GetAbsolutePath(variableName, value);
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

    private static Uri? GetOptionalHttpsUri(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{variableName} must be an absolute HTTPS URI.");
        }

        return uri;
    }

    private static Guid? GetOptionalGuid(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidOperationException($"{variableName} must be a non-empty UUID.");
        }

        return parsed;
    }
}