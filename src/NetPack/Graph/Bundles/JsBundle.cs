namespace NetPack.Graph.Bundles;

using System.Text;
using NetPack.Fragments;
using NetPack.Syntax;
using NetPack.Syntax.Minifier;
using NetPack.Syntax.Printer;
using Ast = NetPack.Syntax.Ast;
using GraphNode = NetPack.Graph.Node;

/// <summary>
/// Bundles a JavaScript module graph into a single ES module.
///
/// Every module is lowered into a factory <c>(module, exports, require) =&gt; { … }</c>
/// stored in a registry keyed by a compact integer id. A tiny <c>require</c>
/// runtime instantiates modules lazily and — crucially — registers a module's
/// <c>exports</c> object in the cache <i>before</i> running its factory, so
/// circular dependencies observe the in-progress exports (matching CommonJS /
/// webpack semantics) instead of <c>undefined</c>.
///
/// When <see cref="OutputOptions.IsReloading"/> is set (dev server) the runtime
/// additionally exposes a <c>module.hot</c> API and a <c>globalThis.__netpack</c>
/// entry point so individual module factories can be swapped without a full
/// reload. In optimizing builds the whole thing is run through the
/// <see cref="Mangler"/> and printed compactly.
/// </summary>
public sealed class JsBundle(BundlerContext context, GraphNode root, BundleFlags flags) : Bundle(context, root, flags)
{
    // Runtime symbols. These live at module scope and are intentionally short;
    // the mangler leaves module-scope names alone, so we keep them tiny here.
    private const string Registry = JsRuntime.Registry;
    private const string Require = JsRuntime.Require;

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
        var transpiler = new JsxToJavaScriptTranspiler(this, options.IsReloading);
        var ast = transpiler.Transpile();

        if (options.IsOptimizing)
        {
            new Mangler().Process(ast);
        }

