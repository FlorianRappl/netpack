namespace NetPack.Fragments;

using Acornima.Ast;

public class JsExternalFragment(Graph.Node root) : JsFragment(root, MakeExternalFragment(root), new Dictionary<Node, Graph.Node>(), [])
{
    private static Module MakeExternalFragment(Graph.Node root)
    {
        var name = root.FileName;
        var hash = name.GetHashCode().ToString("x");
        var id = new Identifier($"_{hash}");
        var specifier = new ImportNamespaceSpecifier(id);
        var moduleExports = new MemberExpression(new Identifier("module"), new Identifier("exports"), false, false);
        var objectAssign = new MemberExpression(new Identifier("Object"), new Identifier("assign"), false, false);
        var assign = new CallExpression(objectAssign, NodeList.From<Expression>([
            new ObjectExpression(NodeList.From<Node>([])),
            new MemberExpression(id, new Identifier("default"), false, false),
            id,
        ]), false);
        var content = new ConditionalExpression(new MemberExpression(id, new Identifier("__esModule"), false, false), id, assign);
        var assignment = new AssignmentExpression(Acornima.Operator.Assignment, moduleExports, content);
        var ast = new Module(NodeList.From<Statement>([
            new ImportDeclaration(NodeList.From<ImportDeclarationSpecifier>([specifier]), new StringLiteral(name, $"'{name}'"), NodeList.From<ImportAttribute>([])),
            new NonSpecialExpressionStatement(assignment),
        ]));

        return ast;
    }
}
