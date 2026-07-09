namespace NetPack.Syntax.Optimizer;

using System.Collections.Generic;
using NetPack.Syntax.Ast;

/// <summary>
/// Collects the set of top-level binding names referenced by a statement,
/// ignoring references that are shadowed by a local declaration. It errs on the
/// safe side: when unsure whether a name is shadowed it treats the reference as
/// a real use, so the tree shaker can only ever keep more code, never remove
/// something that is still needed.
/// </summary>
internal sealed class ReferenceCollector
{
    private readonly HashSet<string> _top;
    private readonly HashSet<string> _uses = new(System.StringComparer.Ordinal);
    private readonly List<HashSet<string>> _scopes = new();

    private ReferenceCollector(HashSet<string> top) => _top = top;

    public static HashSet<string> Collect(Node root, HashSet<string> topNames)
    {
        var collector = new ReferenceCollector(topNames);
        if (root is Statement statement)
        {
            collector.VisitStatement(statement);
        }
        else
        {
            collector.Visit(root);
        }
        return collector._uses;
    }

    private bool Shadowed(string name)
    {
        for (var i = 0; i < _scopes.Count; i++)
        {
            if (_scopes[i].Contains(name)) return true;
        }
        return false;
    }

    private void Declare(string name)
    {
        if (_scopes.Count > 0)
        {
            _scopes[^1].Add(name);
        }
    }

    private void Use(string name)
    {
        if (_top.Contains(name) && !Shadowed(name))
        {
            _uses.Add(name);
        }
    }

    private void PushScope() => _scopes.Add(new HashSet<string>(System.StringComparer.Ordinal));

    private void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

    private void DeclarePattern(Node pattern)
    {
        switch (pattern)
        {
            case Identifier id:
                Declare(id.Name);
                break;
            case ObjectExpression obj:
                foreach (var member in obj.Properties)
                {
                    if (member is SpreadElement spread) DeclarePattern(spread.Argument);
                    else if (member is Property property)
                    {
                        if (property.Computed && property.Key is Expression key) Visit(key);
                        if (property.Value is not null) DeclarePattern(property.Value);
                    }
                }
                break;
            case ArrayExpression arr:
                foreach (var element in arr.Elements)
                {
                    if (element is SpreadElement spread) DeclarePattern(spread.Argument);
                    else if (element is not null) DeclarePattern(element);
                }
                break;
            case AssignmentExpression assign:
                DeclarePattern(assign.Left);
                Visit(assign.Right);
                break;
        }
    }

    private void EnterFunction(IList<Parameter> parameters, Node? body)
    {
        PushScope();
        foreach (var parameter in parameters)
        {
            DeclarePattern(parameter.Pattern);
            if (parameter.Initializer is not null) Visit(parameter.Initializer);
        }
        if (body is BlockStatement block)
        {
            foreach (var statement in block.Body) VisitStatement(statement);
        }
        else if (body is Expression expression)
        {
            Visit(expression);
        }
        PopScope();
    }

    // -- statements --------------------------------------------------------

