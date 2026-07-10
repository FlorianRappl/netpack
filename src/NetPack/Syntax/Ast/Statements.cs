namespace NetPack.Syntax.Ast;

using System.Collections.Generic;

public sealed class BlockStatement : Statement
{
    public BlockStatement(IList<Statement> body) => Body = body;
    public IList<Statement> Body { get; set; }

    /// <summary>
    /// When set, marks this block as the body of a module factory: while the
    /// printer emits the block it attributes node positions to this source for
    /// source-map generation.
    /// </summary>
    public SourceFile? Source { get; set; }

    public override NodeKind Kind => NodeKind.BlockStatement;
}

public sealed class EmptyStatement : Statement
{
    public override NodeKind Kind => NodeKind.EmptyStatement;
}

public sealed class DebuggerStatement : Statement
{
    public override NodeKind Kind => NodeKind.DebuggerStatement;
}

public sealed class ExpressionStatement : Statement
{
    public ExpressionStatement(Expression expression) => Expression = expression;
    public Expression Expression { get; set; }
    public override NodeKind Kind => NodeKind.ExpressionStatement;
}

public enum VariableKind { Var, Let, Const }

public sealed class VariableDeclarator : Node
{
    public VariableDeclarator(Node id, Expression? init)
    {
        Id = id;
        Init = init;
    }
    public Node Id { get; set; }
    public Expression? Init { get; set; }
    public override NodeKind Kind => NodeKind.VariableDeclarator;
}

public sealed class VariableStatement : Declaration
{
    public VariableStatement(VariableKind kind, IList<VariableDeclarator> declarations)
    {
        DeclarationKind = kind;
        Declarations = declarations;
    }
    public VariableKind DeclarationKind { get; set; }
    public IList<VariableDeclarator> Declarations { get; set; }
    public override NodeKind Kind => NodeKind.VariableStatement;
}

public sealed class FunctionDeclaration : Declaration
{
    public FunctionDeclaration(Identifier? id, IList<Parameter> parameters, BlockStatement body, bool async, bool generator)
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
    public override NodeKind Kind => NodeKind.FunctionDeclaration;
}

public sealed class ReturnStatement : Statement
{
    public ReturnStatement(Expression? argument) => Argument = argument;
    public Expression? Argument { get; set; }
    public override NodeKind Kind => NodeKind.ReturnStatement;
}

public sealed class IfStatement : Statement
{
    public IfStatement(Expression test, Statement consequent, Statement? alternate)
    {
        Test = test;
        Consequent = consequent;
        Alternate = alternate;
    }
    public Expression Test { get; set; }
    public Statement Consequent { get; set; }
    public Statement? Alternate { get; set; }
    public override NodeKind Kind => NodeKind.IfStatement;
}

public sealed class WhileStatement : Statement
{
    public WhileStatement(Expression test, Statement body)
    {
        Test = test;
        Body = body;
    }
    public Expression Test { get; set; }
    public Statement Body { get; set; }
    public override NodeKind Kind => NodeKind.WhileStatement;
}

public sealed class DoWhileStatement : Statement
{
    public DoWhileStatement(Statement body, Expression test)
    {
        Body = body;
        Test = test;
    }
    public Statement Body { get; set; }
    public Expression Test { get; set; }
    public override NodeKind Kind => NodeKind.DoWhileStatement;
}

public sealed class ForStatement : Statement
{
    public ForStatement(Node? init, Expression? test, Expression? update, Statement body)
    {
        Init = init;
        Test = test;
        Update = update;
        Body = body;
    }
    public Node? Init { get; set; }
    public Expression? Test { get; set; }
    public Expression? Update { get; set; }
    public Statement Body { get; set; }
    public override NodeKind Kind => NodeKind.ForStatement;
}

/// <summary><c>for (left in right) body</c>.</summary>
public sealed class ForInStatement : Statement
{
    public ForInStatement(Node left, Expression right, Statement body)
    {
        Left = left;
        Right = right;
        Body = body;
    }
    public Node Left { get; set; }
    public Expression Right { get; set; }
    public Statement Body { get; set; }
    public override NodeKind Kind => NodeKind.ForInStatement;
}

/// <summary><c>for (left of right) body</c> (optionally <c>for await</c>).</summary>
public sealed class ForOfStatement : Statement
{
    public ForOfStatement(Node left, Expression right, Statement body, bool await)
    {
        Left = left;
        Right = right;
        Body = body;
        Await = await;
    }
    public Node Left { get; set; }
    public Expression Right { get; set; }
    public Statement Body { get; set; }
    public bool Await { get; set; }
    public override NodeKind Kind => NodeKind.ForOfStatement;
}

public sealed class ThrowStatement : Statement
{
    public ThrowStatement(Expression argument) => Argument = argument;
    public Expression Argument { get; set; }
    public override NodeKind Kind => NodeKind.ThrowStatement;
}

