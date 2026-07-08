namespace NetPack.Graph.Visitors;

using NetPack.Fragments;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
using GraphNode = NetPack.Graph.Node;
using AstNode = NetPack.Syntax.Ast.Node;
using static NetPack.Helpers;

/// <summary>
/// Walks a parsed module to discover its dependencies (static imports,
/// re-exports, dynamic <c>import()</c> and CommonJS <c>require()</c>) and to
/// collect the module's export names. Dead branches guarded by constant string
/// comparisons (e.g. <c>process.env.NODE_ENV</c> after substitution) are not
/// traversed, so their dependencies are not pulled into the graph.
/// </summary>
class JsVisitor(Bundle bundle, GraphNode current, Func<Bundle?, GraphNode, string, Task<GraphNode?>> report) : AstRewriter
{
    private readonly Func<Bundle?, GraphNode, string, Task<GraphNode?>> _report = report;
    private readonly Bundle _bundle = bundle;
    private readonly GraphNode _current = current;
    private readonly List<string> _exportNames = [];
    private readonly List<AstNode> _elements = [];
    private readonly List<Task<GraphNode?>> _tasks = [];

    public async Task<JsFragment> FindChildren(SourceFile ast)
    {
        Visit(ast);
        var nodes = await Task.WhenAll(_tasks);
        var replacements = GetReplacements(nodes, _elements);
        return new JsFragment(_current, ast, replacements, [.. _exportNames]);
    }

    protected override AstNode VisitIfStatement(IfStatement node)
    {
        if (node.Test is BinaryExpression be && be.Left is StringLiteral left && be.Right is StringLiteral right)
        {
            if ((be.Operator == TokenKind.EqualsEqualsEquals && left.Value == right.Value) ||
                (be.Operator == TokenKind.ExclamationEqualsEquals && left.Value != right.Value))
            {
                Visit(node.Consequent);
                return node;
            }
            if ((be.Operator == TokenKind.EqualsEqualsEquals && left.Value != right.Value) ||
                (be.Operator == TokenKind.ExclamationEqualsEquals && left.Value == right.Value))
            {
                if (node.Alternate is not null)
                {
                    Visit(node.Alternate);
                }
                return node;
            }
        }

        return base.VisitIfStatement(node);
    }

    protected override AstNode VisitExportAllDeclaration(ExportAllDeclaration node)
    {
        _elements.Add(node);
        _tasks.Add(_report(_bundle, _current, node.Source.Value));
        return base.VisitExportAllDeclaration(node);
    }

    protected override AstNode VisitImportDeclaration(ImportDeclaration node)
    {
        _elements.Add(node);
        _tasks.Add(_report(_bundle, _current, node.Source.Value));
        return base.VisitImportDeclaration(node);
    }

    protected override AstNode VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
    {
        _exportNames.Add("default");
        return base.VisitExportDefaultDeclaration(node);
    }

    protected override AstNode VisitExportNamedDeclaration(ExportNamedDeclaration node)
    {
        if (node.Source is not null)
        {
            _elements.Add(node);
            _tasks.Add(_report(_bundle, _current, node.Source.Value));
        }

        foreach (var specifier in node.Specifiers)
        {
            if (specifier.Exported is Identifier ident)
            {
                _exportNames.Add(ident.Name);
            }
            else if (specifier.Exported is StringLiteral lit)
            {
                _exportNames.Add(lit.Value);
            }
        }

        if (node.Declaration is not null)
        {
            foreach (var name in GetDeclarationExportNames(node.Declaration))
            {
                _exportNames.Add(name);
            }
        }

        return base.VisitExportNamedDeclaration(node);
    }

    private static IEnumerable<string> GetDeclarationExportNames(Statement declaration)
    {
        switch (declaration)
        {
            case VariableStatement variable:
                foreach (var d in variable.Declarations)
                {
                    if (d.Id is Identifier id)
                    {
                        yield return id.Name;
                    }
                }
                break;
            case FunctionDeclaration func when func.Id is not null:
                yield return func.Id.Name;
                break;
            case ClassDeclaration cls when cls.Id is not null:
                yield return cls.Id.Name;
                break;
        }
    }

    protected override AstNode VisitImportExpression(ImportExpression node)
    {
        if (node.Source is StringLiteral str)
        {
            _elements.Add(node);
            _tasks.Add(_report(null, _current, str.Value));
        }

        return base.VisitImportExpression(node);
    }

    protected override AstNode VisitCallExpression(CallExpression node)
    {
        if (node.Callee is Identifier ident && node.Arguments.Count == 1 &&
            node.Arguments[0] is StringLiteral str && ident.Name == "require")
        {
            _elements.Add(node);
            _tasks.Add(_report(_bundle, _current, str.Value));
        }

        return base.VisitCallExpression(node);
    }
}
