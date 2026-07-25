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

    /// <summary>
    /// Formats the diagnostic with a source code snippet and arrow pointing to the error location.
    /// </summary>
    public string FormatWithSource(string source, string? filePath = null)
    {
        var sb = new System.Text.StringBuilder();
        var severityStr = Severity == DiagnosticSeverity.Error ? "error" : "warning";

        if (filePath is not null)
        {
            sb.AppendLine($"  {severityStr}: {Message}");
            sb.AppendLine($"   --> {filePath}:{Line}:{Column}");
        }
        else
        {
            sb.AppendLine($"  {severityStr}: {Message}");
            sb.AppendLine($"   at line {Line}, column {Column}");
        }

        // Show source snippet with line numbers and arrow
        if (Line > 0 && !string.IsNullOrEmpty(source))
        {
            var lines = source.Split('\n');
            var start = System.Math.Max(0, Line - 2);
            var end = System.Math.Min(lines.Length, Line + 1);

            for (var i = start; i < end; i++)
            {
                var lineNum = (i + 1).ToString().PadLeft(4);
                var marker = i + 1 == Line ? ">" : " ";
                sb.AppendLine($" {marker} {lineNum} | {lines[i]}");

                if (i + 1 == Line)
                {
                    var padding = new string(' ', Column - 1);
                    sb.AppendLine($"       {padding}^");
                }
            }
        }

        return sb.ToString();
    }
}
