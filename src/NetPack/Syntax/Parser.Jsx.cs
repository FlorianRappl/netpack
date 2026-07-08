namespace NetPack.Syntax;

using System.Collections.Generic;
using NetPack.Syntax.Ast;

/// <summary>
/// JSX parsing. The tokenizer exposes mode-specific scans (JSX text and JSX
/// identifiers with hyphens); the parser drives them by repositioning the
/// scanner at grammar boundaries. This covers elements, fragments, member and
/// namespaced element names, attributes (including spreads and expression
/// containers) and children.
/// </summary>
public sealed partial class Parser
{
    private Expression ParseJsxElementOrFragment()
    {
        var start = _current.Start;
        Advance(); // consume '<' (scanner is now positioned just past it)

        // Fragment: <> ... </>
        if (Check(TokenKind.GreaterThan))
        {
            Advance(); // '>'
            var fragChildren = ParseJsxChildren();
            // Closing </>
            Expect(TokenKind.LessThan, "'<'");
            Expect(TokenKind.Slash, "'/'");
            var end = _current.End;
            Expect(TokenKind.GreaterThan, "'>'");
            return new JsxFragment(fragChildren) { Start = start, End = end };
        }

        var name = ParseJsxName();
        SkipTypeArguments(); // <Generic<T> ...>
        var attributes = ParseJsxAttributes();

        if (Check(TokenKind.Slash))
        {
            Advance();
            var end = _current.End;
            Expect(TokenKind.GreaterThan, "'>'");
            var selfClosing = new JsxOpeningElement(name, attributes, selfClosing: true) { Start = start, End = end };
            return new JsxElement(selfClosing, new List<Node>(), null) { Start = start, End = end };
        }

        Expect(TokenKind.GreaterThan, "'>'");
        var opening = new JsxOpeningElement(name, attributes, selfClosing: false) { Start = start, End = _current.Start };
        var children = ParseJsxChildren();
        var closing = ParseJsxClosingElement();
        return new JsxElement(opening, children, closing) { Start = start, End = closing.End };
    }

    private JsxName ParseJsxName()
    {
        var first = ReadJsxIdentifier();
        JsxName node = first;

        if (Check(TokenKind.Colon))
        {
            Advance();
            var second = ReadJsxIdentifier();
            return new JsxNamespacedName(first, second) { Start = first.Start, End = second.End };
        }

        while (Check(TokenKind.Dot))
        {
            Advance();
            var property = ReadJsxIdentifier();
            node = new JsxMemberExpression(node, property) { Start = node.Start, End = property.End };
        }

        return node;
    }

    /// <summary>
    /// Reads a single JSX identifier (allowing hyphens) at the current token
    /// position and advances to the token that follows it.
    /// </summary>
    private JsxIdentifier ReadJsxIdentifier()
    {
        _tokenizer.RepositionTo(_current);
        var token = _tokenizer.ScanJsxIdentifier();
        _current = _tokenizer.Next();
        return new JsxIdentifier(token.Value ?? string.Empty) { Start = token.Start, End = token.End };
    }

    private List<Node> ParseJsxAttributes()
    {
        var attributes = new List<Node>();
        while (!Check(TokenKind.GreaterThan) && !Check(TokenKind.Slash) && !Check(TokenKind.EndOfFile))
        {
            if (Check(TokenKind.OpenBrace))
            {
                var s = _current.Start;
                Advance();
                Expect(TokenKind.DotDotDot, "'...'");
                var expr = ParseAssignment();
                var end = _current.End;
                Expect(TokenKind.CloseBrace, "'}'");
                attributes.Add(new JsxSpreadAttribute(expr) { Start = s, End = end });
                continue;
            }

            var name = ParseJsxName();
            Node? value = null;
            if (Check(TokenKind.Equals))
            {
                Advance();
                if (Check(TokenKind.StringLiteral))
                {
                    value = MakeStringLiteralAndAdvance();
                }
                else if (Check(TokenKind.OpenBrace))
                {
                    Advance();
                    var expr = ParseAssignment();
                    Expect(TokenKind.CloseBrace, "'}'");
                    value = new JsxExpressionContainer(expr) { Start = name.Start, End = expr.End };
                }
                else if (Check(TokenKind.LessThan))
                {
                    value = ParseJsxElementOrFragment();
                }
            }
            attributes.Add(new JsxAttribute(name, value) { Start = name.Start, End = value?.End ?? name.End });
        }
        return attributes;
    }

    private List<Node> ParseJsxChildren()
    {
        var children = new List<Node>();
        while (true)
        {
            // Scan the run of text up to the next '<' or '{'.
            _tokenizer.RepositionTo(_current);
            var text = _tokenizer.ScanJsxText();
            if (!string.IsNullOrWhiteSpace(text.Value))
            {
                children.Add(new JsxText(text.Value ?? string.Empty) { Start = text.Start, End = text.End });
            }
            _current = _tokenizer.Next();

            if (Check(TokenKind.EndOfFile))
            {
                break;
            }

            if (Check(TokenKind.LessThan))
            {
                if (Peek().Kind == TokenKind.Slash)
                {
                    break; // closing tag — leave '<' as current
                }
                children.Add(ParseJsxElementOrFragment());
            }
            else if (Check(TokenKind.OpenBrace))
            {
                var s = _current.Start;
                Advance();
                if (Check(TokenKind.CloseBrace))
                {
                    var end0 = _current.End;
                    Advance();
                    children.Add(new JsxExpressionContainer(null) { Start = s, End = end0 });
                }
                else
                {
                    // Spread child `{...items}` or a plain expression container.
                    Match(TokenKind.DotDotDot);
                    var expr = ParseExpression();
                    var end = _current.End;
                    Expect(TokenKind.CloseBrace, "'}'");
                    children.Add(new JsxExpressionContainer(expr) { Start = s, End = end });
                }
            }
            else
            {
                break;
            }
        }
        return children;
    }

    private JsxClosingElement ParseJsxClosingElement()
    {
        var start = _current.Start;
        Expect(TokenKind.LessThan, "'<'");
        Expect(TokenKind.Slash, "'/'");
        var name = ParseJsxName();
        var end = _current.End;
        Expect(TokenKind.GreaterThan, "'>'");
        return new JsxClosingElement(name) { Start = start, End = end };
    }
}
