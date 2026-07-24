namespace NetPack.Syntax;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Maps identifier text to keyword <see cref="TokenKind"/> values and answers
/// classification questions the parser needs (reserved-ness, contextual
/// keywords, type-only keywords).
/// </summary>
public static class Keywords
{
    private static readonly FrozenDictionary<string, TokenKind> _map = new Dictionary<string, TokenKind>(System.StringComparer.Ordinal)
    {
        // Reserved words
        ["break"] = TokenKind.BreakKeyword,
        ["case"] = TokenKind.CaseKeyword,
        ["catch"] = TokenKind.CatchKeyword,
        ["class"] = TokenKind.ClassKeyword,
        ["const"] = TokenKind.ConstKeyword,
        ["continue"] = TokenKind.ContinueKeyword,
        ["debugger"] = TokenKind.DebuggerKeyword,
        ["default"] = TokenKind.DefaultKeyword,
        ["delete"] = TokenKind.DeleteKeyword,
        ["do"] = TokenKind.DoKeyword,
        ["else"] = TokenKind.ElseKeyword,
        ["enum"] = TokenKind.EnumKeyword,
        ["export"] = TokenKind.ExportKeyword,
        ["extends"] = TokenKind.ExtendsKeyword,
        ["false"] = TokenKind.FalseKeyword,
        ["finally"] = TokenKind.FinallyKeyword,
        ["for"] = TokenKind.ForKeyword,
        ["function"] = TokenKind.FunctionKeyword,
        ["if"] = TokenKind.IfKeyword,
        ["import"] = TokenKind.ImportKeyword,
        ["in"] = TokenKind.InKeyword,
        ["instanceof"] = TokenKind.InstanceOfKeyword,
        ["new"] = TokenKind.NewKeyword,
        ["null"] = TokenKind.NullKeyword,
        ["return"] = TokenKind.ReturnKeyword,
        ["super"] = TokenKind.SuperKeyword,
        ["switch"] = TokenKind.SwitchKeyword,
        ["this"] = TokenKind.ThisKeyword,
        ["throw"] = TokenKind.ThrowKeyword,
        ["true"] = TokenKind.TrueKeyword,
        ["try"] = TokenKind.TryKeyword,
        ["typeof"] = TokenKind.TypeOfKeyword,
        ["var"] = TokenKind.VarKeyword,
        ["void"] = TokenKind.VoidKeyword,
        ["while"] = TokenKind.WhileKeyword,
        ["with"] = TokenKind.WithKeyword,

        // Strict-mode / contextual ECMAScript keywords
        ["implements"] = TokenKind.ImplementsKeyword,
        ["interface"] = TokenKind.InterfaceKeyword,
        ["let"] = TokenKind.LetKeyword,
        ["package"] = TokenKind.PackageKeyword,
        ["private"] = TokenKind.PrivateKeyword,
        ["protected"] = TokenKind.ProtectedKeyword,
        ["public"] = TokenKind.PublicKeyword,
        ["static"] = TokenKind.StaticKeyword,
        ["yield"] = TokenKind.YieldKeyword,
        ["async"] = TokenKind.AsyncKeyword,
        ["await"] = TokenKind.AwaitKeyword,
        ["of"] = TokenKind.OfKeyword,
        ["get"] = TokenKind.GetKeyword,
        ["set"] = TokenKind.SetKeyword,

        // TypeScript contextual keywords
        ["abstract"] = TokenKind.AbstractKeyword,
        ["accessor"] = TokenKind.AccessorKeyword,
        ["as"] = TokenKind.AsKeyword,
        ["asserts"] = TokenKind.AssertsKeyword,
        ["assert"] = TokenKind.AssertKeyword,
        ["any"] = TokenKind.AnyKeyword,
        ["boolean"] = TokenKind.BooleanKeyword,
        ["constructor"] = TokenKind.ConstructorKeyword,
        ["declare"] = TokenKind.DeclareKeyword,
        ["from"] = TokenKind.FromKeyword,
        ["global"] = TokenKind.GlobalKeyword,
        ["infer"] = TokenKind.InferKeyword,
        ["is"] = TokenKind.IsKeyword,
        ["keyof"] = TokenKind.KeyOfKeyword,
        ["module"] = TokenKind.ModuleKeyword,
        ["namespace"] = TokenKind.NamespaceKeyword,
        ["never"] = TokenKind.NeverKeyword,
        ["number"] = TokenKind.NumberKeyword,
        ["object"] = TokenKind.ObjectKeyword,
        ["out"] = TokenKind.OutKeyword,
        ["override"] = TokenKind.OverrideKeyword,
        ["readonly"] = TokenKind.ReadonlyKeyword,
        ["require"] = TokenKind.RequireKeyword,
        ["satisfies"] = TokenKind.SatisfiesKeyword,
        ["string"] = TokenKind.StringKeyword,
        ["symbol"] = TokenKind.SymbolKeyword,
        ["type"] = TokenKind.TypeKeyword,
        ["undefined"] = TokenKind.UndefinedKeyword,
        ["unique"] = TokenKind.UniqueKeyword,
        ["unknown"] = TokenKind.UnknownKeyword,
        ["bigint"] = TokenKind.BigIntKeyword,
        ["intrinsic"] = TokenKind.IntrinsicKeyword,
    }.ToFrozenDictionary(System.StringComparer.Ordinal);

