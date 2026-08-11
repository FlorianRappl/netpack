namespace NetPack;

using System.Text;

/// <summary>Verbosity levels for CLI output, ordered least to most verbose.</summary>
public enum LogLevel
{
    /// <summary>No output at all.</summary>
    Silent = 0,
    /// <summary>Errors only.</summary>
    Error = 1,
    /// <summary>Errors and warnings.</summary>
    Warning = 2,
    /// <summary>Normal build output (the default).</summary>
    Info = 3,
    /// <summary>Adds diagnostic detail.</summary>
    Debug = 4,
    /// <summary>Everything, including fine-grained tracing.</summary>
    Verbose = 5,
}

/// <summary>
/// Process-wide log verbosity. Rather than route every call site through a logger,
/// <see cref="Apply"/> installs gating wrappers around <see cref="Console.Out"/>
/// (treated as the <see cref="LogLevel.Info"/> tier) and <see cref="Console.Error"/>
/// (the <see cref="LogLevel.Error"/> tier), so the existing <c>Console.WriteLine</c>
/// output honours the level with no other changes. The explicit
/// <see cref="Warning"/>/<see cref="Debug"/>/<see cref="Verbose"/> helpers cover the
/// tiers that don't map cleanly onto a stream.
/// </summary>
public static class Log
{
    // The genuine streams, captured before any gating wrapper is installed.
    private static readonly TextWriter RealOut = Console.Out;
    private static readonly TextWriter RealError = Console.Error;

    public static LogLevel Level { get; private set; } = LogLevel.Info;

    /// <summary>True when a message at <paramref name="level"/> should be shown.</summary>
    public static bool Enabled(LogLevel level) => level != LogLevel.Silent && Level >= level;

    /// <summary>Sets the level and installs the stream gating (idempotent).</summary>
    public static void Apply(LogLevel level)
    {
        Level = level;
        Console.SetOut(new GatedWriter(RealOut, LogLevel.Info));
        Console.SetError(new GatedWriter(RealError, LogLevel.Error));
    }

    /// <summary>Parses <c>silent</c>/<c>error</c>/<c>warning</c>/<c>info</c>/
    /// <c>debug</c>/<c>verbose</c> (case-insensitive; <c>warn</c> is accepted too).</summary>
    public static bool TryParse(string? value, out LogLevel level)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "silent": level = LogLevel.Silent; return true;
            case "error": level = LogLevel.Error; return true;
            case "warning" or "warn": level = LogLevel.Warning; return true;
            case "info": level = LogLevel.Info; return true;
            case "debug": level = LogLevel.Debug; return true;
            case "verbose": level = LogLevel.Verbose; return true;
            default: level = LogLevel.Info; return false;
        }
    }

    /// <summary>The valid <c>--log-level</c> values, for help/error messages.</summary>
    public static string Names => "silent, error, warning, info, debug, verbose";

    public static void Error(string message)
    {
        if (Enabled(LogLevel.Error)) RealError.WriteLine(message);
    }

    public static void Warning(string message)
    {
        if (Enabled(LogLevel.Warning)) RealError.WriteLine(message);
    }

    public static void Info(string message)
    {
        if (Enabled(LogLevel.Info)) RealOut.WriteLine(message);
    }

    public static void Debug(string message)
    {
        if (Enabled(LogLevel.Debug)) RealOut.WriteLine(message);
    }

    public static void Verbose(string message)
    {
        if (Enabled(LogLevel.Verbose)) RealOut.WriteLine(message);
    }

    /// <summary>A <see cref="TextWriter"/> that forwards to <paramref name="inner"/>
    /// only while the configured level is at least <paramref name="required"/>.</summary>
    private sealed class GatedWriter(TextWriter inner, LogLevel required) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;

        private bool On => Enabled(required);

        public override void Write(char value) { if (On) inner.Write(value); }

        public override void Write(string? value) { if (On) inner.Write(value); }

        public override void Write(char[] buffer, int index, int count) { if (On) inner.Write(buffer, index, count); }

        public override void WriteLine() { if (On) inner.WriteLine(); }

        public override void WriteLine(string? value) { if (On) inner.WriteLine(value); }

        public override void Flush() => inner.Flush();
    }
}
