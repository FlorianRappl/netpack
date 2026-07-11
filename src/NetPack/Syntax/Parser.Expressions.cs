namespace NetPack.Syntax;

using System.Collections.Generic;
using NetPack.Syntax.Ast;

public sealed partial class Parser
{
    // Disables the `in` operator while parsing a for-statement initializer so
    // that `for (x in y)` is recognized as a for-in head, not a comparison.
    private bool _noIn;

    // -- expression entry points ------------------------------------------

    private Expression ParseExpression()
    {
        var expr = ParseAssignment();
        if (Check(TokenKind.Comma))
        {
            var list = new List<Expression> { expr };
            while (Match(TokenKind.Comma))
            {
                list.Add(ParseAssignment());
            }
            return new SequenceExpression(list) { Start = expr.Start, End = list[^1].End };
        }
        return expr;
    }

    private Expression ParseAssignment()
    {
        if (Check(TokenKind.YieldKeyword))
        {
            return ParseYield();
        }

        var (isArrow, async) = DetectArrow();
        if (isArrow)
        {
            return ParseArrowFunction(async);
        }

        var left = ParseConditional();

        if (IsAssignmentOperator(_current.Kind))
        {
            var op = _current.Kind;
            Advance();
            var right = ParseAssignment();
            return new AssignmentExpression(op, left, right) { Start = left.Start, End = right.End };
        }

        return left;
    }

    private static bool IsAssignmentOperator(TokenKind kind) => kind switch
    {
        TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals
            or TokenKind.AsteriskEquals or TokenKind.AsteriskAsteriskEquals
            or TokenKind.SlashEquals or TokenKind.PercentEquals
            or TokenKind.LessThanLessThanEquals or TokenKind.GreaterThanGreaterThanEquals
            or TokenKind.GreaterThanGreaterThanGreaterThanEquals or TokenKind.AmpersandEquals
            or TokenKind.BarEquals or TokenKind.CaretEquals or TokenKind.AmpersandAmpersandEquals
            or TokenKind.BarBarEquals or TokenKind.QuestionQuestionEquals => true,
        _ => false,
    };

    private Expression ParseConditional()
    {
        var test = ParseBinary(0);
        if (Match(TokenKind.Question))
        {
            var consequent = ParseAssignment();
            Expect(TokenKind.Colon, "':'");
            var alternate = ParseAssignment();
            return new ConditionalExpression(test, consequent, alternate) { Start = test.Start, End = alternate.End };
        }
        return test;
    }

    private int GetBinaryPrecedence(TokenKind kind) => kind switch
    {
        TokenKind.QuestionQuestion => 1,
        TokenKind.BarBar => 2,
        TokenKind.AmpersandAmpersand => 3,
        TokenKind.Bar => 4,
        TokenKind.Caret => 5,
        TokenKind.Ampersand => 6,
        TokenKind.EqualsEquals or TokenKind.ExclamationEquals
            or TokenKind.EqualsEqualsEquals or TokenKind.ExclamationEqualsEquals => 7,
        TokenKind.LessThan or TokenKind.GreaterThan or TokenKind.LessThanEquals
            or TokenKind.GreaterThanEquals or TokenKind.InstanceOfKeyword => 8,
        TokenKind.InKeyword => _noIn ? 0 : 8,
        TokenKind.LessThanLessThan or TokenKind.GreaterThanGreaterThan
            or TokenKind.GreaterThanGreaterThanGreaterThan => 9,
        TokenKind.Plus or TokenKind.Minus => 10,
        TokenKind.Asterisk or TokenKind.Slash or TokenKind.Percent => 11,
        TokenKind.AsteriskAsterisk => 12,
        _ => 0,
    };

    private static bool IsLogical(TokenKind kind)
        => kind is TokenKind.AmpersandAmpersand or TokenKind.BarBar or TokenKind.QuestionQuestion;

    private Expression ParseBinary(int minPrecedence)
    {
        var left = ParseUnary();

        while (true)
        {
            while (TrySkipAsExpression())
            {
                // `expr as T` / `expr satisfies T` — type erased, operand kept.
            }

            var kind = _current.Kind;
            var precedence = GetBinaryPrecedence(kind);
            if (precedence == 0 || precedence <= minPrecedence)
            {
                break;
            }

            Advance();
            // `**` is right-associative; everything else is left-associative.
            var nextMin = kind == TokenKind.AsteriskAsterisk ? precedence - 1 : precedence;
            var right = ParseBinary(nextMin);
            left = IsLogical(kind)
                ? new LogicalExpression(kind, left, right) { Start = left.Start, End = right.End }
                : new BinaryExpression(kind, left, right) { Start = left.Start, End = right.End };
        }

        return left;
    }

