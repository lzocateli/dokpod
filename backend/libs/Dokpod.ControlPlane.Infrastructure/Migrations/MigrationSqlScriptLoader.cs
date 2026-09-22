using System.Reflection;
using System.Text;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

internal static class MigrationSqlScriptLoader
{
    private static readonly Assembly MigrationAssembly = typeof(MigrationSqlScriptLoader).Assembly;

    internal static string Load(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var suffix = $".Migrations.Script._{relativePath.Replace('/', '.')}";
        var resourceName = MigrationAssembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded PostgreSQL migration script '{relativePath}' was not found.");

        using var stream = MigrationAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded PostgreSQL migration resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var script = reader.ReadToEnd();

        return !string.IsNullOrWhiteSpace(script)
            ? script
            : throw new InvalidOperationException(
                $"Embedded PostgreSQL migration script '{relativePath}' is empty.");
    }
}