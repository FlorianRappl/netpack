namespace NetPack.Graph.Bundles;

using System.Text;
using NetPack.Fragments;
using NetPack.Syntax;
using NetPack.Syntax.Minifier;
using NetPack.Syntax.Printer;
using Ast = NetPack.Syntax.Ast;
using GraphNode = NetPack.Graph.Node;

/// <summary>
/// Bundles a JavaScript module graph into a single ES module. The heavy lifting
/// is a tree rewrite (<see cref="JsxToJavaScriptTranspiler"/>) that lowers every
/// module into a registration in a tiny runtime module cache, rewrites imports
/// and <c>require()</c> calls to that cache, and lowers JSX to
/// <c>React.createElement</c> calls. The result is rendered with
/// <see cref="JsPrinter"/> — NetPack's own code generator (no Acornima).
/// </summary>
public sealed class JsBundle(BundlerContext context, GraphNode root, BundleFlags flags) : Bundle(context, root, flags)
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

        if (options.IsOptimizing)
        {
            // Shorten local identifiers before printing compactly.
            new Mangler().Process(ast);
        }

        var printerOptions = options.IsOptimizing ? PrinterOptions.Compact : PrinterOptions.Pretty;
        return JsPrinter.Print(ast, printerOptions);
    }

    private static Ast.StringLiteral MakeString(string text) => new(text, text);

    internal sealed class JsxToJavaScriptTranspiler(JsBundle bundle, bool optimize) : Ast.AstRewriter
    {
        private static readonly Ast.Identifier _modules = new("_modules");
        private static readonly Ast.Identifier _exports = new("exports");
        private static readonly Ast.Identifier _module = new("module");
        private static readonly Ast.Identifier _require = new("require");
        private static readonly Ast.Identifier __default = new("_default");
        private static readonly Ast.Identifier ___adjModule = new("__adjModule");

        private readonly JsBundle _bundle = bundle;
        private readonly bool _optimize = optimize;
        private JsFragment? _current;

        public Ast.SourceFile Transpile()
        {
            var context = _bundle._context;
            var fragments = context.JsFragments;
            var imports = new List<Ast.Statement>();
            var exports = new List<Ast.Statement>();
            var body = new List<Ast.Statement>();
            var statements = new List<Ast.Statement>();
            var refNames = new List<string>();
            var exportNodes = _bundle.Items;
            var referenced = context.Bundles.Values.Where(m => m.IsShared && m != _bundle && _bundle.Items.Contains(m.Root));

            foreach (var reference in referenced)
            {
                var name = $"_{GetName(reference.Root)}";
                var specifiers = new List<Ast.ImportSpecifierBase> { new Ast.ImportDefaultSpecifier(new Ast.Identifier(name)) };
                imports.Add(new Ast.ImportDeclaration(specifiers, MakeString($"./{reference.GetFileName()}"), false));
                refNames.Add(name);
            }

            body.Add(MakeModuleCache(refNames));
            body.Add(MakeModuleFunction());
            body.Add(MakeRequireFunction());
            body.Add(MakeAdjModuleFunction());

            foreach (var node in exportNodes)
            {
                if (fragments.TryGetValue(node, out var fragment))
                {
                    _current = fragment;

                    var ast = (Ast.SourceFile)Visit(fragment.Ast)!;

                    foreach (var statement in ast.Body)
                    {
                        if (statement is Ast.EmptyStatement || statement is Ast.TypeOnlyDeclaration)
                        {
                            // ignore
                        }
                        else if (statement is Ast.ImportDeclaration)
                        {
                            imports.Add(statement);
                        }
                        else if (statement is Ast.ExportNamedDeclaration decl && decl.Declaration is not null)
                        {
                            var identifier = GetIdentifierOf(decl.Declaration);

                            if (identifier is not null)
                            {
                                statements.Add(decl.Declaration);
                                statements.Add(new Ast.ExpressionStatement(SetExport(new Ast.Identifier(identifier.Name), new Ast.Identifier(identifier.Name))));
                            }
                        }
                        else if (!IsExportDeclaration(statement))
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
                exports.Add(new Ast.ExportDefaultDeclaration(_modules));
            }
            else if (fragments.TryGetValue(_bundle.Root, out var fragment))
            {
                var name = GetName(fragment.Root);
                var call = MakeRequireCall(name);
                var exportNames = fragment.ExportNames;

                if (exportNames.Length == 0)
                {
                    exports.Add(new Ast.ExportDefaultDeclaration(call));
                }
                else
                {
                    var offset = 0;
                    var properties = new List<Ast.Node>();

                    foreach (var m in exportNames)
                    {
                        properties.Add(m == "default"
                            ? new Ast.Property(new Ast.Identifier(m), __default, Ast.PropertyKind.Init, false, false, false)
                            : new Ast.Property(new Ast.Identifier(m), new Ast.Identifier(m), Ast.PropertyKind.Init, false, true, false));
                    }

                    body.Add(new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator>
                    {
                        new Ast.VariableDeclarator(new Ast.ObjectExpression(properties), call),
                    }));

                    if (exportNames.Contains("default"))
                    {
                        offset = 1;
                        exports.Add(new Ast.ExportDefaultDeclaration(__default));
                    }

                    if (exportNames.Length > offset)
                    {
                        var names = exportNames
                            .Where(m => m != "default")
                            .Select(m => new Ast.ExportSpecifier(new Ast.Identifier(m), new Ast.Identifier(m), false))
                            .ToList();
                        exports.Add(new Ast.ExportNamedDeclaration(null, names, null, false));
                    }
                }
            }

            var all = new List<Ast.Statement>(imports.Count + body.Count + exports.Count);
            all.AddRange(imports);
            all.AddRange(body);
            all.AddRange(exports);
            return new Ast.SourceFile(_bundle.Root.FileName, all, System.Array.Empty<Diagnostic>());
        }

        private static string GetName(GraphNode node) => node.FileName.GetHashCode().ToString("x");

        private static bool IsExportDeclaration(Ast.Statement statement)
            => statement is Ast.ExportNamedDeclaration or Ast.ExportDefaultDeclaration or Ast.ExportAllDeclaration;

        // -- runtime module-system builders --------------------------------

        private static Ast.VariableStatement MakeModuleCache(IEnumerable<string> refNames)
        {
            var initial = refNames.Select(name => (Ast.Node)new Ast.SpreadElement(new Ast.Identifier(name))).ToList();
            var decl = new Ast.VariableDeclarator(_modules, new Ast.ObjectExpression(initial));
            return new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator> { decl });
        }

        private static Ast.FunctionDeclaration MakeAdjModuleFunction()
        {
            var moduleExports = new Ast.MemberExpression(_module, _exports, false, false);
            var defaultExport = new Ast.MemberExpression(moduleExports, new Ast.Identifier("default"), false, false);
            var defaultTest = new Ast.LogicalExpression(TokenKind.AmpersandAmpersand,
                new Ast.LogicalExpression(TokenKind.BarBar,
                    new Ast.BinaryExpression(TokenKind.EqualsEqualsEquals, new Ast.UnaryExpression(TokenKind.TypeOfKeyword, moduleExports), MakeString("object")),
                    new Ast.BinaryExpression(TokenKind.EqualsEqualsEquals, new Ast.UnaryExpression(TokenKind.TypeOfKeyword, moduleExports), MakeString("function"))
                ),
                new Ast.BinaryExpression(TokenKind.EqualsEqualsEquals, defaultExport, new Ast.Identifier("undefined"))
            );
            var defaultAliasDefinition = new Ast.BlockStatement(new List<Ast.Statement>
            {
                new Ast.ExpressionStatement(new Ast.AssignmentExpression(TokenKind.Equals, defaultExport, moduleExports)),
            });
            var body = new Ast.BlockStatement(new List<Ast.Statement>
            {
                new Ast.IfStatement(defaultTest, defaultAliasDefinition, null),
                new Ast.ReturnStatement(moduleExports),
            });
            return new Ast.FunctionDeclaration(___adjModule, new List<Ast.Parameter> { Param(_module) }, body, false, false);
        }

        private static Ast.CallExpression MakeRequireCall(string name)
            => new(_require, new List<Ast.Expression> { MakeString(name) }, false);

        private static Ast.FunctionDeclaration MakeRequireFunction()
        {
            var name = new Ast.Identifier("name");
            var body = new Ast.BlockStatement(new List<Ast.Statement>
            {
                new Ast.ReturnStatement(new Ast.CallExpression(new Ast.MemberExpression(_modules, name, true, false), new List<Ast.Expression>(), false)),
            });
            return new Ast.FunctionDeclaration(_require, new List<Ast.Parameter> { Param(name) }, body, false, false);
        }

        private static Ast.FunctionDeclaration MakeModuleFunction()
        {
            var name = new Ast.Identifier("name");
            var evalBody = new Ast.BlockStatement(new List<Ast.Statement>
            {
                new Ast.ExpressionStatement(new Ast.AssignmentExpression(TokenKind.Equals, new Ast.Identifier("done"), new Ast.BooleanLiteral(true))),
                new Ast.ExpressionStatement(new Ast.AssignmentExpression(TokenKind.Equals, new Ast.Identifier("result"),
                    new Ast.CallExpression(new Ast.Identifier("run"), new List<Ast.Expression>(), false))),
            });
            var notDone = new Ast.UnaryExpression(TokenKind.Exclamation, new Ast.Identifier("done"));
            var innerBody = new Ast.BlockStatement(new List<Ast.Statement>
            {
                new Ast.IfStatement(notDone, evalBody, null),
                new Ast.ReturnStatement(new Ast.Identifier("result")),
            });
            var arrow = new Ast.ArrowFunctionExpression(new List<Ast.Parameter>(), innerBody, false);
            var body = new Ast.BlockStatement(new List<Ast.Statement>
            {
                new Ast.VariableStatement(Ast.VariableKind.Let, new List<Ast.VariableDeclarator>
                {
                    new Ast.VariableDeclarator(new Ast.Identifier("result"), null),
                    new Ast.VariableDeclarator(new Ast.Identifier("done"), null),
                }),
                new Ast.ExpressionStatement(new Ast.AssignmentExpression(TokenKind.Equals,
                    new Ast.MemberExpression(_modules, name, true, false), arrow)),
            });
            return new Ast.FunctionDeclaration(new Ast.Identifier("addModule"),
                new List<Ast.Parameter> { Param(name), Param(new Ast.Identifier("run")) }, body, false, false);
        }

        private static Ast.ExpressionStatement WrapBody(string name, IEnumerable<Ast.Statement> statements)
        {
            var initial = new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator>
            {
                new Ast.VariableDeclarator(_exports, new Ast.ObjectExpression(new List<Ast.Node>())),
                new Ast.VariableDeclarator(_module, new Ast.ObjectExpression(new List<Ast.Node>
                {
                    new Ast.Property(_exports, _exports, Ast.PropertyKind.Init, false, true, false),
                })),
            });
            var final = new Ast.ReturnStatement(new Ast.CallExpression(___adjModule, new List<Ast.Expression> { _module }, false));
            var content = new List<Ast.Statement> { initial };
            content.AddRange(statements);
            content.Add(final);
            var callee = new Ast.ArrowFunctionExpression(new List<Ast.Parameter>(), new Ast.BlockStatement(content), false);
            var call = new Ast.CallExpression(new Ast.Identifier("addModule"), new List<Ast.Expression> { MakeString(name), callee }, false);
            return new Ast.ExpressionStatement(call);
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
                Ast.Expression init = MakeRequireCall(GetName(reference));

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
                var payload = MakeRequireCall(GetName(reference));
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
                var require = MakeRequireCall(GetName(reference));
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

                var name = GetName(reference);
                return new Ast.CallExpression(node.Callee, new List<Ast.Expression> { MakeString(name) }, false);
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
