namespace NetPack.Syntax.Ast;

using System.Collections.Generic;

/// <summary>Base type for the three import specifier shapes.</summary>
public abstract class ImportSpecifierBase : Node
{
    protected ImportSpecifierBase(Identifier local) => Local = local;
    public Identifier Local { get; set; }
}

/// <summary><c>import { a as b }</c> — <see cref="Imported"/> may be a string
/// literal for arbitrary module export names.</summary>
public sealed class ImportSpecifier : ImportSpecifierBase
{
    public ImportSpecifier(Node imported, Identifier local, bool typeOnly) : base(local)
    {
        Imported = imported;
        TypeOnly = typeOnly;
    }
    public Node Imported { get; set; }
    public bool TypeOnly { get; set; }
    public override NodeKind Kind => NodeKind.ImportSpecifier;
}

/// <summary><c>import a from '...'</c>.</summary>
public sealed class ImportDefaultSpecifier : ImportSpecifierBase
{
    public ImportDefaultSpecifier(Identifier local) : base(local) { }
    public override NodeKind Kind => NodeKind.ImportDefaultSpecifier;
}

/// <summary><c>import * as a from '...'</c>.</summary>
public sealed class ImportNamespaceSpecifier : ImportSpecifierBase
{
    public ImportNamespaceSpecifier(Identifier local) : base(local) { }
    public override NodeKind Kind => NodeKind.ImportNamespaceSpecifier;
}

public sealed class ImportDeclaration : Declaration
{
    public ImportDeclaration(IList<ImportSpecifierBase> specifiers, StringLiteral source, bool typeOnly)
    {
        Specifiers = specifiers;
        Source = source;
        TypeOnly = typeOnly;
    }
    public IList<ImportSpecifierBase> Specifiers { get; set; }
    public StringLiteral Source { get; set; }
    /// <summary>An <c>import type</c> declaration — erased from JS output.</summary>
    public bool TypeOnly { get; set; }
    public override NodeKind Kind => NodeKind.ImportDeclaration;
}

public sealed class ExportSpecifier : Node
{
    public ExportSpecifier(Node local, Node exported, bool typeOnly)
    {
        Local = local;
        Exported = exported;
        TypeOnly = typeOnly;
    }
    /// <summary><see cref="Identifier"/> or <see cref="StringLiteral"/>.</summary>
    public Node Local { get; set; }
    public Node Exported { get; set; }
    public bool TypeOnly { get; set; }
    public override NodeKind Kind => NodeKind.ExportSpecifier;
}

/// <summary><c>export { a, b as c }</c> or <c>export { a } from '...'</c> or
/// <c>export const x = 1</c> (when <see cref="Declaration"/> is set).</summary>
public sealed class ExportNamedDeclaration : Declaration
{
    public ExportNamedDeclaration(Statement? declaration, IList<ExportSpecifier> specifiers, StringLiteral? source, bool typeOnly)
    {
        Declaration = declaration;
        Specifiers = specifiers;
        Source = source;
        TypeOnly = typeOnly;
    }
    public Statement? Declaration { get; set; }
    public IList<ExportSpecifier> Specifiers { get; set; }
    public StringLiteral? Source { get; set; }
    public bool TypeOnly { get; set; }
    public override NodeKind Kind => NodeKind.ExportNamedDeclaration;
}

public sealed class ExportDefaultDeclaration : Declaration
{
    public ExportDefaultDeclaration(Node declaration) => Declaration = declaration;
    /// <summary>An expression, function, or class.</summary>
    public Node Declaration { get; set; }
    public override NodeKind Kind => NodeKind.ExportDefaultDeclaration;
}

/// <summary><c>export * from '...'</c> or <c>export * as ns from '...'</c>.</summary>
public sealed class ExportAllDeclaration : Declaration
{
    public ExportAllDeclaration(StringLiteral source, Identifier? exported)
    {
        Source = source;
        Exported = exported;
    }
    public StringLiteral Source { get; set; }
    public Identifier? Exported { get; set; }
    public override NodeKind Kind => NodeKind.ExportAllDeclaration;
}

/// <summary>
/// A TypeScript construct that produces no runtime output (type alias,
/// interface, or a <c>declare</c>d entity). Kept in the tree so the printer can
/// deliberately skip it, and so tooling can report on it.
/// </summary>
public sealed class TypeOnlyDeclaration : Declaration
{
    public TypeOnlyDeclaration(NodeKind kind, string? name)
    {
        _kind = kind;
        Name = name;
    }
    private readonly NodeKind _kind;
    public string? Name { get; set; }
    public override NodeKind Kind => _kind;
}
