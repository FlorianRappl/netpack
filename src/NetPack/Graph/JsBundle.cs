namespace NetPack.Graph;

using System.Collections.Concurrent;
using System.Text;
using Acornima.Ast;
using Acornima.Jsx;
using Acornima.Jsx.Ast;
using NetPack.Fragments;

public sealed class JsBundle(BundlerContext context, Node root, BundleFlags flags) : Bundle(root, flags)
{
    private static readonly ConcurrentBag<JsFragment> _fragments = [];

    public static ConcurrentBag<JsFragment> Fragments => _fragments;

    public BundlerContext Context => context;

    public override async Task<Stream> CreateStream(bool optimize)
    {
        var content = Stringify(optimize);
        var raw = Encoding.UTF8.GetBytes(content);
        var src = new MemoryStream();
        await src.WriteAsync(raw);
        src.Position = 0;
        return src;
    }

    public string Stringify(bool optimize)
    {
        var transpiler = new JsxToJavaScriptTranspiler(this, optimize);
        var ast = transpiler.Transpile();
        return ast.ToJsx();
    }
    
    internal sealed class JsxToJavaScriptTranspiler(JsBundle bundle, bool optimize) : JsxAstRewriter
    {
        private readonly NullLiteral _nullLiteral = new("null");
        private readonly BooleanLiteral _trueLiteral = new(true, "true");
        private readonly JsBundle _bundle = bundle;
        private readonly bool _optimize = optimize;
        private JsFragment? _current;

        public Program Transpile()
        {
            var imports = new List<Statement>();
            var exports = new List<Statement>();
            var statements = new List<Statement>();
            var node = _bundle.Root;
            var fragment = _fragments.FirstOrDefault(m => m.Root == node);

            if (fragment is not null)
            {
                _current = fragment;

                var ast = VisitAndConvert(fragment.Ast);

                foreach (var statement in ast.Body)
                {
                    if (statement is EmptyStatement)
                    {
                        // ignore
                    }
                    else if (statement is ExportDeclaration)
                    {
                        exports.Add(statement);
                    }
                    else if (statement is ImportDeclaration)
                    {
                        imports.Add(statement);
                    }
                    else
                    {
                        statements.Add(statement);
                    }
                }
            }
            
            return new Module(NodeList.From(imports.Concat(statements).Concat(exports)));
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
                    return node.Alternate is not null ? Visit(node.Alternate) : new EmptyStatement();
                }
            }

            return base.VisitIfStatement(node);
        }

        protected override object? VisitImportExpression(ImportExpression node)
        {
            if (_current?.Replacements.TryGetValue(node, out var referenceNode) ?? false)
            {
                var bundle = _bundle.Context.Bundles.First(m => m.Root == referenceNode);
                var reference = bundle.GetFileName();
                return new ImportExpression(MakeAutoReference(reference));
            }

            return base.VisitImportExpression(node);
        }

        protected override object? VisitImportDeclaration(ImportDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var referenceNode) ?? false)
            {
                var asset = _bundle.Context.Assets.FirstOrDefault(m => m.Root == referenceNode);

                if (asset is not null && node.Specifiers.Count == 1 && node.Specifiers[0] is ImportDefaultSpecifier specifier)
                {
                    var name = specifier.Local;
                    var reference = asset.GetFileName();
                    var declarator = new VariableDeclarator(name, MakeAutoReference(reference));
                    return new VariableDeclaration(VariableDeclarationKind.Const, NodeList.From([declarator]));
                }
            }

            return new EmptyStatement();
        }

        protected override object? VisitExportAllDeclaration(ExportAllDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var referenceNode) ?? false)
            {
                return new EmptyStatement();
            }

            return node;
        }

        protected override object? VisitExportNamedDeclaration(ExportNamedDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var referenceNode) ?? false)
            {
                return new EmptyStatement();
            }

            return node;
        }

        protected override object? VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var referenceNode) ?? false)
            {
                return new EmptyStatement();
            }

            var payload = VisitAndConvert(node.Declaration);
            return new ExportDefaultDeclaration(payload);
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

        private static MemberExpression MakeAutoReference(string reference)
        {
            var url = new Identifier("URL");
            var import = new Identifier("import");
            var importMeta = new MemberExpression(import, new Identifier("meta"), false, false);
            var importMetaUrl = new MemberExpression(importMeta, new Identifier("url"), false, false);
            var urlParse = new MemberExpression(url, new Identifier("parse"), false, false);
            var relative = new CallExpression(urlParse, NodeList.From<Expression>([MakeString($"./{reference}"), importMetaUrl]), false);
            return new MemberExpression(relative, new Identifier("href"), false, false);
        }

        private static Statement WrapBody(NodeList<Statement> statements)
        {
            var body = new FunctionBody(NodeList.From(statements.Where(m => m is not EmptyStatement)), false);
            var callee = new ArrowFunctionExpression([], body, false, false);
            var call = new CallExpression(callee, [], false);
            return new NonSpecialExpressionStatement(call);
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

    }

    private static StringLiteral MakeString(string unencodedText)
    {
        var rawText = MakeJsonString(unencodedText);
        return new StringLiteral(unencodedText, rawText);
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