    /// <summary>The first keyword kind in <see cref="TokenKind"/> enum order.</summary>
    private const TokenKind FirstKeyword = TokenKind.BreakKeyword;

    /// <summary>
    /// Resolves an identifier's text to a keyword kind, or
    /// <see cref="TokenKind.Identifier"/> when it is not a keyword.
    /// </summary>
    public static TokenKind Classify(string text)
        => _map.TryGetValue(text, out var kind) ? kind : TokenKind.Identifier;

    /// <summary>True when the kind denotes any keyword (reserved or contextual).</summary>
    public static bool IsKeyword(TokenKind kind) => kind >= FirstKeyword;

    /// <summary>
    /// True for words that are reserved in every context and therefore can
    /// never be used as a binding identifier in a module.
    /// </summary>
    public static bool IsReservedWord(TokenKind kind) => kind switch
    {
        TokenKind.BreakKeyword or TokenKind.CaseKeyword or TokenKind.CatchKeyword
            or TokenKind.ClassKeyword or TokenKind.ConstKeyword or TokenKind.ContinueKeyword
            or TokenKind.DebuggerKeyword or TokenKind.DefaultKeyword or TokenKind.DeleteKeyword
            or TokenKind.DoKeyword or TokenKind.ElseKeyword or TokenKind.EnumKeyword
            or TokenKind.ExportKeyword or TokenKind.ExtendsKeyword or TokenKind.FalseKeyword
            or TokenKind.FinallyKeyword or TokenKind.ForKeyword or TokenKind.FunctionKeyword
            or TokenKind.IfKeyword or TokenKind.ImportKeyword or TokenKind.InKeyword
            or TokenKind.InstanceOfKeyword or TokenKind.NewKeyword or TokenKind.NullKeyword
            or TokenKind.ReturnKeyword or TokenKind.SuperKeyword or TokenKind.SwitchKeyword
            or TokenKind.ThisKeyword or TokenKind.ThrowKeyword or TokenKind.TrueKeyword
            or TokenKind.TryKeyword or TokenKind.TypeOfKeyword or TokenKind.VarKeyword
            or TokenKind.VoidKeyword or TokenKind.WhileKeyword or TokenKind.WithKeyword => true,
        _ => false,
    };

    /// <summary>
    /// Contextual keywords behave like identifiers except in specific
    /// positions. When the parser is in an identifier position it can accept
    /// these tokens as plain names.
    /// </summary>
    public static bool IsIdentifierName(TokenKind kind)
        => kind == TokenKind.Identifier || (IsKeyword(kind) && !IsReservedWord(kind));
}
