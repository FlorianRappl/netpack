namespace NetPack.Graph.Bundles;

using System.Text;
using NetPack.Fragments;
using NetPack.Syntax;
using NetPack.Syntax.Minifier;
using NetPack.Syntax.Printer;
using Ast = NetPack.Syntax.Ast;
using GraphNode = NetPack.Graph.Node;

/// <summary>
/// Bundles a JavaScript module graph into a single module. The linkage at the
/// module boundary — how the bundle imports its shared siblings and externals,
/// exports the entry/registry, resolves dynamic imports and asset URLs, and is
/// wrapped — is delegated to a <see cref="JsModuleFormat"/> chosen from
/// <see cref="OutputOptions.Format"/> (ESM by default).
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
        SourceMap = null;

        var format = JsModuleFormats.For(options.Format);
        var transpiler = new JsxToJavaScriptTranspiler(this, options.IsReloading, format);
        var ast = transpiler.Transpile();

        if (options.IsOptimizing)
        {
            new Mangler().Process(ast);
        }

        var printerOptions = options.IsOptimizing ? PrinterOptions.Compact : PrinterOptions.Pretty;

        string code;

        if (!options.WithSourceMaps)
        {
            code = JsPrinter.Print(ast, printerOptions);
        }
        else
        {
            var mapFile = $"{GetFileName()}.map";
            var builder = new SourceMapBuilder(GetFileName(), _context.Root);
            var printed = JsPrinter.Print(ast, printerOptions, builder);
            SourceMap = Encoding.UTF8.GetBytes(builder.ToJson());
            code = $"{printed}\n//# sourceMappingURL={mapFile}\n";
        }

        VerifyOutput(code);
        return code;
    }

    /// <summary>
    /// Opt-in self-check (set <c>NETPACK_VERIFY=1</c>): re-parses the generated
    /// bundle and reports the first location where it is not valid JavaScript.
    /// This turns a silently broken bundle into an actionable message pointing at
    /// the exact construct the printer mis-emitted.
    /// </summary>
    private void VerifyOutput(string code)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NETPACK_VERIFY")))
        {
            return;
        }

        var options = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
        var reparsed = Parser.ParseModule(code, GetFileName(), options);

        if (reparsed.Diagnostics.Count == 0)
        {
            return;
        }

        var first = reparsed.Diagnostics[0];
        var start = System.Math.Max(0, first.Position - 50);
        var length = System.Math.Min(120, code.Length - start);
        var snippet = code.Substring(start, length).Replace("\n", " ");

        Console.Error.WriteLine(
            "[netpack] WARNING: generated bundle '{0}' is not valid JS ({1} issue(s)); first at {2}:{3}: {4}",
            GetFileName(), reparsed.Diagnostics.Count, first.Line, first.Column, first.Message);
        Console.Error.WriteLine("[netpack]   …{0}…", snippet);
    }

    private static Ast.StringLiteral MakeString(string text) => new(text, text);

    internal sealed class JsxToJavaScriptTranspiler(JsBundle bundle, bool reloading, JsModuleFormat format) : Ast.AstRewriter
    {
        private readonly JsBundle _bundle = bundle;
        private readonly bool _reloading = reloading;
        private readonly JsModuleFormat _format = format;
        private JsFragment? _current;
        private bool _currentUsesJsx;

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
                imports.Add(_format.ImportSharedBundle(new Ast.Identifier(name), reference.GetFileName()));
                sharedNames.Add(name);
            }

            foreach (var node in exportNodes)
            {
                if (!fragments.TryGetValue(node, out var fragment))
                {
                    continue;
                }

                _current = fragment;
                _currentUsesJsx = false;
                var ast = (Ast.SourceFile)Visit(fragment.Ast)!;
                ast = InjectAutoJsxImport(ast, fragment, _currentUsesJsx);
                var body = new List<Ast.Statement>();

                foreach (var statement in ast.Body)
                {
                    if (statement is Ast.EmptyStatement || statement is Ast.TypeOnlyDeclaration)
                    {
                        // erased
                    }
                    else if (statement is Ast.ImportDeclaration importDeclaration)
                    {
                        // Hoist real ESM imports (e.g. externals) to the bundle top;
                        // factories close over them. The format decides how the
                        // import is expressed.
                        imports.Add(_format.RewriteExternalImport(importDeclaration));
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

                // React Fast Refresh: instrument user component modules so their
                // component registrations and accept boundary are in place.
                if (_reloading && context.ReactRefresh && !node.FileName.Contains("node_modules"))
                {
                    body = ReactRefresh.Instrument(body, id);
                }

                var factory = MakeFactoryArrow(body);

                // Tag the factory body with the module's source so the printer can
                // attribute generated positions back to the original file.
                ((Ast.BlockStatement)factory.Body).Source = fragment.Ast;

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
                trailer.AddRange(_format.ExportRegistry(new Ast.Identifier(Registry)));
            }
            else if (fragments.TryGetValue(_bundle.Root, out var rootFragment))
            {
                var rootRequire = new Ast.CallExpression(
                    new Ast.Identifier(Require),
                    new List<Ast.Expression> { IdLiteral(GetId(rootFragment.Root)) }, false);
                trailer.AddRange(_format.ExportRoot(rootRequire, rootFragment.ExportNames));
            }

            var runtime = BuildRuntime(sharedNames);

            // Install the Fast Refresh runtime once, right after the require
            // runtime and before the entry module executes.
            if (_reloading && !_bundle.IsShared && context.ReactRefresh && context.ReactRefreshRuntime is { } refreshRuntime)
            {
                runtime.AddRange(ReactRefresh.BuildSetup(GetId(refreshRuntime)));
            }

            var all = new List<Ast.Statement>(imports.Count + 1 + runtime.Count + trailer.Count);
            all.AddRange(imports);
            all.Add(registry);
            all.AddRange(runtime);
            all.AddRange(trailer);
            var module = new Ast.SourceFile(_bundle.Root.FileName, all, System.Array.Empty<Diagnostic>());
            return _format.Wrap(module);
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
                return _format.DynamicImport(_format.AutoReference(reference));
            }

            return base.VisitImportExpression(node);
        }

        protected override Ast.Node VisitMemberExpression(Ast.MemberExpression node)
        {
            // Shim the standard `import.meta.hot` API onto the factory's `module`
            // parameter: `import.meta.hot` -> `module.hot`. In dev this resolves to
            // the HMR record; in production `module.hot` is undefined, so
            // `import.meta.hot?.accept(...)` is a harmless no-op.
            if (!node.Computed
                && node.Property is Ast.Identifier { Name: "hot" }
                && node.Object is Ast.MetaProperty { Meta: "import", Property: "meta" })
            {
                return new Ast.MemberExpression(new Ast.Identifier("module"), new Ast.Identifier("hot"), false, false);
            }

            return base.VisitMemberExpression(node);
        }

        protected override Ast.Node VisitImportDeclaration(Ast.ImportDeclaration node)
        {
            if (_current?.Replacements.TryGetValue(node, out var reference) ?? false)
            {
                if (_bundle._context.Assets.TryGetValue(reference, out var asset) && node.Specifiers.Count == 1 && node.Specifiers[0] is Ast.ImportDefaultSpecifier specifier)
                {
                    var local = specifier.Local;
                    var file = asset.GetFileName();
                    var declarator = new Ast.VariableDeclarator(local, _format.AutoReference(file));
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
            // A fresh node: for `import { render }` the parser shares one Identifier
            // for both the imported name and the local binding, so reusing it as the
            // destructuring key would let the mangler rename the property key too
            // (`{ render: render }` → `{ ga: ga }`), breaking the lookup.
            Ast.ImportSpecifier { Imported: Ast.Identifier id } => new Ast.Identifier(id.Name),
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
                    ExportKey(m.Exported),
                    new Ast.MemberExpression(require, m.Local, m.Local is Ast.StringLiteral, false))).ToList();
                return new Ast.ExpressionStatement(new Ast.SequenceExpression(specs));
            }

            if (node.Declaration is null)
            {
                var seq = node.Specifiers.Select(m => (Ast.Expression)SetExport(ExportKey(m.Exported), AsExpression(m.Local))).ToList();
                return new Ast.ExpressionStatement(new Ast.SequenceExpression(seq));
            }

            // Recurse into the exported declaration so nested lowering (JSX,
            // enums, dynamic imports, …) still applies; the Transpile loop then
            // splits it into the declaration plus an `exports.x = x` assignment.
            node.Declaration = (Ast.Statement)Visit(node.Declaration)!;
            return node;
        }

        protected override Ast.Node VisitExportDefaultDeclaration(Ast.ExportDefaultDeclaration node)
        {
            var payload = Visit(node.Declaration)!;
            return SetExport(new Ast.Identifier("default"), payload);
        }

        private static Ast.Expression AsExpression(Ast.Node node)
            => node as Ast.Expression ?? new Ast.Identifier((node as Ast.Identifier)?.Name ?? string.Empty);

        // A fresh key node for `exports.<name>`. The parser shares a single
        // Identifier for a specifier's local and exported names (`export { foo }`),
        // so reusing it as the property key would let the mangler rename the
        // exported name along with the local reference.
        private static Ast.Expression ExportKey(Ast.Node node) => node switch
        {
            Ast.Identifier id => new Ast.Identifier(id.Name),
            Ast.StringLiteral s => s,
            _ => AsExpression(node),
        };

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

        protected override Ast.Node VisitCallExpression(Ast.CallExpression node)
        {
            if (node.Callee is Ast.Identifier ident && node.Arguments.Count == 1 && ident.Name == "require" &&
                (_current?.Replacements.TryGetValue(node, out var reference) ?? false))
            {
                if (_bundle._context.Assets.TryGetValue(reference, out var asset))
                {
                    var file = asset.GetFileName();
                    return _format.AutoReference(file);
                }

                return new Ast.CallExpression(node.Callee, new List<Ast.Expression> { IdLiteral(GetId(reference)) }, false);
            }

            return base.VisitCallExpression(node);
        }

        // -- JSX lowering --------------------------------------------------

        protected override Ast.Node VisitJsxElement(Ast.JsxElement node)
        {
            _currentUsesJsx = true;
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

            var children = node.OpeningElement.SelfClosing
                ? new List<Ast.Expression>()
                : ConvertJsxChildren(node.Children);

            return ReactCreateElement(elementNameArg, attributesArg, children);
        }

        protected override Ast.Node VisitJsxFragment(Ast.JsxFragment node)
        {
            _currentUsesJsx = true;
            var factory = _current?.JsxFragmentFactory ?? "React.Fragment";
            var fragment = BuildQualifiedName(factory);
            var children = ConvertJsxChildren(node.Children);
            return ReactCreateElement(fragment, new Ast.NullLiteral(), children);
        }

        private static Ast.SourceFile InjectAutoJsxImport(Ast.SourceFile ast, JsFragment fragment, bool usesJsx)
        {
            var module = fragment.AutoJsxImportModule;
            var identifier = fragment.AutoJsxImportIdentifier;

            if (!usesJsx || string.IsNullOrEmpty(module) || string.IsNullOrEmpty(identifier))
            {
                return ast;
            }

            if (HasTopLevelBinding(ast, identifier))
            {
                return ast;
            }

            var import = new Ast.ImportDeclaration(
                new List<Ast.ImportSpecifierBase> { new Ast.ImportDefaultSpecifier(new Ast.Identifier(identifier)) },
                MakeString(module),
                false);

            var body = new List<Ast.Statement>(ast.Body.Count + 1) { import };
            body.AddRange(ast.Body);
            return new Ast.SourceFile(ast.FileName, body, ast.Diagnostics);
        }

        private static bool HasTopLevelBinding(Ast.SourceFile ast, string identifier)
        {
            foreach (var statement in ast.Body)
            {
                switch (statement)
                {
                    case Ast.ImportDeclaration import:
                        foreach (var specifier in import.Specifiers)
                        {
                            if (specifier.Local.Name == identifier)
                            {
                                return true;
                            }
                        }
                        break;
                    case Ast.VariableStatement variable:
                        if (variable.Declarations.Any(d => d.Id is Ast.Identifier id && id.Name == identifier))
                        {
                            return true;
                        }
                        break;
                    case Ast.FunctionDeclaration { Id: not null } function when function.Id.Name == identifier:
                        return true;
                    case Ast.ClassDeclaration { Id: not null } cls when cls.Id.Name == identifier:
                        return true;
                }
            }

            return false;
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

        // Returns each rendered child as a separate expression. They are passed as
        // individual trailing arguments to the factory (variadic children) rather
        // than as one array — an array child makes React expect `key` props and
        // warn, whereas static children passed positionally do not.
        private List<Ast.Expression> ConvertJsxChildren(IList<Ast.Node> children)
        {
            var elements = new List<Ast.Expression>(children.Count);
            foreach (var child in children)
            {
                var element = ConvertJsxChild(child);
                if (element is not null)
                {
                    elements.Add(element);
                }
            }
            return elements;
        }

        private Ast.Expression? ConvertJsxChild(Ast.Node child) => child switch
        {
            Ast.JsxText t => string.IsNullOrWhiteSpace(t.Value) ? null : MakeString(t.Value),
            Ast.JsxExpressionContainer c => c.Expression is null ? null : (Ast.Expression)Visit(c.Expression)!,
            Ast.JsxElement or Ast.JsxFragment => (Ast.Expression)Visit(child)!,
            _ => null,
        };

        private Ast.Expression ReactCreateElement(Ast.Expression name, Ast.Expression attributes, IReadOnlyList<Ast.Expression> children)
        {
            var factory = _current?.JsxFactory ?? "React.createElement";
            var callee = BuildQualifiedName(factory);
            var args = new List<Ast.Expression>(children.Count + 2) { name, attributes };
            args.AddRange(children);
            return new Ast.CallExpression(callee, args, false);
        }

        /// <summary>
        /// Builds a (possibly dotted) reference expression from a factory name
        /// such as <c>h</c>, <c>React.createElement</c> or <c>preact.h</c>.
        /// </summary>
        private static Ast.Expression BuildQualifiedName(string dotted)
        {
            if (string.IsNullOrEmpty(dotted))
            {
                dotted = "React.createElement";
            }

            var parts = dotted.Split('.');
            Ast.Expression expression = new Ast.Identifier(parts[0]);

            for (var i = 1; i < parts.Length; i++)
            {
                expression = new Ast.MemberExpression(expression, new Ast.Identifier(parts[i]), false, false);
            }

            return expression;
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
