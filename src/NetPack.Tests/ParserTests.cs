namespace NetPack.Tests;

using System.Linq;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
using Xunit;

public class ParserTests
{
    private static SourceFile Parse(string source, string file = "test.tsx")
        => Parser.ParseModule(source, file);

    [Fact]
    public void Parses_import_with_default_and_named_specifiers()
    {
        var module = Parse("import a, { b as c } from './x';");
        var import = Assert.IsType<ImportDeclaration>(module.Body[0]);
        Assert.Equal("./x", import.Source.Value);
        Assert.Equal(2, import.Specifiers.Count);
        Assert.IsType<ImportDefaultSpecifier>(import.Specifiers[0]);
        var named = Assert.IsType<ImportSpecifier>(import.Specifiers[1]);
        Assert.Equal("c", named.Local.Name);
        Assert.Equal("b", Assert.IsType<Identifier>(named.Imported).Name);
    }

    [Fact]
    public void Parses_namespace_import()
    {
        var module = Parse("import * as ns from './mod';");
        var import = Assert.IsType<ImportDeclaration>(module.Body[0]);
        Assert.IsType<ImportNamespaceSpecifier>(import.Specifiers[0]);
        Assert.Equal("./mod", import.Source.Value);
    }

    [Fact]
    public void Parses_side_effect_import()
    {
        var module = Parse("import './side-effect.css';");
        var import = Assert.IsType<ImportDeclaration>(module.Body[0]);
        Assert.Empty(import.Specifiers);
        Assert.Equal("./side-effect.css", import.Source.Value);
    }

    [Fact]
    public void Parses_dynamic_import_expression()
    {
        var module = Parse("import('./y');", "test.js");
        var statement = Assert.IsType<ExpressionStatement>(module.Body[0]);
        var dynamic = Assert.IsType<ImportExpression>(statement.Expression);
        Assert.Equal("./y", Assert.IsType<StringLiteral>(dynamic.Source).Value);
    }

    [Fact]
    public void Parses_require_call()
    {
        var module = Parse("const r = require('./z');", "test.js");
        var variable = Assert.IsType<VariableStatement>(module.Body[0]);
        var call = Assert.IsType<CallExpression>(variable.Declarations[0].Init);
        Assert.Equal("require", Assert.IsType<Identifier>(call.Callee).Name);
        Assert.Equal("./z", Assert.IsType<StringLiteral>(call.Arguments[0]).Value);
    }

    [Fact]
    public void Parses_export_named_declaration()
    {
        var module = Parse("export const x = 1;", "test.js");
        var export = Assert.IsType<ExportNamedDeclaration>(module.Body[0]);
        Assert.IsType<VariableStatement>(export.Declaration);
    }

    [Fact]
    public void Parses_export_default_function()
    {
        var module = Parse("export default function f() { return 1; }", "test.js");
        var export = Assert.IsType<ExportDefaultDeclaration>(module.Body[0]);
        Assert.IsType<FunctionDeclaration>(export.Declaration);
    }

    [Fact]
    public void Parses_export_from()
    {
        var module = Parse("export { a, b as c } from './m';", "test.js");
        var export = Assert.IsType<ExportNamedDeclaration>(module.Body[0]);
        Assert.Equal("./m", export.Source!.Value);
        Assert.Equal(2, export.Specifiers.Count);
    }

    [Fact]
    public void Parses_export_all_namespace()
    {
        var module = Parse("export * as ns from './m';", "test.js");
        var export = Assert.IsType<ExportAllDeclaration>(module.Body[0]);
        Assert.Equal("./m", export.Source.Value);
        Assert.Equal("ns", export.Exported!.Name);
    }

    [Fact]
    public void Erases_typescript_type_annotation()
    {
        var module = Parse("const x: number = 1;");
        var variable = Assert.IsType<VariableStatement>(module.Body[0]);
        Assert.Equal("x", Assert.IsType<Identifier>(variable.Declarations[0].Id).Name);
        Assert.IsType<NumericLiteral>(variable.Declarations[0].Init);
    }

    [Fact]
    public void Erases_interface_declaration()
    {
        var module = Parse("interface Foo { a: number; b(): void; }\nconst y = 1;");
        Assert.Equal(NodeKind.InterfaceDeclaration, module.Body[0].Kind);
        Assert.IsType<VariableStatement>(module.Body[1]);
    }

    [Fact]
    public void Erases_type_alias()
    {
        var module = Parse("type T = A | B;\nconst y = 2;");
        var alias = Assert.IsType<TypeOnlyDeclaration>(module.Body[0]);
        Assert.Equal("T", alias.Name);
        Assert.IsType<VariableStatement>(module.Body[1]);
    }

    [Fact]
    public void Erases_as_expression()
    {
        var module = Parse("const x = value as SomeType;");
        var variable = Assert.IsType<VariableStatement>(module.Body[0]);
        Assert.Equal("value", Assert.IsType<Identifier>(variable.Declarations[0].Init).Name);
    }

    [Fact]
    public void Parses_arrow_function()
    {
        var module = Parse("const f = (a, b) => a + b;", "test.js");
        var variable = Assert.IsType<VariableStatement>(module.Body[0]);
        var arrow = Assert.IsType<ArrowFunctionExpression>(variable.Declarations[0].Init);
        Assert.Equal(2, arrow.Parameters.Count);
        Assert.IsType<BinaryExpression>(arrow.Body);
    }

    [Fact]
    public void Parses_optional_chaining_and_calls()
    {
        var module = Parse("a?.b.c?.(1);", "test.js");
        var statement = Assert.IsType<ExpressionStatement>(module.Body[0]);
        Assert.IsType<CallExpression>(statement.Expression);
    }

    [Fact]
    public void Parses_template_literal_with_substitution()
    {
        var module = Parse("const s = `a${x}b`;", "test.js");
        var variable = Assert.IsType<VariableStatement>(module.Body[0]);
        var template = Assert.IsType<TemplateLiteral>(variable.Declarations[0].Init);
        Assert.Equal(2, template.Quasis.Count);
        Assert.Single(template.Expressions);
        Assert.Equal("a", template.Quasis[0].Cooked);
        Assert.Equal("b", template.Quasis[1].Cooked);
    }

    [Fact]
    public void Parses_jsx_element()
    {
        var module = Parse("const el = <div className=\"x\">{y}</div>;", "test.jsx");
        var variable = Assert.IsType<VariableStatement>(module.Body[0]);
        var jsx = Assert.IsType<JsxElement>(variable.Declarations[0].Init);
        Assert.Equal("div", Assert.IsType<JsxIdentifier>(jsx.OpeningElement.Name).Name);
        Assert.Contains(jsx.Children, c => c is JsxExpressionContainer);
    }

    [Fact]
    public void Parses_jsx_self_closing_and_fragment()
    {
        var module = Parse("const el = <><Foo bar={1} /></>;", "test.jsx");
        var variable = Assert.IsType<VariableStatement>(module.Body[0]);
        Assert.IsType<JsxFragment>(variable.Declarations[0].Init);
    }

    [Fact]
    public void Valid_module_produces_no_diagnostics()
    {
        var module = Parse("export const add = (a, b) => a + b;\nconst r = require('./x');", "test.js");
        Assert.Empty(module.Diagnostics);
    }
}
