namespace NetPack.Graph;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

/// <summary>The result of compiling a template to a render function body.</summary>
public sealed class VueRenderResult
{
    /// <summary>The expression returned by the render function.</summary>
    public string Body { get; init; } = "null";

    /// <summary>Vue runtime helpers referenced (canonical export names).</summary>
    public IReadOnlyCollection<string> Helpers { get; init; } = [];

    /// <summary>Component tag names to resolve via <c>resolveComponent</c>.</summary>
    public IReadOnlyCollection<string> Components { get; init; } = [];
}

/// <summary>
/// A native, build-time Vue template compiler: it turns the AngleSharp-parsed
/// template DOM into a render expression built from Vue's public runtime helpers
/// (<c>h</c>, <c>toDisplayString</c>, <c>renderList</c>, …). Supported constructs
/// cover interpolation, <c>v-bind</c>/<c>v-on</c>, <c>v-if</c>/<c>v-else-if</c>/
/// <c>v-else</c>, <c>v-for</c>, <c>v-show</c>/<c>v-html</c>/<c>v-text</c>,
/// <c>v-model</c>, components, slots and <c>&lt;slot&gt;</c> outlets. Anything
/// outside the subset raises <see cref="VueTemplateException"/> so the caller can
/// fall back to Vue's runtime compiler.
/// </summary>
public static class VueTemplateCompiler
{
    private const string CtxLocal = "_ctx";

    public static VueRenderResult Compile(IReadOnlyList<INode> roots)
    {
        var compiler = new Compiler();
        var body = compiler.CompileRoot(roots);
        return new VueRenderResult { Body = body, Helpers = compiler.HelperNames, Components = compiler.ComponentNames };
    }

    /// <summary>A component tag's canonical PascalCase name (<c>my-widget</c> → <c>MyWidget</c>).</summary>
    public static string PascalName(string tag) => Capitalize(Camelize(tag));

    /// <summary>A JS-identifier-safe form of a tag for the hoisted component local.</summary>
    public static string Sanitize(string tag) => tag.Replace('-', '_').Replace(':', '_');

    internal static string Camelize(string value) =>
        Regex.Replace(value, "-([a-z])", m => m.Groups[1].Value.ToUpperInvariant());

    internal static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed class Compiler
    {
        private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

        private readonly HashSet<string> _helpers = [];
        private readonly HashSet<string> _components = [];

        public IReadOnlyCollection<string> HelperNames => _helpers;
        public IReadOnlyCollection<string> ComponentNames => _components;

        private string Use(string helper)
        {
            _helpers.Add(helper);
            return "_vue_" + helper;
        }

        public string CompileRoot(IReadOnlyList<INode> roots)
        {
            var children = CompileSiblings(roots, Empty);

            return children.Count switch
            {
                0 => $"{Use("createCommentVNode")}(\"\")",
                1 => children[0],
                _ => $"{Use("h")}({Use("Fragment")}, null, [{string.Join(", ", children)}])",
            };
        }

        // -- children / sibling handling (v-if chains) -------------------------

        private List<string> CompileSiblings(IReadOnlyList<INode> nodes, IReadOnlySet<string> locals)
        {
            var result = new List<string>();

            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];

                if (node is IElement el && HasAttr(el, "v-if"))
                {
                    var branches = new List<(string? Condition, IElement Element)> { (Attr(el, "v-if"), el) };
                    var j = i + 1;

                    while (j < nodes.Count)
                    {
                        if (nodes[j] is IText ws && string.IsNullOrWhiteSpace(ws.Data))
                        {
                            j++;
                            continue;
                        }

                        if (nodes[j] is IElement next && HasAttr(next, "v-else-if"))
                        {
                            branches.Add((Attr(next, "v-else-if"), next));
                            j++;
                            continue;
                        }

                        if (nodes[j] is IElement last && HasAttr(last, "v-else"))
                        {
                            branches.Add((null, last));
                            j++;
                        }

                        break;
                    }

                    result.Add(CompileIfChain(branches, locals));
                    i = j - 1;
                    continue;
                }

                var compiled = CompileNode(node, locals);

                if (compiled is not null)
                {
                    result.Add(compiled);
                }
            }

