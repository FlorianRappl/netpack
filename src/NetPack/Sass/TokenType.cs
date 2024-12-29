namespace NetPack.Sass;

public enum TokenType
{
    Selector,
    Property,
    Value,
    Variable,
    Comment,
    AtRule,    // e.g., @import, @mixin, @include
    CurlyBracketOpen,  // {
    CurlyBracketClose, // }
    Colon,     // :
    Semicolon, // ;
    Unknown    // For any unidentified tokens
}
