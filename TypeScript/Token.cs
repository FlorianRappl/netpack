namespace NetPack.TypeScript;

public readonly ref struct Token
{
    public TokenType Type { get; }
    public ReadOnlySpan<char> Value { get; }

    public Token(TokenType type, ReadOnlySpan<char> value)
    {
        Type = type;
        Value = value;
    }

    public override string ToString() => $"[{Type}] {PrintValue}";

    private string PrintValue => Type != TokenType.Whitespace ? Value.ToString() : Value.Length.ToString();
}
