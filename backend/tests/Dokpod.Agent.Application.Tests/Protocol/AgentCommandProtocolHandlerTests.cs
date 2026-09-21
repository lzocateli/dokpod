using Dokpod.Agent.Application.Commands;
using Dokpod.Agent.Application.Engines;
using Dokpod.Agent.Application.Protocol;
using Dokpod.Agent.Contracts.V1;
using Dokpod.Agent.Infrastructure.Commands;
using Dokpod.Domain.Commands;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Dokpod.Agent.Application.Tests.Protocol;

public sealed class AgentCommandProtocolHandlerTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901");
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_SendsAcceptanceBeforeMutationAndThenResult()
    {
        var journal = new MemoryCommandJournal();
        var engine = new RecordingEngine();
        var handler = CreateHandler(journal, engine);
        var acceptances = new List<CommandAccepted>();
        var results = new List<CommandResult>();

        await handler.HandleAsync(
            EnvironmentId,
            new MessageMetadata { FencingToken = 8 },
            CreateCommand(),
            (acceptance, _) =>
            {
                Assert.Equal(0, engine.ExecutionCount);
                acceptances.Add(acceptance);
                return Task.CompletedTask;
            },
            (result, _) =>
            {
                results.Add(result);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(CommandAcceptance.Accepted, Assert.Single(acceptances).Acceptance);
        Assert.Equal(CommandResultState.Succeeded, Assert.Single(results).State);
        Assert.Equal(1, engine.ExecutionCount);
    }

    [Fact]
    public async Task HandleAsync_ReplaySendsDuplicateAndPersistedResultWithoutMutation()
    {
        var journal = new MemoryCommandJournal();
        var engine = new RecordingEngine();
        var handler = CreateHandler(journal, engine);
        var message = CreateCommand();
        await handler.HandleAsync(
            EnvironmentId,
            new MessageMetadata { FencingToken = 8 },
            message,
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);
        var acceptances = new List<CommandAccepted>();
        var results = new List<CommandResult>();

        await handler.HandleAsync(
            EnvironmentId,
            new MessageMetadata { FencingToken = 9 },
            message,
            (acceptance, _) =>
            {
                acceptances.Add(acceptance);
                return Task.CompletedTask;
            },
            (result, _) =>
            {
                results.Add(result);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(CommandAcceptance.Duplicate, Assert.Single(acceptances).Acceptance);
        Assert.Equal(CommandResultState.Succeeded, Assert.Single(results).State);
        Assert.Equal(1, engine.ExecutionCount);
    }

    [Fact]
    public async Task HandleAsync_RejectedAdmissionDoesNotSendResult()
    {
        var journal = new MemoryCommandJournal();
        var engine = new RecordingEngine();
        var handler = CreateHandler(journal, engine);
        var message = CreateCommand();
        message.Kind = CommandKind.Unspecified;
        var acceptances = new List<CommandAccepted>();
        var results = new List<CommandResult>();

        await handler.HandleAsync(
            EnvironmentId,
            new MessageMetadata { FencingToken = 8 },
            message,
            (acceptance, _) =>
            {
                acceptances.Add(acceptance);
                return Task.CompletedTask;
            },
            (result, _) =>
            {
                results.Add(result);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        var rejection = Assert.Single(acceptances);
        Assert.Equal(CommandAcceptance.Rejected, rejection.Acceptance);
        Assert.Equal("unsupported_command", rejection.FailureCode);
        Assert.Empty(results);
        Assert.Equal(0, engine.ExecutionCount);
    }

    private static AgentCommandProtocolHandler CreateHandler(
        ICommandJournal journal,
        IContainerEngine engine)
    {
        var timeProvider = new FixedTimeProvider();
        var processor = new AgentCommandProcessor(
            new AgentCommandGate(journal, timeProvider),
            journal,
            engine,
            timeProvider);
        return new AgentCommandProtocolHandler(processor);
    }

    private static Dokpod.Agent.Contracts.V1.AgentCommand CreateCommand() => new()
    {
        CommandId = "555c96bb-19dc-4b35-ac39-a83dbb65bc67",
        Kind = CommandKind.RestartContainer,
        ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        ExpectedContainerRevision = "revision-01",
        PayloadHash = ByteString.CopyFrom(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
        Deadline = Timestamp.FromDateTimeOffset(Now.AddMinutes(1)),
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingEngine : IContainerEngine
    {
        public int ExecutionCount { get; private set; }

        public Task<EngineDescriptor> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new EngineDescriptor("test", "1.47"));

        public Task<IReadOnlyList<EngineContainer>> ListContainersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EngineContainer>>([]);

        public Task<EngineContainer?> InspectContainerAsync(string containerId, CancellationToken cancellationToken) =>
            Task.FromResult<EngineContainer?>(
                new EngineContainer(containerId, "fixture", "fixture:latest", "running", "revision-01"));

        public Task<ContainerMutationResult> ExecuteAsync(
            AgentCommandKind commandKind,
            string containerId,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ContainerMutationResult(true, null));
        }
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