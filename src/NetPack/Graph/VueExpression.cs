namespace NetPack.Graph;

using System.Collections.Generic;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
using NetPack.Syntax.Printer;
using AstNode = NetPack.Syntax.Ast.Node;

/// <summary>Raised when a template construct is outside the supported subset of the
/// native compiler; the caller falls back to Vue's runtime template compiler.</summary>
public sealed class VueTemplateException : System.Exception
{
    public VueTemplateException(string message) : base(message) { }
}

/// <summary>
/// Rewrites a Vue template expression so free identifiers resolve against the
/// render context: <c>count + 1</c> becomes <c>_ctx.count + 1</c>. Identifiers
/// bound by the surrounding template (v-for items, inline arrow parameters,
/// <c>$event</c>) and JavaScript globals are left untouched. Anything the rewriter
/// does not understand raises <see cref="VueTemplateException"/> so the whole
/// template can fall back to runtime compilation rather than emit wrong code.
/// </summary>
public static class VueExpression
{
    private static readonly HashSet<string> Globals =
    [
        "true", "false", "null", "undefined", "this", "NaN", "Infinity",
        "Math", "Number", "Date", "Array", "Object", "Boolean", "String", "RegExp",
        "Map", "Set", "WeakMap", "WeakSet", "JSON", "Intl", "BigInt", "Symbol",
        "Promise", "Reflect", "Proxy", "Error", "console", "window", "document",
        "globalThis", "parseInt", "parseFloat", "isNaN", "isFinite",
        "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent",
    ];

    /// <summary>Rewrites <paramref name="expression"/>; <paramref name="locals"/>
    /// are identifiers introduced by the template that must not be prefixed.</summary>
    public static string Prefix(string expression, IReadOnlySet<string> locals)
    {
        var trimmed = expression.Trim();

        if (trimmed.Length == 0)
        {
            throw new VueTemplateException("empty expression");
        }

        var module = Parser.ParseModule($"({trimmed})", "expr.js",
            new ParserOptions { Tolerant = true, TypeScript = false, Jsx = false });

        if (module.Diagnostics.Count > 0 || module.Body.Count == 0 || module.Body[0] is not ExpressionStatement statement)
        {
            throw new VueTemplateException($"cannot parse expression: {expression}");
        }

        var root = statement.Expression is ParenthesizedExpression paren ? paren.Expression : statement.Expression;
        var scope = new HashSet<string>(locals);
        var rewritten = Rewrite(root, scope);
        return JsPrinter.Print(rewritten).Trim();
    }

