namespace NetPack.Graph.Writers;

using System.Globalization;

/// <summary>
/// A single file produced by a build: its output name, byte size and, for JS/CSS
/// bundles, the number of source modules it contains (0 for plain assets and
/// source maps).
/// </summary>
public sealed record EmittedFile(string Name, long Size, int Modules, bool IsBundle);

/// <summary>Formatting helpers for human-readable build output.</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Renders a byte count like <c>12.3 KB</c> (1 KB = 1024 B).</summary>
    public static string Human(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {Units[unit]}");
    }
}