    private void VisitStatement(Statement? statement)
    {
        switch (statement)
        {
            case null:
            case EmptyStatement:
            case DebuggerStatement:
            case TypeOnlyDeclaration:
                break;
            case BlockStatement block:
                PushScope();
                foreach (var s in block.Body) VisitStatement(s);
                PopScope();
                break;
            case VariableStatement variable:
                foreach (var declarator in variable.Declarations)
                {
                    DeclarePattern(declarator.Id);
                    if (declarator.Init is not null) Visit(declarator.Init);
                }
                break;
            case FunctionDeclaration func:
                if (func.Id is not null) Declare(func.Id.Name);
                EnterFunction(func.Parameters, func.Body);
                break;
            case ClassDeclaration cls:
                if (cls.Id is not null) Declare(cls.Id.Name);
                if (cls.SuperClass is not null) Visit(cls.SuperClass);
                VisitClassBody(cls.Body);
                break;
            case ExpressionStatement expr:
                Visit(expr.Expression);
                break;
            case ReturnStatement ret:
                if (ret.Argument is not null) Visit(ret.Argument);
                break;
            case IfStatement ifs:
                Visit(ifs.Test);
                VisitStatement(ifs.Consequent);
                VisitStatement(ifs.Alternate);
                break;
            case WhileStatement whi:
                Visit(whi.Test);
                VisitStatement(whi.Body);
                break;
            case DoWhileStatement dow:
                VisitStatement(dow.Body);
                Visit(dow.Test);
                break;
            case ForStatement fors:
                PushScope();
                if (fors.Init is VariableStatement vs) VisitStatement(vs);
                else if (fors.Init is Expression e) Visit(e);
                if (fors.Test is not null) Visit(fors.Test);
                if (fors.Update is not null) Visit(fors.Update);
                VisitStatement(fors.Body);
                PopScope();
                break;
            case ForInStatement forin:
                PushScope();
                VisitForTarget(forin.Left);
                Visit(forin.Right);
                VisitStatement(forin.Body);
                PopScope();
                break;
            case ForOfStatement forof:
                PushScope();
                VisitForTarget(forof.Left);
                Visit(forof.Right);
                VisitStatement(forof.Body);
                PopScope();
                break;
            case ThrowStatement thr:
                Visit(thr.Argument);
                break;
            case TryStatement trys:
                VisitStatement(trys.Block);
                if (trys.Handler is not null)
                {
                    PushScope();
                    if (trys.Handler.Param is not null) DeclarePattern(trys.Handler.Param);
                    foreach (var s in trys.Handler.Body.Body) VisitStatement(s);
                    PopScope();
                }
                if (trys.Finalizer is not null) VisitStatement(trys.Finalizer);
                break;
            case SwitchStatement sw:
                Visit(sw.Discriminant);
                PushScope();
                foreach (var c in sw.Cases)
                {
                    if (c.Test is not null) Visit(c.Test);
                    foreach (var s in c.Body) VisitStatement(s);
                }
                PopScope();
                break;
            case LabeledStatement lab:
                VisitStatement(lab.Body);
                break;
            case ImportDeclaration:
                break;
            case ExportNamedDeclaration en:
                if (en.Declaration is not null) VisitStatement(en.Declaration);
                if (en.Source is null)
                {
                    foreach (var specifier in en.Specifiers)
                    {
                        if (specifier.Local is Identifier local) Use(local.Name);
                    }
                }
                break;
            case ExportDefaultDeclaration ed:
                if (ed.Declaration is Statement ds) VisitStatement(ds);
                else if (ed.Declaration is Expression de) Visit(de);
                break;
            case ExportAllDeclaration:
                break;
        }
    }

    private void VisitForTarget(Node left)
    {
        if (left is VariableStatement vs) VisitStatement(vs);
        else if (left is Expression e) Visit(e);
    }

    private void VisitClassBody(ClassBody body)
    {
        foreach (var member in body.Members)
        {
            switch (member)
            {
                case MethodDefinition m:
                    foreach (var d in m.Decorators) Visit(d.Expression);
                    if (m.Computed && m.Key is Expression mk) Visit(mk);
                    EnterFunction(m.Value.Parameters, m.Value.Body);
                    break;
                case PropertyDefinition f:
                    foreach (var d in f.Decorators) Visit(d.Expression);
                    if (f.Computed && f.Key is Expression fk) Visit(fk);
                    if (f.Value is not null) Visit(f.Value);
                    break;
                case StaticBlock sb:
                    PushScope();
                    foreach (var s in sb.Body) VisitStatement(s);
                    PopScope();
                    break;
            }
        }
    }

    // -- expressions -------------------------------------------------------

