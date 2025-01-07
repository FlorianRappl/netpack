namespace NetPack.Graph.Visitors;

using Acornima.Ast;
using NetPack.Fragments;
using NetPack.Graph.Bundles;
using static NetPack.Helpers;

class JsVisitor(Bundle bundle, Graph.Node current, Func<Bundle?, Graph.Node, string, Task<Graph.Node?>> report) : Acornima.Jsx.JsxAstVisitor
{
    private readonly Func<Bundle?, Graph.Node, string, Task<Graph.Node?>> _report = report;
    private readonly Bundle _bundle = bundle;
    private readonly Graph.Node _current = current;
    private readonly List<string> _exportNames = [];
    private readonly List<Node> _elements = [];
    private readonly List<Task<Graph.Node?>> _tasks = [];

    public async Task<JsFragment> FindChildren(Module ast)
    {
        Visit(ast);
        var nodes = await Task.WhenAll(_tasks);
        var replacements = GetReplacements(nodes, _elements);
        return new JsFragment(_current, ast, replacements, [.. _exportNames]);
    }

    protected override object? VisitIfStatement(IfStatement node)
    {
        if (node.Test is BinaryExpression be && be.Left is StringLiteral left && be.Right is StringLiteral right)
        {
            if ((be.Operator == Acornima.Operator.StrictEquality && left.Value == right.Value) || (be.Operator == Acornima.Operator.StrictInequality && left.Value != right.Value))
            {
                return Visit(node.Consequent);
            }
            else if ((be.Operator == Acornima.Operator.StrictEquality && left.Value != right.Value) || (be.Operator == Acornima.Operator.StrictInequality && left.Value == right.Value))
            {
                return node.Alternate is not null ? Visit(node.Alternate) : null;
            }
        }

        return base.VisitIfStatement(node);
    }

    protected override object? VisitExportAllDeclaration(ExportAllDeclaration node)
    {
        var file = node.Source.Value;
        _elements.Add(node);
        _tasks.Add(_report(_bundle, _current, file));
        return base.VisitExportAllDeclaration(node);
    }

    protected override object? VisitImportDeclaration(ImportDeclaration node)
    {
        var file = node.Source.Value;
        _elements.Add(node);
        _tasks.Add(_report(_bundle, _current, file));
        return base.VisitImportDeclaration(node);
    }

    protected override object? VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
    {
        _exportNames.Add("default");
        return base.VisitExportDefaultDeclaration(node);
    }

    protected override object? VisitExportNamedDeclaration(ExportNamedDeclaration node)
    {
        var str = node.Source;

        if (str is not null)
        {
            _elements.Add(node);
            _tasks.Add(_report(_bundle, _current, str.Value));
        }

        foreach (var specifier in node.Specifiers)
        {
            if (specifier.Local is Identifier ident)
            {
                _exportNames.Add(ident.Name);
            }
            else if (specifier.Local is StringLiteral lit)
            {
                _exportNames.Add(lit.Value);
            }
        }

        return base.VisitExportNamedDeclaration(node);
    }

    protected override object? VisitImportExpression(ImportExpression node)
    {
        if (node.Source is StringLiteral str)
        {
            _elements.Add(node);
            _tasks.Add(_report(null, _current, str.Value));
        }

        return base.VisitImportExpression(node);
    }

    protected override object? VisitCallExpression(CallExpression node)
    {
        if (node.Callee is Identifier ident && node.Arguments.Count == 1 && node.Arguments[0] is StringLiteral str && ident.Name == "require")
        {
            _elements.Add(node);
            _tasks.Add(_report(_bundle, _current, str.Value));
        }

        return base.VisitCallExpression(node);
    }
}