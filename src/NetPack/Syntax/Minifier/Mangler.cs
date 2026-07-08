namespace NetPack.Syntax.Minifier;

using System.Collections.Generic;
using System.Text;
using NetPack.Syntax.Ast;

/// <summary>
/// A scope-aware identifier minifier ("mangler"). It shortens the names of
/// local bindings — function parameters, and <c>var</c>/<c>let</c>/<c>const</c>/
/// function/class declarations inside function or block scopes — while leaving
/// module-level bindings, globals, property names, and the import/export
/// interface untouched.
///
/// Safety model: every generated name is <b>globally unique</b> and avoids every
/// identifier that already appears anywhere in the program. Because each binding
/// gets its own fresh name and every reference is rewritten to its binding's
/// name, there is no possibility of accidental capture or collision — at the
/// cost of not reusing names across sibling scopes (a future optimization).
///
/// In a NetPack bundle each module's body is wrapped in an arrow function, so a
/// module's top-level declarations are function-scoped and therefore do get
/// mangled; only the small runtime shell (<c>addModule</c>, <c>require</c>, the
/// module cache, and the ESM import/export surface) stays at module scope and is
/// preserved.
/// </summary>
public sealed class Mangler
{
    private sealed class Binding
    {
        public required string Original;
        public string? NewName;
        public bool CanRename = true;
        public readonly List<Identifier> Nodes = new();
    }

    private sealed class Scope
    {
        public Scope? Parent;
        public bool IsFunction;
        public bool IsModule;
        public readonly Dictionary<string, Binding> Decls = new(System.StringComparer.Ordinal);
    }

    private readonly record struct Reference(Identifier Node, Scope Scope);
    private readonly record struct Shorthand(Property Property, Identifier Value);

    private readonly List<Reference> _references = new();
    private readonly List<Binding> _bindings = new();
    private readonly List<Shorthand> _shorthands = new();
    private readonly HashSet<string> _allNames = new(System.StringComparer.Ordinal);

    private Scope _scope = null!;
    private Scope _fnScope = null!;

    public void Process(SourceFile file)
    {
        _scope = _fnScope = new Scope { IsFunction = true, IsModule = true };
        foreach (var statement in file.Body)
        {
            AnalyzeStatement(statement);
        }
        Resolve();
        AssignNames();
        Apply();
    }

    // -- name bookkeeping --------------------------------------------------

    private void AddName(string name) => _allNames.Add(name);

    private Binding? Declare(Scope target, string name, Identifier node, bool canRename = true)
    {
        AddName(name);

        // Module-scope bindings are never renamed and need no binding record;
        // recording their name (above) is enough to keep generated names clear.
        if (target.IsModule)
        {
            return null;
        }

        if (!target.Decls.TryGetValue(name, out var binding))
        {
            binding = new Binding { Original = name, CanRename = canRename };
            target.Decls[name] = binding;
            _bindings.Add(binding);
        }
        else if (!canRename)
        {
            binding.CanRename = false;
        }

        binding.Nodes.Add(node);
        return binding;
    }

    private void Use(Identifier node)
    {
        AddName(node.Name);
        _references.Add(new Reference(node, _scope));
    }

    // -- scope helpers -----------------------------------------------------

    private void EnterFunction(IList<Parameter> parameters, Node? body, Identifier? selfName)
    {
        var prevScope = _scope;
        var prevFn = _fnScope;
        var fn = new Scope { Parent = prevScope, IsFunction = true };
        _scope = fn;
        _fnScope = fn;

        // A named function expression's own name is visible only inside it; keep
        // it un-mangled for safety.
        if (selfName is not null)
        {
            AddName(selfName.Name);
        }

        foreach (var parameter in parameters)
        {
            DeclarePattern(parameter.Pattern, isVar: true);
            if (parameter.Initializer is not null)
            {
                AnalyzeExpression(parameter.Initializer);
            }
        }

        if (body is BlockStatement block)
        {
            foreach (var statement in block.Body)
            {
                AnalyzeStatement(statement);
            }
        }
        else if (body is Expression expression)
        {
            AnalyzeExpression(expression);
        }

        _scope = prevScope;
        _fnScope = prevFn;
    }

    private void EnterBlock(IEnumerable<Statement> body)
    {
        var prev = _scope;
        _scope = new Scope { Parent = prev, IsFunction = false };
        foreach (var statement in body)
        {
            AnalyzeStatement(statement);
        }
        _scope = prev;
    }

