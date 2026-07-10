namespace NetPack.Graph;

using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// Implements React Fast Refresh instrumentation (the equivalent of the
/// <c>react-refresh/babel</c> plugin, adapted to NetPack's module runtime).
///
/// For every module that declares React components, the module body is wrapped
/// so that each top-level component is registered with the Fast Refresh runtime
/// (<c>$RefreshReg$</c>). When a module exports only components it installs a
/// <c>module.hot.accept</c> boundary; on a hot update the module factory re-runs,
/// re-registers its (new) component functions under the same family id, and asks
/// the runtime to reconcile — preserving component state across edits.
///
/// The runtime itself is the official <c>react-refresh/runtime</c> package,
/// resolved from the project's <c>node_modules</c> and exposed through the
/// <c>globalThis.__netpackRefresh</c> shim built by <see cref="BuildSetup"/>.
/// </summary>
public static class ReactRefresh
{
    private const string RefreshVar = "$RefreshReg$";
    private const string Global = "globalThis.__netpackRefresh";

    /// <summary>
    /// Instruments a module factory body: registers each top-level component and
    /// installs a refresh boundary. Returns the body unchanged when the module
    /// declares no components.
    /// </summary>
    public static List<Ast.Statement> Instrument(IReadOnlyList<Ast.Statement> body, int moduleId)
    {
        var hasComponents = body.Any(s => ComponentName(s) is not null);

        if (!hasComponents)
        {
            return body as List<Ast.Statement> ?? [.. body];
        }

        var result = new List<Ast.Statement>(body.Count + 4);

        // Factory-local $RefreshReg$ that attributes registrations to this module.
        result.AddRange(Parse(
            $"var {RefreshVar} = function (type, id) {{ {Global}.register(type, \"{moduleId} \" + id); }};"));

        foreach (var statement in body)
        {
            result.Add(statement);
            var name = ComponentName(statement);

            if (name is not null)
            {
                result.AddRange(Parse($"{RefreshVar}({name}, \"{name}\");"));
            }
        }

        // Self-accepting boundary when every export is a component.
        result.AddRange(Parse(
            $"if (module.hot && {Global} && {Global}.isBoundary(exports)) {{ " +
            $"module.hot.accept(function () {{ {Global}.perform(); }}); }}"));

        return result;
    }

    /// <summary>
    /// Builds the one-time runtime setup: pulls in <c>react-refresh/runtime</c>
    /// (module id <paramref name="runtimeId"/>), injects it into the global hook
    /// and exposes the <c>__netpackRefresh</c> shim used by instrumented modules.
    /// Must run before any component module executes.
    /// </summary>
    public static List<Ast.Statement> BuildSetup(int runtimeId)
    {
        var js =
            "(function () {\n" +
            $"  var RR = {JsRuntime.Require}({runtimeId});\n" +
            "  if (!RR || !RR.injectIntoGlobalHook) return;\n" +
            "  RR.injectIntoGlobalHook(globalThis);\n" +
            "  var __t;\n" +
            "  globalThis.__netpackRefresh = {\n" +
            "    register: function (type, id) { RR.register(type, id); },\n" +
            "    sign: RR.createSignatureFunctionForTransform,\n" +
            "    perform: function () { clearTimeout(__t); __t = setTimeout(RR.performReactRefresh, 30); },\n" +
            "    isBoundary: function (exp) {\n" +
            "      if (exp == null || (typeof exp !== \"object\" && typeof exp !== \"function\")) return false;\n" +
            "      var all = true, any = false;\n" +
            "      for (var k in exp) { if (k === \"__esModule\") continue; any = true; if (!RR.isLikelyComponentType(exp[k])) all = false; }\n" +
            "      return any && all;\n" +
            "    }\n" +
            "  };\n" +
            "})();";

        return Parse(js);
    }

    private static List<Ast.Statement> Parse(string js)
        => [.. Parser.ParseModule(js, "netpack:react-refresh").Body];

    private static string? ComponentName(Ast.Statement statement) => statement switch
    {
        Ast.FunctionDeclaration f when IsComponentName(f.Id?.Name) => f.Id!.Name,
        Ast.ClassDeclaration c when IsComponentName(c.Id?.Name) => c.Id!.Name,
        Ast.VariableStatement v when v.Declarations.Count == 1 => ComponentDeclarator(v.Declarations[0]),
        _ => null,
    };

    private static string? ComponentDeclarator(Ast.VariableDeclarator declarator)
        => declarator.Id is Ast.Identifier id && IsComponentName(id.Name) && IsComponentInit(declarator.Init)
            ? id.Name
            : null;

    private static bool IsComponentInit(Ast.Expression? init)
        => init is Ast.ArrowFunctionExpression or Ast.FunctionExpression || IsHocCall(init);

    // `const X = memo(...)` / `const X = React.forwardRef(...)` and friends.
    private static bool IsHocCall(Ast.Expression? expression)
        => expression is Ast.CallExpression call && IsHocCallee(call.Callee);

    private static bool IsHocCallee(Ast.Expression callee) => callee switch
    {
        Ast.Identifier i => i.Name is "memo" or "forwardRef",
        Ast.MemberExpression m => m.Property is Ast.Identifier p && p.Name is "memo" or "forwardRef",
        _ => false,
    };

    // React's convention: a component's name starts with an uppercase letter.
    private static bool IsComponentName(string? name)
        => !string.IsNullOrEmpty(name) && char.IsUpper(name[0]);
}
