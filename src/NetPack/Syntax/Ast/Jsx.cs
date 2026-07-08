namespace NetPack.Syntax.Ast;

using System.Collections.Generic;

/// <summary>Base for JSX element names (identifier, member, namespaced).</summary>
public abstract class JsxName : Node
{
}

public sealed class JsxIdentifier : JsxName
{
    public JsxIdentifier(string name) => Name = name;
    public string Name { get; set; }
    public override NodeKind Kind => NodeKind.JsxIdentifier;
}

public sealed class JsxMemberExpression : JsxName
{
    public JsxMemberExpression(JsxName obj, JsxIdentifier property)
    {
        Object = obj;
        Property = property;
    }
    public JsxName Object { get; set; }
    public JsxIdentifier Property { get; set; }
    public override NodeKind Kind => NodeKind.JsxMemberExpression;
}

public sealed class JsxNamespacedName : JsxName
{
    public JsxNamespacedName(JsxIdentifier ns, JsxIdentifier name)
    {
        Namespace = ns;
        Name = name;
    }
    public JsxIdentifier Namespace { get; set; }
    public JsxIdentifier Name { get; set; }
    public override NodeKind Kind => NodeKind.JsxNamespacedName;
}

public sealed class JsxText : Node
{
    public JsxText(string value) => Value = value;
    public string Value { get; set; }
    public override NodeKind Kind => NodeKind.JsxText;
}

public sealed class JsxExpressionContainer : Node
{
    public JsxExpressionContainer(Expression? expression) => Expression = expression;
    /// <summary>Null for an empty container <c>{}</c>.</summary>
    public Expression? Expression { get; set; }
    public override NodeKind Kind => NodeKind.JsxExpressionContainer;
}

public sealed class JsxAttribute : Node
{
    public JsxAttribute(JsxName name, Node? value)
    {
        Name = name;
        Value = value;
    }
    public JsxName Name { get; set; }
    /// <summary>A <see cref="StringLiteral"/>, <see cref="JsxExpressionContainer"/>,
    /// <see cref="JsxElement"/> or null (boolean shorthand).</summary>
    public Node? Value { get; set; }
    public override NodeKind Kind => NodeKind.JsxAttribute;
}

public sealed class JsxSpreadAttribute : Node
{
    public JsxSpreadAttribute(Expression argument) => Argument = argument;
    public Expression Argument { get; set; }
    public override NodeKind Kind => NodeKind.JsxSpreadAttribute;
}

public sealed class JsxOpeningElement : Node
{
    public JsxOpeningElement(JsxName name, IList<Node> attributes, bool selfClosing)
    {
        Name = name;
        Attributes = attributes;
        SelfClosing = selfClosing;
    }
    public JsxName Name { get; set; }
    /// <summary>Each entry is a <see cref="JsxAttribute"/> or <see cref="JsxSpreadAttribute"/>.</summary>
    public IList<Node> Attributes { get; set; }
    public bool SelfClosing { get; set; }
    public override NodeKind Kind => NodeKind.JsxOpeningElement;
}

public sealed class JsxClosingElement : Node
{
    public JsxClosingElement(JsxName name) => Name = name;
    public JsxName Name { get; set; }
    public override NodeKind Kind => NodeKind.JsxClosingElement;
}

public sealed class JsxElement : Expression
{
    public JsxElement(JsxOpeningElement opening, IList<Node> children, JsxClosingElement? closing)
    {
        OpeningElement = opening;
        Children = children;
        ClosingElement = closing;
    }
    public JsxOpeningElement OpeningElement { get; set; }
    /// <summary>Each child is JsxText, JsxExpressionContainer, JsxElement or JsxFragment.</summary>
    public IList<Node> Children { get; set; }
    public JsxClosingElement? ClosingElement { get; set; }
    public override NodeKind Kind => NodeKind.JsxElement;
}

public sealed class JsxFragment : Expression
{
    public JsxFragment(IList<Node> children) => Children = children;
    public IList<Node> Children { get; set; }
    public override NodeKind Kind => NodeKind.JsxFragment;
}
