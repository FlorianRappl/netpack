namespace NetPack.Syntax.Ast;

using System.Collections.Generic;

/// <summary>
/// A depth-first tree rewriter over the NetPack AST. The default behaviour
/// visits every child in place and returns each node unchanged, so subclasses
/// only override the handful of node kinds they care about.
///
/// Because AST nodes are mutable, rewrites happen in place: a <c>VisitXxx</c>
/// override may mutate the node's children or return an entirely different node
/// (e.g. lowering a <see cref="JsxElement"/> into a <see cref="CallExpression"/>).
/// The parent slot is updated with whatever the visit returns.
///
/// The same class serves read-only consumers (dependency collection): they
/// override the interesting <c>VisitXxx</c> methods, record what they need, call
/// <c>base.VisitXxx</c> to recurse, and return the node unchanged.
/// </summary>
public abstract class AstRewriter
{
    public Node? Visit(Node? node) => node is null ? null : Dispatch(node);

    protected virtual Node Dispatch(Node node) => node switch
    {
        SourceFile n => VisitSourceFile(n),

        // statements
        ImportDeclaration n => VisitImportDeclaration(n),
        ExportNamedDeclaration n => VisitExportNamedDeclaration(n),
        ExportDefaultDeclaration n => VisitExportDefaultDeclaration(n),
        ExportAllDeclaration n => VisitExportAllDeclaration(n),
        VariableStatement n => VisitVariableStatement(n),
        VariableDeclarator n => VisitVariableDeclarator(n),
        FunctionDeclaration n => VisitFunctionDeclaration(n),
        ClassDeclaration n => VisitClassDeclaration(n),
        ClassBody n => VisitClassBody(n),
        MethodDefinition n => VisitMethodDefinition(n),
        PropertyDefinition n => VisitPropertyDefinition(n),
        StaticBlock n => VisitStaticBlock(n),
        Decorator n => VisitDecorator(n),
        ExpressionStatement n => VisitExpressionStatement(n),
        BlockStatement n => VisitBlockStatement(n),
        ReturnStatement n => VisitReturnStatement(n),
        IfStatement n => VisitIfStatement(n),
        WhileStatement n => VisitWhileStatement(n),
        DoWhileStatement n => VisitDoWhileStatement(n),
        ForStatement n => VisitForStatement(n),
        ForInStatement n => VisitForInStatement(n),
        ForOfStatement n => VisitForOfStatement(n),
        ThrowStatement n => VisitThrowStatement(n),
        TryStatement n => VisitTryStatement(n),
        CatchClause n => VisitCatchClause(n),
        SwitchStatement n => VisitSwitchStatement(n),
        SwitchCase n => VisitSwitchCase(n),
        LabeledStatement n => VisitLabeledStatement(n),

        // clauses
        Parameter n => VisitParameter(n),
        Property n => VisitProperty(n),

        // expressions
        TemplateLiteral n => VisitTemplateLiteral(n),
        TaggedTemplateExpression n => VisitTaggedTemplateExpression(n),
        ArrayExpression n => VisitArrayExpression(n),
        ObjectExpression n => VisitObjectExpression(n),
        SpreadElement n => VisitSpreadElement(n),
        ParenthesizedExpression n => VisitParenthesizedExpression(n),
        MemberExpression n => VisitMemberExpression(n),
        CallExpression n => VisitCallExpression(n),
        NewExpression n => VisitNewExpression(n),
        ImportExpression n => VisitImportExpression(n),
        UnaryExpression n => VisitUnaryExpression(n),
        UpdateExpression n => VisitUpdateExpression(n),
        BinaryExpression n => VisitBinaryExpression(n),
        LogicalExpression n => VisitLogicalExpression(n),
        AssignmentExpression n => VisitAssignmentExpression(n),
        ConditionalExpression n => VisitConditionalExpression(n),
        SequenceExpression n => VisitSequenceExpression(n),
        AwaitExpression n => VisitAwaitExpression(n),
        YieldExpression n => VisitYieldExpression(n),
        FunctionExpression n => VisitFunctionExpression(n),
        ClassExpression n => VisitClassExpression(n),
        ArrowFunctionExpression n => VisitArrowFunctionExpression(n),

        // jsx
        JsxElement n => VisitJsxElement(n),
        JsxFragment n => VisitJsxFragment(n),
        JsxOpeningElement n => VisitJsxOpeningElement(n),
        JsxAttribute n => VisitJsxAttribute(n),
        JsxSpreadAttribute n => VisitJsxSpreadAttribute(n),
        JsxExpressionContainer n => VisitJsxExpressionContainer(n),
        JsxMemberExpression n => VisitJsxMemberExpression(n),
        JsxText n => VisitJsxText(n),

        // leaves (identifiers, literals, this/super, meta, jsx names, raw, …)
        _ => node,
    };

