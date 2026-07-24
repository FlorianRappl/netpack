namespace NetPack.Syntax.Printer;

using System.Collections.Generic;
using System.IO;
using System.Text;
using NetPack.Syntax.Ast;

/// <summary>
/// Accumulates a Source Map v3 as the printer emits code. The printer calls
/// <see cref="AddMapping"/> at each mappable node with the current generated
/// position and the node's original position; segments are VLQ-encoded on the
/// fly. <see cref="ToJson"/> renders the final map (including
/// <c>sourcesContent</c>, so the original files do not need to be fetched).
/// </summary>
public sealed class SourceMapBuilder
{
    private readonly string _file;
    private readonly string _root;
    private readonly List<string> _sources = new();
    private readonly List<string> _sourcesContent = new();
    private readonly Dictionary<SourceFile, int> _sourceIndex = new(ReferenceComparer.Instance);
    private readonly StringBuilder _mappings = new();

    private int _generatedLine;      // line currently represented in _mappings
    private int _lastGeneratedColumn;
    private int _lastSourceIndex;
    private int _lastSourceLine;
    private int _lastSourceColumn;
    private bool _segmentOnLine;
    private int _mappedLine = -1;
    private int _mappedColumn = -1;

    public SourceMapBuilder(string file, string root)
    {
        _file = file;
        _root = root;
    }

    public void AddMapping(int generatedLine, int generatedColumn, SourceFile source, int sourceLine, int sourceColumn)
    {
        // De-duplicate: several nodes can start at the same generated position.
        if (generatedLine == _mappedLine && generatedColumn == _mappedColumn)
        {
            return;
        }

        while (_generatedLine < generatedLine)
        {
            _mappings.Append(';');
            _generatedLine++;
            _segmentOnLine = false;
            _lastGeneratedColumn = 0; // generated column is relative within a line
        }

        if (_segmentOnLine)
        {
            _mappings.Append(',');
        }

        Base64Vlq.Encode(_mappings, generatedColumn - _lastGeneratedColumn);
        _lastGeneratedColumn = generatedColumn;

        var index = Intern(source);
        Base64Vlq.Encode(_mappings, index - _lastSourceIndex);
        _lastSourceIndex = index;
        Base64Vlq.Encode(_mappings, sourceLine - _lastSourceLine);
        _lastSourceLine = sourceLine;
        Base64Vlq.Encode(_mappings, sourceColumn - _lastSourceColumn);
        _lastSourceColumn = sourceColumn;

        _segmentOnLine = true;
        _mappedLine = generatedLine;
        _mappedColumn = generatedColumn;
    }

    public bool IsEmpty => _mappings.Length == 0;

    private int Intern(SourceFile source)
    {
        if (_sourceIndex.TryGetValue(source, out var index))
        {
            return index;
        }
        index = _sources.Count;
        _sourceIndex[source] = index;
        _sources.Add(Relativize(source.FileName));
        _sourcesContent.Add(source.Source);
        return index;
    }

    private string Relativize(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(_root) && Path.IsPathRooted(path))
            {
                return Path.GetRelativePath(_root, path).Replace('\\', '/');
            }
        }
        catch
        {
            // fall through to the raw path
        }
        return path.Replace('\\', '/');
    }

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"version\":3,\"file\":");
        JsonString(sb, _file);
        sb.Append(",\"sourceRoot\":\"\",\"sources\":[");
        AppendStringArray(sb, _sources);
        sb.Append("],\"sourcesContent\":[");
        AppendStringArray(sb, _sourcesContent);
        sb.Append("],\"names\":[],\"mappings\":");
        JsonString(sb, _mappings.ToString());
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendStringArray(StringBuilder sb, List<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) sb.Append(',');
            JsonString(sb, values[i]);
        }
    }

    private static void JsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    private sealed class ReferenceComparer : IEqualityComparer<SourceFile>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(SourceFile? x, SourceFile? y) => ReferenceEquals(x, y);
        public int GetHashCode(SourceFile obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