            return result;
        }

        private string CompileIfChain(List<(string? Condition, IElement Element)> branches, IReadOnlySet<string> locals)
        {
            var comment = $"{Use("createCommentVNode")}(\"v-if\", true)";
            var expr = comment;

            for (var i = branches.Count - 1; i >= 0; i--)
            {
                var (condition, element) = branches[i];
                var vnode = CompileElement(element, locals);

                expr = condition is null
                    ? vnode
                    : $"{VueExpression.Prefix(condition, locals)} ? {vnode} : {expr}";
            }

            return expr;
        }

        private string? CompileNode(INode node, IReadOnlySet<string> locals)
        {
            return node.NodeType switch
            {
                NodeType.Element => CompileElement((IElement)node, locals),
                NodeType.Text => CompileText((IText)node, locals),
                _ => null, // comments and everything else are dropped
            };
        }

        private string? CompileText(IText text, IReadOnlySet<string> locals)
        {
            var data = text.Data;

            if (!data.Contains("{{"))
            {
                return string.IsNullOrWhiteSpace(data) && data.Contains('\n') ? null : Json(data);
            }

            var parts = new List<string>();
            var index = 0;

            while (index < data.Length)
            {
                var open = data.IndexOf("{{", index, System.StringComparison.Ordinal);

                if (open < 0)
                {
                    if (index < data.Length)
                    {
                        parts.Add(Json(data[index..]));
                    }
                    break;
                }

                if (open > index)
                {
                    parts.Add(Json(data[index..open]));
                }

                var close = data.IndexOf("}}", open + 2, System.StringComparison.Ordinal);

                if (close < 0)
                {
                    throw new VueTemplateException("unterminated interpolation");
                }

                var inner = data[(open + 2)..close];
                parts.Add($"{Use("toDisplayString")}({VueExpression.Prefix(inner, locals)})");
                index = close + 2;
            }

            return parts.Count == 0 ? null : string.Join(" + ", parts);
        }

        // -- elements ----------------------------------------------------------

        private string CompileElement(IElement element, IReadOnlySet<string> locals)
        {
            if (HasAttr(element, "v-for"))
            {
                var (vars, list) = ParseFor(Attr(element, "v-for")!);
                var inner = new HashSet<string>(locals);

                foreach (var name in vars)
                {
                    inner.Add(name);
                }

                var core = CompileElementCore(element, inner);
                var renderList = $"{Use("renderList")}({VueExpression.Prefix(list, locals)}, ({string.Join(", ", vars)}) => {core})";
                return $"{Use("h")}({Use("Fragment")}, null, {renderList})";
            }

            return CompileElementCore(element, locals);
        }

        private string CompileElementCore(IElement element, IReadOnlySet<string> locals)
        {
            var tag = element.LocalName;

            if (tag == "slot")
            {
                return CompileSlotOutlet(element, locals);
            }

            if (tag == "template")
            {
                var kids = CompileSiblings(element.ChildNodes.ToList(), locals);
                return kids.Count switch
                {
                    0 => $"{Use("createCommentVNode")}(\"\")",
                    1 => kids[0],
                    _ => $"[{string.Join(", ", kids)}]",
                };
            }

            bool isComponent;
            string typeExpr;

            if (tag == "component")
            {
                // <component :is="…"> — a dynamically resolved component.
                var isExpr = DynamicIs(element, locals)
                    ?? throw new VueTemplateException("<component> requires an `is` binding");
                typeExpr = $"{Use("resolveDynamicComponent")}({isExpr})";
                isComponent = true;
            }
            else
            {
                isComponent = IsComponent(tag);
                typeExpr = isComponent ? ComponentRef(tag) : Json(tag);
            }

            var props = new List<string>();
            var directives = new List<string>();
            string? textChild = null;
            var vModel = default((string Directive, string Expr, string? Mods)?);

            foreach (var attr in element.Attributes)
            {
                ApplyAttribute(element, isComponent, attr, locals, props, directives, ref textChild, ref vModel);
            }

            // children
            string? childrenArg;

            if (textChild is not null)
            {
                childrenArg = textChild;
            }
            else if (isComponent)
            {
                childrenArg = CompileSlots(element, locals);
            }
            else
            {
                var kids = CompileSiblings(element.ChildNodes.ToList(), locals);
                childrenArg = kids.Count switch
                {
                    0 => null,
                    1 => kids[0],
                    _ => $"[{string.Join(", ", kids)}]",
                };
            }

            var propsArg = props.Count == 0 ? "null" : $"{{ {string.Join(", ", props)} }}";

            var vnode = childrenArg is null
                ? $"{Use("h")}({typeExpr}, {propsArg})"
                : $"{Use("h")}({typeExpr}, {propsArg}, {childrenArg})";

            // v-model on native elements and v-show attach as directives. A native
            // v-model carries its modifiers as the directive's fourth element.
            if (vModel is { } model)
            {
                directives.Add(model.Mods is null
                    ? $"[{Use(model.Directive)}, {model.Expr}]"
                    : $"[{Use(model.Directive)}, {model.Expr}, void 0, {model.Mods}]");
            }

            if (directives.Count > 0)
            {
                vnode = $"{Use("withDirectives")}({vnode}, [{string.Join(", ", directives)}])";
            }

            return vnode;
        }

        private void ApplyAttribute(
            IElement element, bool isComponent, IAttr attr, IReadOnlySet<string> locals,
            List<string> props, List<string> directives, ref string? textChild,
            ref (string Directive, string Expr, string? Mods)? vModel)
        {
            var name = attr.Name;
            var value = attr.Value;

            // The `is` binding of <component> is consumed as the element type.
            if (element.LocalName == "component" && name is "is" or ":is" or "v-bind:is")
            {
                return;
            }

            switch (name)
            {
                case "v-if" or "v-else-if" or "v-else" or "v-for":
                    return; // structural, handled elsewhere

                case "v-once" or "v-cloak" or "v-pre":
                    return; // no runtime effect for our purposes

                case "v-show":
                    directives.Add($"[{Use("vShow")}, {VueExpression.Prefix(value, locals)}]");
                    return;

                case "v-html":
                    props.Add($"innerHTML: {VueExpression.Prefix(value, locals)}");
                    return;

                case "v-text":
                    textChild = $"{Use("toDisplayString")}({VueExpression.Prefix(value, locals)})";
                    return;

                case "ref":
                    props.Add($"ref: {Json(value)}");
                    return;

                case "key":
                    props.Add($"key: {Json(value)}");
                    return;

                case "v-bind":
                    throw new VueTemplateException("v-bind=\"object\" is not supported");
            }

            if (name == "v-model" || name.StartsWith("v-model:", System.StringComparison.Ordinal)
                || name.StartsWith("v-model.", System.StringComparison.Ordinal))
            {
                var (arg, mods) = ParseModelName(name);
                vModel = BuildVModel(element, isComponent, arg, mods, value, locals, props);
                return;
            }

            if (name.StartsWith(':') || name.StartsWith("v-bind:", System.StringComparison.Ordinal))
            {
                var raw = name.StartsWith(':') ? name[1..] : name["v-bind:".Length..];
                ApplyBind(raw, value, locals, props);
                return;
            }

            if (name.StartsWith('@') || name.StartsWith("v-on:", System.StringComparison.Ordinal))
            {
                var raw = name.StartsWith('@') ? name[1..] : name["v-on:".Length..];
                var (key, handler) = CompileEventBinding(raw, value, locals);
                props.Add($"{key}: {handler}");
                return;
            }

            if (name.StartsWith("v-slot", System.StringComparison.Ordinal) || name.StartsWith('#'))
            {
                return; // handled by the component-slot path
            }

            if (name.StartsWith("v-", System.StringComparison.Ordinal))
            {
                throw new VueTemplateException($"custom directive {name} is not supported");
            }

            // Plain static attribute.
            props.Add($"{PropKey(name)}: {Json(value)}");
        }

        private string? DynamicIs(IElement element, IReadOnlySet<string> locals)
        {
            foreach (var attr in element.Attributes)
            {
                if (attr.Name is ":is" or "v-bind:is")
                {
                    return VueExpression.Prefix(attr.Value, locals);
                }

                if (attr.Name == "is")
                {
                    return Json(attr.Value);
                }
            }

            return null;
        }

        // -- v-bind (:) with modifiers ----------------------------------------

        private void ApplyBind(string raw, string value, IReadOnlySet<string> locals, List<string> props)
        {
            var dot = raw.IndexOf('.');
            var key = dot < 0 ? raw : raw[..dot];
            var modifiers = dot < 0 ? System.Array.Empty<string>() : SplitModifiers(raw[(dot + 1)..]);

            foreach (var modifier in modifiers)
            {
                key = modifier switch
                {
                    "camel" => Camelize(key),
                    "prop" => "." + key,   // force a DOM property
                    "attr" => "^" + key,   // force an attribute
                    _ => throw new VueTemplateException($"unsupported v-bind modifier .{modifier}"),
                };
            }

            props.Add($"{PropKey(key)}: {VueExpression.Prefix(value, locals)}");
        }

        // -- v-on (@) with modifiers ------------------------------------------

        private static readonly HashSet<string> EventOptionModifiers = ["passive", "once", "capture"];

        private static readonly HashSet<string> SystemModifiers =
            ["stop", "prevent", "self", "ctrl", "shift", "alt", "meta", "exact", "middle"];

        private (string Key, string Handler) CompileEventBinding(string raw, string value, IReadOnlySet<string> locals)
        {
            var dot = raw.IndexOf('.');
            var eventName = dot < 0 ? raw : raw[..dot];
            var modifiers = dot < 0 ? System.Array.Empty<string>() : SplitModifiers(raw[(dot + 1)..]);

            var isKeyboard = eventName is "keyup" or "keydown" or "keypress";
            var keyModifiers = new List<string>();
            var systemModifiers = new List<string>();
            var eventOptions = new List<string>();

            foreach (var modifier in modifiers)
            {
                if (EventOptionModifiers.Contains(modifier))
                {
                    eventOptions.Add(modifier);
                }
                else if (modifier is "left" or "right")
                {
                    // ambiguous: a key on keyboard events, a mouse button otherwise
                    (isKeyboard ? keyModifiers : systemModifiers).Add(modifier);
                }
                else if (SystemModifiers.Contains(modifier))
                {
                    systemModifiers.Add(modifier);
                }
                else
                {
                    keyModifiers.Add(modifier);
                }
            }

            var handler = string.IsNullOrWhiteSpace(value) ? "() => {}" : CompileHandler(value, locals);

            if (systemModifiers.Count > 0)
            {
                handler = $"{Use("withModifiers")}({handler}, [{string.Join(", ", systemModifiers.Select(Json))}])";
            }

            if (keyModifiers.Count > 0)
            {
                handler = $"{Use("withKeys")}({handler}, [{string.Join(", ", keyModifiers.Select(Json))}])";
            }

            var key = "on" + Capitalize(Camelize(eventName));

            foreach (var option in eventOptions)
            {
                key += Capitalize(option);
            }

            return (IsIdentifier(key) ? key : Json(key), handler);
        }

        // -- v-model with modifiers -------------------------------------------

        private static (string Arg, List<string> Modifiers) ParseModelName(string name)
        {
            var body = name["v-model".Length..]; // "", ":arg", ".mod", ":arg.mods"
            var arg = "modelValue";
            var modifiers = new List<string>();

            if (body.StartsWith(':'))
            {
                var rest = body[1..];
                var dot = rest.IndexOf('.');
                arg = dot < 0 ? rest : rest[..dot];

                if (dot >= 0)
                {
                    modifiers.AddRange(SplitModifiers(rest[(dot + 1)..]));
                }
            }
            else if (body.StartsWith('.'))
            {
                modifiers.AddRange(SplitModifiers(body[1..]));
            }

            return (arg, modifiers);
        }

        private (string Directive, string Expr, string? Mods)? BuildVModel(
            IElement element, bool isComponent, string arg, List<string> modifiers,
            string value, IReadOnlySet<string> locals, List<string> props)
        {
            var bound = VueExpression.Prefix(value, locals);
            var setter = $"$event => (({bound}) = $event)";
            var modsObject = modifiers.Count > 0
                ? $"{{ {string.Join(", ", modifiers.Select(m => $"{m}: true"))} }}"
                : null;

            props.Add($"{EventKey("update:" + arg)}: {setter}");

            if (isComponent)
            {
                // Component v-model: a prop + update handler (+ modifiers prop), no directive.
                props.Add($"{PropKey(arg)}: {bound}");

                if (modsObject is not null)
                {
                    props.Add($"{PropKey(ModifiersProp(arg))}: {modsObject}");
                }

                return null;
            }

            // Native form control: the vModel* directive keeps the DOM in sync,
            // reads the update handler above and honours its modifiers.
            return (NativeModelDirective(element), bound, modsObject);
        }

        private static string ModifiersProp(string arg) => arg == "modelValue" ? "modelModifiers" : arg + "Modifiers";

        private static string NativeModelDirective(IElement element)
        {
            var tag = element.LocalName;

            if (tag == "select")
            {
                return "vModelSelect";
            }

            if (tag == "textarea")
            {
                return "vModelText";
            }

            var type = element.GetAttribute("type");

            return type switch
            {
                "checkbox" => "vModelCheckbox",
                "radio" => "vModelRadio",
                _ => "vModelText",
            };
        }

        private static string[] SplitModifiers(string text) =>
            text.Split('.', System.StringSplitOptions.RemoveEmptyEntries);

        // -- slots -------------------------------------------------------------

        private string CompileSlotOutlet(IElement element, IReadOnlySet<string> locals)
        {
            var slotName = Attr(element, "name") is { } n ? Json(n) : "\"default\"";
            var fallback = CompileSiblings(element.ChildNodes.ToList(), locals);
            var fallbackArg = fallback.Count > 0 ? $", () => [{string.Join(", ", fallback)}]" : "";
            return $"{Use("renderSlot")}({CtxLocal}.$slots, {slotName}{fallbackArg})";
        }

        private string? CompileSlots(IElement component, IReadOnlySet<string> locals)
        {
            var named = new List<(string Name, string? Binding, IReadOnlyList<INode> Nodes)>();
            var defaultNodes = new List<INode>();

            foreach (var child in component.ChildNodes)
            {
                if (child is IElement el && el.LocalName == "template")
                {
                    var (name, binding) = SlotDirective(el);

                    if (name is not null)
                    {
                        named.Add((name, binding, el.ChildNodes.ToList()));
                        continue;
                    }
                }

                defaultNodes.Add(child);
            }

            if (named.Count == 0 && CompileSiblings(defaultNodes, locals) is { Count: 0 })
            {
                return null;
            }

            var entries = new List<string>();

            if (defaultNodes.Count > 0)
            {
                var kids = CompileSiblings(defaultNodes, locals);

                if (kids.Count > 0)
                {
                    entries.Add($"default: {Use("withCtx")}(() => [{string.Join(", ", kids)}])");
                }
            }

            foreach (var (name, binding, nodes) in named)
            {
                var inner = locals;
                var param = "";

                if (binding is not null)
                {
                    var scope = new HashSet<string>(locals);
                    CollectBindingNames(binding, scope);
                    inner = scope;
                    param = binding;
                }

                var kids = CompileSiblings(nodes, inner);
                entries.Add($"{PropKey(name)}: {Use("withCtx")}(({param}) => [{string.Join(", ", kids)}])");
            }

            return entries.Count == 0 ? null : $"{{ {string.Join(", ", entries)} }}";
        }

        private static (string? Name, string? Binding) SlotDirective(IElement template)
        {
            foreach (var attr in template.Attributes)
            {
                if (attr.Name == "v-slot")
                {
                    return ("default", NullIfEmpty(attr.Value));
                }

                if (attr.Name.StartsWith("v-slot:", System.StringComparison.Ordinal))
                {
                    return (attr.Name["v-slot:".Length..], NullIfEmpty(attr.Value));
                }

                if (attr.Name.StartsWith('#'))
                {
                    return (attr.Name[1..], NullIfEmpty(attr.Value));
                }
            }

            return (null, null);
        }

        // -- helpers -----------------------------------------------------------

        private string CompileHandler(string value, IReadOnlySet<string> locals)
        {
            // A bare method path (`onClick="submit"` / `"user.save"`) is passed as a
            // function reference; anything else becomes an inline `$event => (…)`.
            if (Regex.IsMatch(value.Trim(), @"^[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*|\[[^\]]*\])*$"))
            {
                return VueExpression.Prefix(value, locals);
            }

            var scope = new HashSet<string>(locals) { "$event" };
            return $"$event => ({VueExpression.Prefix(value, scope)})";
        }

        private string ComponentRef(string tag)
        {
            _components.Add(tag);
            _helpers.Add("resolveComponent");
            return "_component_" + Sanitize(tag);
        }

        private static (List<string> Vars, string List) ParseFor(string expression)
        {
            var match = Regex.Match(expression.Trim(), @"^\s*(.*?)\s+(?:in|of)\s+(.*)$", RegexOptions.Singleline);

            if (!match.Success)
            {
                throw new VueTemplateException($"unsupported v-for: {expression}");
            }

            var lhs = match.Groups[1].Value.Trim();
            var rhs = match.Groups[2].Value.Trim();

            if (lhs.StartsWith('(') && lhs.EndsWith(')'))
            {
                lhs = lhs[1..^1];
            }

            var vars = lhs.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

            if (vars.Any(v => !Regex.IsMatch(v, @"^[A-Za-z_$][\w$]*$")))
            {
                throw new VueTemplateException($"destructuring v-for is not supported: {expression}");
            }

            return (vars, rhs);
        }

        private static void CollectBindingNames(string binding, HashSet<string> names)
        {
            foreach (Match m in Regex.Matches(binding, @"[A-Za-z_$][\w$]*"))
            {
                names.Add(m.Value);
            }
        }

        private static bool IsComponent(string tag) => !NativeTags.Contains(tag);

        private static string PropKey(string key) => IsIdentifier(key) ? key : Json(key);

        private static string EventKey(string raw)
        {
            var key = "on" + Capitalize(Camelize(raw));
            return IsIdentifier(key) ? key : Json(key);
        }

        private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool IsIdentifier(string name)
        {
            if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] is '_' or '$'))
            {
                return false;
            }

            for (var i = 1; i < name.Length; i++)
            {
                if (!(char.IsLetterOrDigit(name[i]) || name[i] is '_' or '$'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Json(string value) => CssModules.JsString(value);

        private static bool HasAttr(IElement element, string name) => element.HasAttribute(name);

        private static string? Attr(IElement element, string name) => element.GetAttribute(name);
    }

    private static readonly HashSet<string> NativeTags =
    [
        // HTML
        "a", "abbr", "address", "area", "article", "aside", "audio", "b", "base", "bdi",
        "bdo", "blockquote", "body", "br", "button", "canvas", "caption", "cite", "code",
        "col", "colgroup", "data", "datalist", "dd", "del", "details", "dfn", "dialog",
        "div", "dl", "dt", "em", "embed", "fieldset", "figcaption", "figure", "footer",
        "form", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup", "hr",
        "html", "i", "iframe", "img", "input", "ins", "kbd", "label", "legend", "li",
        "link", "main", "map", "mark", "menu", "meta", "meter", "nav", "noscript",
        "object", "ol", "optgroup", "option", "output", "p", "param", "picture", "pre",
        "progress", "q", "rp", "rt", "ruby", "s", "samp", "script", "section", "select",
        "small", "source", "span", "strong", "style", "sub", "summary", "sup", "table",
        "tbody", "td", "textarea", "tfoot", "th", "thead", "time", "title", "tr", "track",
        "u", "ul", "var", "video", "wbr",
        // SVG (common)
        "svg", "path", "circle", "rect", "line", "polyline", "polygon", "ellipse", "g",
        "text", "defs", "use", "symbol", "clippath", "mask", "pattern", "image", "tspan",
        "lineargradient", "radialgradient", "stop", "filter",
    ];
}
