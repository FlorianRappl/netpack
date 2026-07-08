namespace NetPack.Syntax.Ast;

using System.Collections.Generic;

public sealed class Identifier : Expression
{
    public Identifier(string name) => Name = name;
    public string Name { get; set; }
    public override NodeKind Kind => NodeKind.Identifier;
}

public sealed class PrivateIdentifier : Expression
{
    public PrivateIdentifier(string name) => Name = name;
    public string Name { get; set; }
    public override NodeKind Kind => NodeKind.PrivateIdentifier;
}

/// <summary>A string literal. <see cref="Value"/> is the cooked value and
/// <see cref="Raw"/> is the original text including quotes.</summary>
public sealed class StringLiteral : Expression
{
    public StringLiteral(string value, string raw)
    {
        Value = value;
        Raw = raw;
    }
    public string Value { get; set; }
    public string Raw { get; set; }
    public override NodeKind Kind => NodeKind.StringLiteral;
}

public sealed class NumericLiteral : Expression
{
    public NumericLiteral(string raw) => Raw = raw;
    public string Raw { get; set; }
    public override NodeKind Kind => NodeKind.NumericLiteral;
}

public sealed class BigIntLiteral : Expression
{
    public BigIntLiteral(string raw) => Raw = raw;
    public string Raw { get; set; }
    public override NodeKind Kind => NodeKind.BigIntLiteral;
}

public sealed class BooleanLiteral : Expression
{
    public BooleanLiteral(bool value) => Value = value;
    public bool Value { get; set; }
    public override NodeKind Kind => NodeKind.BooleanLiteral;
}

public sealed class NullLiteral : Expression
{
    public override NodeKind Kind => NodeKind.NullLiteral;
}

public sealed class RegExpLiteral : Expression
{
    public RegExpLiteral(string raw) => Raw = raw;
    public string Raw { get; set; }
    public override NodeKind Kind => NodeKind.RegExpLiteral;
}

public sealed class ThisExpression : Expression
{
    public override NodeKind Kind => NodeKind.ThisExpression;
}

public sealed class SuperExpression : Expression
{
    public override NodeKind Kind => NodeKind.SuperExpression;
}

/// <summary>A single cooked/raw piece of a template literal.</summary>
public sealed class TemplateElement : Node
{
    public TemplateElement(string cooked, string raw, bool tail)
    {
        Cooked = cooked;
        Raw = raw;
        Tail = tail;
    }
    public string Cooked { get; set; }
    public string Raw { get; set; }
    public bool Tail { get; set; }
    public override NodeKind Kind => NodeKind.TemplateElement;
}

public sealed class TemplateLiteral : Expression
{
    public TemplateLiteral(IList<TemplateElement> quasis, IList<Expression> expressions)
    {
        Quasis = quasis;
        Expressions = expressions;
    }
    public IList<TemplateElement> Quasis { get; set; }
    public IList<Expression> Expressions { get; set; }
    public override NodeKind Kind => NodeKind.TemplateLiteral;
}

public sealed class TaggedTemplateExpression : Expression
{
    public TaggedTemplateExpression(Expression tag, TemplateLiteral quasi)
    {
        Tag = tag;
        Quasi = quasi;
    }
    public Expression Tag { get; set; }
    public TemplateLiteral Quasi { get; set; }
    public override NodeKind Kind => NodeKind.TaggedTemplateExpression;
}

public sealed class ArrayExpression : Expression
{
    public ArrayExpression(IList<Expression?> elements) => Elements = elements;
    /// <summary>Null entries represent elisions (holes) in the array.</summary>
    public IList<Expression?> Elements { get; set; }
    public override NodeKind Kind => NodeKind.ArrayExpression;
}

public enum PropertyKind { Init, Get, Set, Spread }

