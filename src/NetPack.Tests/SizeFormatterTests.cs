namespace NetPack.Tests;

using NetPack.Graph.Writers;
using Xunit;

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10240, "10.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    public void Formats_bytes_human_readable(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Human(bytes));
    }

    [Fact]
    public void Uses_invariant_decimal_point()
    {
        // Regardless of the ambient culture the separator is a dot.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE"); // uses ',' as decimal
            Assert.Equal("1.5 KB", SizeFormatter.Human(1536));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
