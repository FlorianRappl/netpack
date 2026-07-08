namespace NetPack.Fragments;

using NetPack.Syntax;
using NetPack.Syntax.Ast;
using GraphNode = NetPack.Graph.Node;

/// <summary>
/// A synthetic module for an external dependency. It re-exports the external
/// import so the rest of the bundle can consume it through the normal module
/// interop shape:
/// <code>
/// import * as _hash from "name";
/// module.exports = _hash.__esModule ? _hash : Object.assign({}, _hash.default, _hash);
/// </code>
/// </summary>
public class JsExternalFragment : JsFragment
{
    private JsExternalFragment(GraphNode root, SourceFile ast)
        : base(root, ast, new Dictionary<Node, GraphNode>(), [])
    {
    }

    public static JsFragment CreateFrom(GraphNode node)
    {
        var ast = MakeExternalFragment(node);
        return new JsExternalFragment(node, ast);
    }

    private static SourceFile MakeExternalFragment(GraphNode root)
    {
        var name = root.FileName;
        var hash = name.GetHashCode().ToString("x");
        var local = $"_{hash}";

        var specifier = new ImportNamespaceSpecifier(new Identifier(local));
        var import = new ImportDeclaration(
            new List<ImportSpecifierBase> { specifier },
            new StringLiteral(name, name),
            typeOnly: false);

        var moduleExports = new MemberExpression(new Identifier("module"), new Identifier("exports"), false, false);
        var objectAssign = new MemberExpression(new Identifier("Object"), new Identifier("assign"), false, false);
        var assign = new CallExpression(objectAssign, new List<Expression>
        {
            new ObjectExpression(new List<Node>()),
            new MemberExpression(new Identifier(local), new Identifier("default"), false, false),
            new Identifier(local),
        }, optional: false);
        var content = new ConditionalExpression(
            new MemberExpression(new Identifier(local), new Identifier("__esModule"), false, false),
            new Identifier(local),
            assign);
        var assignment = new AssignmentExpression(TokenKind.Equals, moduleExports, content);

        var body = new List<Statement>
        {
            import,
            new ExpressionStatement(assignment),
        };

        return new SourceFile(name, body, System.Array.Empty<Diagnostic>());
    }
}
