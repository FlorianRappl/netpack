namespace NetPack.Syntax;

using System.Collections.Generic;
using NetPack.Syntax.Ast;

public sealed partial class Parser
{
    private StringLiteral MakeStringLiteral(in Token token)
        => new(token.Value ?? string.Empty, _tokenizer.GetText(token)) { Start = token.Start, End = token.End };

    private StringLiteral ExpectStringLiteral()
    {
        if (Check(TokenKind.StringLiteral))
        {
            var token = _current;
            Advance();
            return MakeStringLiteral(token);
        }
        Error("Expected a string literal module specifier.");
        return new StringLiteral(string.Empty, "\"\"") { Start = _current.Start, End = _current.Start };
    }

    // -- imports -----------------------------------------------------------

    private Statement ParseImportOrDynamic()
    {
        // `import(...)` (dynamic) and `import.meta` are expressions.
        var next = Peek();
        if (next.Kind == TokenKind.OpenParen || next.Kind == TokenKind.Dot)
        {
            return ParseExpressionStatement();
        }

        var start = _current.Start;
        Advance(); // consume 'import'

        var typeOnly = false;
        // `import type ...` but not `import type from '...'` (where `type` is the name).
        if (Check(TokenKind.TypeKeyword) && _options.TypeScript)
        {
            var after = Peek();
            if (after.Kind != TokenKind.FromKeyword && after.Kind != TokenKind.Comma)
            {
                typeOnly = true;
                Advance();
            }
        }

        // `import 'side-effect';`
        if (Check(TokenKind.StringLiteral))
        {
            var src0 = ExpectStringLiteral();
            SkipImportAttributes();
            ConsumeSemicolon();
            return new ImportDeclaration(new List<ImportSpecifierBase>(), src0, typeOnly) { Start = start, End = src0.End };
        }

        var specifiers = new List<ImportSpecifierBase>();

        // Default import.
        if (Keywords.IsIdentifierName(_current.Kind))
        {
            var local = ParseIdentifierName();
            specifiers.Add(new ImportDefaultSpecifier(local) { Start = local.Start, End = local.End });
            Match(TokenKind.Comma);
        }

        if (Check(TokenKind.Asterisk))
        {
            Advance();
            Expect(TokenKind.AsKeyword, "'as'");
            var local = ParseBindingIdentifier();
            specifiers.Add(new ImportNamespaceSpecifier(local) { Start = local.Start, End = local.End });
        }
        else if (Check(TokenKind.OpenBrace))
        {
            ParseNamedImports(specifiers);
        }

        var src = new StringLiteral(string.Empty, "\"\"");
        if (Match(TokenKind.FromKeyword))
        {
            src = ExpectStringLiteral();
        }
        SkipImportAttributes();
        ConsumeSemicolon();
        return new ImportDeclaration(specifiers, src, typeOnly) { Start = start, End = src.End };
    }

    private void ParseNamedImports(List<ImportSpecifierBase> specifiers)
    {
        Expect(TokenKind.OpenBrace, "'{'");
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            var specStart = _current.Start;
            var memberTypeOnly = false;
            if (Check(TokenKind.TypeKeyword) && _options.TypeScript && Peek().Kind != TokenKind.Comma && Peek().Kind != TokenKind.CloseBrace && Peek().Kind != TokenKind.AsKeyword)
            {
                memberTypeOnly = true;
                Advance();
            }

            // Imported name: identifier or string literal.
            Node imported = Check(TokenKind.StringLiteral)
                ? (Node)MakeStringLiteralAndAdvance()
                : ParseModuleExportName();

            Identifier local;
            if (Match(TokenKind.AsKeyword))
            {
                local = ParseBindingIdentifier();
            }
            else if (imported is Identifier ident)
            {
                local = ident;
            }
            else
            {
                Error("A string-named import requires an 'as' binding.");
                local = new Identifier("_");
            }

            specifiers.Add(new ImportSpecifier(imported, local, memberTypeOnly) { Start = specStart, End = local.End });
            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        Expect(TokenKind.CloseBrace, "'}'");
    }

    private StringLiteral MakeStringLiteralAndAdvance()
    {
        var token = _current;
        Advance();
        return MakeStringLiteral(token);
    }

    /// <summary>An import/export member name: an identifier name or a string.</summary>
    private Node ParseModuleExportName()
    {
        if (Check(TokenKind.StringLiteral))
        {
            return MakeStringLiteralAndAdvance();
        }
        return ParseIdentifierName();
    }

    // -- exports -----------------------------------------------------------

    private Statement ParseExport() => ParseExport(System.Array.Empty<Decorator>());

