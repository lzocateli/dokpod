using System.Text.Json;
using Dokpod.Domain.Commands;

namespace Dokpod.Agent.Infrastructure.Commands;

public sealed class FileCommandJournal : ICommandJournal
{
    private readonly string journalDirectory;
    private readonly string resultDirectory;

    public FileCommandJournal(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        if (!Path.IsPathFullyQualified(dataDirectory))
        {
            throw new ArgumentException("The journal data directory must be an absolute path.", nameof(dataDirectory));
        }

        journalDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "commands");
        resultDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "results");
    }

    public async ValueTask<JournaledCommandResult?> FindResultAsync(
        Guid environmentId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        var resultPath = GetResultPath(environmentId, commandId);
        return File.Exists(resultPath)
            ? await ReadAsync<JournaledCommandResult>(resultPath, cancellationToken)
            : null;
    }

    public async ValueTask SaveResultAsync(
        JournaledCommandResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(resultDirectory);

        var resultPath = GetResultPath(result.EnvironmentId, result.CommandId);
        var temporaryPath = $"{resultPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteDurablyAsync(temporaryPath, result, cancellationToken);
            File.Move(temporaryPath, resultPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async ValueTask<JournaledCommand?> AppendIfAbsentAsync(
        JournaledCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Directory.CreateDirectory(journalDirectory);

        var recordPath = GetRecordPath(command.EnvironmentId, command.CommandId);
        if (File.Exists(recordPath))
        {
            return await ReadAsync<JournaledCommand>(recordPath, cancellationToken);
        }

        var temporaryPath = $"{recordPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await WriteDurablyAsync(temporaryPath, command, cancellationToken);

            try
            {
                File.Move(temporaryPath, recordPath);
                return null;
            }
            catch (IOException) when (File.Exists(recordPath))
            {
                return await ReadAsync<JournaledCommand>(recordPath, cancellationToken);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private string GetRecordPath(Guid environmentId, Guid commandId) =>
        Path.Combine(journalDirectory, $"{environmentId:N}-{commandId:N}.json");

    private string GetResultPath(Guid environmentId, Guid commandId) =>
        Path.Combine(resultDirectory, $"{environmentId:N}-{commandId:N}.json");

    private static async Task WriteDurablyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            });

        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task<T> ReadAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("The command journal record is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The command journal record is invalid.", exception);
        }
    }
}