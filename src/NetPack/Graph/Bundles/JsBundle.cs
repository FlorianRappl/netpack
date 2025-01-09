namespace NetPack.Graph.Bundles;

using System.Text;
using Acornima.Ast;
using Acornima.Jsx;
using Acornima.Jsx.Ast;
using NetPack.Fragments;

public sealed class JsBundle(BundlerContext context, Graph.Node root, BundleFlags flags) : Bundle(context, root, flags)
{
    public override async Task<Stream> CreateStream(OutputOptions options)
    {
        var content = Stringify(options);
        var raw = Encoding.UTF8.GetBytes(content);
        var src = new MemoryStream();
        await src.WriteAsync(raw);
        src.Position = 0;
        return src;
    }

    public string Stringify(OutputOptions options)
    {
        var transpiler = new JsxToJavaScriptTranspiler(this, options.IsOptimizing);
        var ast = transpiler.Transpile();
        return ast.ToJsx();
    }

    internal sealed class JsxToJavaScriptTranspiler(JsBundle bundle, bool optimize) : JsxAstRewriter
    {
        private static readonly NullLiteral _nullLiteral = new("null");
        private static readonly BooleanLiteral _trueLiteral = new(true, "true");
        private static readonly Identifier _modules = new("_modules");
        private static readonly Identifier _require = new("require");
        private static readonly Identifier _default = new("_default");
        private readonly JsBundle _bundle = bundle;
        private readonly bool _optimize = optimize;
        private JsFragment? _current;

        public Program Transpile()
        {
            var context = _bundle._context;
            var fragments = context.JsFragments;
            var imports = new List<Statement>();
            var exports = new List<Statement>();
            var body = new List<Statement>();
            var statements = new List<Statement>();
            var refNames = new List<string>();
            var exportNodes = _bundle.Items;
            var referenced = context.Bundles.Values.Where(m => m.IsShared && m != _bundle && _bundle.Items.Contains(m.Root));

            foreach (var reference in referenced)
            {
                var name = $"_{GetName(reference.Root)}";
                var specifier = NodeList.From<ImportDeclarationSpecifier>([new ImportDefaultSpecifier(new Identifier(name))]);
                imports.Add(new ImportDeclaration(specifier, MakeString($"./{reference.GetFileName()}"), []));
                refNames.Add(name);
            }

            body.Add(MakeModuleCache(refNames));
            body.Add(MakeModuleFunction());
            body.Add(MakeRequireFunction());

            foreach (var node in exportNodes)
            {
                if (fragments.TryGetValue(node, out var fragment))
                {
                    _current = fragment;

                    var ast = VisitAndConvert(fragment.Ast);

                    foreach (var statement in ast.Body)
                    {
                        if (statement is EmptyStatement)
                        {
                            // ignore
                        }
                        else if (statement is ImportDeclaration)
                        {
                            imports.Add(statement);
                        }
                        else if (statement is not ExportDeclaration)
                        {
                            statements.Add(statement);
                        }
                    }

                    body.Add(WrapBody(GetName(node), statements));
                    statements.Clear();
                }
            }

            if (_bundle.IsShared)
            {
                exports.Add(new ExportDefaultDeclaration(_modules));
            }
            else if (fragments.TryGetValue(_bundle.Root, out var fragment))
            {
                var name = GetName(fragment.Root);
                var call = MakeRequireCall(name);
                var exportNames = fragment.ExportNames;

                if (exportNames.Length == 0)
                {
                    exports.Add(new ExportDefaultDeclaration(call));
                }
                else
                {
                    var offset = 0;

                    body.Add(new VariableDeclaration(VariableDeclarationKind.Const, NodeList.From(new VariableDeclarator(new ObjectExpression(
                        NodeList.From<Node>(exportNames.Select(m => m == "default" ?
                          new ObjectProperty(PropertyKind.Property, new Identifier(m), _default, false, false, false) :
                          new ObjectProperty(PropertyKind.Property, new Identifier(m), new Identifier(m), false, true, false)))
                    ), call))));

                    if (exportNames.Contains("default"))
                    {
                        offset = 1;
                        exports.Add(new ExportDefaultDeclaration(_default));
                    }

                    if (exportNames.Length > offset)
                    {
                        var names = exportNames.Where(m => m != "default").Select(m => new ExportSpecifier(new Identifier(m)));
                        exports.Add(new ExportNamedDeclaration(null, NodeList.From(names), null, []));
                    }
                }
            }

            return new Module(NodeList.From(imports.Concat(body).Concat(exports)));
        }

