using Dokpod.Domain.Commands;
using Xunit;

namespace Dokpod.Domain.Tests.Commands;

public sealed class AgentCommandGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AdmitAsync_AcceptsCommandOnceAndDetectsReplay()
    {
        var journal = new MemoryCommandJournal();
        var gate = new AgentCommandGate(journal, new FixedTimeProvider(Now));
        var command = CreateCommand(payloadHash: "sha256:abc", fencingToken: 8);

        var first = await gate.AdmitAsync(command, activeFencingToken: 8, TestContext.Current.CancellationToken);
        var replay = await gate.AdmitAsync(command, activeFencingToken: 8, TestContext.Current.CancellationToken);

        Assert.Equal(CommandAdmission.Accepted, first);
        Assert.Equal(CommandAdmission.Duplicate, replay);
    }

    [Fact]
    public async Task AdmitAsync_AcceptsOnlyOneConcurrentDelivery()
    {
        var gate = new AgentCommandGate(new MemoryCommandJournal(), new FixedTimeProvider(Now));
        var command = CreateCommand(payloadHash: "sha256:abc", fencingToken: 8);

        var results = await Task.WhenAll(
            gate.AdmitAsync(command, activeFencingToken: 8, TestContext.Current.CancellationToken),
            gate.AdmitAsync(command, activeFencingToken: 8, TestContext.Current.CancellationToken));

        Assert.Equal(1, results.Count(result => result == CommandAdmission.Accepted));
        Assert.Equal(1, results.Count(result => result == CommandAdmission.Duplicate));
    }

    [Fact]
    public async Task AdmitAsync_RejectsReusedIdWithDifferentPayload()
    {
        var journal = new MemoryCommandJournal();
        var gate = new AgentCommandGate(journal, new FixedTimeProvider(Now));
        var command = CreateCommand(payloadHash: "sha256:abc", fencingToken: 8);

        await gate.AdmitAsync(command, activeFencingToken: 8, TestContext.Current.CancellationToken);
        var conflicting = command with { PayloadHash = "sha256:def" };

        var result = await gate.AdmitAsync(
            conflicting,
            activeFencingToken: 8,
            TestContext.Current.CancellationToken);

        Assert.Equal(CommandAdmission.ConflictingPayload, result);
    }

    [Theory]
    [InlineData(AgentCommandKind.StartContainer, 7, CommandAdmission.StaleSession)]
    [InlineData(AgentCommandKind.Unknown, 8, CommandAdmission.Unsupported)]
    public async Task AdmitAsync_RejectsInvalidCommand(
        AgentCommandKind kind,
        long fencingToken,
        CommandAdmission expected)
    {
        var gate = new AgentCommandGate(new MemoryCommandJournal(), new FixedTimeProvider(Now));
        var command = CreateCommand("sha256:abc", fencingToken) with { Kind = kind };

        var result = await gate.AdmitAsync(command, activeFencingToken: 8, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task AdmitAsync_RejectsExpiredCommand()
    {
        var gate = new AgentCommandGate(new MemoryCommandJournal(), new FixedTimeProvider(Now));
        var command = CreateCommand("sha256:abc", fencingToken: 8) with { DeadlineUtc = Now };

        var result = await gate.AdmitAsync(command, activeFencingToken: 8, TestContext.Current.CancellationToken);

        Assert.Equal(CommandAdmission.Expired, result);
    }

    private static AgentCommand CreateCommand(string payloadHash, long fencingToken) =>
        new(
            Guid.Parse("f8158257-9f00-49c3-9411-1c8879a171d8"),
            Guid.Parse("e0a1be53-b479-4aaf-866a-bab5546ed872"),
            AgentCommandKind.StartContainer,
            "container-01",
            "revision-01",
            payloadHash,
            Now.AddMinutes(1),
            fencingToken);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MemoryCommandJournal : ICommandJournal
    {
        private readonly object sync = new();
        private readonly Dictionary<(Guid EnvironmentId, Guid CommandId), JournaledCommand> commands = [];
        private readonly Dictionary<(Guid EnvironmentId, Guid CommandId), JournaledCommandResult> results = [];

        public Task<JournaledCommand?> AppendIfAbsentAsync(
            JournaledCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (sync)
            {
                var key = (command.EnvironmentId, command.CommandId);
                if (commands.TryGetValue(key, out var existing))
                {
                    return Task.FromResult<JournaledCommand?>(existing);
                }

                commands.Add(key, command);
                return Task.FromResult<JournaledCommand?>(null);
            }
        }

        public Task<JournaledCommandResult?> FindResultAsync(
            Guid environmentId,
            Guid commandId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                results.TryGetValue((environmentId, commandId), out var result);
                return Task.FromResult(result);
            }
        }

        public Task SaveResultAsync(JournaledCommandResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                results[(result.EnvironmentId, result.CommandId)] = result;
                return Task.CompletedTask;
            }
        }
    }
}