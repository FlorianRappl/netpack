namespace NetPack.Chunks;

using System.Text;
using Acornima.Ast;
using Acornima.Jsx;
using Acornima.Jsx.Ast;
using NetPack.Graph;

class JsChunk(Module ast, (Graph.Node? Node, Acornima.Ast.Node Element)[] replacements) : IChunk
{
    private readonly Module _ast = ast;
    private readonly (Graph.Node? Node, Acornima.Ast.Node Element)[] _replacements = replacements;

    public string Stringify(BundlerContext context, bool optimize)
    {
        var transpiler = new JsxToJavaScriptTranspiler();
        var ast = transpiler.Transpile(_ast);
        ast = ast.UpdateWith(NodeList.From(ast.Body.Where(m => m is not EmptyStatement)));

        foreach (var replacement in _replacements)
        {
            var element = replacement.Element;
            var node = replacement.Node;

            if (node is not null)
            {
                var bundle = context.Bundles.FirstOrDefault(m => m.Root == node);
                var asset = context.Assets.FirstOrDefault(m => m.Root == node);
                var reference = bundle?.GetFileName() ?? asset?.GetFileName() ?? Path.GetFileName(node.FileName);
            }
        }

        return ast.ToJsx();
    }


    internal sealed class JsxToJavaScriptTranspiler : JsxAstRewriter
    {
        private readonly NullLiteral _nullLiteral;
        private readonly BooleanLiteral _trueLiteral;

        public JsxToJavaScriptTranspiler()
        {
            _nullLiteral = new NullLiteral("null");
            _trueLiteral = new BooleanLiteral(true, "true");
        }

        public Program Transpile(Program root)
        {
            return VisitAndConvert(root);
        }
        
        protected override object? VisitImportDeclaration(ImportDeclaration node)
        {
            return new EmptyStatement();
        }

        protected override object? VisitExportAllDeclaration(ExportAllDeclaration node)
        {
            return new EmptyStatement();
        }

        protected override object? VisitExportNamedDeclaration(ExportNamedDeclaration node)
        {
            return new EmptyStatement();
        }

        protected override object? VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
        {
            var declarations = new List<VariableDeclarator>(1)
            {
                new(new Identifier("_default"), VisitAndConvert(node.Declaration) as Expression)
            };
            return new VariableDeclaration(VariableDeclarationKind.Const, NodeList.From(declarations));
        }

        public override object? VisitJsxAttribute(JsxAttribute node)
        {
            var attributeName = MakeString(node.Name.GetQualifiedName());
            var attributeValue = node.Value is null ? _trueLiteral : VisitAndConvert(node.Value);
            return new ObjectProperty(PropertyKind.Init, attributeName, attributeValue, computed: false, shorthand: false, method: false);
        }

        public override object? VisitJsxElement(JsxElement node)
        {
            var elementNameArg = GetName(node.OpeningElement.Name);

            Expression attributesArg;

            if (node.OpeningElement.Attributes.Count > 0)
            {
                var properties = new List<Acornima.Ast.Node>(capacity: node.OpeningElement.Attributes.Count);

                foreach (var attribute in node.OpeningElement.Attributes)
                {
                    properties.Add((Acornima.Ast.Node)Visit(attribute)!);
                }

                attributesArg = new ObjectExpression(NodeList.From(properties));
            }
            else
            {
                attributesArg = _nullLiteral;
            }

            var childrenArg = node.OpeningElement.SelfClosing
                ? _nullLiteral
                : VisitJsxElementChildren(node.Children);

            var react = new Identifier("React");
            var createElement = new Identifier("createElement");
            var reactCreateElement = new MemberExpression(react, createElement, false, false);
            return new CallExpression(reactCreateElement, NodeList.From(elementNameArg, attributesArg, childrenArg), false);
        }

        private Expression VisitJsxElementChildren(in NodeList<JsxNode> children)
        {
            if (children.Count > 0)
            {
                var elements = new List<Expression?>(capacity: children.Count);

                foreach (var child in children)
                {
                    var element = (Expression)Visit(child)!;

                    if (element is not JsxEmptyExpression && element is not null)
                    {
                        elements.Add(element);
                    }
                }

                return new ArrayExpression(NodeList.From(elements));
            }

            return _nullLiteral;
        }

        public override object? VisitJsxFragment(JsxFragment node)
        {
            var react = new Identifier("React");
            var createElement = new Identifier("createElement");
            var fragment = new Identifier("Fragment");
            var elementNameArg =  new MemberExpression(react, fragment, false, false);
            var childrenArg = VisitJsxElementChildren(node.Children);
            var reactCreateElement = new MemberExpression(react, createElement, false, false);
            return new CallExpression(reactCreateElement, NodeList.From(elementNameArg, _nullLiteral, childrenArg), false);
        }

        public override object? VisitJsxExpressionContainer(JsxExpressionContainer node)
        {
            return VisitAndConvert(node.Expression);
        }

        public override object? VisitJsxSpreadAttribute(JsxSpreadAttribute node)
        {
            return new SpreadElement(node.Argument);
        }

        public override object? VisitJsxText(JsxText node)
        {
            if (!string.IsNullOrWhiteSpace(node.Value))
            {
                return MakeString(node.Value);
            }

            return null;
        }

        private static StringLiteral MakeString(string unencodedText)
        {
            var rawText = MakeJsonString(unencodedText);
            return new StringLiteral(unencodedText, rawText);
        }

        private static Expression GetName(JsxName name)
        {
            if (name is JsxMemberExpression member)
            {
                return GetReference(member);
            }
            else if (name is JsxIdentifier ident && !string.IsNullOrEmpty(ident.Name) && char.IsUpper(ident.Name[0]))
            {
                return GetReference(name);
            }

            return MakeString(name.GetQualifiedName());
        }

        private static Expression GetReference(JsxName name)
        {
            if (name is JsxMemberExpression member)
            {
                var obj = GetReference(member.Object);
                var prop = member.Property;
                var propName = new Identifier(prop.Name);
                return new MemberExpression(obj, propName, false, false);
            }
            else if (name is JsxIdentifier ident)
            {
                return new Identifier(ident.Name);
            }
            else
            {
                return new Identifier(name.GetQualifiedName());
            }
        }

        private static string MakeJsonString(string value)
        {
            var sb = new StringBuilder();
            sb.Append('\"');  // Start with a double quote

            foreach (char c in value)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(c))
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4")); // Unicode escape
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            sb.Append('\"');  // End with a double quote
            return sb.ToString();
        }
    }
}
