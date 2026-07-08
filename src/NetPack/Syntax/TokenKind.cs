namespace NetPack.Syntax;

/// <summary>
/// The complete set of lexical token kinds recognized by the NetPack
/// JavaScript / TypeScript / JSX tokenizer.
///
/// Every reserved word, contextual keyword and punctuator has a dedicated
/// member so that the parser can switch on a single <see cref="TokenKind"/>
/// value instead of comparing raw strings. This mirrors the design used by
/// high-performance tokenizers such as the ones in esbuild and the TypeScript
/// compiler (their <c>SyntaxKind</c>).
/// </summary>
public enum TokenKind : byte
{
    // ---- Special ---------------------------------------------------------
    Unknown = 0,
    EndOfFile,

    // ---- Trivia (only produced when the lexer is asked to keep it) -------
    SingleLineComment,
    MultiLineComment,
    HashbangComment,
    WhiteSpace,
    LineTerminator,

    // ---- Literals --------------------------------------------------------
    NumericLiteral,
    BigIntLiteral,
    StringLiteral,
    RegExpLiteral,

    /// <summary>A template with no substitutions: <c>`abc`</c>.</summary>
    NoSubstitutionTemplate,
    /// <summary>The <c>`abc${</c> portion of a template.</summary>
    TemplateHead,
    /// <summary>The <c>}abc${</c> portion of a template.</summary>
    TemplateMiddle,
    /// <summary>The <c>}abc`</c> portion of a template.</summary>
    TemplateTail,

    // ---- Names -----------------------------------------------------------
    Identifier,
    /// <summary>A private class member name: <c>#field</c>.</summary>
    PrivateIdentifier,

    // ---- Punctuators -----------------------------------------------------
    OpenBrace,            // {
    CloseBrace,           // }
    OpenParen,            // (
    CloseParen,           // )
    OpenBracket,          // [
    CloseBracket,         // ]
    Dot,                  // .
    DotDotDot,            // ...
    Semicolon,            // ;
    Comma,                // ,
    Question,             // ?
    QuestionDot,          // ?.
    QuestionQuestion,     // ??
    Colon,                // :
    At,                   // @  (decorators)
    Arrow,                // =>
    Backtick,             // `

    LessThan,             // <
    GreaterThan,          // >
    LessThanEquals,       // <=
    GreaterThanEquals,    // >=
    EqualsEquals,         // ==
    ExclamationEquals,    // !=
    EqualsEqualsEquals,   // ===
    ExclamationEqualsEquals, // !==

    Plus,                 // +
    Minus,                // -
    Asterisk,             // *
    AsteriskAsterisk,     // **
    Slash,                // /
    Percent,              // %
    PlusPlus,             // ++
    MinusMinus,           // --
    LessThanLessThan,     // <<
    GreaterThanGreaterThan,             // >>
    GreaterThanGreaterThanGreaterThan,  // >>>
    Ampersand,            // &
    Bar,                  // |
    Caret,                // ^
    Exclamation,          // !
    Tilde,                // ~
    AmpersandAmpersand,   // &&
    BarBar,               // ||

    // ---- Assignment operators -------------------------------------------
    Equals,               // =
    PlusEquals,           // +=
    MinusEquals,          // -=
    AsteriskEquals,       // *=
    AsteriskAsteriskEquals, // **=
    SlashEquals,          // /=
    PercentEquals,        // %=
    LessThanLessThanEquals,             // <<=
    GreaterThanGreaterThanEquals,       // >>=
    GreaterThanGreaterThanGreaterThanEquals, // >>>=
    AmpersandEquals,      // &=
    BarEquals,            // |=
    CaretEquals,          // ^=
    AmpersandAmpersandEquals, // &&=
    BarBarEquals,         // ||=
    QuestionQuestionEquals,   // ??=

    // ---- Reserved words (ECMAScript) ------------------------------------
    BreakKeyword,
    CaseKeyword,
    CatchKeyword,
    ClassKeyword,
    ConstKeyword,
    ContinueKeyword,
    DebuggerKeyword,
    DefaultKeyword,
    DeleteKeyword,
    DoKeyword,
    ElseKeyword,
    EnumKeyword,
    ExportKeyword,
    ExtendsKeyword,
    FalseKeyword,
    FinallyKeyword,
    ForKeyword,
    FunctionKeyword,
    IfKeyword,
    ImportKeyword,
    InKeyword,
    InstanceOfKeyword,
    NewKeyword,
    NullKeyword,
    ReturnKeyword,
    SuperKeyword,
    SwitchKeyword,
    ThisKeyword,
    ThrowKeyword,
    TrueKeyword,
    TryKeyword,
    TypeOfKeyword,
    VarKeyword,
    VoidKeyword,
    WhileKeyword,
    WithKeyword,

    // ---- Strict-mode / contextual ECMAScript keywords -------------------
    ImplementsKeyword,
    InterfaceKeyword,
    LetKeyword,
    PackageKeyword,
    PrivateKeyword,
    ProtectedKeyword,
    PublicKeyword,
    StaticKeyword,
    YieldKeyword,
    AsyncKeyword,
    AwaitKeyword,
    OfKeyword,
    GetKeyword,
    SetKeyword,

    // ---- TypeScript contextual keywords ---------------------------------
    AbstractKeyword,
    AccessorKeyword,
    AsKeyword,
    AssertsKeyword,
    AssertKeyword,
    AnyKeyword,
    BooleanKeyword,
    ConstructorKeyword,
    DeclareKeyword,
    FromKeyword,
    GlobalKeyword,
    InferKeyword,
    IsKeyword,
    KeyOfKeyword,
    ModuleKeyword,
    NamespaceKeyword,
    NeverKeyword,
    NumberKeyword,
    ObjectKeyword,
    OutKeyword,
    OverrideKeyword,
    ReadonlyKeyword,
    RequireKeyword,
    SatisfiesKeyword,
    StringKeyword,
    SymbolKeyword,
    TypeKeyword,
    UndefinedKeyword,
    UniqueKeyword,
    UnknownKeyword,
    BigIntKeyword,
    IntrinsicKeyword,
}