    private Expression ParseUnary()
    {
        var token = _current;
        switch (token.Kind)
        {
            case TokenKind.AwaitKeyword:
            {
                Advance();
                var arg = ParseUnary();
                return new AwaitExpression(arg) { Start = token.Start, End = arg.End };
            }
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Tilde:
            case TokenKind.Exclamation:
            case TokenKind.TypeOfKeyword:
            case TokenKind.VoidKeyword:
            case TokenKind.DeleteKeyword:
            {
                Advance();
                var arg = ParseUnary();
                return new UnaryExpression(token.Kind, arg) { Start = token.Start, End = arg.End };
            }
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
            {
                Advance();
                var arg = ParseUnary();
                return new UpdateExpression(token.Kind, arg, prefix: true) { Start = token.Start, End = arg.End };
            }
            case TokenKind.LessThan when _options.TypeScript && !_options.Jsx:
            {
                // TypeScript type assertion `<T>expr` (not valid in .tsx).
                SkipAngleBrackets();
                return ParseUnary();
            }
            default:
                return ParsePostfix();
        }
    }

    private Expression ParsePostfix()
    {
        var expr = ParseLeftHandSide();
        if ((Check(TokenKind.PlusPlus) || Check(TokenKind.MinusMinus)) && !_current.PrecededByNewLine)
        {
            var op = _current.Kind;
            var end = _current.End;
            Advance();
            expr = new UpdateExpression(op, expr, prefix: false) { Start = expr.Start, End = end };
        }
        return expr;
    }

    private Expression ParseLeftHandSide()
    {
        var expr = ParsePrimary();
        return ParseCallMemberChain(expr);
    }

