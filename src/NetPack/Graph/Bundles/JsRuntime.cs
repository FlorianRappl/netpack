namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using System.Text;

/// <summary>Runtime envelope for bundle serialization.</summary>
public static class JsRuntime
{
    /// <summary>Registry object symbol (module id → factory).</summary>
    public const string Registry = "__m";

    /// <summary>Require function symbol.</summary>
    public const string Require = "__r";

    /// <summary>
    /// Emits the JavaScript runtime prelude for a JS bundle as plain source text.
    /// It is deliberately written as ordinary JavaScript (rather than assembled from
    /// AST nodes) and then parsed back by the bundler, so the printer formats it and
    /// the mangler shortens its locals like any other code.
    ///
    /// The registry (<c>__m</c>) is declared separately by the bundle; this prelude
    /// wires up the lazy <c>require</c> runtime (<c>__r</c>) with a cache
    /// (<c>__c</c>) that stores each module's <c>exports</c> object <i>before</i>
    /// running its factory — the key to correct circular-dependency behaviour — plus
    /// the ESM/CJS default interop and, optionally, the hot-module-replacement
    /// machinery.
    ///
    /// HMR: when <paramref name="reloading"/> is set the runtime records the module
    /// dependency graph (which module required which), exposes a <c>module.hot</c>
    /// API, and installs <c>globalThis.__netpack.apply(updates)</c>. Applying an
    /// update swaps the changed module factories, bubbles from each changed module up
    /// through its importers to the nearest <c>hot.accept</c> boundary, disposes and
    /// re-executes that boundary (falling back to a full reload when no boundary
    /// accepts the change).
    /// </summary>
    public static string Build(bool isShared, IReadOnlyList<string> sharedNames, bool reloading)
    {
        var sb = new StringBuilder();

        if (sharedNames.Count > 0)
        {
            sb.Append("Object.assign(").Append(Registry).Append(", ").Append(string.Join(", ", sharedNames)).Append(");\n");
        }

        // Shared bundles only provide the registry; the consuming entry owns the
        // require runtime.
        if (isShared)
        {
            return sb.ToString();
        }

        return reloading ? BuildHot(sb) : BuildPlain(sb);
    }

    private static string BuildPlain(StringBuilder sb)
    {
        sb.Append("var __c = {};\n");
        sb.Append("function ").Append(Require).Append("(id) {\n");
        sb.Append("  var mod = __c[id];\n");
        sb.Append("  if (mod) return mod.exports;\n");
        sb.Append("  mod = __c[id] = { exports: {} };\n");
        sb.Append("  ").Append(Registry).Append("[id](mod, mod.exports, ").Append(Require).Append(");\n");
        sb.Append("  var e = mod.exports;\n");
        sb.Append("  if (e && (typeof e == \"object\" || typeof e == \"function\") && e.default === void 0) e.default = e;\n");
        sb.Append("  return e;\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string BuildHot(StringBuilder sb)
    {
        sb.Append("var __c = {};\n");
        sb.Append("var __h = {};\n");

        // Hot record: per-module data, dispose handlers, importers (parents) and
        // whether the module accepts its own updates.
        sb.Append("function __rec() { return { data: {}, disposers: [], parents: [], accepted: false, cb: null }; }\n");

        sb.Append("function __hot(id) {\n");
        sb.Append("  var rec = __h[id] || (__h[id] = __rec());\n");
        sb.Append("  return {\n");
        sb.Append("    data: rec.data,\n");
        sb.Append("    accept: function (dep, cb) { rec.accepted = true; var fn = typeof dep == \"function\" ? dep : cb; if (typeof fn == \"function\") rec.cb = fn; },\n");
        sb.Append("    dispose: function (cb) { rec.disposers.push(cb); },\n");
        sb.Append("    decline: function () { rec.declined = true; },\n");
        sb.Append("    invalidate: function () { rec.accepted = false; }\n");
        sb.Append("  };\n");
        sb.Append("}\n");

        sb.Append("function ").Append(Require).Append("(id) {\n");
        sb.Append("  var mod = __c[id];\n");
        sb.Append("  if (mod) return mod.exports;\n");
        sb.Append("  mod = __c[id] = { exports: {} };\n");
        sb.Append("  mod.hot = __hot(id);\n");
        sb.Append("  var req = function (dep) {\n");
        sb.Append("    var rec = __h[dep] || (__h[dep] = __rec());\n");
        sb.Append("    if (rec.parents.indexOf(id) < 0) rec.parents.push(id);\n");
        sb.Append("    return ").Append(Require).Append("(dep);\n");
        sb.Append("  };\n");
        sb.Append("  ").Append(Registry).Append("[id](mod, mod.exports, req);\n");
        sb.Append("  var e = mod.exports;\n");
        sb.Append("  if (e && (typeof e == \"object\" || typeof e == \"function\") && e.default === void 0) e.default = e;\n");
        sb.Append("  return e;\n");
        sb.Append("}\n");

        sb.Append("function __apply(updates) {\n");
        sb.Append("  var dirty = [];\n");
        sb.Append("  updates.forEach(function (u) { ").Append(Registry).Append("[u.id] = u.factory; dirty.push(u.id); });\n");
        sb.Append("  var outdated = {};\n");
        sb.Append("  var boundaries = [];\n");
        // Only bubble modules that are actually loaded; freshly added modules are
        // just registered and will run when an importer re-requires them.
        sb.Append("  var queue = dirty.filter(function (id) { return __h[id] && __c[id]; });\n");
        sb.Append("  while (queue.length) {\n");
        sb.Append("    var id = queue.shift();\n");
        sb.Append("    if (outdated[id]) continue;\n");
        sb.Append("    outdated[id] = true;\n");
        sb.Append("    var rec = __h[id];\n");
        sb.Append("    if (rec && rec.accepted) { boundaries.push(id); continue; }\n");
        sb.Append("    var parents = rec ? rec.parents : [];\n");
        sb.Append("    if (!parents.length) { location.reload(); return; }\n");
        sb.Append("    for (var i = 0; i < parents.length; i++) queue.push(parents[i]);\n");
        sb.Append("  }\n");
        sb.Append("  Object.keys(outdated).forEach(function (id) {\n");
        sb.Append("    var rec = __h[id];\n");
        sb.Append("    if (rec) { rec.disposers.forEach(function (fn) { try { fn(rec.data); } catch (err) {} }); rec.disposers = []; }\n");
        sb.Append("    delete __c[id];\n");
        sb.Append("  });\n");
        sb.Append("  boundaries.forEach(function (id) {\n");
        sb.Append("    try {\n");
        sb.Append("      var cb = __h[id] && __h[id].cb;\n");
        sb.Append("      ").Append(Require).Append("(id);\n");
        sb.Append("      if (typeof cb == \"function\") cb();\n");
        sb.Append("    } catch (err) { location.reload(); }\n");
        sb.Append("  });\n");
        sb.Append("}\n");

        sb.Append("globalThis.__netpack = { require: ").Append(Require).Append(", modules: ").Append(Registry).Append(", apply: __apply };\n");
        return sb.ToString();
    }
}
