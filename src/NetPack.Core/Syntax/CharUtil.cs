namespace NetPack.Syntax;

using System.Globalization;
using System.Runtime.CompilerServices;

/// <summary>
/// Character classification helpers used by the tokenizer. The common ASCII
/// ranges are handled with branch-light comparisons; non-ASCII characters fall
/// back to Unicode general categories that approximate the ECMAScript
/// <c>ID_Start</c> / <c>ID_Continue</c> productions.
/// </summary>
internal static class CharUtil
{
    public const char Bom = (char)0xFEFF;
    public const char ZeroWidthNonJoiner = (char)0x200C;
    public const char ZeroWidthJoiner = (char)0x200D;
    public const char LineSeparator = (char)0x2028;
    public const char ParagraphSeparator = (char)0x2029;
    public const char NextLine = (char)0x0085;
    public const char NonBreakingSpace = (char)0x00A0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLineTerminator(char c)
        => c is '\n' or '\r' or LineSeparator or ParagraphSeparator;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWhiteSpace(char c)
    {
        switch (c)
        {
            case ' ':
            case '\t':
            case '\v':
            case '\f':
            case NonBreakingSpace:
            case Bom:
                return true;
            default:
                // Other Unicode space separators (rare) — only test non-ASCII.
                return c > 127 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDecimalDigit(char c) => c >= '0' && c <= '9';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsOctalDigit(char c) => c >= '0' && c <= '7';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBinaryDigit(char c) => c == '0' || c == '1';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HexValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    /// <summary>True when <paramref name="c"/> may start an identifier.</summary>
    public static bool IsIdentifierStart(char c)
    {
        if (c >= 'a' && c <= 'z') return true;
        if (c >= 'A' && c <= 'Z') return true;
        if (c == '$' || c == '_') return true;
        if (c < 128) return false;
        return IsUnicodeIdentifierStart(c);
    }

    /// <summary>True when <paramref name="c"/> may continue an identifier.</summary>
    public static bool IsIdentifierPart(char c)
    {
        if (c >= 'a' && c <= 'z') return true;
        if (c >= 'A' && c <= 'Z') return true;
        if (c >= '0' && c <= '9') return true;
        if (c == '$' || c == '_') return true;
        if (c < 128) return false;
        if (c == ZeroWidthNonJoiner || c == ZeroWidthJoiner) return true;
        return IsUnicodeIdentifierPart(c);
    }

    private static bool IsUnicodeIdentifierStart(char c)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;
    }

    private static bool IsUnicodeIdentifierPart(char c)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
    }
}