    // -- declarations ------------------------------------------------------

    private void DeclarePattern(Node pattern, bool isVar)
    {
        switch (pattern)
        {
            case Identifier id:
                Declare(isVar ? _fnScope : _scope, id.Name, id);
                break;
            case ObjectExpression obj:
                foreach (var member in obj.Properties)
                {
                    if (member is SpreadElement spread)
                    {
                        DeclarePattern(spread.Argument, isVar);
                    }
                    else if (member is Property property)
                    {
                        if (property.Computed && property.Key is Expression computedKey)
                        {
                            AnalyzeExpression(computedKey);
                        }

                        if (IsSimpleShorthand(property, out var shorthandId))
                        {
                            var binding = Declare(isVar ? _fnScope : _scope, shorthandId.Name, shorthandId);
                            if (binding is not null)
                            {
                                _shorthands.Add(new Shorthand(property, shorthandId));
                            }
                        }
                        else if (property.Shorthand && property.Key is Identifier keyId)
                        {
                            // `{ x = default }` binding pattern: keep the name to
                            // avoid a lossy expansion, but still shadow correctly.
                            Declare(isVar ? _fnScope : _scope, keyId.Name, keyId, canRename: false);
                            if (property.Value is Expression def && !ReferenceEquals(def, property.Key))
                            {
                                AnalyzeExpression(def);
                            }
                        }
                        else if (property.Value is not null)
                        {
                            DeclarePattern(property.Value, isVar);
                        }
                    }
                }
                break;
            case ArrayExpression arr:
                foreach (var element in arr.Elements)
                {
                    if (element is null)
                    {
                        continue;
                    }
                    if (element is SpreadElement spread)
                    {
                        DeclarePattern(spread.Argument, isVar);
                    }
                    else
                    {
                        DeclarePattern(element, isVar);
                    }
                }
                break;
            case AssignmentExpression assign:
                DeclarePattern(assign.Left, isVar);
                AnalyzeExpression(assign.Right);
                break;
            default:
                // e.g. `for (obj.prop in xs)` — an assignment target, not a binding.
                if (pattern is Expression expression)
                {
                    AnalyzeExpression(expression);
                }
                break;
        }
    }

    private static bool IsSimpleShorthand(Property property, out Identifier id)
    {
        if (property.Shorthand && property.Value is Identifier value && ReferenceEquals(property.Value, property.Key))
        {
            id = value;
            return true;
        }
        id = null!;
        return false;
    }

    // -- statements --------------------------------------------------------

    private void AnalyzeStatement(Statement? statement)
    {
        switch (statement)
        {
            case null:
            case EmptyStatement:
            case DebuggerStatement:
            case TypeOnlyDeclaration:
                break;
            case BlockStatement block:
                EnterBlock(block.Body);
                break;
            case VariableStatement variable:
                AnalyzeVariableStatement(variable);
                break;
            case FunctionDeclaration func:
                if (func.Id is not null)
                {
                    Declare(_scope, func.Id.Name, func.Id);
                }
                EnterFunction(func.Parameters, func.Body, null);
                break;
            case ClassDeclaration cls:
                AnalyzeClass(cls.Id, cls.SuperClass, cls.Body, cls.Decorators, declareName: true);
                break;
            case ExpressionStatement expr:
                AnalyzeExpression(expr.Expression);
                break;
            case ReturnStatement ret:
                if (ret.Argument is not null) AnalyzeExpression(ret.Argument);
                break;
            case IfStatement ifs:
                AnalyzeExpression(ifs.Test);
                AnalyzeStatement(ifs.Consequent);
                AnalyzeStatement(ifs.Alternate);
                break;
            case WhileStatement whi:
                AnalyzeExpression(whi.Test);
                AnalyzeStatement(whi.Body);
                break;
            case DoWhileStatement dow:
                AnalyzeStatement(dow.Body);
                AnalyzeExpression(dow.Test);
                break;
            case ForStatement fors:
                AnalyzeForHead(fors);
                break;
            case ForInStatement forin:
                AnalyzeForInOf(forin.Left, forin.Right, forin.Body);
                break;
            case ForOfStatement forof:
                AnalyzeForInOf(forof.Left, forof.Right, forof.Body);
                break;
            case ThrowStatement thr:
                AnalyzeExpression(thr.Argument);
                break;
            case TryStatement trys:
                AnalyzeStatement(trys.Block);
                if (trys.Handler is not null)
                {
                    var prev = _scope;
                    _scope = new Scope { Parent = prev, IsFunction = false };
                    if (trys.Handler.Param is not null) DeclarePattern(trys.Handler.Param, isVar: false);
                    foreach (var s in trys.Handler.Body.Body) AnalyzeStatement(s);
                    _scope = prev;
                }
                if (trys.Finalizer is not null) AnalyzeStatement(trys.Finalizer);
                break;
            case SwitchStatement sw:
                AnalyzeExpression(sw.Discriminant);
                var prevScope = _scope;
                _scope = new Scope { Parent = prevScope, IsFunction = false };
                foreach (var c in sw.Cases)
                {
                    if (c.Test is not null) AnalyzeExpression(c.Test);
                    foreach (var s in c.Body) AnalyzeStatement(s);
                }
                _scope = prevScope;
                break;
            case LabeledStatement lab:
                AnalyzeStatement(lab.Body);
                break;
            case ImportDeclaration import:
                foreach (var spec in import.Specifiers) AddName(spec.Local.Name);
                break;
            case ExportNamedDeclaration en:
                if (en.Declaration is not null) AnalyzeStatement(en.Declaration);
                foreach (var spec in en.Specifiers)
                {
                    if (spec.Local is Identifier li) AddName(li.Name);
                }
                break;
            case ExportDefaultDeclaration ed:
                if (ed.Declaration is Statement ds) AnalyzeStatement(ds);
                else if (ed.Declaration is Expression de) AnalyzeExpression(de);
                break;
            case ExportAllDeclaration:
                break;
            default:
                break;
        }
    }

