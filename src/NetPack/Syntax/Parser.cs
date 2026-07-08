namespace NetPack.Syntax;

using System.Collections.Generic;
using NetPack.Syntax.Ast;

/// <summary>
/// A recursive-descent parser that turns a token stream into the NetPack AST.
///
/// This is the first iteration of NetPack's own front-end (replacing Acornima).
/// It covers the module grammar the bundler actually walks — imports, exports,
/// declarations, statements and the full expression grammar — plus pragmatic
/// TypeScript type-erasure and JSX. Areas that a later phase will deepen (full
/// class bodies, exhaustive TS type parsing) are handled by balanced-token
/// skipping and are marked in the code.
///
/// The parser is tolerant by default: recoverable problems are recorded as
/// <see cref="Diagnostic"/> values rather than thrown, matching how the bundler
/// previously used Acornima's tolerant mode.
/// </summary>
public sealed partial class Parser
{
    private readonly Tokenizer _tokenizer;
    private readonly string _source;
    private readonly string _fileName;
    private readonly ParserOptions _options;
    private readonly List<Diagnostic> _diagnostics = new();

    private Token _current;

    public Parser(string source, string fileName, ParserOptions options = default)
    {
        _source = source ?? string.Empty;
        _fileName = fileName ?? "<unknown>";
        _options = options.Equals(default(ParserOptions)) ? ParserOptions.Default : options;
        _tokenizer = new Tokenizer(_source, new TokenizerOptions
        {
            Tolerant = _options.Tolerant,
            AllowHashbang = true,
            Variant = _options.Jsx ? LanguageVariant.Jsx : LanguageVariant.Standard,
        });
        _current = _tokenizer.Next();
    }

    /// <summary>All diagnostics from tokenizing and parsing.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Convenience factory mirroring the old <c>JsxParser.ParseModule</c>.</summary>
    public static SourceFile ParseModule(string source, string fileName, ParserOptions options = default)
        => new Parser(source, fileName, options).ParseModule();

    /// <summary>Parses the whole source as a module.</summary>
    public SourceFile ParseModule()
    {
        var body = new List<Statement>();

        while (!Check(TokenKind.EndOfFile))
        {
            var before = _current.Start;
            var statement = ParseStatement();
            if (statement is not null)
            {
                body.Add(statement);
            }

            // Guard against non-advancing loops on malformed input.
            if (_current.Start == before && !Check(TokenKind.EndOfFile))
            {
                Advance();
            }
        }

        var diagnostics = new List<Diagnostic>(_tokenizer.Diagnostics);
        diagnostics.AddRange(_diagnostics);
        return new SourceFile(_fileName, body, diagnostics)
        {
            Start = 0,
            End = _source.Length,
        };
    }

    // -- token stream helpers ---------------------------------------------

    private bool Check(TokenKind kind) => _current.Kind == kind;

    private Token Advance()
    {
        var token = _current;
        _current = _tokenizer.Next();
        return token;
    }

    private bool Match(TokenKind kind)
    {
        if (_current.Kind == kind)
        {
            Advance();
            return true;
        }
        return false;
    }

    private Token Expect(TokenKind kind, string description)
    {
        if (_current.Kind == kind)
        {
            return Advance();
        }

        Error($"Expected {description}.");
        // Recovery: synthesize an empty token at the current position.
        return new Token(kind, _current.Start, _current.Start, _current.Line, _current.Column, TokenFlags.None, null);
    }

    /// <summary>One-token look-ahead without consuming the current token.</summary>
    private Token Peek()
    {
        var snapshot = _tokenizer.Snapshot();
        var current = _current;
        var next = _tokenizer.Next();
        _tokenizer.Reset(snapshot.Position, snapshot.Line, snapshot.LineStart, snapshot.PreviousKind);
        _current = current;
        return next;
    }

    private void Error(string message)
        => _diagnostics.Add(new Diagnostic(message, _current.Start, _current.Line, _current.Column));

    private void ConsumeSemicolon()
    {
        if (Match(TokenKind.Semicolon))
        {
            return;
        }

        // Automatic semicolon insertion: allowed before `}`, at EOF, or when a
        // line terminator precedes the offending token.
        if (Check(TokenKind.CloseBrace) || Check(TokenKind.EndOfFile) || _current.PrecededByNewLine)
        {
            return;
        }

        Error("Expected a semicolon.");
    }

    private string CurrentText() => _current.Value ?? _tokenizer.GetText(_current);