        var printerOptions = options.IsOptimizing ? PrinterOptions.Compact : PrinterOptions.Pretty;
        return JsPrinter.Print(ast, printerOptions);
    }

    private static Ast.StringLiteral MakeString(string text) => new(text, text);

    internal sealed class JsxToJavaScriptTranspiler(JsBundle bundle, bool reloading) : Ast.AstRewriter
    {
        private static readonly Ast.Identifier __default = new("_default");

        private readonly JsBundle _bundle = bundle;
        private readonly bool _reloading = reloading;
        private JsFragment? _current;

        public Ast.SourceFile Transpile()
        {
            var context = _bundle._context;
            var fragments = context.JsFragments;
            var imports = new List<Ast.Statement>();
            var registrations = new List<Ast.Node>();
            var trailer = new List<Ast.Statement>();
            var exportNodes = _bundle.Items;
            var referenced = context.Bundles.Values
                .Where(m => m.IsShared && m != _bundle && _bundle.Items.Contains(m.Root))
                .ToList();

            var sharedNames = new List<string>();
            var index = 0;
            foreach (var reference in referenced)
            {
                var name = $"__s{index++}";
                imports.Add(new Ast.ImportDeclaration(
                    new List<Ast.ImportSpecifierBase> { new Ast.ImportDefaultSpecifier(new Ast.Identifier(name)) },
                    MakeString($"./{reference.GetFileName()}"), false));
                sharedNames.Add(name);
            }

            foreach (var node in exportNodes)
            {
                if (!fragments.TryGetValue(node, out var fragment))
                {
                    continue;
                }

                _current = fragment;
                var ast = (Ast.SourceFile)Visit(fragment.Ast)!;
                var body = new List<Ast.Statement>();

                foreach (var statement in ast.Body)
                {
                    if (statement is Ast.EmptyStatement || statement is Ast.TypeOnlyDeclaration)
                    {
                        // erased
                    }
                    else if (statement is Ast.ImportDeclaration)
                    {
                        // Hoist real ESM imports (e.g. externals) to the bundle top;
                        // factories close over them.
                        imports.Add(statement);
                    }
                    else if (statement is Ast.ExportNamedDeclaration decl && decl.Declaration is not null)
                    {
                        var identifier = GetIdentifierOf(decl.Declaration);
                        if (identifier is not null)
                        {
                            body.Add(decl.Declaration);
                            body.Add(new Ast.ExpressionStatement(SetExport(new Ast.Identifier(identifier.Name), new Ast.Identifier(identifier.Name))));
                        }
                    }
                    else if (!IsExportDeclaration(statement))
                    {
                        body.Add(statement);
                    }
                }

                var id = GetId(node);
                var factory = MakeFactoryArrow(body);

                if (_reloading)
                {
                    // Capture the pre-mangle factory source so the dev server can
                    // diff and hot-swap this module later.
                    _bundle._context.ModuleFactories[id] = JsPrinter.Print(factory, PrinterOptions.Pretty);
                }

                registrations.Add(new Ast.Property(IdLiteral(id), factory, Ast.PropertyKind.Init, computed: false, shorthand: false, method: false));
            }

            // const __m = { 0: (module, exports, require) => { … }, 1: … };
            var registry = new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator>
            {
                new Ast.VariableDeclarator(new Ast.Identifier(Registry), new Ast.ObjectExpression(registrations)),
            });

            if (_bundle.IsShared)
            {
                trailer.Add(new Ast.ExportDefaultDeclaration(new Ast.Identifier(Registry)));
            }
            else if (fragments.TryGetValue(_bundle.Root, out var rootFragment))
            {
                BuildRootExports(rootFragment, trailer);
            }

            var runtime = BuildRuntime(sharedNames);

            var all = new List<Ast.Statement>(imports.Count + 1 + runtime.Count + trailer.Count);
            all.AddRange(imports);
            all.Add(registry);
            all.AddRange(runtime);
            all.AddRange(trailer);
            return new Ast.SourceFile(_bundle.Root.FileName, all, System.Array.Empty<Diagnostic>());
        }

        private int GetId(GraphNode node) => _bundle._context.GetModuleId(node);

        private static Ast.NumericLiteral IdLiteral(int id) => new(id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private static bool IsExportDeclaration(Ast.Statement statement)
            => statement is Ast.ExportNamedDeclaration or Ast.ExportDefaultDeclaration or Ast.ExportAllDeclaration;

        // -- registry + runtime --------------------------------------------

        private static Ast.ArrowFunctionExpression MakeFactoryArrow(List<Ast.Statement> body)
        {
            var parameters = new List<Ast.Parameter>
            {
                Param(new Ast.Identifier("module")),
                Param(new Ast.Identifier("exports")),
                Param(new Ast.Identifier("require")),
            };
            return new Ast.ArrowFunctionExpression(parameters, new Ast.BlockStatement(body), false);
        }

        /// <summary>Builds the runtime prelude by writing it as ordinary JS and
        /// parsing it, so the printer and mangler treat it like any other code
        /// (its locals get shortened; the module-scope <c>__r</c>/<c>__m</c> stay).</summary>
        private List<Ast.Statement> BuildRuntime(IReadOnlyList<string> sharedNames)
        {
            var source = JsRuntime.Build(_bundle.IsShared, sharedNames, _reloading);
            if (source.Length == 0)
            {
                return new List<Ast.Statement>();
            }
            var options = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
            var module = Parser.ParseModule(source, "netpack:runtime", options);
            return new List<Ast.Statement>(module.Body);
        }

        private void BuildRootExports(JsFragment fragment, List<Ast.Statement> trailer)
        {
            var id = GetId(fragment.Root);
            var call = new Ast.CallExpression(new Ast.Identifier(Require), new List<Ast.Expression> { IdLiteral(id) }, false);
            var exportNames = fragment.ExportNames;

            if (exportNames.Length == 0)
            {
                trailer.Add(new Ast.ExportDefaultDeclaration(call));
                return;
            }

            var offset = 0;
            var properties = new List<Ast.Node>();
            foreach (var m in exportNames)
            {
                properties.Add(m == "default"
                    ? new Ast.Property(new Ast.Identifier(m), __default, Ast.PropertyKind.Init, false, false, false)
                    : new Ast.Property(new Ast.Identifier(m), new Ast.Identifier(m), Ast.PropertyKind.Init, false, true, false));
            }

            trailer.Add(new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator>
            {
                new Ast.VariableDeclarator(new Ast.ObjectExpression(properties), call),
            }));

            if (exportNames.Contains("default"))
            {
                offset = 1;
                trailer.Add(new Ast.ExportDefaultDeclaration(__default));
            }

            if (exportNames.Length > offset)
            {
                var names = exportNames
                    .Where(m => m != "default")
                    .Select(m => new Ast.ExportSpecifier(new Ast.Identifier(m), new Ast.Identifier(m), false))
                    .ToList();
                trailer.Add(new Ast.ExportNamedDeclaration(null, names, null, false));
            }
        }

        private static Ast.Parameter Param(Ast.Identifier id) => new(id, null, false);

        private static Ast.Identifier? GetIdentifierOf(Ast.Statement declaration) => declaration switch
        {
            Ast.VariableStatement variable => variable.Declarations.Count > 0 ? variable.Declarations[0].Id as Ast.Identifier : null,
            Ast.ClassDeclaration cls => cls.Id,
            Ast.FunctionDeclaration func => func.Id,
            _ => null,
        };

        // -- constant folding ----------------------------------------------

        protected override Ast.Node VisitIfStatement(Ast.IfStatement node)
        {
            if (node.Test is Ast.BinaryExpression be && be.Left is Ast.StringLiteral left && be.Right is Ast.StringLiteral right)
            {
                if ((be.Operator == TokenKind.EqualsEqualsEquals && left.Value == right.Value) ||
                    (be.Operator == TokenKind.ExclamationEqualsEquals && left.Value != right.Value))
                {
                    return Visit(node.Consequent)!;
                }
                if ((be.Operator == TokenKind.EqualsEqualsEquals && left.Value != right.Value) ||
                    (be.Operator == TokenKind.ExclamationEqualsEquals && left.Value == right.Value))
                {
                    return node.Alternate is not null ? Visit(node.Alternate)! : new Ast.EmptyStatement();
                }
            }

            return base.VisitIfStatement(node);
        }

        // -- import / require rewriting ------------------------------------

        protected override Ast.Node VisitImportExpression(Ast.ImportExpression node)
        {
            if (_current?.Replacements.TryGetValue(node, out var referenceNode) ?? false)
            {
                var reference = _bundle.GetReference(referenceNode);
                return new Ast.ImportExpression(MakeAutoReference(reference));
            }

            return base.VisitImportExpression(node);
        }

        protected override Ast.Node VisitImportDeclaration(Ast.ImportDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var reference) ?? false)
            {
                if (_bundle._context.Assets.TryGetValue(reference, out var asset) && node.Specifiers.Count == 1 && node.Specifiers[0] is Ast.ImportDefaultSpecifier specifier)
                {
                    var local = specifier.Local;
                    var file = asset.GetFileName();
                    var declarator = new Ast.VariableDeclarator(local, MakeAutoReference(file));
                    return new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator> { declarator });
                }

                var properties = new List<Ast.Node>();
                var decls = new List<Ast.VariableDeclarator>();
                Ast.Expression init = RequireCall(GetId(reference));

                foreach (var spec in node.Specifiers)
                {
                    if (spec is Ast.ImportNamespaceSpecifier)
                    {
                        var id = new Ast.Identifier(spec.Local.Name);
                        decls.Add(new Ast.VariableDeclarator(id, init));
                        init = id;
                    }
                    else
                    {
                        properties.Add(new Ast.Property(GetImportName(spec), spec.Local, Ast.PropertyKind.Init, false, false, false));
                    }
                }

                if (properties.Count > 0)
                {
                    var variables = new Ast.ObjectExpression(properties);
                    decls.Add(new Ast.VariableDeclarator(variables, init));
                }

                if (decls.Count > 0)
                {
                    return new Ast.VariableStatement(Ast.VariableKind.Const, decls);
                }

                return new Ast.ExpressionStatement(init);
            }

            return base.VisitImportDeclaration(node);
        }

        private static Ast.Node GetImportName(Ast.ImportSpecifierBase m) => m switch
        {
            Ast.ImportSpecifier spec => spec.Imported,
            Ast.ImportDefaultSpecifier => new Ast.Identifier("default"),
            _ => m.Local,
        };

        protected override Ast.Node VisitExportAllDeclaration(Ast.ExportAllDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var reference) ?? false)
            {
                var payload = RequireCall(GetId(reference));
                var objectAssign = new Ast.MemberExpression(new Ast.Identifier("Object"), new Ast.Identifier("assign"), false, false);
                var call = new Ast.CallExpression(objectAssign, new List<Ast.Expression> { new Ast.Identifier("exports"), payload }, false);
                return new Ast.ExpressionStatement(call);
            }

            return node;
        }

        protected override Ast.Node VisitExportNamedDeclaration(Ast.ExportNamedDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var reference) ?? false)
            {
                var require = RequireCall(GetId(reference));
                var specs = node.Specifiers.Select(m => (Ast.Expression)SetExport(
                    AsExpression(m.Exported),
                    new Ast.MemberExpression(require, m.Local, m.Local is Ast.StringLiteral, false))).ToList();
                return new Ast.ExpressionStatement(new Ast.SequenceExpression(specs));
            }

            if (node.Declaration is null)
            {
                var seq = node.Specifiers.Select(m => (Ast.Expression)SetExport(AsExpression(m.Exported), AsExpression(m.Local))).ToList();
                return new Ast.ExpressionStatement(new Ast.SequenceExpression(seq));
            }

            return node;
        }

        protected override Ast.Node VisitExportDefaultDeclaration(Ast.ExportDefaultDeclaration node)
        {
            var payload = Visit(node.Declaration)!;
            return SetExport(new Ast.Identifier("default"), payload);
        }

        private static Ast.Expression AsExpression(Ast.Node node)
            => node as Ast.Expression ?? new Ast.Identifier((node as Ast.Identifier)?.Name ?? string.Empty);

        private static Ast.AssignmentExpression SetExport(Ast.Expression name, Ast.Expression expr)
        {
            var computed = name is Ast.StringLiteral;
            var left = new Ast.MemberExpression(new Ast.Identifier("exports"), name, computed, false);
            return new Ast.AssignmentExpression(TokenKind.Equals, left, expr);
        }

        private static Ast.Statement SetExport(Ast.Expression name, Ast.Node payload)
        {
            var computed = name is Ast.StringLiteral;

            switch (payload)
            {
                case Ast.Expression expr:
                    return new Ast.ExpressionStatement(SetExport(name, expr));
                case Ast.FunctionDeclaration func:
                {
                    var left = new Ast.MemberExpression(new Ast.Identifier("exports"), name, computed, false);
                    var fuxpr = new Ast.FunctionExpression(func.Id, func.Parameters, func.Body, func.Async, func.Generator);
                    return new Ast.ExpressionStatement(new Ast.AssignmentExpression(TokenKind.Equals, left, fuxpr));
                }
                case Ast.ClassDeclaration cls:
                {
                    var left = new Ast.MemberExpression(new Ast.Identifier("exports"), name, computed, false);
                    var cexpr = new Ast.ClassExpression(cls.Id, cls.SuperClass, cls.Body);
                    return new Ast.ExpressionStatement(new Ast.AssignmentExpression(TokenKind.Equals, left, cexpr));
                }
                default:
                    return payload as Ast.Statement ?? new Ast.EmptyStatement();
            }
        }

        private static Ast.CallExpression RequireCall(int id)
            => new(new Ast.Identifier("require"), new List<Ast.Expression> { IdLiteral(id) }, false);

        private static Ast.MemberExpression MakeAutoReference(string reference)
        {
            var import = new Ast.Identifier("import");
            var importMeta = new Ast.MemberExpression(import, new Ast.Identifier("meta"), false, false);
            var importMetaUrl = new Ast.MemberExpression(importMeta, new Ast.Identifier("url"), false, false);
            var urlParse = new Ast.MemberExpression(new Ast.Identifier("URL"), new Ast.Identifier("parse"), false, false);
            var relative = new Ast.CallExpression(urlParse, new List<Ast.Expression> { MakeString($"./{reference}"), importMetaUrl }, false);
            return new Ast.MemberExpression(relative, new Ast.Identifier("href"), false, false);
        }

        protected override Ast.Node VisitCallExpression(Ast.CallExpression node)
        {
            if (node.Callee is Ast.Identifier ident && node.Arguments.Count == 1 && ident.Name == "require" &&
                (_current?.Replacements.TryGetValue(node, out var reference) ?? false))
            {
                if (_bundle._context.Assets.TryGetValue(reference, out var asset))
                {
                    var file = asset.GetFileName();
                    return MakeAutoReference(file);
                }

                return new Ast.CallExpression(node.Callee, new List<Ast.Expression> { IdLiteral(GetId(reference)) }, false);
            }

            return base.VisitCallExpression(node);
        }

        // -- JSX lowering --------------------------------------------------

        protected override Ast.Node VisitJsxElement(Ast.JsxElement node)
        {
            var elementNameArg = GetName(node.OpeningElement.Name);

            Ast.Expression attributesArg;
            if (node.OpeningElement.Attributes.Count > 0)
            {
                var properties = new List<Ast.Node>(node.OpeningElement.Attributes.Count);
                foreach (var attribute in node.OpeningElement.Attributes)
                {
                    if (attribute is Ast.JsxSpreadAttribute spread)
                    {
                        properties.Add(new Ast.SpreadElement((Ast.Expression)Visit(spread.Argument)!));
                    }
                    else if (attribute is Ast.JsxAttribute attr)
                    {
                        properties.Add(ConvertAttribute(attr));
                    }
                }
                attributesArg = new Ast.ObjectExpression(properties);
            }
            else
            {
                attributesArg = new Ast.NullLiteral();
            }

            var childrenArg = node.OpeningElement.SelfClosing
                ? (Ast.Expression)new Ast.NullLiteral()
                : ConvertJsxChildren(node.Children);

            return ReactCreateElement(elementNameArg, attributesArg, childrenArg);
        }

        protected override Ast.Node VisitJsxFragment(Ast.JsxFragment node)
        {
            var fragment = new Ast.MemberExpression(new Ast.Identifier("React"), new Ast.Identifier("Fragment"), false, false);
            var childrenArg = ConvertJsxChildren(node.Children);
            return ReactCreateElement(fragment, new Ast.NullLiteral(), childrenArg);
        }

        private Ast.Node ConvertAttribute(Ast.JsxAttribute node)
        {
            var attributeName = MakeString(GetQualifiedName(node.Name));
            Ast.Expression attributeValue = node.Value is null ? new Ast.BooleanLiteral(true) : ConvertJsxAttributeValue(node.Value);
            return new Ast.Property(attributeName, attributeValue, Ast.PropertyKind.Init, false, false, false);
        }

        private Ast.Expression ConvertJsxAttributeValue(Ast.Node value) => value switch
        {
            Ast.JsxExpressionContainer c => c.Expression is null ? new Ast.NullLiteral() : (Ast.Expression)Visit(c.Expression)!,
            Ast.StringLiteral s => s,
            _ => (Ast.Expression)Visit(value)!,
        };

        private Ast.Expression ConvertJsxChildren(IList<Ast.Node> children)
        {
            if (children.Count == 0)
            {
                return new Ast.NullLiteral();
            }

            var elements = new List<Ast.Expression?>(children.Count);
            foreach (var child in children)
            {
                var element = ConvertJsxChild(child);
                if (element is not null)
                {
                    elements.Add(element);
                }
            }
            return new Ast.ArrayExpression(elements);
        }

        private Ast.Expression? ConvertJsxChild(Ast.Node child) => child switch
        {
            Ast.JsxText t => string.IsNullOrWhiteSpace(t.Value) ? null : MakeString(t.Value),
            Ast.JsxExpressionContainer c => c.Expression is null ? null : (Ast.Expression)Visit(c.Expression)!,
            Ast.JsxElement or Ast.JsxFragment => (Ast.Expression)Visit(child)!,
            _ => null,
        };

        private static Ast.Expression ReactCreateElement(Ast.Expression name, Ast.Expression attributes, Ast.Expression children)
        {
            var createElement = new Ast.MemberExpression(new Ast.Identifier("React"), new Ast.Identifier("createElement"), false, false);
            return new Ast.CallExpression(createElement, new List<Ast.Expression> { name, attributes, children }, false);
        }

        private static Ast.Expression GetName(Ast.JsxName name)
        {
            if (name is Ast.JsxMemberExpression member)
            {
                return GetReference(member);
            }
            if (name is Ast.JsxIdentifier ident && !string.IsNullOrEmpty(ident.Name) && char.IsUpper(ident.Name[0]))
            {
                return GetReference(name);
            }
            return MakeString(GetQualifiedName(name));
        }

        private static Ast.Expression GetReference(Ast.JsxName name)
        {
            if (name is Ast.JsxMemberExpression member)
            {
                var obj = GetReference(member.Object);
                return new Ast.MemberExpression(obj, new Ast.Identifier(member.Property.Name), false, false);
            }
            if (name is Ast.JsxIdentifier ident)
            {
                return new Ast.Identifier(ident.Name);
            }
            return new Ast.Identifier(GetQualifiedName(name));
        }

        private static string GetQualifiedName(Ast.JsxName name) => name switch
        {
            Ast.JsxIdentifier id => id.Name,
            Ast.JsxMemberExpression m => $"{GetQualifiedName(m.Object)}.{m.Property.Name}",
            Ast.JsxNamespacedName ns => $"{ns.Namespace.Name}:{ns.Name.Name}",
            _ => string.Empty,
        };
    }
}
