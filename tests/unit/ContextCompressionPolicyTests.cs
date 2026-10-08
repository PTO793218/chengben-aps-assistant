using WePilot.Agent.Agents;
using Xunit;

namespace WePilot.Tests;

public sealed class ContextCompressionPolicyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 0)]
    [InlineData(16, 0)]
    public void DoesNotCompressBeforeTrigger(int messageCount, int expected)
    {
        Assert.Equal(expected, ContextCompressionPolicy.GetCompressMessageCount(messageCount, 0, 8, 4));
    }

    [Fact]
    public void CompressesOlderMessagesAndKeepsFourRecentRounds()
    {
        Assert.Equal(10, ContextCompressionPolicy.GetCompressMessageCount(18, 0, 8, 4));
    }

    [Fact]
    public void OnlyCountsMessagesAfterExistingSummary()
    {
        Assert.Equal(10, ContextCompressionPolicy.GetCompressMessageCount(28, 10, 8, 4));
    }
}