    private void AnalyzeVariableStatement(VariableStatement variable)
    {
        var isVar = variable.DeclarationKind == VariableKind.Var;
        foreach (var declarator in variable.Declarations)
        {
            DeclarePattern(declarator.Id, isVar);
            if (declarator.Init is not null)
            {
                AnalyzeExpression(declarator.Init);
            }
        }
    }

    private void AnalyzeForHead(ForStatement fors)
    {
        var prev = _scope;
        _scope = new Scope { Parent = prev, IsFunction = false };
        if (fors.Init is VariableStatement vs) AnalyzeVariableStatement(vs);
        else if (fors.Init is Expression e) AnalyzeExpression(e);
        if (fors.Test is not null) AnalyzeExpression(fors.Test);
        if (fors.Update is not null) AnalyzeExpression(fors.Update);
        AnalyzeStatement(fors.Body);
        _scope = prev;
    }

    private void AnalyzeForInOf(Node left, Expression right, Statement body)
    {
        var prev = _scope;
        _scope = new Scope { Parent = prev, IsFunction = false };
        if (left is VariableStatement vs) AnalyzeVariableStatement(vs);
        else if (left is Expression e) AnalyzeExpression(e);
        AnalyzeExpression(right);
        AnalyzeStatement(body);
        _scope = prev;
    }

    private void AnalyzeClass(Identifier? id, Expression? superClass, ClassBody body, IList<Decorator> decorators, bool declareName)
    {
        foreach (var decorator in decorators) AnalyzeExpression(decorator.Expression);

        if (id is not null)
        {
            if (declareName) Declare(_scope, id.Name, id);
            else AddName(id.Name);
        }
        if (superClass is not null) AnalyzeExpression(superClass);

        foreach (var member in body.Members)
        {
            switch (member)
            {
                case MethodDefinition m:
                    foreach (var d in m.Decorators) AnalyzeExpression(d.Expression);
                    if (m.Computed && m.Key is Expression mk) AnalyzeExpression(mk);
                    else AddKeyName(m.Key);
                    EnterFunction(m.Value.Parameters, m.Value.Body, null);
                    break;
                case PropertyDefinition f:
                    foreach (var d in f.Decorators) AnalyzeExpression(d.Expression);
                    if (f.Computed && f.Key is Expression fk) AnalyzeExpression(fk);
                    else AddKeyName(f.Key);
                    if (f.Value is not null) AnalyzeExpression(f.Value);
                    break;
                case StaticBlock sb:
                    EnterFunctionBody(sb.Body);
                    break;
            }
        }
    }