    private Statement ParseExport(IList<Decorator> decorators)
    {
        var start = _current.Start;
        Advance(); // consume 'export'

        // export default ...
        if (Match(TokenKind.DefaultKeyword))
        {
            Node decl;
            if (Check(TokenKind.FunctionKeyword))
            {
                decl = ParseFunctionDeclaration(async: false);
            }
            else if (Check(TokenKind.AsyncKeyword) && Peek().Kind == TokenKind.FunctionKeyword)
            {
                Advance();
                decl = ParseFunctionDeclaration(async: true);
            }
            else if (Check(TokenKind.ClassKeyword))
            {
                decl = ParseClassDeclaration(decorators);
            }
            else
            {
                var expr = ParseAssignment();
                ConsumeSemicolon();
                decl = expr;
            }
            return new ExportDefaultDeclaration(decl) { Start = start, End = decl.End };
        }

        // export * [as ns] from '...'
        if (Check(TokenKind.Asterisk))
        {
            Advance();
            Identifier? exported = null;
            if (Match(TokenKind.AsKeyword))
            {
                exported = ParseIdentifierName();
            }
            Expect(TokenKind.FromKeyword, "'from'");
            var source = ExpectStringLiteral();
            SkipImportAttributes();
            ConsumeSemicolon();
            return new ExportAllDeclaration(source, exported) { Start = start, End = source.End };
        }

        // export type ... (TS) — could be `export type {…}` or `export type X = …`.
        var exportTypeOnly = false;
        if (Check(TokenKind.TypeKeyword) && _options.TypeScript && (Peek().Kind == TokenKind.OpenBrace || Peek().Kind == TokenKind.Asterisk))
        {
            exportTypeOnly = true;
            Advance();
            if (Check(TokenKind.Asterisk))
            {
                Advance();
                Identifier? exported = null;
                if (Match(TokenKind.AsKeyword)) exported = ParseIdentifierName();
                Expect(TokenKind.FromKeyword, "'from'");
                var source = ExpectStringLiteral();
                ConsumeSemicolon();
                return new ExportAllDeclaration(source, exported) { Start = start, End = source.End };
            }
        }

        // export { ... } [from '...']
        if (Check(TokenKind.OpenBrace))
        {
            var specifiers = ParseNamedExports();
            StringLiteral? source = null;
            if (Match(TokenKind.FromKeyword))
            {
                source = ExpectStringLiteral();
                SkipImportAttributes();
            }
            ConsumeSemicolon();
            return new ExportNamedDeclaration(null, specifiers, source, exportTypeOnly) { Start = start, End = source?.End ?? _current.Start };
        }

        // export <declaration>
        var declaration = ParseExportedDeclaration(decorators);
        return new ExportNamedDeclaration(declaration, new List<ExportSpecifier>(), null, false)
        {
            Start = start,
            End = declaration?.End ?? _current.Start,
        };
    }

    private Statement? ParseExportedDeclaration(IList<Decorator> decorators)
    {
        switch (_current.Kind)
        {
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
            case TokenKind.AsyncKeyword:
                Advance();
                return ParseFunctionDeclaration(async: true);
            case TokenKind.AbstractKeyword when Peek().Kind == TokenKind.ClassKeyword:
                Advance();
                return ParseClassDeclaration(decorators);
            case TokenKind.ClassKeyword:
                return ParseClassDeclaration(decorators);
            case TokenKind.InterfaceKeyword:
            case TokenKind.TypeKeyword:
            case TokenKind.EnumKeyword:
            case TokenKind.NamespaceKeyword:
            case TokenKind.ModuleKeyword:
            case TokenKind.DeclareKeyword:
                return ParseTypeScriptOrExpressionStatement();
            default:
                Error("Unexpected token after 'export'.");
                return ParseExpressionStatement();
        }
    }

    private List<ExportSpecifier> ParseNamedExports()
    {
        Expect(TokenKind.OpenBrace, "'{'");
        var specifiers = new List<ExportSpecifier>();
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            var specStart = _current.Start;
            var memberTypeOnly = false;
            if (Check(TokenKind.TypeKeyword) && _options.TypeScript && Peek().Kind != TokenKind.Comma && Peek().Kind != TokenKind.CloseBrace && Peek().Kind != TokenKind.AsKeyword)
            {
                memberTypeOnly = true;
                Advance();
            }

            var local = ParseModuleExportName();
            var exported = local;
            if (Match(TokenKind.AsKeyword))
            {
                exported = ParseModuleExportName();
            }
            specifiers.Add(new ExportSpecifier(local, exported, memberTypeOnly) { Start = specStart, End = exported.End });
            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        Expect(TokenKind.CloseBrace, "'}'");
        return specifiers;
    }

    /// <summary>Skips an import/export assertion or attributes clause
    /// (<c>assert { type: 'json' }</c> / <c>with { type: 'json' }</c>).</summary>
    private void SkipImportAttributes()
    {
        if ((Check(TokenKind.AssertKeyword) || Check(TokenKind.WithKeyword)) && !_current.PrecededByNewLine)
        {
            Advance();
            if (Check(TokenKind.OpenBrace))
            {
                SkipBalancedBraces();
            }
        }
    }
}
