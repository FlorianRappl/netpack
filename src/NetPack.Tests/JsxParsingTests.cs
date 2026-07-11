namespace NetPack.Tests;

using NetPack.Syntax;
using Xunit;

/// <summary>
/// Guards JSX parsing — in particular that a closing tag <c>&lt;/name&gt;</c> is
/// recognised. The <c>/</c> right after <c>&lt;</c> must not be scanned as a regex
/// literal, which would swallow the closing tag and every statement after the
/// element.
/// </summary>
public class JsxParsingTests
{
    private static SourceFile Parse(string source)
        => Parser.ParseModule(source, "in.jsx", new ParserOptions { Jsx = true, Tolerant = true });

    [Theory]
    [InlineData("const a = <div>hello</div>;\nconsole.log('after');")]
    [InlineData("const a = <React.Suspense>x</React.Suspense>;\nconsole.log('after');")]
    [InlineData("const a = <React.Suspense fallback={<b>Loading ...</b>}>x</React.Suspense>;\nconsole.log('after');")]
    [InlineData("const a = <ul><li>one</li><li>two</li></ul>;\nconsole.log('after');")]
    [InlineData("const App = () => { return (<div>hi</div>); };\nrender(<App />, root);\nconsole.log('after');")]
    public void Closing_tags_do_not_swallow_following_statements(string source)
    {
        var module = Parse(source);

        Assert.Empty(module.Diagnostics);
        // The trailing statement(s) must survive as siblings of the JSX, not be
        // absorbed into the element's children.
        Assert.True(module.Body.Count >= 2, $"expected the trailing statement to remain top-level; got {module.Body.Count} statement(s)");
    }

    [Fact]
    public void Member_named_closing_tag_parses()
    {
        var module = Parse("const a = <React.Fragment>x</React.Fragment>;");
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Division_after_less_than_with_space_is_still_regex()
    {
        // `a < /re/` (with a space) is a comparison against a regex, not a JSX
        // closing tag — the JSX heuristic must not hijack it.
        var module = Parse("var m = a < /re/.source.length;");
        Assert.Empty(module.Diagnostics);
    }
}