    private static Expression Rewrite(Expression node, HashSet<string> scope)
    {
        switch (node)
        {
            case Identifier id:
                return scope.Contains(id.Name) || Globals.Contains(id.Name)
                    ? id
                    : new MemberExpression(new Identifier("_ctx"), new Identifier(id.Name), false, false);

            case StringLiteral:
            case NumericLiteral:
            case BigIntLiteral:
            case BooleanLiteral:
            case NullLiteral:
            case RegExpLiteral:
            case ThisExpression:
                return node;

            case MemberExpression member:
                member.Object = Rewrite(member.Object, scope);
                if (member.Computed)
                {
                    member.Property = Rewrite((Expression)member.Property, scope);
                }
                return member;

            case CallExpression call:
                call.Callee = Rewrite(call.Callee, scope);
                RewriteList(call.Arguments, scope);
                return call;

            case NewExpression @new:
                @new.Callee = Rewrite(@new.Callee, scope);
                RewriteList(@new.Arguments, scope);
                return @new;

            case BinaryExpression binary:
                binary.Left = Rewrite(binary.Left, scope);
                binary.Right = Rewrite(binary.Right, scope);
                return binary;

            case LogicalExpression logical:
                logical.Left = Rewrite(logical.Left, scope);
                logical.Right = Rewrite(logical.Right, scope);
                return logical;

            case AssignmentExpression assign:
                assign.Left = Rewrite(assign.Left, scope);
                assign.Right = Rewrite(assign.Right, scope);
                return assign;

            case ConditionalExpression cond:
                cond.Test = Rewrite(cond.Test, scope);
                cond.Consequent = Rewrite(cond.Consequent, scope);
                cond.Alternate = Rewrite(cond.Alternate, scope);
                return cond;

            case UnaryExpression unary:
                unary.Argument = Rewrite(unary.Argument, scope);
                return unary;

            case UpdateExpression update:
                update.Argument = Rewrite(update.Argument, scope);
                return update;

            case SequenceExpression sequence:
                RewriteList(sequence.Expressions, scope);
                return sequence;

            case ParenthesizedExpression paren:
                paren.Expression = Rewrite(paren.Expression, scope);
                return paren;

            case SpreadElement spread:
                spread.Argument = Rewrite(spread.Argument, scope);
                return spread;

            case ArrayExpression array:
                for (var i = 0; i < array.Elements.Count; i++)
                {
                    if (array.Elements[i] is { } element)
                    {
                        array.Elements[i] = Rewrite(element, scope);
                    }
                }
                return array;

            case ObjectExpression obj:
                foreach (var property in obj.Properties)
                {
                    RewriteObjectMember(property, scope);
                }
                return obj;

            case TemplateLiteral template:
                RewriteList(template.Expressions, scope);
                return template;

            case ArrowFunctionExpression arrow:
                return RewriteArrow(arrow, scope);

            default:
                throw new VueTemplateException($"unsupported expression ({node.Kind})");
        }
    }

    private static Expression RewriteArrow(ArrowFunctionExpression arrow, HashSet<string> scope)
    {
        var inner = new HashSet<string>(scope);

        foreach (var parameter in arrow.Parameters)
        {
            CollectNames(parameter.Pattern, inner);
        }

        if (arrow.Body is Expression body)
        {
            arrow.Body = Rewrite(body, inner);
            return arrow;
        }

        // Statement-bodied arrows are rare in templates; fall back rather than
        // walk a full block.
        throw new VueTemplateException("statement-bodied arrow in template expression");
    }

    private static void RewriteObjectMember(AstNode member, HashSet<string> scope)
    {
        switch (member)
        {
            case Property property:
                if (property.Computed)
                {
                    property.Key = Rewrite((Expression)property.Key, scope);
                }

                if (property.Shorthand && property.Key is Identifier key)
                {
                    // `{ foo }` -> `{ foo: _ctx.foo }`
                    property.Value = Rewrite(new Identifier(key.Name), scope);
                    property.Shorthand = false;
                }
                else if (property.Value is Expression value)
                {
                    property.Value = Rewrite(value, scope);
                }
                break;

            case SpreadElement spread:
                spread.Argument = Rewrite(spread.Argument, scope);
                break;
        }
    }

    private static void RewriteList(IList<Expression> list, HashSet<string> scope)
    {
        for (var i = 0; i < list.Count; i++)
        {
            list[i] = Rewrite(list[i], scope);
        }
    }

    private static void CollectNames(AstNode pattern, HashSet<string> names)
    {
        switch (pattern)
        {
            case Identifier id:
                names.Add(id.Name);
                break;

            case ObjectExpression obj:
                foreach (var member in obj.Properties)
                {
                    if (member is Property property)
                    {
                        CollectNames(property.Value ?? property.Key, names);
                    }
                    else if (member is SpreadElement spread)
                    {
                        CollectNames(spread.Argument, names);
                    }
                }
                break;

            case ArrayExpression array:
                foreach (var element in array.Elements)
                {
                    if (element is not null)
                    {
                        CollectNames(element, names);
                    }
                }
                break;

            case AssignmentExpression assign:
                CollectNames(assign.Left, names);
                break;

            case SpreadElement spread:
                CollectNames(spread.Argument, names);
                break;
        }
    }
}
