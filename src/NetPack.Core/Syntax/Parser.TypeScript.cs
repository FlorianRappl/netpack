namespace NetPack.Syntax;

using NetPack.Syntax.Ast;

/// <summary>
/// TypeScript-specific parsing. NetPack erases types rather than modelling them
/// fully: type annotations, parameters' type info, generics, <c>as</c>/
/// <c>satisfies</c> and non-null assertions are consumed and dropped so the
/// resulting AST is plain JavaScript. Whole type-only declarations
/// (<c>interface</c>, <c>type</c>, <c>declare ...</c>) collapse to a
/// <see cref="TypeOnlyDeclaration"/> that the printer skips.
///
/// This uses balanced-token skipping rather than building a type AST, which is
/// sufficient for a bundler that only needs the runtime-relevant JavaScript.
/// A dedicated type parser can replace these helpers later without touching the
/// JS grammar.
/// </summary>
public sealed partial class Parser
{
    /// <summary>Consumes <c>: Type</c> if present (variable / parameter position).</summary>
    private void SkipTypeAnnotation()
    {
        if (_options.TypeScript && Check(TokenKind.Colon))
        {
            Advance();
            SkipType(stopAtBrace: false);
        }
    }

    /// <summary>Consumes a return type annotation <c>: Type</c> before a body,
    /// stopping at the opening brace of the body.</summary>
    private void SkipReturnType()
    {
        if (_options.TypeScript && Check(TokenKind.Colon))
        {
            Advance();
            SkipType(stopAtBrace: true);
        }
    }

    /// <summary>Consumes a <c>&lt;...&gt;</c> type parameter list if present.</summary>
    private void SkipTypeParameters()
    {
        if (_options.TypeScript && Check(TokenKind.LessThan))
        {
            SkipAngleBrackets();
        }
    }

    /// <summary>Consumes a <c>&lt;...&gt;</c> type argument list if present.</summary>
    private void SkipTypeArguments()
    {
        if (_options.TypeScript && Check(TokenKind.LessThan))
        {
            SkipAngleBrackets();
        }
    }

    private void SkipAngleBrackets()
    {
        var depth = 0;
        do
        {
            switch (_current.Kind)
            {
                case TokenKind.LessThan:
                    depth++;
                    break;
                case TokenKind.GreaterThan:
                case TokenKind.GreaterThanEquals:
                    depth -= 1;
                    break;
                case TokenKind.GreaterThanGreaterThan:
                case TokenKind.GreaterThanGreaterThanEquals:
                    depth -= 2;
                    break;
                case TokenKind.GreaterThanGreaterThanGreaterThan:
                case TokenKind.GreaterThanGreaterThanGreaterThanEquals:
                    depth -= 3;
                    break;
                case TokenKind.EndOfFile:
                    return;
            }
            if (depth < 0)
            {
                depth = 0;
            }
            Advance();
        }
        while (depth > 0);
    }

    /// <summary>
    /// Skips a type expression by balancing brackets. Stops at a depth-0
    /// delimiter without consuming it.
    /// </summary>
    private void SkipType(bool stopAtBrace)
    {
        var depth = 0;
        while (!Check(TokenKind.EndOfFile))
        {
            var kind = _current.Kind;

            if (depth == 0)
            {
                switch (kind)
                {
                    case TokenKind.Comma:
                    case TokenKind.Semicolon:
                    case TokenKind.CloseParen:
                    case TokenKind.CloseBracket:
                    case TokenKind.CloseBrace:
                    case TokenKind.Equals:
                    case TokenKind.Arrow:
                        return;
                }

                if (kind == TokenKind.OpenBrace && stopAtBrace)
                {
                    return;
                }
            }

            switch (kind)
            {
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
                    if (depth > 0) depth--;
                    break;
            }

            Advance();
        }
    }

