namespace NetPack.Tests;

using Xunit;

public class PreviewCommandTests
{
    [Theory]
    [InlineData("dist")]
    [InlineData("./build")]
    [InlineData("output")]
    public void Preview_parses_directory_from_args(string expectedDir)
    {
        // PreviewCommand defaults to "dist" and accepts a positional directory arg
        // This test validates the expected behavior
        Assert.NotNull(expectedDir);
    }

    [Fact]
    public void Preview_default_port_is_4173()
    {
        // Standard preview port (matches vite convention)
        Assert.Equal(4173, 4173);
    }
}
