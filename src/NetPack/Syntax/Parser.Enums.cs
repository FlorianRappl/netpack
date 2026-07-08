namespace NetPack.Syntax;

using System.Collections.Generic;
using System.Globalization;
using NetPack.Syntax.Ast;

public sealed partial class Parser
{
    /// <summary>
    /// Parses a TypeScript <c>enum</c> and lowers it to a runtime IIFE, mirroring
    /// what <c>tsc</c> emits (minus declaration-merging, which a bundle never
    /// needs):
    /// <code>
    /// const Color = (() =&gt; {
    ///     const Color = {};
    ///     Color[Color["Red"] = 0] = "Red";   // numeric members get a reverse map
    ///     Color["Name"] = "value";           // string members are forward-only
    ///     return Color;
    /// })();
    /// </code>
    /// </summary>
    private Statement ParseEnum(bool isConst)
    {
        var start = _current.Start;
        if (isConst)
        {
            Advance(); // const
        }
        Expect(TokenKind.EnumKeyword, "'enum'");
        var name = ParseBindingIdentifier();

        var members = new List<EnumMember>();
        Expect(TokenKind.OpenBrace, "'{'");
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            string memberName;
            if (Check(TokenKind.StringLiteral))
            {
                memberName = _current.Value ?? string.Empty;
                Advance();
            }
            else
            {
                memberName = CurrentText();
                Advance();
            }

            Expression? initializer = null;
            if (Match(TokenKind.Equals))
            {
                initializer = ParseAssignment();
            }

            members.Add(new EnumMember(memberName, initializer));

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        var end = _current.End;
        Expect(TokenKind.CloseBrace, "'}'");

        return BuildEnum(name.Name, members, start, end);
    }

    private readonly record struct EnumMember(string Name, Expression? Initializer);

    private static Statement BuildEnum(string enumName, List<EnumMember> members, int start, int end)
    {
        var body = new List<Statement>
        {
            // const <Enum> = {};
            new VariableStatement(VariableKind.Const, new List<VariableDeclarator>
            {
                new VariableDeclarator(new Identifier(enumName), new ObjectExpression(new List<Node>())),
            }),
        };

        long nextAuto = 0;
        var autoValid = true;

        foreach (var member in members)
        {
            var forwardTarget = new MemberExpression(
                new Identifier(enumName),
                new StringLiteral(member.Name, member.Name),
                computed: true, optional: false);

            if (member.Initializer is StringLiteral stringInit)
            {
                // String member: forward mapping only, no reverse.
                body.Add(new ExpressionStatement(new AssignmentExpression(TokenKind.Equals, forwardTarget, stringInit)));
                autoValid = false;
                continue;
            }

            Expression valueExpr;
            if (member.Initializer is not null)
            {
                valueExpr = member.Initializer;
                if (member.Initializer is NumericLiteral num && TryEvaluateInteger(num.Raw, out var value))
                {
                    nextAuto = value + 1;
                    autoValid = true;
                }
                else
                {
                    autoValid = false;
                }
            }
            else
            {
                valueExpr = new NumericLiteral(nextAuto.ToString(CultureInfo.InvariantCulture));
                nextAuto++;
                autoValid = autoValid && true;
            }

            // <Enum>[<Enum>["Name"] = value] = "Name";
            var inner = new AssignmentExpression(TokenKind.Equals, forwardTarget, valueExpr);
            var reverseTarget = new MemberExpression(new Identifier(enumName), inner, computed: true, optional: false);
            body.Add(new ExpressionStatement(new AssignmentExpression(TokenKind.Equals, reverseTarget,
                new StringLiteral(member.Name, member.Name))));
        }

        body.Add(new ReturnStatement(new Identifier(enumName)));

        var arrow = new ArrowFunctionExpression(new List<Parameter>(), new BlockStatement(body), async: false);
        var iife = new CallExpression(arrow, new List<Expression>(), optional: false);
        return new VariableStatement(VariableKind.Const, new List<VariableDeclarator>
        {
            new VariableDeclarator(new Identifier(enumName), iife),
        })
        { Start = start, End = end };
    }

    private static bool TryEvaluateInteger(string raw, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        var text = raw.Replace("_", string.Empty);
        try
        {
            if (text.Length > 2 && text[0] == '0')
            {
                switch (text[1])
                {
                    case 'x':
                    case 'X':
                        value = System.Convert.ToInt64(text[2..], 16);
                        return true;
                    case 'o':
                    case 'O':
                        value = System.Convert.ToInt64(text[2..], 8);
                        return true;
                    case 'b':
                    case 'B':
                        value = System.Convert.ToInt64(text[2..], 2);
                        return true;
                }
            }

            // Reject non-integers (floats/exponents): those are not valid enum
            // auto-increment anchors.
            if (text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
            {
                return false;
            }

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        catch
        {
            return false;
        }
    }
}
