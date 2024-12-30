namespace NetPack.Html;

using System;

public readonly ref struct Token
{
    public TokenType Type { get; }
    public ReadOnlySpan<char> Value { get; }

    public Token(TokenType type, ReadOnlySpan<char> value)
    {
        Type = type;
        Value = value;
    }

    public override string ToString()
    {
        return $"{Type}: {Value.ToString()}";
    }
}