    private Identifier ParseIdentifierName()
    {
        var token = _current;
        var name = token.Value ?? _tokenizer.GetText(token);
        Advance();
        return new Identifier(name) { Start = token.Start, End = token.End };
    }

    private Identifier ParseBindingIdentifier()
    {
        if (!Keywords.IsIdentifierName(_current.Kind))
        {
            Error("Expected an identifier.");
        }
        return ParseIdentifierName();
    }

    // -- statements --------------------------------------------------------

    private Statement? ParseStatement()
    {
        switch (_current.Kind)
        {
            case TokenKind.Semicolon:
                Advance();
                return new EmptyStatement { Start = _current.Start, End = _current.Start };
            case TokenKind.OpenBrace:
                return ParseBlock();
            case TokenKind.ImportKeyword:
                return ParseImportOrDynamic();
            case TokenKind.ExportKeyword:
                return ParseExport();
            case TokenKind.VarKeyword:
            case TokenKind.LetKeyword:
                return ParseVariableStatement();
            case TokenKind.ConstKeyword:
                if (_options.TypeScript && Peek().Kind == TokenKind.EnumKeyword)
                {
                    return ParseEnum(isConst: true);
                }
                return ParseVariableStatement();
            case TokenKind.FunctionKeyword:
                return ParseFunctionDeclaration(async: false);
            case TokenKind.ClassKeyword:
                return ParseClassDeclaration();
            case TokenKind.ReturnKeyword:
                return ParseReturn();
            case TokenKind.IfKeyword:
                return ParseIf();
            case TokenKind.ForKeyword:
                return ParseFor();
            case TokenKind.WhileKeyword:
                return ParseWhile();
            case TokenKind.DoKeyword:
                return ParseDoWhile();
            case TokenKind.DebuggerKeyword:
            {
                var dbgStart = _current.Start;
                Advance();
                ConsumeSemicolon();
                return new DebuggerStatement { Start = dbgStart, End = _current.Start };
            }
            case TokenKind.At:
                return ParseDecoratedDeclaration();
            case TokenKind.ThrowKeyword:
                return ParseThrow();
            case TokenKind.TryKeyword:
                return ParseTry();
            case TokenKind.SwitchKeyword:
                return ParseSwitch();
            case TokenKind.BreakKeyword:
                return ParseBreakContinue(isBreak: true);
            case TokenKind.ContinueKeyword:
                return ParseBreakContinue(isBreak: false);
            case TokenKind.AsyncKeyword:
                if (Peek().Kind == TokenKind.FunctionKeyword)
                {
                    Advance(); // consume async
                    return ParseFunctionDeclaration(async: true);
                }
                return ParseTypeScriptOrExpressionStatement();
            case TokenKind.InterfaceKeyword:
            case TokenKind.TypeKeyword:
            case TokenKind.EnumKeyword:
            case TokenKind.NamespaceKeyword:
            case TokenKind.ModuleKeyword:
            case TokenKind.DeclareKeyword:
            case TokenKind.AbstractKeyword:
                return ParseTypeScriptOrExpressionStatement();
            default:
                // Labeled statement: `label: statement`.
                if (_current.Kind == TokenKind.Identifier && Peek().Kind == TokenKind.Colon)
                {
                    var labelStart = _current.Start;
                    var label = CurrentText();
                    Advance(); // label
                    Advance(); // ':'
                    var body = ParseStatement() ?? new EmptyStatement();
                    return new LabeledStatement(label, body) { Start = labelStart, End = body.End };
                }
                return ParseExpressionStatement();
        }
    }

    private Statement ParseDecoratedDeclaration()
    {
        var decorators = ParseDecorators();
        if (Check(TokenKind.ExportKeyword))
        {
            return ParseExport(decorators);
        }
        if (Check(TokenKind.AbstractKeyword))
        {
            Advance();
        }
        return ParseClassDeclaration(decorators);
    }

