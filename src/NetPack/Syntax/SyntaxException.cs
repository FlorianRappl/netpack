namespace NetPack.Syntax;

using System;

/// <summary>
/// Thrown by the tokenizer or parser when a fatal syntax error is encountered
/// and tolerant recovery is disabled. In tolerant mode (the NetPack default)
/// errors are collected as <see cref="Diagnostic"/> values instead.
/// </summary>
public sealed class SyntaxException : Exception
{
    public SyntaxException(string message, int position, int line, int column)
        : base($"{message} ({line}:{column})")
    {
        Position = position;
        Line = line;
        Column = column;
    }

    public int Position { get; }

    public int Line { get; }

    public int Column { get; }
}