        private static string GetName(Graph.Node node) => node.FileName.GetHashCode().ToString("x");

        private static VariableDeclaration MakeModuleCache(IEnumerable<string> refNames)
        {
            var initial = NodeList.From<Node>(refNames.Select(name => new SpreadElement(new Identifier(name))));
            var decl = new VariableDeclarator(_modules, new ObjectExpression(initial));
            return new VariableDeclaration(VariableDeclarationKind.Const, NodeList.From([decl]));
        }

        private static CallExpression MakeRequireCall(string name)
        {
            return new CallExpression(_require, NodeList.From<Expression>(MakeString(name)), false);
        }

        private static FunctionDeclaration MakeRequireFunction()
        {
            var name = new Identifier("name");
            var body = new FunctionBody(NodeList.From<Statement>([
                new ReturnStatement(new CallExpression(new MemberExpression(_modules, name, true, false), [], false)),
            ]), false);
            var parameters = NodeList.From<Node>([name]);
            return new FunctionDeclaration(_require, parameters, body, false, false);
        }

        private static FunctionDeclaration MakeModuleFunction()
        {
            var name = new Identifier("name");
            var evalBody = new NestedBlockStatement(NodeList.From<Statement>([
                new NonSpecialExpressionStatement(
                    new AssignmentExpression(Acornima.Operator.Assignment, new Identifier("done"), _trueLiteral)
                ),
                new NonSpecialExpressionStatement(
                    new AssignmentExpression(Acornima.Operator.Assignment, new Identifier("result"),
                    new CallExpression(new Identifier("run"), [], false))
                ),
            ]));
            var notDone = new NonUpdateUnaryExpression(Acornima.Operator.LogicalNot, new Identifier("done"));
            var innerBody = new FunctionBody(NodeList.From<Statement>([
                new IfStatement(notDone, evalBody, null),
                new ReturnStatement(new Identifier("result")),
            ]), false);
            var body = new FunctionBody(NodeList.From<Statement>([
                new VariableDeclaration(VariableDeclarationKind.Let, NodeList.From([
                    new VariableDeclarator(new Identifier("result"), null),
                    new VariableDeclarator(new Identifier("done"), null),
                ])),
                new NonSpecialExpressionStatement(
                    new AssignmentExpression(Acornima.Operator.Assignment, new MemberExpression(_modules, name, true, false),
                    new ArrowFunctionExpression([], innerBody, false, false))
                ),
            ]), false);
            var parameters = NodeList.From<Node>([name, new Identifier("run")]);
            return new FunctionDeclaration(new Identifier("addModule"), parameters, body, false, false);
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
                var reference = _bundle.GetReference(referenceNode);
                return new ImportExpression(MakeAutoReference(reference));
            }

            return base.VisitImportExpression(node);
        }