    /// <summary>Consumes an <c>as</c> / <c>satisfies</c> type operator suffix.</summary>
    private bool TrySkipAsExpression()
    {
        if (!_options.TypeScript)
        {
            return false;
        }

        if (Check(TokenKind.AsKeyword) || Check(TokenKind.SatisfiesKeyword))
        {
            // `as` may not begin on a new line (restricted production), but the
            // bundler is permissive here.
            Advance();
            if (Check(TokenKind.ConstKeyword))
            {
                Advance(); // `as const`
            }
            else
            {
                SkipType(stopAtBrace: false);
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to consume type arguments that precede a call or tagged
    /// template (<c>foo&lt;T&gt;(x)</c>). Backtracks if the <c>&lt;</c> turns out
    /// to be a relational operator.
    /// </summary>
    private bool TryTakeCallTypeArguments()
    {
        if (!_options.TypeScript || !Check(TokenKind.LessThan))
        {
            return false;
        }

        var snapshot = _tokenizer.Snapshot();
        var current = _current;
        SkipAngleBrackets();

        var ok = Check(TokenKind.OpenParen)
            || Check(TokenKind.NoSubstitutionTemplate)
            || Check(TokenKind.TemplateHead);

        if (!ok)
        {
            _tokenizer.Reset(snapshot.Position, snapshot.Line, snapshot.LineStart, snapshot.PreviousKind);
            _current = current;
            return false;
        }

        return true;
    }

    private void SkipBalancedBraces()
    {
        if (!Check(TokenKind.OpenBrace))
        {
            return;
        }
        var depth = 0;
        do
        {
            if (Check(TokenKind.OpenBrace)) depth++;
            else if (Check(TokenKind.CloseBrace)) depth--;
            else if (Check(TokenKind.EndOfFile)) return;
            Advance();
        }
        while (depth > 0);
    }

    /// <summary>
    /// Consumes tokens up to and including the natural end of a declaration —
    /// a top-level <c>;</c> or the closing brace of a top-level <c>{ }</c> body.
    /// Used to erase <c>declare</c>d entities and other type-only declarations.
    /// </summary>
    private void SkipStatementLike()
    {
        var depth = 0;
        while (!Check(TokenKind.EndOfFile))
        {
            var kind = _current.Kind;

            if (kind is TokenKind.OpenBrace or TokenKind.OpenParen or TokenKind.OpenBracket)
            {
                depth++;
                Advance();
                continue;
            }

            if (kind is TokenKind.CloseBrace or TokenKind.CloseParen or TokenKind.CloseBracket)
            {
                if (depth == 0)
                {
                    return; // don't consume an enclosing closer
                }
                depth--;
                Advance();
                if (depth == 0 && kind == TokenKind.CloseBrace)
                {
                    return; // end of a block body
                }
                continue;
            }

            if (depth == 0 && kind == TokenKind.Semicolon)
            {
                Advance();
                return;
            }

            Advance();
        }
    }

    /// <summary>
    /// Skips a <c>type X = ...</c> alias body. Balances brackets and stops at a
    /// top-level <c>;</c> (consumed) or an ASI boundary (a newline before a
    /// token that cannot continue a type).
    /// </summary>
    private void SkipTypeAliasBody()
    {
        var depth = 0;
        var consumedAny = false;
        while (!Check(TokenKind.EndOfFile))
        {
            var kind = _current.Kind;

            if (depth == 0)
            {
                if (kind == TokenKind.Semicolon)
                {
                    Advance();
                    return;
                }
                if (consumedAny && _current.PrecededByNewLine && !CanContinueType(kind))
                {
                    return;
                }
            }

            switch (kind)
            {
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
                    if (depth > 0) depth--;
                    break;
            }

            Advance();
            consumedAny = true;
        }
    }

    private static bool CanContinueType(TokenKind kind) => kind switch
    {
        TokenKind.Bar or TokenKind.Ampersand or TokenKind.Dot or TokenKind.LessThan
            or TokenKind.OpenBracket or TokenKind.Question or TokenKind.Arrow
            or TokenKind.ExtendsKeyword or TokenKind.KeyOfKeyword or TokenKind.TypeOfKeyword
            or TokenKind.InferKeyword or TokenKind.Colon => true,
        _ => false,
    };

    // -- TypeScript statement dispatch ------------------------------------

    private Statement ParseTypeScriptOrExpressionStatement()
    {
        if (!_options.TypeScript)
        {
            return ParseExpressionStatement();
        }

        switch (_current.Kind)
        {
            case TokenKind.AbstractKeyword:
                if (Peek().Kind == TokenKind.ClassKeyword)
                {
                    Advance(); // abstract
                    return ParseClassDeclaration();
                }
                return ParseExpressionStatement();

            case TokenKind.DeclareKeyword:
            {
                var start = _current.Start;
                Advance();
                SkipStatementLike();
                return new TypeOnlyDeclaration(NodeKind.TypeAliasDeclaration, null) { Start = start, End = _current.Start };
            }

            case TokenKind.InterfaceKeyword:
                if (Keywords.IsIdentifierName(Peek().Kind))
                {
                    var start = _current.Start;
                    SkipStatementLike();
                    return new TypeOnlyDeclaration(NodeKind.InterfaceDeclaration, null) { Start = start, End = _current.Start };
                }
                return ParseExpressionStatement();

            case TokenKind.TypeKeyword:
            {
                var next = Peek();
                if (Keywords.IsIdentifierName(next.Kind))
                {
                    var start = _current.Start;
                    Advance(); // type
                    var name = CurrentText();
                    Advance(); // name
                    SkipTypeParameters();
                    if (Match(TokenKind.Equals))
                    {
                        SkipTypeAliasBody();
                    }
                    return new TypeOnlyDeclaration(NodeKind.TypeAliasDeclaration, name) { Start = start, End = _current.Start };
                }
                return ParseExpressionStatement();
            }

            case TokenKind.EnumKeyword:
                if (Keywords.IsIdentifierName(Peek().Kind))
                {
                    return ParseEnum(isConst: false);
                }
                return ParseExpressionStatement();

            case TokenKind.NamespaceKeyword:
            case TokenKind.ModuleKeyword:
                if (Keywords.IsIdentifierName(Peek().Kind) || Peek().Kind == TokenKind.StringLiteral)
                {
                    var start = _current.Start;
                    SkipStatementLike();
                    return new TypeOnlyDeclaration(NodeKind.TypeAliasDeclaration, null) { Start = start, End = _current.Start };
                }
                return ParseExpressionStatement();

            default:
                return ParseExpressionStatement();
        }
    }
}