    private Expression ParseCallMemberChain(Expression expr)
    {
        while (true)
        {
            if (Check(TokenKind.Dot))
            {
                Advance();
                var property = ParseMemberName();
                expr = new MemberExpression(expr, property, computed: false, optional: false) { Start = expr.Start, End = property.End };
            }
            else if (Check(TokenKind.QuestionDot))
            {
                Advance();
                if (Check(TokenKind.OpenParen))
                {
                    var (args, end) = ParseArguments();
                    expr = new CallExpression(expr, args, optional: true) { Start = expr.Start, End = end };
                }
                else if (Check(TokenKind.OpenBracket))
                {
                    Advance();
                    var index = ParseExpression();
                    var end = _current.End;
                    Expect(TokenKind.CloseBracket, "']'");
                    expr = new MemberExpression(expr, index, computed: true, optional: true) { Start = expr.Start, End = end };
                }
                else
                {
                    var property = ParseMemberName();
                    expr = new MemberExpression(expr, property, computed: false, optional: true) { Start = expr.Start, End = property.End };
                }
            }
            else if (Check(TokenKind.OpenBracket))
            {
                Advance();
                var index = ParseExpression();
                var end = _current.End;
                Expect(TokenKind.CloseBracket, "']'");
                expr = new MemberExpression(expr, index, computed: true, optional: false) { Start = expr.Start, End = end };
            }
            else if (Check(TokenKind.OpenParen))
            {
                var (args, end) = ParseArguments();
                expr = new CallExpression(expr, args, optional: false) { Start = expr.Start, End = end };
            }
            else if (Check(TokenKind.NoSubstitutionTemplate) || Check(TokenKind.TemplateHead))
            {
                var quasi = ParseTemplateLiteral();
                expr = new TaggedTemplateExpression(expr, quasi) { Start = expr.Start, End = quasi.End };
            }
            else if (_options.TypeScript && Check(TokenKind.Exclamation) && !_current.PrecededByNewLine)
            {
                // Non-null assertion `expr!` — erased.
                Advance();
            }
            else if (_options.TypeScript && Check(TokenKind.LessThan))
            {
                if (!TryTakeCallTypeArguments())
                {
                    break;
                }
                if (Check(TokenKind.OpenParen))
                {
                    var (args, end) = ParseArguments();
                    expr = new CallExpression(expr, args, optional: false) { Start = expr.Start, End = end };
                }
                else if (Check(TokenKind.NoSubstitutionTemplate) || Check(TokenKind.TemplateHead))
                {
                    var quasi = ParseTemplateLiteral();
                    expr = new TaggedTemplateExpression(expr, quasi) { Start = expr.Start, End = quasi.End };
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }
        return expr;
    }

    private Node ParseMemberName()
    {
        if (Check(TokenKind.PrivateIdentifier))
        {
            var token = _current;
            Advance();
            return new PrivateIdentifier(token.Value ?? "#") { Start = token.Start, End = token.End };
        }
        var name = CurrentText();
        var t = _current;
        Advance();
        return new Identifier(name) { Start = t.Start, End = t.End };
    }

    private (List<Expression> Arguments, int End) ParseArguments()
    {
        Expect(TokenKind.OpenParen, "'('");
        var list = new List<Expression>();
        while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
        {
            if (Check(TokenKind.DotDotDot))
            {
                var start = _current.Start;
                Advance();
                var arg = ParseAssignment();
                list.Add(new SpreadElement(arg) { Start = start, End = arg.End });
            }
            else
            {
                list.Add(ParseAssignment());
            }
            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        var end = _current.End;
        Expect(TokenKind.CloseParen, "')'");
        return (list, end);
    }

    // -- primary -----------------------------------------------------------

    private Expression ParsePrimary()
    {
        var token = _current;
        switch (token.Kind)
        {
            case TokenKind.NumericLiteral:
                Advance();
                return new NumericLiteral(token.Value ?? _tokenizer.GetText(token)) { Start = token.Start, End = token.End };
            case TokenKind.BigIntLiteral:
                Advance();
                return new BigIntLiteral(token.Value ?? _tokenizer.GetText(token)) { Start = token.Start, End = token.End };
            case TokenKind.StringLiteral:
                Advance();
                return MakeStringLiteral(token);
            case TokenKind.RegExpLiteral:
                Advance();
                return new RegExpLiteral(_tokenizer.GetText(token)) { Start = token.Start, End = token.End };
            case TokenKind.TrueKeyword:
            case TokenKind.FalseKeyword:
                Advance();
                return new BooleanLiteral(token.Kind == TokenKind.TrueKeyword) { Start = token.Start, End = token.End };
            case TokenKind.NullKeyword:
                Advance();
                return new NullLiteral { Start = token.Start, End = token.End };
            case TokenKind.ThisKeyword:
                Advance();
                return new ThisExpression { Start = token.Start, End = token.End };
            case TokenKind.SuperKeyword:
                Advance();
                return new SuperExpression { Start = token.Start, End = token.End };
            case TokenKind.NoSubstitutionTemplate:
            case TokenKind.TemplateHead:
                return ParseTemplateLiteral();
            case TokenKind.OpenBracket:
                return ParseArrayExpression();
            case TokenKind.OpenBrace:
                return ParseObjectExpression();
            case TokenKind.OpenParen:
                return ParseParenthesized();
            case TokenKind.FunctionKeyword:
                return ParseFunctionExpression(async: false);
            case TokenKind.ClassKeyword:
                return ParseClassExpression();
            case TokenKind.NewKeyword:
                return ParseNew();
            case TokenKind.ImportKeyword:
                return ParseImportExpression();
            case TokenKind.AsyncKeyword:
                if (Peek().Kind == TokenKind.FunctionKeyword)
                {
                    Advance();
                    return ParseFunctionExpression(async: true);
                }
                Advance();
                return new Identifier(token.Value ?? "async") { Start = token.Start, End = token.End };
            case TokenKind.LessThan when _options.Jsx:
                return ParseJsxElementOrFragment();
            default:
                if (Keywords.IsIdentifierName(token.Kind))
                {
                    Advance();
                    return new Identifier(token.Value ?? _tokenizer.GetText(token)) { Start = token.Start, End = token.End };
                }
                Error($"Unexpected token '{_tokenizer.GetText(token)}'.");
                Advance();
                return new Identifier(string.Empty) { Start = token.Start, End = token.End };
        }
    }

    private Expression ParseImportExpression()
    {
        var token = _current;
        Advance(); // consume 'import'
        if (Check(TokenKind.Dot))
        {
            Advance();
            var prop = ParseIdentifierName();
            return new MetaProperty("import", prop.Name) { Start = token.Start, End = prop.End };
        }
        Expect(TokenKind.OpenParen, "'('");
        var source = ParseAssignment();
        // Optional second argument (import attributes) — parsed and dropped.
        if (Match(TokenKind.Comma) && !Check(TokenKind.CloseParen))
        {
            ParseAssignment();
            Match(TokenKind.Comma);
        }
        var end = _current.End;
        Expect(TokenKind.CloseParen, "')'");
        return new ImportExpression(source) { Start = token.Start, End = end };
    }

    private Expression ParseNew()
    {
        var start = _current.Start;
        Advance(); // 'new'
        if (Check(TokenKind.Dot))
        {
            Advance();
            var prop = ParseIdentifierName();
            return new MetaProperty("new", prop.Name) { Start = start, End = prop.End };
        }

        var callee = ParsePrimary();
        callee = ParseNewCallee(callee);
        if (_options.TypeScript && Check(TokenKind.LessThan))
        {
            TryTakeCallTypeArguments();
        }

        List<Expression> args;
        int end;
        if (Check(TokenKind.OpenParen))
        {
            (args, end) = ParseArguments();
        }
        else
        {
            args = new List<Expression>();
            end = callee.End;
        }

        return new NewExpression(callee, args) { Start = start, End = end };
    }

    // Member accesses bind to `new`, but calls do not.
    private Expression ParseNewCallee(Expression expr)
    {
        while (true)
        {
            if (Check(TokenKind.Dot))
            {
                Advance();
                var property = ParseMemberName();
                expr = new MemberExpression(expr, property, computed: false, optional: false) { Start = expr.Start, End = property.End };
            }
            else if (Check(TokenKind.OpenBracket))
            {
                Advance();
                var index = ParseExpression();
                var end = _current.End;
                Expect(TokenKind.CloseBracket, "']'");
                expr = new MemberExpression(expr, index, computed: true, optional: false) { Start = expr.Start, End = end };
            }
            else
            {
                break;
            }
        }
        return expr;
    }

    private Expression ParseParenthesized()
    {
        var start = _current.Start;
        Expect(TokenKind.OpenParen, "'('");
        var expression = ParseExpression();
        var end = _current.End;
        Expect(TokenKind.CloseParen, "')'");
        return new ParenthesizedExpression(expression) { Start = start, End = end };
    }

    private Expression ParseArrayExpression()
    {
        var start = _current.Start;
        Expect(TokenKind.OpenBracket, "'['");
        var elements = new List<Expression?>();
        while (!Check(TokenKind.CloseBracket) && !Check(TokenKind.EndOfFile))
        {
            if (Check(TokenKind.Comma))
            {
                elements.Add(null); // elision
                Advance();
                continue;
            }
            if (Check(TokenKind.DotDotDot))
            {
                var s = _current.Start;
                Advance();
                var arg = ParseAssignment();
                elements.Add(new SpreadElement(arg) { Start = s, End = arg.End });
            }
            else
            {
                elements.Add(ParseAssignment());
            }
            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        var end = _current.End;
        Expect(TokenKind.CloseBracket, "']'");
        return new ArrayExpression(elements) { Start = start, End = end };
    }

    private Expression ParseObjectExpression()
    {
        var start = _current.Start;
        Expect(TokenKind.OpenBrace, "'{'");
        var properties = new List<Node>();
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            if (Check(TokenKind.DotDotDot))
            {
                var s = _current.Start;
                Advance();
                var arg = ParseAssignment();
                properties.Add(new SpreadElement(arg) { Start = s, End = arg.End });
            }
            else
            {
                properties.Add(ParseObjectMember());
            }
            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        var end = _current.End;
        Expect(TokenKind.CloseBrace, "'}'");
        return new ObjectExpression(properties) { Start = start, End = end };
    }

    private Property ParseObjectMember()
    {
        var start = _current.Start;
        var async = false;
        var generator = false;
        var propertyKind = PropertyKind.Init;

        if ((Check(TokenKind.GetKeyword) || Check(TokenKind.SetKeyword)) && !IsPropertyValueFollows())
        {
            propertyKind = Check(TokenKind.GetKeyword) ? PropertyKind.Get : PropertyKind.Set;
            Advance();
        }
        else if (Check(TokenKind.AsyncKeyword) && !IsPropertyValueFollows() && !_current.PrecededByNewLine)
        {
            async = true;
            Advance();
            if (Check(TokenKind.Asterisk))
            {
                generator = true;
                Advance();
            }
        }
        else if (Check(TokenKind.Asterisk))
        {
            generator = true;
            Advance();
        }

        var key = ParsePropertyKey(out var computed);

        if (Check(TokenKind.OpenParen) || Check(TokenKind.LessThan))
        {
            SkipTypeParameters();
            var (parameters, _) = ParseParameterList();
            SkipReturnType();
            var body = ParseBlock();
            var fn = new FunctionExpression(null, parameters, body, async, generator) { Start = start, End = body.End };
            var methodKind = propertyKind == PropertyKind.Init ? PropertyKind.Init : propertyKind;
            return new Property(key, fn, methodKind, computed, shorthand: false, method: true) { Start = start, End = body.End };
        }

        if (propertyKind is PropertyKind.Get or PropertyKind.Set)
        {
            // `get`/`set` used as a plain shorthand key.
            return new Property(key, key, PropertyKind.Init, computed: false, shorthand: true, method: false) { Start = start, End = key.End };
        }

        if (Match(TokenKind.Colon))
        {
            var value = ParseAssignment();
            return new Property(key, value, PropertyKind.Init, computed, shorthand: false, method: false) { Start = start, End = value.End };
        }

        // Shorthand, optionally with a default value for destructuring patterns.
        Node? shorthandValue = key;
        var end = key.End;
        if (Match(TokenKind.Equals))
        {
            var def = ParseAssignment();
            shorthandValue = def;
            end = def.End;
        }
        return new Property(key, shorthandValue, PropertyKind.Init, computed: false, shorthand: true, method: false) { Start = start, End = end };
    }

    private bool IsPropertyValueFollows()
    {
        var next = Peek().Kind;
        return next is TokenKind.Colon or TokenKind.OpenParen or TokenKind.Comma
            or TokenKind.CloseBrace or TokenKind.Equals or TokenKind.LessThan;
    }

    private Node ParsePropertyKey(out bool computed)
    {
        computed = false;
        if (Check(TokenKind.OpenBracket))
        {
            computed = true;
            Advance();
            var expr = ParseAssignment();
            Expect(TokenKind.CloseBracket, "']'");
            return expr;
        }
        if (Check(TokenKind.StringLiteral))
        {
            return MakeStringLiteralAndAdvance();
        }
        if (Check(TokenKind.NumericLiteral))
        {
            var t = _current;
            Advance();
            return new NumericLiteral(t.Value ?? _tokenizer.GetText(t)) { Start = t.Start, End = t.End };
        }
        if (Check(TokenKind.BigIntLiteral))
        {
            var t = _current;
            Advance();
            return new BigIntLiteral(t.Value ?? _tokenizer.GetText(t)) { Start = t.Start, End = t.End };
        }
        if (Check(TokenKind.PrivateIdentifier))
        {
            var t = _current;
            Advance();
            return new PrivateIdentifier(t.Value ?? "#") { Start = t.Start, End = t.End };
        }
        var token = _current;
        var name = CurrentText();
        Advance();
        return new Identifier(name) { Start = token.Start, End = token.End };
    }

    private Expression ParseFunctionExpression(bool async)
    {
        var start = _current.Start;
        Expect(TokenKind.FunctionKeyword, "'function'");
        var generator = Match(TokenKind.Asterisk);
        Identifier? id = Keywords.IsIdentifierName(_current.Kind) && !Check(TokenKind.OpenParen)
            ? ParseIdentifierName()
            : null;
        SkipTypeParameters();
        var (parameters, _) = ParseParameterList();
        SkipReturnType();
        var body = ParseBlock();
        return new FunctionExpression(id, parameters, body, async, generator) { Start = start, End = body.End };
    }

    private Expression ParseClassExpression()
    {
        var start = _current.Start;
        var (id, superClass, body) = ParseClassTail();
        return new ClassExpression(id, superClass, body) { Start = start, End = body.End };
    }

    private Expression ParseYield()
    {
        var start = _current.Start;
        Advance();
        var delegated = Match(TokenKind.Asterisk);
        Expression? argument = null;
        if (!_current.PrecededByNewLine && CanStartExpression(_current.Kind))
        {
            argument = ParseAssignment();
        }
        return new YieldExpression(argument, delegated) { Start = start, End = argument?.End ?? start };
    }

    private static bool CanStartExpression(TokenKind kind) => kind switch
    {
        TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace
            or TokenKind.Comma or TokenKind.Semicolon or TokenKind.Colon
            or TokenKind.EndOfFile => false,
        _ => true,
    };

    // -- parameters --------------------------------------------------------

    private List<Parameter> ParseParameters()
    {
        var (list, _) = ParseParameterList();
        return list;
    }

    private (List<Parameter> Parameters, int End) ParseParameterList()
    {
        Expect(TokenKind.OpenParen, "'('");
        var list = new List<Parameter>();
        while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
        {
            // Parameter decorators (TS) — parsed and dropped.
            while (Check(TokenKind.At))
            {
                Advance();
                ParseLeftHandSide();
            }
            // Parameter property modifiers (TS): public / private / protected / readonly / override.
            while (_options.TypeScript && IsParameterModifier(_current.Kind))
            {
                Advance();
            }

            var start = _current.Start;
            var rest = Match(TokenKind.DotDotDot);
            var pattern = ParseBindingTarget();
            Match(TokenKind.Question); // optional parameter (TS)
            SkipTypeAnnotation();
            Expression? initializer = null;
            if (Match(TokenKind.Equals))
            {
                initializer = ParseAssignment();
            }
            list.Add(new Parameter(pattern, initializer, rest) { Start = start, End = initializer?.End ?? pattern.End });

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        var end = _current.End;
        Expect(TokenKind.CloseParen, "')'");
        return (list, end);
    }

    private bool IsParameterModifier(TokenKind kind)
    {
        if (kind is not (TokenKind.PublicKeyword or TokenKind.PrivateKeyword
            or TokenKind.ProtectedKeyword or TokenKind.ReadonlyKeyword or TokenKind.OverrideKeyword))
        {
            return false;
        }
        // Only a modifier if another binding follows.
        var next = Peek().Kind;
        return next is not (TokenKind.Comma or TokenKind.CloseParen or TokenKind.Colon
            or TokenKind.Question or TokenKind.Equals);
    }

    // -- templates ---------------------------------------------------------

    private TemplateLiteral ParseTemplateLiteral()
    {
        var start = _current.Start;
        var quasis = new List<TemplateElement>();
        var expressions = new List<Expression>();

        var head = _current;
        if (head.Kind == TokenKind.NoSubstitutionTemplate)
        {
            Advance();
            quasis.Add(new TemplateElement(head.Value ?? string.Empty, _tokenizer.GetText(head), tail: true) { Start = head.Start, End = head.End });
            return new TemplateLiteral(quasis, expressions) { Start = start, End = head.End };
        }

        // TemplateHead
        quasis.Add(new TemplateElement(head.Value ?? string.Empty, string.Empty, tail: false) { Start = head.Start, End = head.End });
        Advance(); // load first token of the first substitution

        while (true)
        {
            var expr = ParseExpression();
            expressions.Add(expr);

            if (!Check(TokenKind.CloseBrace))
            {
                Error("Expected '}' in template literal.");
                break;
            }

            var continuation = _tokenizer.ReScanTemplateContinuationAt(_current);
            var tail = continuation.Kind == TokenKind.TemplateTail;
            quasis.Add(new TemplateElement(continuation.Value ?? string.Empty, string.Empty, tail) { Start = continuation.Start, End = continuation.End });
            _current = continuation;
            Advance(); // load the token following this template chunk

            if (tail)
            {
                break;
            }
        }

        return new TemplateLiteral(quasis, expressions) { Start = start, End = quasis[^1].End };
    }

    // -- arrow detection ---------------------------------------------------

    private (bool IsArrow, bool Async) DetectArrow()
    {
        var kind = _current.Kind;

        if (kind == TokenKind.AsyncKeyword)
        {
            var next = Peek();
            if (next.PrecededByNewLine)
            {
                return (false, false);
            }
            if (next.Kind == TokenKind.OpenParen)
            {
                return (ScanParenIsArrow(afterAsync: true), true);
            }
            if (Keywords.IsIdentifierName(next.Kind) && next.Kind != TokenKind.AsyncKeyword)
            {
                return (ScanAsyncIdentifierIsArrow(), true);
            }
            return (false, false);
        }

        if (kind == TokenKind.OpenParen)
        {
            return (ScanParenIsArrow(afterAsync: false), false);
        }

        if (Keywords.IsIdentifierName(kind) && Peek().Kind == TokenKind.Arrow)
        {
            return (true, false);
        }

        return (false, false);
    }

    private bool ScanAsyncIdentifierIsArrow()
    {
        var snapshot = _tokenizer.Snapshot();
        var current = _current;
        Advance(); // async
        Advance(); // identifier
        var result = _current.Kind == TokenKind.Arrow;
        _tokenizer.Reset(snapshot.Position, snapshot.Line, snapshot.LineStart, snapshot.PreviousKind);
        _current = current;
        return result;
    }

    private bool ScanParenIsArrow(bool afterAsync)
    {
        var snapshot = _tokenizer.Snapshot();
        var current = _current;
        var result = false;

        if (afterAsync)
        {
            Advance(); // consume async -> current should be '('
        }

        if (_current.Kind == TokenKind.OpenParen)
        {
            var depth = 0;
            do
            {
                if (_current.Kind == TokenKind.OpenParen) depth++;
                else if (_current.Kind == TokenKind.CloseParen) depth--;
                else if (_current.Kind == TokenKind.EndOfFile) break;
                Advance();
            }
            while (depth > 0);

            // After ')', a `=>` marks the parenthesized list as arrow parameters.
            // A `:` can introduce a TypeScript return type (`(x): T => y`) — but
            // ONLY when a `=>` actually follows the type. Otherwise the `:` belongs
            // to an enclosing ternary (`cond ? (x) : y`), which must not be treated
            // as an arrow.
            if (_current.Kind == TokenKind.Arrow)
            {
                result = true;
            }
            else if (_current.Kind == TokenKind.Colon && _options.TypeScript)
            {
                result = ScanReturnTypeArrow();
            }
        }

        _tokenizer.Reset(snapshot.Position, snapshot.Line, snapshot.LineStart, snapshot.PreviousKind);
        _current = current;
        return result;
    }

    /// <summary>
    /// With <c>_current</c> at the <c>:</c> following an arrow's parameter list,
    /// scans the (TypeScript) return type and reports whether a depth-0 <c>=&gt;</c>
    /// follows it. This disambiguates <c>(x): T =&gt; y</c> (arrow) from
    /// <c>cond ? (x) : y</c> (ternary). Runs inside the caller's tokenizer
    /// snapshot, so its scanning is rolled back afterwards.
    /// </summary>
    private bool ScanReturnTypeArrow()
    {
        Advance(); // consume ':'
        var depth = 0;

        for (var guard = 0; guard < 2048; guard++)
        {
            switch (_current.Kind)
            {
                case TokenKind.EndOfFile:
                    return false;
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                case TokenKind.OpenBrace:
                case TokenKind.LessThan:
                    depth++;
                    break;
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                case TokenKind.CloseBrace:
                case TokenKind.GreaterThan:
                    if (depth == 0) return false; // exited the enclosing context
                    depth--;
                    break;
                case TokenKind.Arrow:
                    if (depth == 0) return true;
                    break;
                case TokenKind.Semicolon:
                case TokenKind.Comma:
                case TokenKind.Colon:
                case TokenKind.Question:
                    if (depth == 0) return false; // ternary / statement terminator
                    break;
            }

            Advance();
        }

        return false;
    }

    private Expression ParseArrowFunction(bool async)
    {
        var start = _current.Start;
        if (async)
        {
            Advance(); // async
        }

        List<Parameter> parameters;
        if (Check(TokenKind.OpenParen))
        {
            SkipTypeParameters();
            parameters = ParseParameters();
        }
        else
        {
            var id = ParseIdentifierName();
            parameters = new List<Parameter> { new Parameter(id, null, rest: false) { Start = id.Start, End = id.End } };
        }

        SkipReturnType(); // `: T` before `=>`
        Expect(TokenKind.Arrow, "'=>'");

        Node body = Check(TokenKind.OpenBrace) ? ParseBlock() : ParseAssignment();
        return new ArrowFunctionExpression(parameters, body, async) { Start = start, End = body.End };
    }
}
