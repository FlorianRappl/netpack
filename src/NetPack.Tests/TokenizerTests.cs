namespace NetPack.Tests;

using System.Linq;
using NetPack.Syntax;
using Xunit;

public class TokenizerTests
{
    private static TokenKind[] Kinds(string source)
        => new Tokenizer(source).Tokenize().Select(t => t.Kind).ToArray();

    private static Token[] Significant(string source)
        => new Tokenizer(source).Tokenize().Where(t => t.Kind != TokenKind.EndOfFile).ToArray();

    [Fact]
    public void Tokenizes_simple_punctuators()
    {
        Assert.Equal(
            new[] { TokenKind.OpenParen, TokenKind.CloseParen, TokenKind.OpenBrace, TokenKind.CloseBrace, TokenKind.Semicolon, TokenKind.EndOfFile },
            Kinds("(){};"));
    }

    [Theory]
    [InlineData("=>", TokenKind.Arrow)]
    [InlineData("...", TokenKind.DotDotDot)]
    [InlineData("?.", TokenKind.QuestionDot)]
    [InlineData("??", TokenKind.QuestionQuestion)]
    [InlineData("??=", TokenKind.QuestionQuestionEquals)]
    [InlineData("**", TokenKind.AsteriskAsterisk)]
    [InlineData("**=", TokenKind.AsteriskAsteriskEquals)]
    [InlineData(">>>", TokenKind.GreaterThanGreaterThanGreaterThan)]
    [InlineData(">>>=", TokenKind.GreaterThanGreaterThanGreaterThanEquals)]
    [InlineData("===", TokenKind.EqualsEqualsEquals)]
    [InlineData("&&=", TokenKind.AmpersandAmpersandEquals)]
    public void Tokenizes_multi_char_operators(string source, TokenKind expected)
    {
        Assert.Equal(expected, Significant(source).Single().Kind);
    }

    [Fact]
    public void Distinguishes_keywords_from_identifiers()
    {
        var tokens = Significant("const x = function");
        Assert.Equal(TokenKind.ConstKeyword, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal(TokenKind.Equals, tokens[2].Kind);
        Assert.Equal(TokenKind.FunctionKeyword, tokens[3].Kind);
    }

    [Fact]
    public void Recognizes_typescript_contextual_keywords()
    {
        var tokens = Significant("type interface enum satisfies as readonly");
        Assert.Equal(
            new[] { TokenKind.TypeKeyword, TokenKind.InterfaceKeyword, TokenKind.EnumKeyword, TokenKind.SatisfiesKeyword, TokenKind.AsKeyword, TokenKind.ReadonlyKeyword },
            tokens.Select(t => t.Kind).ToArray());
    }

    [Theory]
    [InlineData("0x1F")]
    [InlineData("0o17")]
    [InlineData("0b1010")]
    [InlineData("1_000_000")]
    [InlineData("3.14")]
    [InlineData(".5")]
    [InlineData("1e10")]
    [InlineData("2.5e-3")]
    public void Tokenizes_numeric_literals(string source)
    {
        var token = Significant(source).Single();
        Assert.Equal(TokenKind.NumericLiteral, token.Kind);
        Assert.Equal(source, token.Value);
    }

    [Fact]
    public void Tokenizes_bigint_literal()
    {
        var token = Significant("123n").Single();
        Assert.Equal(TokenKind.BigIntLiteral, token.Kind);
    }

    [Fact]
    public void Cooks_string_escapes()
    {
        var token = Significant("\"a\\n\\t\\u0042\"").Single();
        Assert.Equal(TokenKind.StringLiteral, token.Kind);
        Assert.Equal("a\n\tB", token.Value);
    }

    [Fact]
    public void Tokenizes_no_substitution_template()
    {
        var token = Significant("`hello world`").Single();
        Assert.Equal(TokenKind.NoSubstitutionTemplate, token.Kind);
        Assert.Equal("hello world", token.Value);
    }

    [Fact]
    public void Tokenizes_template_head()
    {
        var token = Significant("`a${").First();
        Assert.Equal(TokenKind.TemplateHead, token.Kind);
        Assert.Equal("a", token.Value);
    }

    [Fact]
    public void Treats_slash_as_regex_at_expression_start()
    {
        var token = Significant("/ab+c/gi").First();
        Assert.Equal(TokenKind.RegExpLiteral, token.Kind);
        Assert.Equal("/ab+c/gi", token.Value);
    }

    [Fact]
    public void Treats_slash_as_division_after_identifier()
    {
        var kinds = Significant("a / b").Select(t => t.Kind).ToArray();
        Assert.Equal(new[] { TokenKind.Identifier, TokenKind.Slash, TokenKind.Identifier }, kinds);
    }

    [Fact]
    public void Regex_with_char_class_containing_slash()
    {
        var token = Significant("/[/]/").First();
        Assert.Equal(TokenKind.RegExpLiteral, token.Kind);
        Assert.Equal("/[/]/", token.Value);
    }

    [Fact]
    public void Skips_line_and_block_comments()
    {
        var kinds = Kinds("a // comment\n /* block */ b");
        Assert.Equal(new[] { TokenKind.Identifier, TokenKind.Identifier, TokenKind.EndOfFile }, kinds);
    }

    [Fact]
    public void Marks_newline_before_token()
    {
        var tokens = Significant("a\nb");
        Assert.False(tokens[0].PrecededByNewLine);
        Assert.True(tokens[1].PrecededByNewLine);
    }

    [Fact]
    public void Skips_hashbang()
    {
        var kinds = Kinds("#!/usr/bin/env node\nconst x = 1");
        Assert.Equal(TokenKind.ConstKeyword, kinds[0]);
    }

    [Fact]
    public void Tokenizes_private_identifier()
    {
        var token = Significant("#field").Single();
        Assert.Equal(TokenKind.PrivateIdentifier, token.Kind);
        Assert.Equal("#field", token.Value);
    }

    [Fact]
    public void Records_unterminated_string_diagnostic()
    {
        var tokenizer = new Tokenizer("\"abc");
        tokenizer.Tokenize();
        Assert.NotEmpty(tokenizer.Diagnostics);
    }
}