    private void EnterFunctionBody(IList<Statement> statements)
    {
        var prevScope = _scope;
        var prevFn = _fnScope;
        var fn = new Scope { Parent = prevScope, IsFunction = true };
        _scope = fn;
        _fnScope = fn;
        foreach (var s in statements) AnalyzeStatement(s);
        _scope = prevScope;
        _fnScope = prevFn;
    }

    // -- expressions -------------------------------------------------------

    private void AnalyzeExpression(Expression? expression)
    {
        switch (expression)
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
                Use(id);
                break;
            case TemplateLiteral tpl:
                foreach (var e in tpl.Expressions) AnalyzeExpression(e);
                break;
            case TaggedTemplateExpression tag:
                AnalyzeExpression(tag.Tag);
                foreach (var e in tag.Quasi.Expressions) AnalyzeExpression(e);
                break;
            case ArrayExpression arr:
                foreach (var el in arr.Elements) if (el is not null) AnalyzeExpression(el);
                break;
            case ObjectExpression obj:
                AnalyzeObjectExpression(obj);
                break;
            case SpreadElement spread:
                AnalyzeExpression(spread.Argument);
                break;
            case ParenthesizedExpression paren:
                AnalyzeExpression(paren.Expression);
                break;
            case MemberExpression member:
                AnalyzeExpression(member.Object);
                if (member.Computed && member.Property is Expression prop) AnalyzeExpression(prop);
                else AddKeyName(member.Property);
                break;
            case CallExpression call:
                AnalyzeExpression(call.Callee);
                foreach (var a in call.Arguments) AnalyzeExpression(a);
                break;
            case NewExpression neww:
                AnalyzeExpression(neww.Callee);
                foreach (var a in neww.Arguments) AnalyzeExpression(a);
                break;
            case ImportExpression imp:
                AnalyzeExpression(imp.Source);
                break;
            case UnaryExpression un:
                AnalyzeExpression(un.Argument);
                break;
            case UpdateExpression up:
                AnalyzeExpression(up.Argument);
                break;
            case AwaitExpression aw:
                AnalyzeExpression(aw.Argument);
                break;
            case YieldExpression yi:
                if (yi.Argument is not null) AnalyzeExpression(yi.Argument);
                break;
            case BinaryExpression bin:
                AnalyzeExpression(bin.Left);
                AnalyzeExpression(bin.Right);
                break;
            case LogicalExpression log:
                AnalyzeExpression(log.Left);
                AnalyzeExpression(log.Right);
                break;
            case AssignmentExpression asg:
                AnalyzeExpression(asg.Left);
                AnalyzeExpression(asg.Right);
                break;
            case ConditionalExpression cond:
                AnalyzeExpression(cond.Test);
                AnalyzeExpression(cond.Consequent);
                AnalyzeExpression(cond.Alternate);
                break;
            case SequenceExpression seq:
                foreach (var e in seq.Expressions) AnalyzeExpression(e);
                break;
            case FunctionExpression fn:
                EnterFunction(fn.Parameters, fn.Body, fn.Id);
                break;
            case ArrowFunctionExpression arrow:
                EnterFunction(arrow.Parameters, arrow.Body, null);
                break;
            case ClassExpression ce:
                AnalyzeClass(ce.Id, ce.SuperClass, ce.Body, System.Array.Empty<Decorator>(), declareName: false);
                break;
            case JsxElement jsx:
                AnalyzeJsxElement(jsx);
                break;
            case JsxFragment frag:
                foreach (var c in frag.Children) AnalyzeJsxChild(c);
                break;
            default:
                break;
        }
    }

    private void AnalyzeObjectExpression(ObjectExpression obj)
    {
        foreach (var member in obj.Properties)
        {
            if (member is SpreadElement spread)
            {
                AnalyzeExpression(spread.Argument);
                continue;
            }
            if (member is not Property property)
            {
                continue;
            }

            if (property.Computed && property.Key is Expression computedKey)
            {
                AnalyzeExpression(computedKey);
            }
            else
            {
                AddKeyName(property.Key);
            }

            if (property.Method && property.Value is FunctionExpression method)
            {
                EnterFunction(method.Parameters, method.Body, null);
            }
            else if (IsSimpleShorthand(property, out var shorthandId))
            {
                Use(shorthandId);
                _shorthands.Add(new Shorthand(property, shorthandId));
            }
            else if (property.Value is Expression value)
            {
                AnalyzeExpression(value);
            }
        }
    }

    private void AddKeyName(Node key)
    {
        switch (key)
        {
            case Identifier id: AddName(id.Name); break;
            case PrivateIdentifier pid: AddName(pid.Name); break;
        }
    }

    private void AnalyzeJsxElement(JsxElement jsx)
    {
        foreach (var attribute in jsx.OpeningElement.Attributes)
        {
            if (attribute is JsxSpreadAttribute spread) AnalyzeExpression(spread.Argument);
            else if (attribute is JsxAttribute { Value: JsxExpressionContainer { Expression: { } e } }) AnalyzeExpression(e);
        }
        foreach (var child in jsx.Children) AnalyzeJsxChild(child);
    }

    private void AnalyzeJsxChild(Node child)
    {
        if (child is JsxExpressionContainer { Expression: { } e }) AnalyzeExpression(e);
        else if (child is JsxElement el) AnalyzeJsxElement(el);
        else if (child is JsxFragment fr) foreach (var c in fr.Children) AnalyzeJsxChild(c);
    }

    // -- resolution + renaming --------------------------------------------

    private void Resolve()
    {
        foreach (var reference in _references)
        {
            var scope = reference.Scope;
            while (scope is not null)
            {
                if (scope.Decls.TryGetValue(reference.Node.Name, out var binding))
                {
                    binding.Nodes.Add(reference.Node);
                    break;
                }
                scope = scope.Parent;
            }
            // Unresolved => free/global; left untouched.
        }
    }

    private void AssignNames()
    {
        var generator = new NameGenerator(_allNames);
        foreach (var binding in _bindings)
        {
            if (binding.CanRename)
            {
                binding.NewName = generator.Next();
            }
        }
    }

    private void Apply()
    {
        // Which identifier nodes are being renamed, and to what.
        var renames = new Dictionary<Identifier, string>(ReferenceEqualityComparer.Instance);
        foreach (var binding in _bindings)
        {
            if (binding.NewName is null)
            {
                continue;
            }
            foreach (var node in binding.Nodes)
            {
                renames[node] = binding.NewName;
            }
        }

        // Expand `{ x }` to `{ x: <new> }` before renaming, so the key keeps the
        // original name while the value picks up the mangled one.
        foreach (var shorthand in _shorthands)
        {
            if (renames.ContainsKey(shorthand.Value))
            {
                shorthand.Property.Shorthand = false;
                shorthand.Property.Key = new Identifier(shorthand.Value.Name)
                {
                    Start = shorthand.Value.Start,
                    End = shorthand.Value.End,
                };
            }
        }

        foreach (var pair in renames)
        {
            pair.Key.Name = pair.Value;
        }
    }

    // -- helpers -----------------------------------------------------------

    private sealed class ReferenceEqualityComparer : IEqualityComparer<Identifier>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public bool Equals(Identifier? x, Identifier? y) => ReferenceEquals(x, y);
        public int GetHashCode(Identifier obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class NameGenerator
    {
        private const string First = "abcdefghijklmnopqrstuvwxyz";
        private const string Rest = "abcdefghijklmnopqrstuvwxyz0123456789";
        private static readonly HashSet<string> Reserved = new(System.StringComparer.Ordinal)
        {
            "do", "if", "in", "for", "let", "new", "try", "var", "case", "else", "enum",
            "eval", "null", "this", "true", "void", "with", "await", "break", "catch",
            "class", "const", "false", "super", "throw", "while", "yield", "delete",
            "export", "import", "public", "return", "static", "switch", "typeof",
            "default", "extends", "finally", "package", "private", "continue", "debugger",
            "function", "arguments", "interface", "protected", "implements", "instanceof",
        };

        private readonly HashSet<string> _taken;
        private int _counter;

        public NameGenerator(HashSet<string> taken) => _taken = taken;

        public string Next()
        {
            while (true)
            {
                var name = Encode(_counter++);
                if (!Reserved.Contains(name) && !_taken.Contains(name))
                {
                    return name;
                }
            }
        }

        private static string Encode(int n)
        {
            var sb = new StringBuilder(4);
            sb.Append(First[n % 26]);
            n /= 26;
            while (n > 0)
            {
                n--;
                sb.Append(Rest[n % 36]);
                n /= 36;
            }
            return sb.ToString();
        }
    }
}
