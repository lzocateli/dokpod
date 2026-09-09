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
    public async Task ProcessAsync_SerializesMutationsForTheSameContainer()
    {
        var journal = new MemoryCommandJournal();
        var engine = new FakeEngine
        {
            Container = CreateContainer("revision-01"),
            BlockExecution = true,
        };
        var processor = CreateProcessor(journal, engine);
        var first = processor.ProcessAsync(CreateCommand(), 8, TestContext.Current.CancellationToken);

        await engine.ExecutionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = processor.ProcessAsync(
            CreateCommand() with { CommandId = Guid.Parse("8bf6d9bc-e8c4-432e-8ba4-8443ab7ce533") },
            8,
            TestContext.Current.CancellationToken);

        await Task.Yield();
        Assert.Equal(1, engine.ExecutionCount);

        engine.ReleaseExecution();
        await Task.WhenAll(first, second);

        Assert.Equal(2, engine.ExecutionCount);
    }

    [Fact]
    public async Task ProcessAsync_AllowsMutationsForDifferentEnvironmentsInParallel()
    {
        var journal = new MemoryCommandJournal();
        var engine = new FakeEngine
        {
            Container = CreateContainer("revision-01"),
            BlockExecution = true,
        };
        var processor = CreateProcessor(journal, engine);
        var first = processor.ProcessAsync(CreateCommand(), 8, TestContext.Current.CancellationToken);

        await engine.ExecutionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = processor.ProcessAsync(
            CreateCommand() with
            {
                EnvironmentId = Guid.Parse("6fb83c37-7ba2-44e4-a485-00af4cd2f55d"),
                CommandId = Guid.Parse("8bf6d9bc-e8c4-432e-8ba4-8443ab7ce533"),
            },
            8,
            TestContext.Current.CancellationToken);

        await engine.TwoExecutionsStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        engine.ReleaseExecution();
        await Task.WhenAll(first, second);

        Assert.Equal(2, engine.ExecutionCount);
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
        public bool BlockExecution { get; init; }
        private int executionCount;
        public int ExecutionCount => executionCount;
        public TaskCompletionSource ExecutionStarted { get; } = new();
        public TaskCompletionSource TwoExecutionsStarted { get; } = new();
        private TaskCompletionSource? ExecutionRelease { get; } = new();

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
            var currentExecutionCount = Interlocked.Increment(ref executionCount);
            ExecutionStarted.TrySetResult();
            if (currentExecutionCount == 2)
            {
                TwoExecutionsStarted.TrySetResult();
            }

            return ExecuteCoreAsync();

            async Task<ContainerMutationResult> ExecuteCoreAsync()
            {
                if (BlockExecution)
                {
                    await ExecutionRelease!.Task.WaitAsync(cancellationToken);
                }

                if (ExecutionException is not null)
                {
                    throw ExecutionException;
                }

                return new ContainerMutationResult(true, null);
            }
        }

        public void ReleaseExecution() => ExecutionRelease!.TrySetResult();
    }

    private sealed class MemoryCommandJournal : ICommandJournal
    {
        private readonly Dictionary<(Guid, Guid), JournaledCommand> commands = [];
        private readonly Dictionary<(Guid, Guid), JournaledCommandResult> results = [];

        public Task<JournaledCommand?> AppendIfAbsentAsync(
            JournaledCommand command,
            CancellationToken cancellationToken)
        {
            var key = (command.EnvironmentId, command.CommandId);
            if (commands.TryGetValue(key, out var existing))
            {
                return Task.FromResult<JournaledCommand?>(existing);
            }

            commands.Add(key, command);
            return Task.FromResult<JournaledCommand?>(null);
        }

        public Task<JournaledCommandResult?> FindResultAsync(
            Guid environmentId,
            Guid commandId,
            CancellationToken cancellationToken)
        {
            results.TryGetValue((environmentId, commandId), out var result);
            return Task.FromResult(result);
        }

        public Task SaveResultAsync(JournaledCommandResult result, CancellationToken cancellationToken)
        {
            results[(result.EnvironmentId, result.CommandId)] = result;
            return Task.CompletedTask;
        }
    }
}