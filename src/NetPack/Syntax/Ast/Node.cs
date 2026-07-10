namespace NetPack.Syntax.Ast;

using System.Collections.Generic;

/// <summary>
/// Discriminator for every AST node kind. Having a flat enum lets consumers
/// switch cheaply without reflection (important for AOT) and keeps
/// pattern-matching exhaustive.
/// </summary>
public enum NodeKind
{
    SourceFile,

    // Statements
    ImportDeclaration,
    ExportNamedDeclaration,
    ExportDefaultDeclaration,
    ExportAllDeclaration,
    VariableStatement,
    FunctionDeclaration,
    ClassDeclaration,
    ExpressionStatement,
    BlockStatement,
    ReturnStatement,
    IfStatement,
    ForStatement,
    ForInStatement,
    ForOfStatement,
    WhileStatement,
    DoWhileStatement,
    ThrowStatement,
    TryStatement,
    SwitchStatement,
    BreakStatement,
    ContinueStatement,
    LabeledStatement,
    DebuggerStatement,
    EmptyStatement,
    // TypeScript declarations that are erased in JS output.
    TypeAliasDeclaration,
    InterfaceDeclaration,
    EnumDeclaration,

    // Clauses / helpers
    VariableDeclarator,
    ImportSpecifier,
    ImportDefaultSpecifier,
    ImportNamespaceSpecifier,
    ExportSpecifier,
    Property,
    Parameter,
    CatchClause,
    SwitchCase,
    TemplateElement,
    ClassBody,
    MethodDefinition,
    PropertyDefinition,
    StaticBlock,
    Decorator,

    // Expressions
    Identifier,
    PrivateIdentifier,
    NumericLiteral,
    BigIntLiteral,
    StringLiteral,
    BooleanLiteral,
    NullLiteral,
    RegExpLiteral,
    TemplateLiteral,
    TaggedTemplateExpression,
    ArrayExpression,
    ObjectExpression,
    FunctionExpression,
    ArrowFunctionExpression,
    ClassExpression,
    CallExpression,
    NewExpression,
    MemberExpression,
    BinaryExpression,
    LogicalExpression,
    UnaryExpression,
    UpdateExpression,
    AssignmentExpression,
    ConditionalExpression,
    SequenceExpression,
    SpreadElement,
    ParenthesizedExpression,
    ImportExpression,
    AwaitExpression,
    YieldExpression,
    ThisExpression,
    SuperExpression,
    MetaProperty,

    // JSX
    JsxElement,
    JsxFragment,
    JsxOpeningElement,
    JsxClosingElement,
    JsxAttribute,
    JsxSpreadAttribute,
    JsxExpressionContainer,
    JsxText,
    JsxIdentifier,
    JsxMemberExpression,
    JsxNamespacedName,

    // Raw / passthrough
    Raw,
}

/// <summary>Base type for every AST node. Carries source span information.</summary>
public abstract class Node
{
    /// <summary>Inclusive start offset into the source text.</summary>
    public int Start { get; set; }

    /// <summary>Exclusive end offset into the source text.</summary>
    public int End { get; set; }

    /// <summary>The discriminating kind of this node.</summary>
    public abstract NodeKind Kind { get; }
}

/// <summary>Marker base type for expression nodes.</summary>
public abstract class Expression : Node
{
}

/// <summary>Marker base type for statement nodes.</summary>
public abstract class Statement : Node
{
}

/// <summary>Marker base type for declaration statements.</summary>
public abstract class Declaration : Statement
{
}

/// <summary>
/// A verbatim chunk of already-formed source. Used by code generators and
/// synthetic-AST builders to splice in text that does not need to be modelled
/// as structured nodes.
/// </summary>
public sealed class Raw : Expression
{
    public Raw(string text) => Text = text;
    public string Text { get; set; }
    public override NodeKind Kind => NodeKind.Raw;
}

/// <summary>
/// The root of a parsed module. NetPack always parses in module mode, matching
/// how the bundler treats every entry.
/// </summary>
public sealed class SourceFile : Node
{
    private int[]? _lineStarts;

    public SourceFile(string fileName, IList<Statement> body, IReadOnlyList<Diagnostic> diagnostics, string source = "")
    {
        FileName = fileName;
        Body = body;
        Diagnostics = diagnostics;
        Source = source;
    }

    public string FileName { get; }

    public IList<Statement> Body { get; set; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>The original source text (used for source maps: line/column
    /// resolution and <c>sourcesContent</c>). Empty for synthetic modules.</summary>
    public string Source { get; }

    /// <summary>Converts a character offset into a zero-based (line, column)
    /// position, as required by Source Map v3.</summary>
    public (int Line, int Column) GetLineColumn(int offset)
    {
        var starts = _lineStarts ??= ComputeLineStarts(Source);
        // Largest line-start that is &lt;= offset.
        int lo = 0, hi = starts.Length - 1, line = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (starts[mid] <= offset)
            {
                line = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return (line, offset - starts[line]);
    }

    private static int[] ComputeLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\n')
            {
                starts.Add(i + 1);
            }
            else if (c == '\r')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n') i++;
                starts.Add(i + 1);
            }
        }
        return starts.ToArray();
    }

    public override NodeKind Kind => NodeKind.SourceFile;
}