public sealed class CatchClause : Node
{
    public CatchClause(Node? param, BlockStatement body)
    {
        Param = param;
        Body = body;
    }
    public Node? Param { get; set; }
    public BlockStatement Body { get; set; }
    public override NodeKind Kind => NodeKind.CatchClause;
}

public sealed class TryStatement : Statement
{
    public TryStatement(BlockStatement block, CatchClause? handler, BlockStatement? finalizer)
    {
        Block = block;
        Handler = handler;
        Finalizer = finalizer;
    }
    public BlockStatement Block { get; set; }
    public CatchClause? Handler { get; set; }
    public BlockStatement? Finalizer { get; set; }
    public override NodeKind Kind => NodeKind.TryStatement;
}

public sealed class BreakStatement : Statement
{
    public BreakStatement(string? label) => Label = label;
    public string? Label { get; set; }
    public override NodeKind Kind => NodeKind.BreakStatement;
}

public sealed class ContinueStatement : Statement
{
    public ContinueStatement(string? label) => Label = label;
    public string? Label { get; set; }
    public override NodeKind Kind => NodeKind.ContinueStatement;
}

public sealed class LabeledStatement : Statement
{
    public LabeledStatement(string label, Statement body)
    {
        Label = label;
        Body = body;
    }
    public string Label { get; set; }
    public Statement Body { get; set; }
    public override NodeKind Kind => NodeKind.LabeledStatement;
}

public sealed class SwitchCase : Node
{
    public SwitchCase(Expression? test, IList<Statement> body)
    {
        Test = test;
        Body = body;
    }
    /// <summary>Null for the <c>default:</c> clause.</summary>
    public Expression? Test { get; set; }
    public IList<Statement> Body { get; set; }
    public override NodeKind Kind => NodeKind.SwitchCase;
}

public sealed class SwitchStatement : Statement
{
    public SwitchStatement(Expression discriminant, IList<SwitchCase> cases)
    {
        Discriminant = discriminant;
        Cases = cases;
    }
    public Expression Discriminant { get; set; }
    public IList<SwitchCase> Cases { get; set; }
    public override NodeKind Kind => NodeKind.SwitchStatement;
}

// -- classes --------------------------------------------------------------

/// <summary>A decorator <c>@expr</c> applied to a class or class member.</summary>
public sealed class Decorator : Node
{
    public Decorator(Expression expression) => Expression = expression;
    public Expression Expression { get; set; }
    public override NodeKind Kind => NodeKind.Decorator;
}

public enum MethodKind { Method, Get, Set, Constructor }

/// <summary>A method, getter, setter or constructor in a class body.</summary>
public sealed class MethodDefinition : Node
{
    public MethodDefinition(Node key, FunctionExpression value, MethodKind kind, bool computed, bool @static, IList<Decorator> decorators)
    {
        Key = key;
        Value = value;
        MethodKind = kind;
        Computed = computed;
        Static = @static;
        Decorators = decorators;
    }
    public Node Key { get; set; }
    public FunctionExpression Value { get; set; }
    public MethodKind MethodKind { get; set; }
    public bool Computed { get; set; }
    public bool Static { get; set; }
    public IList<Decorator> Decorators { get; set; }
    public override NodeKind Kind => NodeKind.MethodDefinition;
}

/// <summary>A class field <c>x = 1</c> (optionally <c>static</c>).</summary>
public sealed class PropertyDefinition : Node
{
    public PropertyDefinition(Node key, Expression? value, bool computed, bool @static, IList<Decorator> decorators)
    {
        Key = key;
        Value = value;
        Computed = computed;
        Static = @static;
        Decorators = decorators;
    }
    public Node Key { get; set; }
    public Expression? Value { get; set; }
    public bool Computed { get; set; }
    public bool Static { get; set; }
    public IList<Decorator> Decorators { get; set; }
    public override NodeKind Kind => NodeKind.PropertyDefinition;
}

/// <summary>A <c>static { ... }</c> initialization block.</summary>
public sealed class StaticBlock : Node
{
    public StaticBlock(IList<Statement> body) => Body = body;
    public IList<Statement> Body { get; set; }
    public override NodeKind Kind => NodeKind.StaticBlock;
}

/// <summary>The body of a class: a list of members (MethodDefinition,
/// PropertyDefinition or StaticBlock).</summary>
public sealed class ClassBody : Node
{
    public ClassBody(IList<Node> members) => Members = members;
    public IList<Node> Members { get; set; }
    public override NodeKind Kind => NodeKind.ClassBody;
}

public sealed class ClassDeclaration : Declaration
{
    public ClassDeclaration(Identifier? id, Expression? superClass, ClassBody body, IList<Decorator> decorators)
    {
        Id = id;
        SuperClass = superClass;
        Body = body;
        Decorators = decorators;
    }
    public Identifier? Id { get; set; }
    public Expression? SuperClass { get; set; }
    public ClassBody Body { get; set; }
    public IList<Decorator> Decorators { get; set; }
    public override NodeKind Kind => NodeKind.ClassDeclaration;
}