public sealed class Property : Node
{
    public Property(Node key, Node? value, PropertyKind kind, bool computed, bool shorthand, bool method)
    {
        Key = key;
        Value = value;
        PropertyKind = kind;
        Computed = computed;
        Shorthand = shorthand;
        Method = method;
    }
    public Node Key { get; set; }
    public Node? Value { get; set; }
    public PropertyKind PropertyKind { get; set; }
    public bool Computed { get; set; }
    public bool Shorthand { get; set; }
    public bool Method { get; set; }
    public override NodeKind Kind => NodeKind.Property;
}

public sealed class ObjectExpression : Expression
{
    public ObjectExpression(IList<Node> properties) => Properties = properties;
    /// <summary>Elements are <see cref="Property"/> or <see cref="SpreadElement"/>.</summary>
    public IList<Node> Properties { get; set; }
    public override NodeKind Kind => NodeKind.ObjectExpression;
}

public sealed class SpreadElement : Expression
{
    public SpreadElement(Expression argument) => Argument = argument;
    public Expression Argument { get; set; }
    public override NodeKind Kind => NodeKind.SpreadElement;
}

public sealed class ParenthesizedExpression : Expression
{
    public ParenthesizedExpression(Expression expression) => Expression = expression;
    public Expression Expression { get; set; }
    public override NodeKind Kind => NodeKind.ParenthesizedExpression;
}

public sealed class MemberExpression : Expression
{
    public MemberExpression(Expression obj, Node property, bool computed, bool optional)
    {
        Object = obj;
        Property = property;
        Computed = computed;
        Optional = optional;
    }
    public Expression Object { get; set; }
    public Node Property { get; set; }
    public bool Computed { get; set; }
    public bool Optional { get; set; }
    public override NodeKind Kind => NodeKind.MemberExpression;
}

public sealed class CallExpression : Expression
{
    public CallExpression(Expression callee, IList<Expression> arguments, bool optional)
    {
        Callee = callee;
        Arguments = arguments;
        Optional = optional;
    }
    public Expression Callee { get; set; }
    public IList<Expression> Arguments { get; set; }
    public bool Optional { get; set; }
    public override NodeKind Kind => NodeKind.CallExpression;
}

public sealed class NewExpression : Expression
{
    public NewExpression(Expression callee, IList<Expression> arguments)
    {
        Callee = callee;
        Arguments = arguments;
    }
    public Expression Callee { get; set; }
    public IList<Expression> Arguments { get; set; }
    public override NodeKind Kind => NodeKind.NewExpression;
}

/// <summary>A dynamic <c>import(specifier)</c> expression.</summary>
public sealed class ImportExpression : Expression
{
    public ImportExpression(Expression source) => Source = source;
    public Expression Source { get; set; }
    public override NodeKind Kind => NodeKind.ImportExpression;
}

/// <summary><c>import.meta</c> or <c>new.target</c>.</summary>
public sealed class MetaProperty : Expression
{
    public MetaProperty(string meta, string property)
    {
        Meta = meta;
        Property = property;
    }
    public string Meta { get; set; }
    public string Property { get; set; }
    public override NodeKind Kind => NodeKind.MetaProperty;
}

public sealed class UnaryExpression : Expression
{
    public UnaryExpression(TokenKind op, Expression argument)
    {
        Operator = op;
        Argument = argument;
    }
    public TokenKind Operator { get; set; }
    public Expression Argument { get; set; }
    public override NodeKind Kind => NodeKind.UnaryExpression;
}

public sealed class UpdateExpression : Expression
{
    public UpdateExpression(TokenKind op, Expression argument, bool prefix)
    {
        Operator = op;
        Argument = argument;
        Prefix = prefix;
    }
    public TokenKind Operator { get; set; }
    public Expression Argument { get; set; }
    public bool Prefix { get; set; }
    public override NodeKind Kind => NodeKind.UpdateExpression;
}

public sealed class BinaryExpression : Expression
{
    public BinaryExpression(TokenKind op, Expression left, Expression right)
    {
        Operator = op;
        Left = left;
        Right = right;
    }
    public TokenKind Operator { get; set; }
    public Expression Left { get; set; }
    public Expression Right { get; set; }
    public override NodeKind Kind => NodeKind.BinaryExpression;
}

