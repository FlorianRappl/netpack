namespace NetPack.Syntax;

using System.Collections.Generic;
using NetPack.Syntax.Ast;

public sealed partial class Parser
{
    private ClassDeclaration ParseClassDeclaration(IList<Decorator> decorators)
    {
        var start = decorators.Count > 0 ? decorators[0].Start : _current.Start;
        var (id, superClass, body) = ParseClassTail();
        return new ClassDeclaration(id, superClass, body, decorators) { Start = start, End = body.End };
    }

    /// <summary>Parses <c>class [Id] [extends X] { members }</c> from the
    /// <c>class</c> keyword onward. Shared by declaration and expression forms.</summary>
    private (Identifier? Id, Expression? SuperClass, ClassBody Body) ParseClassTail()
    {
        Expect(TokenKind.ClassKeyword, "'class'");
        Identifier? id = Keywords.IsIdentifierName(_current.Kind)
            && !Check(TokenKind.ExtendsKeyword) && !Check(TokenKind.OpenBrace) && !Check(TokenKind.ImplementsKeyword)
            ? ParseIdentifierName()
            : null;
        SkipTypeParameters();

        Expression? superClass = null;
        if (Match(TokenKind.ExtendsKeyword))
        {
            superClass = ParseLeftHandSide();
            SkipTypeArguments();
        }
        // TypeScript `implements I, J` — erased.
        if (Check(TokenKind.ImplementsKeyword))
        {
            Advance();
            do { SkipType(stopAtBrace: true); } while (Match(TokenKind.Comma));
        }

        var body = ParseClassBody();
        return (id, superClass, body);
    }

    private ClassBody ParseClassBody()
    {
        var start = _current.Start;
        Expect(TokenKind.OpenBrace, "'{'");
        var members = new List<Node>();
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            if (Match(TokenKind.Semicolon))
            {
                continue; // stray semicolons between members
            }
            var before = _current.Start;
            var member = ParseClassMember();
            if (member is not null)
            {
                members.Add(member);
            }
            if (_current.Start == before && !Check(TokenKind.CloseBrace))
            {
                Advance(); // guarantee progress on malformed input
            }
        }
        var end = _current.End;
        Expect(TokenKind.CloseBrace, "'}'");
        return new ClassBody(members) { Start = start, End = end };
    }

    private Node? ParseClassMember()
    {
        var start = _current.Start;
        var decorators = ParseDecorators();

        var isStatic = false;
        var isDeclare = false;
        var isAbstract = false;

        // Modifiers may appear in any order; `static { }` is a static block.
        while (true)
        {
            if (Check(TokenKind.StaticKeyword))
            {
                var next = Peek();
                if (next.Kind == TokenKind.OpenBrace)
                {
                    Advance(); // static
                    var block = ParseBlock();
                    return new StaticBlock(block.Body) { Start = start, End = block.End };
                }
                if (IsClassElementDelimiter(next.Kind))
                {
                    break; // `static` is the member name
                }
                isStatic = true;
                Advance();
                continue;
            }

            if (_options.TypeScript && IsTypeScriptModifier(_current.Kind) && !IsClassElementDelimiter(Peek().Kind))
            {
                if (Check(TokenKind.DeclareKeyword)) isDeclare = true;
                if (Check(TokenKind.AbstractKeyword)) isAbstract = true;
                Advance();
                continue;
            }

            break;
        }

        var async = false;
        var generator = false;
        var methodKind = MethodKind.Method;

        if (Check(TokenKind.AsyncKeyword) && !IsClassElementDelimiter(Peek().Kind) && !Peek().PrecededByNewLine)
        {
            async = true;
            Advance();
        }
        if (Check(TokenKind.Asterisk))
        {
            generator = true;
            Advance();
        }
        if ((Check(TokenKind.GetKeyword) || Check(TokenKind.SetKeyword)) && !IsClassElementDelimiter(Peek().Kind))
        {
            methodKind = Check(TokenKind.GetKeyword) ? MethodKind.Get : MethodKind.Set;
            Advance();
        }

        var key = ParsePropertyKey(out var computed);
        Match(TokenKind.Question);     // optional member (TS)
        Match(TokenKind.Exclamation);  // definite assignment (TS)

        if (Check(TokenKind.OpenParen) || Check(TokenKind.LessThan))
        {
            SkipTypeParameters();
            var (parameters, _) = ParseParameterList();
            SkipReturnType();

            if (!Check(TokenKind.OpenBrace))
            {
                // Body-less method: TS overload signature / abstract / declare —
                // dropped from JavaScript output.
                ConsumeSemicolon();
                return null;
            }

            var body = ParseBlock();
            var fn = new FunctionExpression(null, parameters, body, async, generator) { Start = start, End = body.End };
            var kind = methodKind == MethodKind.Method && !isStatic && !computed
                && key is Identifier { Name: "constructor" }
                ? MethodKind.Constructor
                : methodKind;
            return new MethodDefinition(key, fn, kind, computed, isStatic, decorators) { Start = start, End = body.End };
        }

        // Field / property definition.
        SkipTypeAnnotation();
        Expression? value = null;
        if (Match(TokenKind.Equals))
        {
            value = ParseAssignment();
        }
        var end = value?.End ?? key.End;
        ConsumeSemicolon();

        if (isDeclare || isAbstract)
        {
            return null; // ambient / abstract field — no runtime output
        }

        return new PropertyDefinition(key, value, computed, isStatic, decorators) { Start = start, End = end };
    }

    private IList<Decorator> ParseDecorators()
    {
        List<Decorator>? list = null;
        while (Check(TokenKind.At))
        {
            var start = _current.Start;
            Advance();
            var expression = ParseLeftHandSide();
            (list ??= new List<Decorator>()).Add(new Decorator(expression) { Start = start, End = expression.End });
        }
        return (IList<Decorator>?)list ?? System.Array.Empty<Decorator>();
    }

    private static readonly HashSet<TokenKind> _tsModifiers = new()
    {
        TokenKind.AbstractKeyword, TokenKind.ReadonlyKeyword, TokenKind.PublicKeyword,
        TokenKind.PrivateKeyword, TokenKind.ProtectedKeyword, TokenKind.OverrideKeyword,
        TokenKind.DeclareKeyword, TokenKind.AccessorKeyword,
    };

    private static bool IsTypeScriptModifier(TokenKind kind) => _tsModifiers.Contains(kind);

    /// <summary>
    /// True when <paramref name="kind"/> is a token that follows a class element
    /// name (meaning a preceding contextual word like <c>static</c>/<c>get</c>
    /// was the name itself, not a modifier).
    /// </summary>
    private static bool IsClassElementDelimiter(TokenKind kind) => kind switch
    {
        TokenKind.OpenParen or TokenKind.Equals or TokenKind.Semicolon
            or TokenKind.CloseBrace or TokenKind.Colon or TokenKind.Question
            or TokenKind.Exclamation or TokenKind.LessThan => true,
        _ => false,
    };
}
