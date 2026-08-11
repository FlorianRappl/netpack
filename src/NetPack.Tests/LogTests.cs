namespace NetPack.Tests;

using NetPack;
using Xunit;

/// <summary>
/// Unit tests for the <c>--log-level</c> parsing. Only the pure surface is
/// exercised — <see cref="Log.Apply"/> swaps the process-wide Console streams, so
/// it is intentionally not called here (that would affect other parallel tests).
/// </summary>
public class LogTests
{
    [Theory]
    [InlineData("silent", LogLevel.Silent)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("info", LogLevel.Info)]
    [InlineData("DEBUG", LogLevel.Debug)]
    [InlineData(" verbose ", LogLevel.Verbose)]
    public void Parses_known_levels(string input, LogLevel expected)
    {
        Assert.True(Log.TryParse(input, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_unknown_levels_and_falls_back_to_info(string? input)
    {
        Assert.False(Log.TryParse(input, out var level));
        Assert.Equal(LogLevel.Info, level);
    }

    [Fact]
    public void Names_lists_every_level()
    {
        foreach (var name in new[] { "silent", "error", "warning", "info", "debug", "verbose" })
        {
            Assert.Contains(name, Log.Names);
        }
    }
}