public sealed class LogicalExpression : Expression
{
    public LogicalExpression(TokenKind op, Expression left, Expression right)
    {
        Operator = op;
        Left = left;
        Right = right;
    }
    public TokenKind Operator { get; set; }
    public Expression Left { get; set; }
    public Expression Right { get; set; }
    public override NodeKind Kind => NodeKind.LogicalExpression;
}

public sealed class AssignmentExpression : Expression
{
    public AssignmentExpression(TokenKind op, Expression left, Expression right)
    {
        Operator = op;
        Left = left;
        Right = right;
    }
    public TokenKind Operator { get; set; }
    public Expression Left { get; set; }
    public Expression Right { get; set; }
    public override NodeKind Kind => NodeKind.AssignmentExpression;
}

public sealed class ConditionalExpression : Expression
{
    public ConditionalExpression(Expression test, Expression consequent, Expression alternate)
    {
        Test = test;
        Consequent = consequent;
        Alternate = alternate;
    }
    public Expression Test { get; set; }
    public Expression Consequent { get; set; }
    public Expression Alternate { get; set; }
    public override NodeKind Kind => NodeKind.ConditionalExpression;
}

public sealed class SequenceExpression : Expression
{
    public SequenceExpression(IList<Expression> expressions) => Expressions = expressions;
    public IList<Expression> Expressions { get; set; }
    public override NodeKind Kind => NodeKind.SequenceExpression;
}

public sealed class AwaitExpression : Expression
{
    public AwaitExpression(Expression argument) => Argument = argument;
    public Expression Argument { get; set; }
    public override NodeKind Kind => NodeKind.AwaitExpression;
}

public sealed class YieldExpression : Expression
{
    public YieldExpression(Expression? argument, bool delegated)
    {
        Argument = argument;
        Delegated = delegated;
    }
    public Expression? Argument { get; set; }
    public bool Delegated { get; set; }
    public override NodeKind Kind => NodeKind.YieldExpression;
}

/// <summary>A function parameter, optionally with a default value or rest.</summary>
public sealed class Parameter : Node
{
    public Parameter(Node pattern, Expression? initializer, bool rest)
    {
        Pattern = pattern;
        Initializer = initializer;
        Rest = rest;
    }
    public Node Pattern { get; set; }
    public Expression? Initializer { get; set; }
    public bool Rest { get; set; }
    public override NodeKind Kind => NodeKind.Parameter;
}

public sealed class FunctionExpression : Expression
{
    public FunctionExpression(Identifier? id, IList<Parameter> parameters, BlockStatement body, bool async, bool generator)
    {
        Id = id;
        Parameters = parameters;
        Body = body;
        Async = async;
        Generator = generator;
    }
    public Identifier? Id { get; set; }
    public IList<Parameter> Parameters { get; set; }
    public BlockStatement Body { get; set; }
    public bool Async { get; set; }
    public bool Generator { get; set; }
    public override NodeKind Kind => NodeKind.FunctionExpression;
}

/// <summary>A class used in expression position.</summary>
public sealed class ClassExpression : Expression
{
    public ClassExpression(Identifier? id, Expression? superClass, ClassBody body)
    {
        Id = id;
        SuperClass = superClass;
        Body = body;
    }
    public Identifier? Id { get; set; }
    public Expression? SuperClass { get; set; }
    public ClassBody Body { get; set; }
    public override NodeKind Kind => NodeKind.ClassExpression;
}

public sealed class ArrowFunctionExpression : Expression
{
    public ArrowFunctionExpression(IList<Parameter> parameters, Node body, bool async)
    {
        Parameters = parameters;
        Body = body;
        Async = async;
    }
    public IList<Parameter> Parameters { get; set; }
    /// <summary>Either a <see cref="BlockStatement"/> or an expression node.</summary>
    public Node Body { get; set; }
    public bool Async { get; set; }
    /// <summary>True when the arrow has an expression body (<c>x =&gt; x + 1</c>).</summary>
    public bool IsExpressionBody => Body is Expression;
    public override NodeKind Kind => NodeKind.ArrowFunctionExpression;
}