    private void Visit(Node? node)
    {
        switch (node)
        {
            case null:
            case NumericLiteral:
            case BigIntLiteral:
            case StringLiteral:
            case BooleanLiteral:
            case NullLiteral:
            case RegExpLiteral:
            case ThisExpression:
            case SuperExpression:
            case PrivateIdentifier:
            case MetaProperty:
            case Raw:
                break;
            case Identifier id:
                Use(id.Name);
                break;
            case TemplateLiteral tpl:
                foreach (var e in tpl.Expressions) Visit(e);
                break;
            case TaggedTemplateExpression tag:
                Visit(tag.Tag);
                foreach (var e in tag.Quasi.Expressions) Visit(e);
                break;
            case ArrayExpression arr:
                foreach (var el in arr.Elements) if (el is not null) Visit(el);
                break;
            case ObjectExpression obj:
                foreach (var member in obj.Properties)
                {
                    if (member is SpreadElement spread) { Visit(spread.Argument); continue; }
                    if (member is not Property property) continue;
                    if (property.Computed && property.Key is Expression key) Visit(key);
                    if (property.Method && property.Value is FunctionExpression method) EnterFunction(method.Parameters, method.Body);
                    else if (property.Value is Expression value) Visit(value);
                }
                break;
            case SpreadElement spreadEl:
                Visit(spreadEl.Argument);
                break;
            case ParenthesizedExpression paren:
                Visit(paren.Expression);
                break;
            case MemberExpression member:
                Visit(member.Object);
                if (member.Computed && member.Property is Expression prop) Visit(prop);
                break;
            case CallExpression call:
                Visit(call.Callee);
                foreach (var a in call.Arguments) Visit(a);
                break;
            case NewExpression neww:
                Visit(neww.Callee);
                foreach (var a in neww.Arguments) Visit(a);
                break;
            case ImportExpression imp:
                Visit(imp.Source);
                break;
            case UnaryExpression un:
                Visit(un.Argument);
                break;
            case UpdateExpression up:
                Visit(up.Argument);
                break;
            case AwaitExpression aw:
                Visit(aw.Argument);
                break;
            case YieldExpression yi:
                if (yi.Argument is not null) Visit(yi.Argument);
                break;
            case BinaryExpression bin:
                Visit(bin.Left);
                Visit(bin.Right);
                break;
            case LogicalExpression log:
                Visit(log.Left);
                Visit(log.Right);
                break;
            case AssignmentExpression asg:
                Visit(asg.Left);
                Visit(asg.Right);
                break;
            case ConditionalExpression cond:
                Visit(cond.Test);
                Visit(cond.Consequent);
                Visit(cond.Alternate);
                break;
            case SequenceExpression seq:
                foreach (var e in seq.Expressions) Visit(e);
                break;
            case FunctionExpression fn:
                if (fn.Id is not null) { PushScope(); Declare(fn.Id.Name); EnterFunction(fn.Parameters, fn.Body); PopScope(); }
                else EnterFunction(fn.Parameters, fn.Body);
                break;
            case ArrowFunctionExpression arrow:
                EnterFunction(arrow.Parameters, arrow.Body);
                break;
            case ClassExpression ce:
                if (ce.SuperClass is not null) Visit(ce.SuperClass);
                VisitClassBody(ce.Body);
                break;
            case JsxElement jsx:
                VisitJsx(jsx);
                break;
            case JsxFragment frag:
                foreach (var c in frag.Children) VisitJsxChild(c);
                break;
        }
    }

    private void VisitJsx(JsxElement jsx)
    {
        foreach (var attribute in jsx.OpeningElement.Attributes)
        {
            if (attribute is JsxSpreadAttribute spread) Visit(spread.Argument);
            else if (attribute is JsxAttribute { Value: JsxExpressionContainer { Expression: { } e } }) Visit(e);
        }
        foreach (var child in jsx.Children) VisitJsxChild(child);
    }

    private void VisitJsxChild(Node child)
    {
        if (child is JsxExpressionContainer { Expression: { } e }) Visit(e);
        else if (child is JsxElement el) VisitJsx(el);
        else if (child is JsxFragment fr) foreach (var c in fr.Children) VisitJsxChild(c);
    }
}
