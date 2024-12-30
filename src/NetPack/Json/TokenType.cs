namespace NetPack.Json;

public enum TokenType
{
    ObjectStart,
    ObjectEnd,
    ArrayStart,
    ArrayEnd,
    Key,
    String,
    Float,
    Integer,
    True,
    False,
    Null,
    Whitespace,
    Operator,
    Unknown
}
