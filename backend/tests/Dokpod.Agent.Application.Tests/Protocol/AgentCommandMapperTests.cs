using Dokpod.Agent.Application.Protocol;
using Dokpod.Agent.Contracts.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Dokpod.Agent.Application.Tests.Protocol;

public sealed class AgentCommandMapperTests
{
    [Fact]
    public void TryMap_MapsAWellFormedCommand()
    {
        var mapped = AgentCommandMapper.TryMap(
            Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901"),
            new MessageMetadata { FencingToken = 8 },
            CreateMessage(),
            out var command,
            out var failureCode);

        Assert.True(mapped);
        Assert.Equal(string.Empty, failureCode);
        Assert.NotNull(command);
        Assert.Equal(Dokpod.Domain.Commands.AgentCommandKind.RestartContainer, command.Kind);
        Assert.Equal("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F", command.PayloadHash);
    }

    [Theory]
    [InlineData("not-a-guid", "command_id_invalid")]
    [InlineData("0123456789abcdef", "container_id_invalid")]
    public void TryMap_RejectsMalformedCommandIdentity(string commandIdOrContainerId, string expectedFailureCode)
    {
        var message = CreateMessage();
        if (expectedFailureCode == "command_id_invalid")
        {
            message.CommandId = commandIdOrContainerId;
        }
        else
        {
            message.ContainerId = commandIdOrContainerId;
        }

        var mapped = AgentCommandMapper.TryMap(
            Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901"),
            new MessageMetadata { FencingToken = 8 },
            message,
            out var command,
            out var failureCode);

        Assert.False(mapped);
        Assert.Null(command);
        Assert.Equal(expectedFailureCode, failureCode);
    }

    [Fact]
    public void TryMap_RejectsPayloadHashWithUnexpectedLength()
    {
        var message = CreateMessage();
        message.PayloadHash = ByteString.CopyFrom([0x01]);

        var mapped = AgentCommandMapper.TryMap(
            Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901"),
            new MessageMetadata { FencingToken = 8 },
            message,
            out var command,
            out var failureCode);

        Assert.False(mapped);
        Assert.Null(command);
        Assert.Equal("payload_hash_invalid", failureCode);
    }

    [Fact]
    public void TryMap_RejectsFencingTokenOutsideTheDomainRange()
    {
        var mapped = AgentCommandMapper.TryMap(
            Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901"),
            new MessageMetadata { FencingToken = ulong.MaxValue },
            CreateMessage(),
            out var command,
            out var failureCode);

        Assert.False(mapped);
        Assert.Null(command);
        Assert.Equal("command_metadata_invalid", failureCode);
    }

    private static Dokpod.Agent.Contracts.V1.AgentCommand CreateMessage() => new()
    {
        CommandId = "555c96bb-19dc-4b35-ac39-a83dbb65bc67",
        Kind = CommandKind.RestartContainer,
        ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        ExpectedContainerRevision = "revision-01",
        PayloadHash = ByteString.CopyFrom(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
        Deadline = Timestamp.FromDateTime(DateTime.SpecifyKind(new DateTime(2026, 9, 8, 18, 0, 0), DateTimeKind.Utc)),
    };
}