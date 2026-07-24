namespace NetPack.Graph;

using System.Text;

/// <summary>
/// Scans the leading comments of a source file for JSX pragmas that override the
/// JSX factory for that single file:
/// <list type="bullet">
///   <item><c>@jsx &lt;factory&gt;</c> — e.g. <c>/** @jsx h */</c></item>
///   <item><c>@jsxFrag &lt;factory&gt;</c> — e.g. <c>/** @jsxFrag Fragment */</c></item>
/// </list>
/// A pragma is only honoured when it appears before any code, i.e. inside the
/// run of comments and whitespace at the very top of the file.
/// </summary>
public static class JsxPragma
{
    public readonly record struct Result(string? Factory, string? FragmentFactory);

    public static Result Scan(string source)
    {
        var comments = LeadingComments(source);

        if (comments.Length == 0)
        {
            return default;
        }

        // Look for @jsxFrag first; because @jsx is a prefix of @jsxFrag the
        // separator check below keeps the two from colliding.
        return new Result(Find(comments, "@jsx"), Find(comments, "@jsxFrag"));
    }

    /// <summary>
    /// Collects the inner text of the comments that precede the first token,
    /// separated by newlines. Stops at the first non-comment, non-whitespace
    /// character (the start of code).
    /// </summary>
    private static string LeadingComments(string s)
    {
        var sb = new StringBuilder();
        var i = 0;
        var n = s.Length;

        while (i < n)
        {
            var c = s[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < n && s[i + 1] == '/')
            {
                i += 2;
                var start = i;
                while (i < n && s[i] != '\n' && s[i] != '\r')
                {
                    i++;
                }
                sb.Append(s, start, i - start).Append('\n');
                continue;
            }

            if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                i += 2;
                var start = i;
                while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/'))
                {
                    i++;
                }
                var end = Math.Min(i, n);
                sb.Append(s, start, end - start).Append('\n');
                i = i + 1 < n ? i + 2 : n; // skip the closing */
                continue;
            }

            break; // first code character
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds <paramref name="tag"/> followed by whitespace and returns the
    /// factory token that follows (an identifier, optionally dotted). Returns
    /// null when the tag is absent. The whitespace requirement stops <c>@jsx</c>
    /// from matching the <c>@jsxFrag</c> prefix.
    /// </summary>
    private static string? Find(string text, string tag)
    {
        var idx = 0;

        while ((idx = text.IndexOf(tag, idx, StringComparison.Ordinal)) >= 0)
        {
            var after = idx + tag.Length;

            if (after < text.Length && char.IsWhiteSpace(text[after]))
            {
                var j = after;
                while (j < text.Length && char.IsWhiteSpace(text[j]))
                {
                    j++;
                }

                var start = j;
                while (j < text.Length && IsFactoryChar(text[j]))
                {
                    j++;
                }

                if (j > start)
                {
                    return text[start..j];
                }
            }

            idx = after;
        }

        return null;
    }

    private static bool IsFactoryChar(char c)
        => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '$';
}
