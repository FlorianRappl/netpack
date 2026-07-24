namespace NetPack.Syntax;

/// <summary>Severity of a <see cref="Diagnostic"/>.</summary>
public enum DiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>
/// A tolerant diagnostic emitted by the tokenizer or parser. NetPack favours
/// recovery over hard failure so a single malformed construct does not abort a
/// whole bundle; problems are collected here instead of thrown.
/// </summary>
public readonly struct Diagnostic
{
    public Diagnostic(string message, int position, int line, int column, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        Message = message;
        Position = position;
        Line = line;
        Column = column;
        Severity = severity;
    }

    public string Message { get; }

    public int Position { get; }

    public int Line { get; }

    public int Column { get; }

    public DiagnosticSeverity Severity { get; }

    public override string ToString() => $"{Severity} ({Line}:{Column}): {Message}";
}