        protected override object? VisitImportDeclaration(ImportDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var reference) ?? false)
            {
                if (_bundle._context.Assets.TryGetValue(reference, out var asset) && node.Specifiers.Count == 1 && node.Specifiers[0] is ImportDefaultSpecifier specifier)
                {
                    var name = specifier.Local;
                    var file = asset.GetFileName();
                    var declarator = new VariableDeclarator(name, MakeAutoReference(file));
                    return new VariableDeclaration(VariableDeclarationKind.Const, NodeList.From([declarator]));
                }

                var properties = new List<ObjectProperty>();
                var decls = new List<VariableDeclarator>();
                Expression init = MakeRequireCall(GetName(reference));

                foreach (var spec in node.Specifiers)
                {
                    if (spec is ImportNamespaceSpecifier)
                    {
                        var id = new Identifier(spec.Local.Name);
                        decls.Add(new VariableDeclarator(id, init));
                        init = id;
                    }
                    else
                    {
                        properties.Add(new ObjectProperty(PropertyKind.Property, GetImportName(spec), spec.Local, false, false, false));
                    }
                }

                if (properties.Count > 0)
                {
                    var variables = new ObjectExpression(NodeList.From<Node>(properties));
                    decls.Add(new VariableDeclarator(variables, init));
                }

                if (decls.Count > 0)
                {
                    return new VariableDeclaration(VariableDeclarationKind.Const, NodeList.From(decls));
                }

                return new NonSpecialExpressionStatement(init);
            }

            return base.VisitImportDeclaration(node);
        }

        private static Expression GetImportName(ImportDeclarationSpecifier m)
        {
            if (m is ImportSpecifier spec)
            {
                return spec.Imported;
            }
            else if (m is ImportDefaultSpecifier)
            {
                return new Identifier("default");
            }

            return m.Local;
        }

        protected override object? VisitExportAllDeclaration(ExportAllDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var reference) ?? false)
            {
                var payload = MakeRequireCall(GetName(reference));
                var objectAssign = new MemberExpression(new Identifier("Object"), new Identifier("assign"), false, false);
                var call = new CallExpression(objectAssign, NodeList.From<Expression>([new Identifier("exports"), payload]), false);
                return new NonSpecialExpressionStatement(call);
            }

            return node;
        }

        protected override object? VisitExportNamedDeclaration(ExportNamedDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var reference) ?? false)
            {
                var require = MakeRequireCall(GetName(reference));
                var specs = node.Specifiers.Select(m => SetExport(m.Exported, new MemberExpression(require, m.Local, m.Local is StringLiteral, false)));
                var sequence = NodeList.From(specs);
                return new NonSpecialExpressionStatement(new SequenceExpression(sequence));
            }

            return new NonSpecialExpressionStatement(new SequenceExpression(NodeList.From(node.Specifiers.Select(m => SetExport(m.Exported, m.Local)))));
        }

        protected override object? VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
        {
            var payload = VisitAndConvert(node.Declaration);
            return SetExport(new Identifier("default"), payload);
        }

        private static Expression SetExport(Expression name, Expression expr)
        {
            var computed = name is StringLiteral;
            var left = new MemberExpression(new Identifier("exports"), name, computed, false);
            return new AssignmentExpression(Acornima.Operator.Assignment, left, expr);
        }

        private static StatementOrExpression SetExport(Expression name, StatementOrExpression payload)
        {
            var computed = name is StringLiteral;

            if (payload is Expression expr)
            {
                var assignment = SetExport(name, expr);
                return new NonSpecialExpressionStatement(assignment);
            }
            else if (payload is FunctionDeclaration func)
            {
                var left = new MemberExpression(new Identifier("exports"), name, computed, false);
                var fuxpr = new FunctionExpression(func.Id, func.Params, func.Body, func.Generator, func.Async);
                var assignment = new AssignmentExpression(Acornima.Operator.Assignment, left, fuxpr);
                return new NonSpecialExpressionStatement(assignment);
            }

            return payload;
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
                var properties = new List<Node>(capacity: node.OpeningElement.Attributes.Count);

                foreach (var attribute in node.OpeningElement.Attributes)
                {
                    properties.Add((Node)Visit(attribute)!);
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

        protected override object? VisitCallExpression(CallExpression node)
        {
            if (node.Callee is Identifier ident && node.Arguments.Count == 1 && ident.Name == "require" && (_current?.Replacements.TryGetValue(node, out var reference) ?? false))
            {
                if (_bundle._context.Assets.TryGetValue(reference, out var asset))
                {
                    var file = asset.GetFileName();
                    return MakeAutoReference(file);
                }

                var name = GetName(reference);
                return new CallExpression(node.Callee, NodeList.From<Expression>([MakeString(name)]), false);
            }

            return base.VisitCallExpression(node);
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
            var elementNameArg = new MemberExpression(react, fragment, false, false);
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

        private static NonSpecialExpressionStatement WrapBody(string name, IEnumerable<Statement> statements)
        {
            var module = new Identifier("module");
            var exports = new Identifier("exports");
            var initial = new VariableDeclaration(VariableDeclarationKind.Const, NodeList.From([
                new VariableDeclarator(exports, new ObjectExpression([])),
                new VariableDeclarator(module, new ObjectExpression(NodeList.From<Node>([
                    new ObjectProperty(PropertyKind.Property, exports, exports, false, true, false)
                ]))),
            ]));
            var final = new ReturnStatement(new MemberExpression(module, exports, false, false));
            var content = statements.Prepend(initial).Append(final);
            var body = new FunctionBody(NodeList.From(content), false);
            var callee = new ArrowFunctionExpression([], body, false, false);
            var call = new CallExpression(new Identifier("addModule"), NodeList.From<Expression>([MakeString(name), callee]), false);
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