    // -- casting helpers ---------------------------------------------------

    protected Expression Ex(Expression e) => (Expression)Visit(e)!;
    protected Expression? ExOpt(Expression? e) => e is null ? null : (Expression)Visit(e)!;
    protected Statement St(Statement s) => (Statement)Visit(s)!;
    protected Statement? StOpt(Statement? s) => s is null ? null : (Statement)Visit(s)!;
    protected Node Nd(Node n) => Visit(n)!;
    protected Node? NdOpt(Node? n) => n is null ? null : Visit(n)!;
    protected T Ch<T>(T n) where T : Node => (T)Visit(n)!;
    protected T? ChOpt<T>(T? n) where T : Node => n is null ? null : (T)Visit(n)!;

    protected void RewriteList<T>(IList<T> list) where T : Node
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Visit(list[i]) is T rewritten)
            {
                list[i] = rewritten;
            }
        }
    }

    protected void RewriteElements(IList<Expression?> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is Expression e && Visit(e) is Expression r)
            {
                list[i] = r;
            }
        }
    }

    // -- statements --------------------------------------------------------

    protected virtual Node VisitSourceFile(SourceFile node)
    {
        RewriteList(node.Body);
        return node;
    }

    protected virtual Node VisitImportDeclaration(ImportDeclaration node)
    {
        RewriteList(node.Specifiers);
        return node;
    }

    protected virtual Node VisitExportNamedDeclaration(ExportNamedDeclaration node)
    {
        node.Declaration = StOpt(node.Declaration);
        RewriteList(node.Specifiers);
        return node;
    }

    protected virtual Node VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
    {
        node.Declaration = Nd(node.Declaration);
        return node;
    }

    protected virtual Node VisitExportAllDeclaration(ExportAllDeclaration node) => node;

    protected virtual Node VisitVariableStatement(VariableStatement node)
    {
        RewriteList(node.Declarations);
        return node;
    }

    protected virtual Node VisitVariableDeclarator(VariableDeclarator node)
    {
        node.Id = Nd(node.Id);
        node.Init = ExOpt(node.Init);
        return node;
    }

    protected virtual Node VisitFunctionDeclaration(FunctionDeclaration node)
    {
        RewriteList(node.Parameters);
        node.Body = Ch(node.Body);
        return node;
    }

    protected virtual Node VisitClassDeclaration(ClassDeclaration node)
    {
        RewriteList(node.Decorators);
        node.SuperClass = ExOpt(node.SuperClass);
        node.Body = Ch(node.Body);
        return node;
    }

    protected virtual Node VisitClassBody(ClassBody node)
    {
        RewriteList(node.Members);
        return node;
    }

    protected virtual Node VisitMethodDefinition(MethodDefinition node)
    {
        RewriteList(node.Decorators);
        node.Key = Nd(node.Key);
        node.Value = Ch(node.Value);
        return node;
    }

    protected virtual Node VisitPropertyDefinition(PropertyDefinition node)
    {
        RewriteList(node.Decorators);
        node.Key = Nd(node.Key);
        node.Value = ExOpt(node.Value);
        return node;
    }

    protected virtual Node VisitStaticBlock(StaticBlock node)
    {
        RewriteList(node.Body);
        return node;
    }

    protected virtual Node VisitDecorator(Decorator node)
    {
        node.Expression = Ex(node.Expression);
        return node;
    }

    protected virtual Node VisitExpressionStatement(ExpressionStatement node)
    {
        node.Expression = Ex(node.Expression);
        return node;
    }

    protected virtual Node VisitBlockStatement(BlockStatement node)
    {
        RewriteList(node.Body);
        return node;
    }

    protected virtual Node VisitReturnStatement(ReturnStatement node)
    {
        node.Argument = ExOpt(node.Argument);
        return node;
    }

    protected virtual Node VisitIfStatement(IfStatement node)
    {
        node.Test = Ex(node.Test);
        node.Consequent = St(node.Consequent);
        node.Alternate = StOpt(node.Alternate);
        return node;
    }

    protected virtual Node VisitWhileStatement(WhileStatement node)
    {
        node.Test = Ex(node.Test);
        node.Body = St(node.Body);
        return node;
    }

    protected virtual Node VisitDoWhileStatement(DoWhileStatement node)
    {
        node.Body = St(node.Body);
        node.Test = Ex(node.Test);
        return node;
    }

    protected virtual Node VisitForStatement(ForStatement node)
    {
        node.Init = NdOpt(node.Init);
        node.Test = ExOpt(node.Test);
        node.Update = ExOpt(node.Update);
        node.Body = St(node.Body);
        return node;
    }

    protected virtual Node VisitForInStatement(ForInStatement node)
    {
        node.Left = Nd(node.Left);
        node.Right = Ex(node.Right);
        node.Body = St(node.Body);
        return node;
    }

    protected virtual Node VisitForOfStatement(ForOfStatement node)
    {
        node.Left = Nd(node.Left);
        node.Right = Ex(node.Right);
        node.Body = St(node.Body);
        return node;
    }

    protected virtual Node VisitThrowStatement(ThrowStatement node)
    {
        node.Argument = Ex(node.Argument);
        return node;
    }

    protected virtual Node VisitTryStatement(TryStatement node)
    {
        node.Block = Ch(node.Block);
        node.Handler = ChOpt(node.Handler);
        node.Finalizer = ChOpt(node.Finalizer);
        return node;
    }

    protected virtual Node VisitCatchClause(CatchClause node)
    {
        node.Param = NdOpt(node.Param);
        node.Body = Ch(node.Body);
        return node;
    }

    protected virtual Node VisitSwitchStatement(SwitchStatement node)
    {
        node.Discriminant = Ex(node.Discriminant);
        RewriteList(node.Cases);
        return node;
    }

    protected virtual Node VisitSwitchCase(SwitchCase node)
    {
        node.Test = ExOpt(node.Test);
        RewriteList(node.Body);
        return node;
    }

    protected virtual Node VisitLabeledStatement(LabeledStatement node)
    {
        node.Body = St(node.Body);
        return node;
    }

    protected virtual Node VisitParameter(Parameter node)
    {
        node.Pattern = Nd(node.Pattern);
        node.Initializer = ExOpt(node.Initializer);
        return node;
    }

    protected virtual Node VisitProperty(Property node)
    {
        node.Key = Nd(node.Key);
        node.Value = NdOpt(node.Value);
        return node;
    }

    // -- expressions -------------------------------------------------------

    protected virtual Node VisitTemplateLiteral(TemplateLiteral node)
    {
        RewriteList(node.Expressions);
        return node;
    }

    protected virtual Node VisitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        node.Tag = Ex(node.Tag);
        node.Quasi = Ch(node.Quasi);
        return node;
    }

    protected virtual Node VisitArrayExpression(ArrayExpression node)
    {
        RewriteElements(node.Elements);
        return node;
    }

    protected virtual Node VisitObjectExpression(ObjectExpression node)
    {
        RewriteList(node.Properties);
        return node;
    }

    protected virtual Node VisitSpreadElement(SpreadElement node)
    {
        node.Argument = Ex(node.Argument);
        return node;
    }

    protected virtual Node VisitParenthesizedExpression(ParenthesizedExpression node)
    {
        node.Expression = Ex(node.Expression);
        return node;
    }

    protected virtual Node VisitMemberExpression(MemberExpression node)
    {
        node.Object = Ex(node.Object);
        node.Property = Nd(node.Property);
        return node;
    }

    protected virtual Node VisitCallExpression(CallExpression node)
    {
        node.Callee = Ex(node.Callee);
        RewriteList(node.Arguments);
        return node;
    }

    protected virtual Node VisitNewExpression(NewExpression node)
    {
        node.Callee = Ex(node.Callee);
        RewriteList(node.Arguments);
        return node;
    }

    protected virtual Node VisitImportExpression(ImportExpression node)
    {
        node.Source = Ex(node.Source);
        return node;
    }

    protected virtual Node VisitUnaryExpression(UnaryExpression node)
    {
        node.Argument = Ex(node.Argument);
        return node;
    }

    protected virtual Node VisitUpdateExpression(UpdateExpression node)
    {
        node.Argument = Ex(node.Argument);
        return node;
    }

    protected virtual Node VisitBinaryExpression(BinaryExpression node)
    {
        node.Left = Ex(node.Left);
        node.Right = Ex(node.Right);
        return node;
    }

    protected virtual Node VisitLogicalExpression(LogicalExpression node)
    {
        node.Left = Ex(node.Left);
        node.Right = Ex(node.Right);
        return node;
    }

    protected virtual Node VisitAssignmentExpression(AssignmentExpression node)
    {
        node.Left = Ex(node.Left);
        node.Right = Ex(node.Right);
        return node;
    }

    protected virtual Node VisitConditionalExpression(ConditionalExpression node)
    {
        node.Test = Ex(node.Test);
        node.Consequent = Ex(node.Consequent);
        node.Alternate = Ex(node.Alternate);
        return node;
    }

    protected virtual Node VisitSequenceExpression(SequenceExpression node)
    {
        RewriteList(node.Expressions);
        return node;
    }

    protected virtual Node VisitAwaitExpression(AwaitExpression node)
    {
        node.Argument = Ex(node.Argument);
        return node;
    }

    protected virtual Node VisitYieldExpression(YieldExpression node)
    {
        node.Argument = ExOpt(node.Argument);
        return node;
    }

    protected virtual Node VisitFunctionExpression(FunctionExpression node)
    {
        RewriteList(node.Parameters);
        node.Body = Ch(node.Body);
        return node;
    }

    protected virtual Node VisitClassExpression(ClassExpression node)
    {
        node.SuperClass = ExOpt(node.SuperClass);
        node.Body = Ch(node.Body);
        return node;
    }

    protected virtual Node VisitArrowFunctionExpression(ArrowFunctionExpression node)
    {
        RewriteList(node.Parameters);
        node.Body = Nd(node.Body);
        return node;
    }

    // -- jsx ---------------------------------------------------------------

    protected virtual Node VisitJsxElement(JsxElement node)
    {
        node.OpeningElement = Ch(node.OpeningElement);
        RewriteList(node.Children);
        node.ClosingElement = ChOpt(node.ClosingElement);
        return node;
    }

    protected virtual Node VisitJsxFragment(JsxFragment node)
    {
        RewriteList(node.Children);
        return node;
    }

    protected virtual Node VisitJsxOpeningElement(JsxOpeningElement node)
    {
        RewriteList(node.Attributes);
        return node;
    }

    protected virtual Node VisitJsxAttribute(JsxAttribute node)
    {
        node.Value = NdOpt(node.Value);
        return node;
    }

    protected virtual Node VisitJsxSpreadAttribute(JsxSpreadAttribute node)
    {
        node.Argument = Ex(node.Argument);
        return node;
    }

    protected virtual Node VisitJsxExpressionContainer(JsxExpressionContainer node)
    {
        node.Expression = ExOpt(node.Expression);
        return node;
    }

    protected virtual Node VisitJsxMemberExpression(JsxMemberExpression node) => node;

    protected virtual Node VisitJsxText(JsxText node) => node;
}
