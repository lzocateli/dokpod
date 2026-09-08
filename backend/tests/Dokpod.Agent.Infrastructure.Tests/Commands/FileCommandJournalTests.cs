using Dokpod.Agent.Infrastructure.Commands;
using Dokpod.Domain.Commands;
using Xunit;

namespace Dokpod.Agent.Infrastructure.Tests.Commands;

public sealed class FileCommandJournalTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), $"dokpod-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task AppendIfAbsentAsync_PersistsRecordAcrossInstances()
    {
        var command = CreateCommand("sha256:abc");
        var firstJournal = new FileCommandJournal(dataDirectory);

        var first = await firstJournal.AppendIfAbsentAsync(command, TestContext.Current.CancellationToken);
        var reopenedJournal = new FileCommandJournal(dataDirectory);
        var replay = await reopenedJournal.AppendIfAbsentAsync(command, TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.Equal(command, replay);
    }

    [Fact]
    public async Task AppendIfAbsentAsync_AllowsOnlyOneWriterAcrossInstances()
    {
        var command = CreateCommand("sha256:abc");
        var firstJournal = new FileCommandJournal(dataDirectory);
        var secondJournal = new FileCommandJournal(dataDirectory);

        var results = await Task.WhenAll(
            firstJournal.AppendIfAbsentAsync(command, TestContext.Current.CancellationToken).AsTask(),
            secondJournal.AppendIfAbsentAsync(command, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, results.Count(result => result == command));
    }

    [Fact]
    public async Task AppendIfAbsentAsync_SeparatesEnvironments()
    {
        var first = CreateCommand("sha256:abc");
        var second = first with { EnvironmentId = Guid.Parse("9520c7c1-f517-49ae-a1f6-7eb2876a63f3") };
        var journal = new FileCommandJournal(dataDirectory);

        var firstResult = await journal.AppendIfAbsentAsync(first, TestContext.Current.CancellationToken);
        var secondResult = await journal.AppendIfAbsentAsync(second, TestContext.Current.CancellationToken);

        Assert.Null(firstResult);
        Assert.Null(secondResult);
    }

    [Fact]
    public async Task SaveResultAsync_PersistsResultAcrossInstances()
    {
        var command = CreateCommand("sha256:abc");
        var result = new JournaledCommandResult(
            command.EnvironmentId,
            command.CommandId,
            CommandExecutionState.Indeterminate,
            "engine_result_unknown",
            "revision-01",
            new DateTimeOffset(2026, 9, 7, 18, 0, 0, TimeSpan.Zero));
        var journal = new FileCommandJournal(dataDirectory);

        await journal.SaveResultAsync(result, TestContext.Current.CancellationToken);
        var reopenedJournal = new FileCommandJournal(dataDirectory);
        var persisted = await reopenedJournal.FindResultAsync(
            result.EnvironmentId,
            result.CommandId,
            TestContext.Current.CancellationToken);

        Assert.Equal(result, persisted);
    }

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static JournaledCommand CreateCommand(string payloadHash) =>
        new(
            Guid.Parse("5c53eb7b-b073-43c3-aac5-6be7671b380d"),
            Guid.Parse("508688cc-d027-4a84-b6cb-16f66fbe5af0"),
            payloadHash,
            CommandAdmission.Accepted);
}