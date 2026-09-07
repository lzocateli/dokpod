using Dokpod.Agent.Application.Commands;
using Dokpod.Agent.Application.Engines;
using Dokpod.Domain.Commands;
using Xunit;

namespace Dokpod.Agent.Application.Tests.Commands;

public sealed class AgentCommandProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessAsync_ExecutesAndPersistsSuccessfulResult()
    {
        var journal = new MemoryCommandJournal();
        var engine = new FakeEngine { Container = CreateContainer("revision-01") };
        var processor = CreateProcessor(journal, engine);

        var result = await processor.ProcessAsync(CreateCommand(), 8, TestContext.Current.CancellationToken);
        var persisted = await journal.FindResultAsync(
            result.EnvironmentId,
            result.CommandId,
            TestContext.Current.CancellationToken);

        Assert.Equal(CommandExecutionState.Succeeded, result.State);
        Assert.Equal(result, persisted);
        Assert.Equal(1, engine.ExecutionCount);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotExecuteWhenTargetRevisionChanged()
    {
        var journal = new MemoryCommandJournal();
        var engine = new FakeEngine { Container = CreateContainer("revision-02") };
        var processor = CreateProcessor(journal, engine);

        var result = await processor.ProcessAsync(CreateCommand(), 8, TestContext.Current.CancellationToken);

        Assert.Equal(CommandExecutionState.Failed, result.State);
        Assert.Equal("stale_target", result.FailureCode);
        Assert.Equal(0, engine.ExecutionCount);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsPersistedResultWithoutRepeatingMutation()
    {
        var journal = new MemoryCommandJournal();
        var engine = new FakeEngine { Container = CreateContainer("revision-01") };
        var processor = CreateProcessor(journal, engine);
        var command = CreateCommand();

        var first = await processor.ProcessAsync(command, 8, TestContext.Current.CancellationToken);
        var replay = await processor.ProcessAsync(command, 8, TestContext.Current.CancellationToken);

        Assert.Equal(first, replay);
        Assert.Equal(1, engine.ExecutionCount);
    }

    [Fact]
    public async Task ProcessAsync_MarksUnknownEngineOutcomeAsIndeterminate()
    {
        var journal = new MemoryCommandJournal();
        var engine = new FakeEngine
        {
            Container = CreateContainer("revision-01"),
            ExecutionException = new HttpRequestException("synthetic failure"),
        };
        var processor = CreateProcessor(journal, engine);

        var result = await processor.ProcessAsync(CreateCommand(), 8, TestContext.Current.CancellationToken);

        Assert.Equal(CommandExecutionState.Indeterminate, result.State);
        Assert.Equal("engine_result_unknown", result.FailureCode);
    }

    private static AgentCommandProcessor CreateProcessor(ICommandJournal journal, IContainerEngine engine)
    {
        var timeProvider = new FixedTimeProvider(Now);
        return new AgentCommandProcessor(new AgentCommandGate(journal, timeProvider), journal, engine, timeProvider);
    }

    private static AgentCommand CreateCommand() =>
        new(
            Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901"),
            Guid.Parse("555c96bb-19dc-4b35-ac39-a83dbb65bc67"),
            AgentCommandKind.RestartContainer,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "revision-01",
            "sha256:abc",
            Now.AddMinutes(1),
            8);

    private static EngineContainer CreateContainer(string revision) =>
        new(CreateCommand().ContainerId, "fixture", "fixture:latest", "running", revision);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeEngine : IContainerEngine
    {
        public EngineContainer? Container { get; init; }
        public Exception? ExecutionException { get; init; }
        public int ExecutionCount { get; private set; }

        public Task<EngineDescriptor> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new EngineDescriptor("test", "1.47"));

        public Task<IReadOnlyList<EngineContainer>> ListContainersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EngineContainer>>(Container is null ? [] : [Container]);

        public Task<EngineContainer?> InspectContainerAsync(string containerId, CancellationToken cancellationToken) =>
            Task.FromResult(Container);

        public Task<ContainerMutationResult> ExecuteAsync(
            AgentCommandKind commandKind,
            string containerId,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ExecutionException is null
                ? Task.FromResult(new ContainerMutationResult(true, null))
                : Task.FromException<ContainerMutationResult>(ExecutionException);
        }
    }

    private sealed class MemoryCommandJournal : ICommandJournal
    {
        private readonly Dictionary<(Guid, Guid), JournaledCommand> commands = [];
        private readonly Dictionary<(Guid, Guid), JournaledCommandResult> results = [];

        public ValueTask<JournaledCommand?> AppendIfAbsentAsync(
            JournaledCommand command,
            CancellationToken cancellationToken)
        {
            var key = (command.EnvironmentId, command.CommandId);
            if (commands.TryGetValue(key, out var existing))
            {
                return ValueTask.FromResult<JournaledCommand?>(existing);
            }

            commands.Add(key, command);
            return ValueTask.FromResult<JournaledCommand?>(null);
        }

        public ValueTask<JournaledCommandResult?> FindResultAsync(
            Guid environmentId,
            Guid commandId,
            CancellationToken cancellationToken)
        {
            results.TryGetValue((environmentId, commandId), out var result);
            return ValueTask.FromResult(result);
        }

        public ValueTask SaveResultAsync(JournaledCommandResult result, CancellationToken cancellationToken)
        {
            results[(result.EnvironmentId, result.CommandId)] = result;
            return ValueTask.CompletedTask;
        }
    }
}