    private BlockStatement ParseBlock()
    {
        var start = _current.Start;
        Expect(TokenKind.OpenBrace, "'{'");
        var body = new List<Statement>();
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            var before = _current.Start;
            var statement = ParseStatement();
            if (statement is not null)
            {
                body.Add(statement);
            }
            if (_current.Start == before && !Check(TokenKind.CloseBrace))
            {
                Advance();
            }
        }
        var end = _current.End;
        Expect(TokenKind.CloseBrace, "'}'");
        return new BlockStatement(body) { Start = start, End = end };
    }

    private VariableStatement ParseVariableStatement()
    {
        var start = _current.Start;
        var kind = _current.Kind switch
        {
            TokenKind.VarKeyword => VariableKind.Var,
            TokenKind.LetKeyword => VariableKind.Let,
            _ => VariableKind.Const,
        };
        Advance();
        var declarators = ParseVariableDeclarators();
        var end = declarators.Count > 0 ? declarators[^1].End : start;
        ConsumeSemicolon();
        return new VariableStatement(kind, declarators) { Start = start, End = end };
    }

    private List<VariableDeclarator> ParseVariableDeclarators()
    {
        var list = new List<VariableDeclarator>();
        do
        {
            var start = _current.Start;
            var id = ParseBindingTarget();
            SkipTypeAnnotation();
            // Definite assignment assertion `let x!: T`.
            Match(TokenKind.Exclamation);
            SkipTypeAnnotation();
            Expression? init = null;
            if (Match(TokenKind.Equals))
            {
                init = ParseAssignment();
            }
            list.Add(new VariableDeclarator(id, init) { Start = start, End = init?.End ?? id.End });
        }
        while (Match(TokenKind.Comma));
        return list;
    }

    private Node ParseBindingTarget()
    {
        if (Check(TokenKind.OpenBrace))
        {
            return ParseObjectExpression();
        }
        if (Check(TokenKind.OpenBracket))
        {
            return ParseArrayExpression();
        }
        return ParseBindingIdentifier();
    }

    private FunctionDeclaration ParseFunctionDeclaration(bool async)
    {
        var start = _current.Start;
        Expect(TokenKind.FunctionKeyword, "'function'");
        var generator = Match(TokenKind.Asterisk);
        Identifier? id = Keywords.IsIdentifierName(_current.Kind) ? ParseIdentifierName() : null;
        SkipTypeParameters();
        var parameters = ParseParameters();
        SkipReturnType();
        var body = ParseBlock();
        return new FunctionDeclaration(id, parameters, body, async, generator) { Start = start, End = body.End };
    }

    private ClassDeclaration ParseClassDeclaration()
        => ParseClassDeclaration(new List<Decorator>());

    private ReturnStatement ParseReturn()
    {
        var start = _current.Start;
        Advance();
        Expression? arg = null;
        if (!Check(TokenKind.Semicolon) && !Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile) && !_current.PrecededByNewLine)
        {
            arg = ParseExpression();
        }
        var end = arg?.End ?? start;
        ConsumeSemicolon();
        return new ReturnStatement(arg) { Start = start, End = end };
    }

    private IfStatement ParseIf()
    {
        var start = _current.Start;
        Advance();
        Expect(TokenKind.OpenParen, "'('");
        var test = ParseExpression();
        Expect(TokenKind.CloseParen, "')'");
        var consequent = ParseStatement() ?? new EmptyStatement();
        Statement? alternate = null;
        if (Match(TokenKind.ElseKeyword))
        {
            alternate = ParseStatement();
        }
        return new IfStatement(test, consequent, alternate) { Start = start, End = (alternate ?? consequent).End };
    }

    private Statement ParseFor()
    {
        var start = _current.Start;
        Advance();
        var isAwait = Match(TokenKind.AwaitKeyword); // for await (x of y)
        Expect(TokenKind.OpenParen, "'('");

        Node? init = null;
        // Disable the `in` operator so `for (x in y)` reads as a for-in head.
        var savedNoIn = _noIn;
        _noIn = true;
        if (!Check(TokenKind.Semicolon))
        {
            if (Check(TokenKind.VarKeyword) || Check(TokenKind.LetKeyword) || Check(TokenKind.ConstKeyword))
            {
                var kind = _current.Kind switch
                {
                    TokenKind.VarKeyword => VariableKind.Var,
                    TokenKind.LetKeyword => VariableKind.Let,
                    _ => VariableKind.Const,
                };
                Advance();
                var decls = ParseVariableDeclarators();
                init = new VariableStatement(kind, decls);
            }
            else
            {
                init = ParseExpression();
            }
        }
        _noIn = savedNoIn;

        if (Check(TokenKind.InKeyword))
        {
            Advance();
            var right = ParseExpression();
            Expect(TokenKind.CloseParen, "')'");
            var b = ParseStatement() ?? new EmptyStatement();
            return new ForInStatement(init!, right, b) { Start = start, End = b.End };
        }

        if (Check(TokenKind.OfKeyword))
        {
            Advance();
            var right = ParseAssignment();
            Expect(TokenKind.CloseParen, "')'");
            var b = ParseStatement() ?? new EmptyStatement();
            return new ForOfStatement(init!, right, b, isAwait) { Start = start, End = b.End };
        }

        Expect(TokenKind.Semicolon, "';'");
        Expression? test = Check(TokenKind.Semicolon) ? null : ParseExpression();
        Expect(TokenKind.Semicolon, "';'");
        Expression? update = Check(TokenKind.CloseParen) ? null : ParseExpression();
        Expect(TokenKind.CloseParen, "')'");
        var body = ParseStatement() ?? new EmptyStatement();
        return new ForStatement(init, test, update, body) { Start = start, End = body.End };
    }

    private DoWhileStatement ParseDoWhile()
    {
        var start = _current.Start;
        Advance(); // do
        var body = ParseStatement() ?? new EmptyStatement();
        Expect(TokenKind.WhileKeyword, "'while'");
        Expect(TokenKind.OpenParen, "'('");
        var test = ParseExpression();
        Expect(TokenKind.CloseParen, "')'");
        Match(TokenKind.Semicolon);
        return new DoWhileStatement(body, test) { Start = start, End = test.End };
    }

    private WhileStatement ParseWhile()
    {
        var start = _current.Start;
        Advance();
        Expect(TokenKind.OpenParen, "'('");
        var test = ParseExpression();
        Expect(TokenKind.CloseParen, "')'");
        var body = ParseStatement() ?? new EmptyStatement();
        return new WhileStatement(test, body) { Start = start, End = body.End };
    }

    private ThrowStatement ParseThrow()
    {
        var start = _current.Start;
        Advance();
        var arg = ParseExpression();
        ConsumeSemicolon();
        return new ThrowStatement(arg) { Start = start, End = arg.End };
    }

    private TryStatement ParseTry()
    {
        var start = _current.Start;
        Advance();
        var block = ParseBlock();
        CatchClause? handler = null;
        if (Match(TokenKind.CatchKeyword))
        {
            Node? param = null;
            if (Match(TokenKind.OpenParen))
            {
                param = ParseBindingTarget();
                SkipTypeAnnotation();
                Expect(TokenKind.CloseParen, "')'");
            }
            var body = ParseBlock();
            handler = new CatchClause(param, body) { Start = body.Start, End = body.End };
        }
        BlockStatement? finalizer = null;
        if (Match(TokenKind.FinallyKeyword))
        {
            finalizer = ParseBlock();
        }
        var end = finalizer?.End ?? handler?.Body.End ?? block.End;
        return new TryStatement(block, handler, finalizer) { Start = start, End = end };
    }

    private SwitchStatement ParseSwitch()
    {
        var start = _current.Start;
        Advance();
        Expect(TokenKind.OpenParen, "'('");
        var discriminant = ParseExpression();
        Expect(TokenKind.CloseParen, "')'");
        Expect(TokenKind.OpenBrace, "'{'");
        var cases = new List<SwitchCase>();
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            Expression? test = null;
            if (Match(TokenKind.CaseKeyword))
            {
                test = ParseExpression();
            }
            else
            {
                Expect(TokenKind.DefaultKeyword, "'case' or 'default'");
            }
            Expect(TokenKind.Colon, "':'");
            var body = new List<Statement>();
            while (!Check(TokenKind.CaseKeyword) && !Check(TokenKind.DefaultKeyword) && !Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
            {
                var s = ParseStatement();
                if (s is not null) body.Add(s);
            }
            cases.Add(new SwitchCase(test, body));
        }
        var end = _current.End;
        Expect(TokenKind.CloseBrace, "'}'");
        return new SwitchStatement(discriminant, cases) { Start = start, End = end };
    }

    private Statement ParseBreakContinue(bool isBreak)
    {
        var start = _current.Start;
        Advance();
        string? label = null;
        if (!_current.PrecededByNewLine && Keywords.IsIdentifierName(_current.Kind) && !Check(TokenKind.Semicolon))
        {
            label = CurrentText();
            Advance();
        }
        ConsumeSemicolon();
        return isBreak
            ? new BreakStatement(label) { Start = start }
            : new ContinueStatement(label) { Start = start };
    }

    private ExpressionStatement ParseExpressionStatement()
    {
        var start = _current.Start;
        var expr = ParseExpression();
        ConsumeSemicolon();
        return new ExpressionStatement(expr) { Start = start, End = expr.End };
    }
}
