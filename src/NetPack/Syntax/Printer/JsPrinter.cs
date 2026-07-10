namespace NetPack.Syntax.Printer;

using System.Collections.Generic;
using System.Text;
using NetPack.Syntax;
using NetPack.Syntax.Ast;

/// <summary>Options controlling <see cref="JsPrinter"/> output.</summary>
public readonly record struct PrinterOptions
{
    /// <summary>Emit compact output: no indentation, minimal whitespace. This is
    /// the whitespace-level minification NetPack applies for optimized builds.</summary>
    public bool Minify { get; init; }

    public static PrinterOptions Pretty => new() { Minify = false };
    public static PrinterOptions Compact => new() { Minify = true };
}

/// <summary>
/// Generates JavaScript source text from a NetPack AST. This replaces
/// Acornima's <c>ToJsx()</c> serializer. It is precedence-aware, so it inserts
/// the minimal parentheses required for correctness even for synthetic trees
/// produced by the bundler (which do not carry explicit parenthesis nodes).
/// </summary>
public sealed class JsPrinter
{
    private readonly StringBuilder _sb = new();
    private readonly bool _min;
    private int _indent;

    // Source-map state (only tracked when a builder is attached).
    private readonly SourceMapBuilder? _map;
    private readonly Stack<SourceFile> _sources = new();
    private int _genLine;
    private int _genColumn;

    public JsPrinter(PrinterOptions options = default, SourceMapBuilder? map = null)
    {
        _min = options.Minify;
        _map = map;
    }

    public static string Print(Node node, PrinterOptions options = default)
    {
        var printer = new JsPrinter(options);
        printer.Emit(node);
        return printer._sb.ToString();
    }

    /// <summary>Prints and simultaneously fills <paramref name="map"/> with a
    /// source map for the generated output.</summary>
    public static string Print(Node node, PrinterOptions options, SourceMapBuilder map)
    {
        var printer = new JsPrinter(options, map);
        printer.Emit(node);
        return printer._sb.ToString();
    }

    private void Emit(Node node)
    {
        switch (node)
        {
            case SourceFile file:
                PrintStatements(file.Body, topLevel: true);
                break;
            case Statement statement:
                PrintStatement(statement);
                break;
            case Expression expression:
                PrintExpression(expression, Prec.Sequence);
                break;
            default:
                _sb.Append(JsPrinter.Print(WrapUnknown(node)));
                break;
        }
    }

    private static Expression WrapUnknown(Node node) => node as Expression ?? new Raw(string.Empty);

    // -- output primitives -------------------------------------------------

    private void Write(string text)
    {
        _sb.Append(text);
        if (_map is not null) Advance(text);
    }

    private void Write(char c)
    {
        _sb.Append(c);
        if (_map is not null)
        {
            if (c == '\n') { _genLine++; _genColumn = 0; }
            else _genColumn++;
        }
    }

    private void Advance(string text)
    {
        foreach (var c in text)
        {
            if (c == '\n') { _genLine++; _genColumn = 0; }
            else _genColumn++;
        }
    }

    private void Space()
    {
        if (!_min) Write(' ');
    }

    private void SpaceOrNewLine()
    {
        if (_min) return;
        NewLine();
    }

    private void NewLine()
    {
        if (_min)
        {
            return;
        }
        Write('\n');
        for (var i = 0; i < _indent; i++)
        {
            Write("  ");
        }
    }

    private void Semicolon()
    {
        Write(';');
    }

    /// <summary>Records a source-map segment for <paramref name="node"/> at the
    /// current generated position, attributing it to the source currently on the
    /// stack (a module factory body). Skips synthetic nodes (empty span).</summary>
    private void Mark(Node node)
    {
        if (_map is null || _sources.Count == 0 || node.End <= node.Start)
        {
            return;
        }
        var source = _sources.Peek();
        if (source.Source.Length == 0 || node.Start >= source.Source.Length)
        {
            return;
        }
        var (line, column) = source.GetLineColumn(node.Start);
        _map.AddMapping(_genLine, _genColumn, source, line, column);
    }

    // -- statements --------------------------------------------------------

    private void PrintStatements(IList<Statement> body, bool topLevel)
    {
        for (var i = 0; i < body.Count; i++)
        {
            if (body[i] is EmptyStatement)
            {
                continue;
            }
            if (i > 0)
            {
                NewLine();
            }
            PrintStatement(body[i]);
        }
    }

    private void PrintStatement(Statement statement)
    {
        Mark(statement);
        switch (statement)
        {
            case EmptyStatement:
                break;
            case DebuggerStatement:
                Write("debugger");
                Semicolon();
                break;
            case ExpressionStatement s:
                PrintExpressionStatement(s);
                break;
            case BlockStatement s:
                PrintBlock(s);
                break;
            case VariableStatement s:
                PrintVariableStatement(s);
                Semicolon();
                break;
            case FunctionDeclaration s:
                PrintFunction(s.Id, s.Parameters, s.Body, s.Async, s.Generator, isDeclaration: true);
                break;
            case ClassDeclaration s:
                PrintClass(s.Decorators, s.Id, s.SuperClass, s.Body);
                break;
            case ReturnStatement s:
                Write("return");
                if (s.Argument is not null)
                {
                    Write(' ');
                    PrintExpression(s.Argument, Prec.Sequence);
                }
                Semicolon();
                break;
            case IfStatement s:
                PrintIf(s);
                break;
            case ForStatement s:
                PrintFor(s);
                break;
            case ForInStatement s:
                PrintForIn(s);
                break;
            case ForOfStatement s:
                PrintForOf(s);
                break;
            case WhileStatement s:
                Write("while");
                Space();
                Write('(');
                PrintExpression(s.Test, Prec.Sequence);
                Write(')');
                PrintBody(s.Body);
                break;
            case DoWhileStatement s:
                Write("do");
                PrintBody(s.Body);
                SpaceOrNewLine();
                Write("while");
                Space();
                Write('(');
                PrintExpression(s.Test, Prec.Sequence);
                Write(')');
                Semicolon();
                break;
            case ThrowStatement s:
                Write("throw ");
                PrintExpression(s.Argument, Prec.Sequence);
                Semicolon();
                break;
            case TryStatement s:
                PrintTry(s);
                break;
            case SwitchStatement s:
                PrintSwitch(s);
                break;
            case BreakStatement s:
                Write("break");
                if (s.Label is not null) Write(" " + s.Label);
                Semicolon();
                break;
            case ContinueStatement s:
                Write("continue");
                if (s.Label is not null) Write(" " + s.Label);
                Semicolon();
                break;
            case LabeledStatement s:
                Write(s.Label);
                Write(':');
                Space();
                PrintStatement(s.Body);
                break;
            case ImportDeclaration s:
                PrintImportDeclaration(s);
                break;
            case ExportNamedDeclaration s:
                PrintExportNamed(s);
                break;
            case ExportDefaultDeclaration s:
                PrintExportDefault(s);
                break;
            case ExportAllDeclaration s:
                PrintExportAll(s);
                break;
            case TypeOnlyDeclaration:
                // Erased TypeScript declaration — emits nothing.
                break;
            default:
                // Unknown statement: best-effort, avoid crashing the build.
                break;
        }
    }

    private void PrintExpressionStatement(ExpressionStatement s)
    {
        // Guard against a statement that would be misparsed if it began with
        // `{`, `function`, or `class`.
        var needsParens = StartsWithProblematicToken(s.Expression);
        if (needsParens) Write('(');
        PrintExpression(s.Expression, Prec.Sequence);
        if (needsParens) Write(')');
        Semicolon();
    }

    // True when the expression's *leftmost* token is `{`, `function` or `class`,
    // which would make an expression statement be misparsed as a block or a
    // (nameless) declaration. This must follow the left spine of the expression,
    // e.g. the callee of an IIFE `(function(){})()` or the object of `{}.foo`.
    private static bool StartsWithProblematicToken(Expression e) => e switch
    {
        ObjectExpression => true,
        FunctionExpression => true,
        ClassExpression => true,
        // The printer strips parentheses and relies on precedence to re-add them,
        // so a parenthesized function/object/class is still leftmost here.
        ParenthesizedExpression p => StartsWithProblematicToken(p.Expression),
        CallExpression c => StartsWithProblematicToken(c.Callee),
        NewExpression => false, // begins with the `new` keyword
        MemberExpression m => StartsWithProblematicToken(m.Object),
        BinaryExpression b => StartsWithProblematicToken(b.Left),
        LogicalExpression l => StartsWithProblematicToken(l.Left),
        AssignmentExpression a => StartsWithProblematicToken(a.Left),
        ConditionalExpression c => StartsWithProblematicToken(c.Test),
        SequenceExpression s => s.Expressions.Count > 0 && StartsWithProblematicToken(s.Expressions[0]),
        TaggedTemplateExpression t => StartsWithProblematicToken(t.Tag),
        UpdateExpression u => !u.Prefix && StartsWithProblematicToken(u.Argument),
        _ => false,
    };

    private void PrintBlock(BlockStatement block)
    {
        // A factory body carries its module's source; make it the current source
        // for mappings emitted while the block is printed.
        var pushed = _map is not null && block.Source is not null;
        if (pushed) _sources.Push(block.Source!);

        Write('{');
        if (block.Body.Count == 0)
        {
            Write('}');
            if (pushed) _sources.Pop();
            return;
        }
        _indent++;
        foreach (var statement in block.Body)
        {
            if (statement is EmptyStatement) continue;
            NewLine();
            PrintStatement(statement);
        }
        _indent--;
        NewLine();
        Write('}');

        if (pushed) _sources.Pop();
    }

    /// <summary>Prints a statement as the body of a control-flow construct.</summary>
    private void PrintBody(Statement body)
    {
        if (body is BlockStatement block)
        {
            Space();
            PrintBlock(block);
        }
        else if (body is EmptyStatement)
        {
            Semicolon();
        }
        else
        {
            _indent++;
            NewLine();
            PrintStatement(body);
            _indent--;
        }
    }

    private void PrintVariableStatement(VariableStatement s)
    {
        Write(s.DeclarationKind switch
        {
            VariableKind.Var => "var",
            VariableKind.Let => "let",
            _ => "const",
        });
        Write(' ');
        for (var i = 0; i < s.Declarations.Count; i++)
        {
            if (i > 0)
            {
                Write(',');
                Space();
            }
            var d = s.Declarations[i];
            PrintBindingTarget(d.Id);
            if (d.Init is not null)
            {
                Space();
                Write('=');
                Space();
                PrintExpression(d.Init, Prec.Assignment);
            }
        }
    }

    private void PrintIf(IfStatement s)
    {
        Write("if");
        Space();
        Write('(');
        PrintExpression(s.Test, Prec.Sequence);
        Write(')');
        PrintBody(s.Consequent);
        if (s.Alternate is not null)
        {
            SpaceOrNewLine();
            Write("else");
            if (s.Alternate is IfStatement)
            {
                Write(' ');
                PrintStatement(s.Alternate);
            }
            else
            {
                PrintBody(s.Alternate);
            }
        }
    }

    private void PrintFor(ForStatement s)
    {
        Write("for");
        Space();
        Write('(');
        if (s.Init is VariableStatement vs) PrintVariableStatement(vs);
        else if (s.Init is Expression e) PrintExpression(e, Prec.Sequence);
        Semicolon();
        if (s.Test is not null) { Space(); PrintExpression(s.Test, Prec.Sequence); }
        Semicolon();
        if (s.Update is not null) { Space(); PrintExpression(s.Update, Prec.Sequence); }
        Write(')');
        PrintBody(s.Body);
    }

    private void PrintForIn(ForInStatement s)
    {
        Write("for");
        Space();
        Write('(');
        PrintForTarget(s.Left);
        Write(" in ");
        PrintExpression(s.Right, Prec.Sequence);
        Write(')');
        PrintBody(s.Body);
    }

    private void PrintForOf(ForOfStatement s)
    {
        Write("for");
        if (s.Await) Write(" await");
        Space();
        Write('(');
        PrintForTarget(s.Left);
        Write(" of ");
        PrintExpression(s.Right, Prec.Assignment);
        Write(')');
        PrintBody(s.Body);
    }

    private void PrintForTarget(Node left)
    {
        if (left is VariableStatement vs) PrintVariableStatement(vs);
        else if (left is Expression e) PrintExpression(e, Prec.Assignment);
        else PrintBindingTarget(left);
    }

    private void PrintTry(TryStatement s)
    {
        Write("try");
        Space();
        PrintBlock(s.Block);
        if (s.Handler is not null)
        {
            Space();
            Write("catch");
            if (s.Handler.Param is not null)
            {
                Space();
                Write('(');
                PrintBindingTarget(s.Handler.Param);
                Write(')');
            }
            Space();
            PrintBlock(s.Handler.Body);
        }
        if (s.Finalizer is not null)
        {
            Space();
            Write("finally");
            Space();
            PrintBlock(s.Finalizer);
        }
    }

    private void PrintSwitch(SwitchStatement s)
    {
        Write("switch");
        Space();
        Write('(');
        PrintExpression(s.Discriminant, Prec.Sequence);
        Write(')');
        Space();
        Write('{');
        _indent++;
        foreach (var c in s.Cases)
        {
            NewLine();
            if (c.Test is not null)
            {
                Write("case ");
                PrintExpression(c.Test, Prec.Sequence);
                Write(':');
            }
            else
            {
                Write("default:");
            }
            _indent++;
            foreach (var st in c.Body)
            {
                if (st is EmptyStatement) continue;
                NewLine();
                PrintStatement(st);
            }
            _indent--;
        }
        _indent--;
        NewLine();
        Write('}');
    }

    // -- module statements -------------------------------------------------

    private void PrintImportDeclaration(ImportDeclaration s)
    {
        Write("import");
        if (s.Specifiers.Count == 0)
        {
            Write(' ');
            PrintString(s.Source.Value);
            Semicolon();
            return;
        }

        Write(' ');
        var wroteNamedOpen = false;
        var first = true;
        foreach (var spec in s.Specifiers)
        {
            switch (spec)
            {
                case ImportDefaultSpecifier def:
                    if (!first) Write(", ");
                    Write(def.Local.Name);
                    first = false;
                    break;
                case ImportNamespaceSpecifier ns:
                    if (!first) Write(", ");
                    Write("* as ");
                    Write(ns.Local.Name);
                    first = false;
                    break;
                case ImportSpecifier named:
                    if (!wroteNamedOpen)
                    {
                        if (!first) Write(", ");
                        Write('{');
                        Space();
                        wroteNamedOpen = true;
                        first = true;
                    }
                    if (!first) Write(", ");
                    PrintModuleName(named.Imported);
                    if (!(named.Imported is Identifier ii && ii.Name == named.Local.Name))
                    {
                        Write(" as ");
                        Write(named.Local.Name);
                    }
                    first = false;
                    break;
            }
        }
        if (wroteNamedOpen)
        {
            Space();
            Write('}');
        }
        Write(" from ");
        PrintString(s.Source.Value);
        Semicolon();
    }

    private void PrintExportNamed(ExportNamedDeclaration s)
    {
        if (s.Declaration is not null)
        {
            Write("export ");
            PrintStatement(s.Declaration);
            return;
        }
        Write("export ");
        Write('{');
        Space();
        for (var i = 0; i < s.Specifiers.Count; i++)
        {
            if (i > 0) Write(", ");
            var spec = s.Specifiers[i];
            PrintModuleName(spec.Local);
            if (!SameName(spec.Local, spec.Exported))
            {
                Write(" as ");
                PrintModuleName(spec.Exported);
            }
        }
        Space();
        Write('}');
        if (s.Source is not null)
        {
            Write(" from ");
            PrintString(s.Source.Value);
        }
        Semicolon();
    }

    private void PrintExportDefault(ExportDefaultDeclaration s)
    {
        Write("export default ");
        if (s.Declaration is Statement statement)
        {
            PrintStatement(statement);
        }
        else if (s.Declaration is Expression expression)
        {
            PrintExpression(expression, Prec.Assignment);
            Semicolon();
        }
    }

    private void PrintExportAll(ExportAllDeclaration s)
    {
        Write("export * ");
        if (s.Exported is not null)
        {
            Write("as ");
            Write(s.Exported.Name);
            Write(' ');
        }
        Write("from ");
        PrintString(s.Source.Value);
        Semicolon();
    }

    private void PrintModuleName(Node node)
    {
        if (node is StringLiteral str) PrintString(str.Value);
        else if (node is Identifier id) Write(id.Name);
    }

    private static bool SameName(Node a, Node b)
        => a is Identifier ia && b is Identifier ib && ia.Name == ib.Name;

    // -- expressions -------------------------------------------------------

    private void PrintExpression(Expression expression, Prec minPrecedence)
    {
        // Drop explicit parenthesis nodes; precedence re-inserts the minimal
        // parentheses required, which keeps synthetic (bundler-built) trees
        // correct too.
        while (expression is ParenthesizedExpression paren)
        {
            expression = paren.Expression;
        }
        Mark(expression);
        var precedence = ExpressionPrecedence(expression);
        var wrap = precedence < minPrecedence;
        if (wrap) Write('(');
        PrintExpressionCore(expression);
        if (wrap) Write(')');
    }

    private void PrintExpressionCore(Expression expression)
    {
        switch (expression)
        {
            case Identifier e: Write(e.Name); break;
            case PrivateIdentifier e: Write(e.Name); break;
            case NumericLiteral e: Write(e.Raw); break;
            case BigIntLiteral e: Write(e.Raw); break;
            case StringLiteral e: PrintString(e.Value); break;
            case BooleanLiteral e: Write(e.Value ? "true" : "false"); break;
            case NullLiteral: Write("null"); break;
            case RegExpLiteral e: Write(e.Raw); break;
            case ThisExpression: Write("this"); break;
            case SuperExpression: Write("super"); break;
            case Raw e: Write(e.Text); break;
            case MetaProperty e: Write(e.Meta); Write('.'); Write(e.Property); break;
            case TemplateLiteral e: PrintTemplate(e); break;
            case TaggedTemplateExpression e: PrintExpression(e.Tag, Prec.Call); PrintTemplate(e.Quasi); break;
            case ArrayExpression e: PrintArray(e); break;
            case ObjectExpression e: PrintObject(e); break;
            case SpreadElement e: Write("..."); PrintExpression(e.Argument, Prec.Assignment); break;
            case ParenthesizedExpression e: PrintExpressionCore(e.Expression); break;
            case SequenceExpression e: PrintSequence(e); break;
            case AssignmentExpression e: PrintAssignment(e); break;
            case ConditionalExpression e: PrintConditional(e); break;
            case LogicalExpression e: PrintBinaryLike(e.Operator, e.Left, e.Right); break;
            case BinaryExpression e: PrintBinaryLike(e.Operator, e.Left, e.Right); break;
            case UnaryExpression e: PrintUnary(e); break;
            case UpdateExpression e: PrintUpdate(e); break;
            case MemberExpression e: PrintMember(e); break;
            case CallExpression e: PrintCall(e); break;
            case NewExpression e: PrintNew(e); break;
            case ImportExpression e: Write("import("); PrintExpression(e.Source, Prec.Assignment); Write(')'); break;
            case AwaitExpression e: Write("await "); PrintExpression(e.Argument, Prec.Unary); break;
            case YieldExpression e: PrintYield(e); break;
            case FunctionExpression e: PrintFunction(e.Id, e.Parameters, e.Body, e.Async, e.Generator, isDeclaration: false); break;
            case ArrowFunctionExpression e: PrintArrow(e); break;
            case ClassExpression e: PrintClass(System.Array.Empty<Decorator>(), e.Id, e.SuperClass, e.Body); break;
            case JsxElement e: PrintJsxElement(e); break;
            case JsxFragment e: PrintJsxFragment(e); break;
            default: break;
        }
    }

    private void PrintSequence(SequenceExpression e)
    {
        var first = true;
        foreach (var expression in e.Expressions)
        {
            if (expression is null) continue; // never emit a stray comma
            if (!first) { Write(','); Space(); }
            first = false;
            PrintExpression(expression, Prec.Assignment);
        }
    }

    private void PrintAssignment(AssignmentExpression e)
    {
        PrintExpression(e.Left, Prec.Call);
        Space();
        Write(OperatorText(e.Operator));
        Space();
        PrintExpression(e.Right, Prec.Assignment);
    }

    private void PrintConditional(ConditionalExpression e)
    {
        PrintExpression(e.Test, Prec.Coalesce);
        Space();
        Write('?');
        Space();
        PrintExpression(e.Consequent, Prec.Assignment);
        Space();
        Write(':');
        Space();
        PrintExpression(e.Alternate, Prec.Assignment);
    }

    private void PrintBinaryLike(TokenKind op, Expression left, Expression right)
    {
        var (precedence, rightAssociative) = BinaryInfo(op);
        PrintExpression(left, rightAssociative ? precedence + 1 : precedence);
        var text = OperatorText(op);
        var word = IsWordOperator(op);
        if (word) Write(' '); else Space();
        Write(text);
        if (word) Write(' '); else Space();
        PrintExpression(right, rightAssociative ? precedence : precedence + 1);
    }

    private void PrintUnary(UnaryExpression e)
    {
        var text = OperatorText(e.Operator);
        Write(text);
        if (IsWordOperator(e.Operator)) Write(' ');
        PrintExpression(e.Argument, Prec.Unary);
    }

    private void PrintUpdate(UpdateExpression e)
    {
        if (e.Prefix)
        {
            Write(OperatorText(e.Operator));
            PrintExpression(e.Argument, Prec.Unary);
        }
        else
        {
            PrintExpression(e.Argument, Prec.Postfix);
            Write(OperatorText(e.Operator));
        }
    }

    private void PrintMember(MemberExpression e)
    {
        PrintExpression(e.Object, Prec.Call);
        if (e.Computed)
        {
            if (e.Optional) Write("?.");
            Write('[');
            if (e.Property is Expression pe) PrintExpression(pe, Prec.Sequence);
            Write(']');
        }
        else
        {
            Write(e.Optional ? "?." : ".");
            if (e.Property is Identifier id) Write(id.Name);
            else if (e.Property is PrivateIdentifier pid) Write(pid.Name);
        }
    }

    private void PrintCall(CallExpression e)
    {
        PrintExpression(e.Callee, Prec.Call);
        if (e.Optional) Write("?.");
        PrintArguments(e.Arguments);
    }

    private void PrintNew(NewExpression e)
    {
        Write("new ");
        // A call-expression callee must be parenthesized to bind `new` correctly.
        var calleePrec = e.Callee is CallExpression ? Prec.Primary : Prec.Call;
        PrintExpression(e.Callee, calleePrec);
        PrintArguments(e.Arguments);
    }

    private void PrintArguments(IList<Expression> args)
    {
        Write('(');
        var first = true;
        foreach (var arg in args)
        {
            if (arg is null) continue; // never emit a stray comma
            if (!first) { Write(','); Space(); }
            first = false;
            PrintExpression(arg, Prec.Assignment);
        }
        Write(')');
    }

    private void PrintYield(YieldExpression e)
    {
        Write("yield");
        if (e.Delegated) Write('*');
        if (e.Argument is not null)
        {
            Write(' ');
            PrintExpression(e.Argument, Prec.Assignment);
        }
    }

    private void PrintArray(ArrayExpression e)
    {
        Write('[');
        for (var i = 0; i < e.Elements.Count; i++)
        {
            if (i > 0) { Write(','); Space(); }
            if (e.Elements[i] is Expression el) PrintExpression(el, Prec.Assignment);
        }
        Write(']');
    }

    private void PrintObject(ObjectExpression e)
    {
        if (e.Properties.Count == 0)
        {
            Write("{}");
            return;
        }
        Write('{');
        Space();
        var first = true;
        foreach (var member in e.Properties)
        {
            // Only Property / SpreadElement render; skipping anything else keeps
            // us from emitting a separator with no member after it.
            if (member is not (SpreadElement or Property)) continue;
            if (!first) { Write(','); Space(); }
            first = false;
            PrintObjectMember(member);
        }
        Space();
        Write('}');
    }

    private void PrintObjectMember(Node member)
    {
        if (member is SpreadElement spread)
        {
            Write("...");
            PrintExpression(spread.Argument, Prec.Assignment);
            return;
        }
        if (member is not Property p)
        {
            return;
        }

        if (p.PropertyKind is PropertyKind.Get or PropertyKind.Set && p.Value is FunctionExpression accessor)
        {
            Write(p.PropertyKind == PropertyKind.Get ? "get " : "set ");
            PrintPropertyKey(p.Key, p.Computed);
            PrintFunctionTail(accessor.Parameters, accessor.Body);
            return;
        }

        if (p.Method && p.Value is FunctionExpression method)
        {
            if (method.Async) Write("async ");
            if (method.Generator) Write('*');
            PrintPropertyKey(p.Key, p.Computed);
            PrintFunctionTail(method.Parameters, method.Body);
            return;
        }

        if (p.Shorthand)
        {
            // Plain shorthand (`{ a }`) stores the key identifier as its own
            // value. A *different* value means a destructuring default such as
            // `{ a = 1 }` or `{ a = b }`, which must keep the `= value` form —
            // printing it as `{ a: 1 }` (or dropping it) is invalid pattern syntax.
            PrintPropertyKey(p.Key, p.Computed);
            if (p.Value is Expression shorthandValue && !IsSameIdentifier(p.Key, shorthandValue))
            {
                Space();
                Write('=');
                Space();
                PrintExpression(shorthandValue, Prec.Assignment);
            }
            return;
        }

        PrintPropertyKey(p.Key, p.Computed);
        Write(':');
        Space();
        if (p.Value is Expression value) PrintExpression(value, Prec.Assignment);
    }

    private static bool IsSameIdentifier(Node key, Expression value)
        => key is Identifier k && value is Identifier v && k.Name == v.Name;

    private void PrintPropertyKey(Node key, bool computed)
    {
        if (computed)
        {
            Write('[');
            if (key is Expression e) PrintExpression(e, Prec.Assignment);
            Write(']');
            return;
        }
        switch (key)
        {
            case Identifier id: Write(id.Name); break;
            case PrivateIdentifier pid: Write(pid.Name); break;
            case StringLiteral str: PrintString(str.Value); break;
            case NumericLiteral num: Write(num.Raw); break;
            default:
                if (key is Expression e) PrintExpression(e, Prec.Assignment);
                break;
        }
    }

    // -- functions / classes ----------------------------------------------

    private void PrintFunction(Identifier? id, IList<Parameter> parameters, BlockStatement body, bool async, bool generator, bool isDeclaration)
    {
        if (async) Write("async ");
        Write("function");
        if (generator) Write('*');
        if (id is not null) { Write(' '); Write(id.Name); }
        else if (!generator) Write(' ');
        PrintParameters(parameters);
        Space();
        PrintBlock(body);
    }

    private void PrintFunctionTail(IList<Parameter> parameters, BlockStatement body)
    {
        PrintParameters(parameters);
        Space();
        PrintBlock(body);
    }

    private void PrintParameters(IList<Parameter> parameters)
    {
        Write('(');
        var first = true;
        foreach (var p in parameters)
        {
            if (p is null) continue; // never emit a stray comma
            if (!first) { Write(','); Space(); }
            first = false;
            if (p.Rest) Write("...");
            PrintBindingTarget(p.Pattern);
            if (p.Initializer is not null)
            {
                Space();
                Write('=');
                Space();
                PrintExpression(p.Initializer, Prec.Assignment);
            }
        }
        Write(')');
    }

    private void PrintArrow(ArrowFunctionExpression e)
    {
        if (e.Async) Write("async ");
        // Always parenthesize the parameter list for simplicity and safety.
        PrintParameters(e.Parameters);
        Space();
        Write("=>");
        Space();
        if (e.Body is BlockStatement block)
        {
            PrintBlock(block);
        }
        else if (e.Body is ObjectExpression obj)
        {
            Write('(');
            PrintObject(obj);
            Write(')');
        }
        else if (e.Body is Expression expr)
        {
            PrintExpression(expr, Prec.Assignment);
        }
    }

    private void PrintClass(IList<Decorator> decorators, Identifier? id, Expression? superClass, ClassBody body)
    {
        foreach (var d in decorators)
        {
            Write('@');
            PrintExpression(d.Expression, Prec.Call);
            SpaceOrNewLine();
        }
        Write("class");
        if (id is not null) { Write(' '); Write(id.Name); }
        if (superClass is not null)
        {
            Write(" extends ");
            PrintExpression(superClass, Prec.Call);
        }
        Space();
        PrintClassBody(body);
    }

    private void PrintClassBody(ClassBody body)
    {
        Write('{');
        if (body.Members.Count == 0)
        {
            Write('}');
            return;
        }
        _indent++;
        foreach (var member in body.Members)
        {
            NewLine();
            PrintClassMember(member);
        }
        _indent--;
        NewLine();
        Write('}');
    }

    private void PrintClassMember(Node member)
    {
        switch (member)
        {
            case StaticBlock sb:
                Write("static");
                Space();
                Write('{');
                _indent++;
                foreach (var st in sb.Body)
                {
                    if (st is EmptyStatement) continue;
                    NewLine();
                    PrintStatement(st);
                }
                _indent--;
                NewLine();
                Write('}');
                break;
            case MethodDefinition m:
                foreach (var d in m.Decorators) { Write('@'); PrintExpression(d.Expression, Prec.Call); Space(); }
                if (m.Static) Write("static ");
                if (m.Value.Async) Write("async ");
                if (m.Value.Generator) Write('*');
                if (m.MethodKind == MethodKind.Get) Write("get ");
                if (m.MethodKind == MethodKind.Set) Write("set ");
                PrintPropertyKey(m.Key, m.Computed);
                PrintFunctionTail(m.Value.Parameters, m.Value.Body);
                break;
            case PropertyDefinition f:
                foreach (var d in f.Decorators) { Write('@'); PrintExpression(d.Expression, Prec.Call); Space(); }
                if (f.Static) Write("static ");
                PrintPropertyKey(f.Key, f.Computed);
                if (f.Value is not null)
                {
                    Space();
                    Write('=');
                    Space();
                    PrintExpression(f.Value, Prec.Assignment);
                }
                Semicolon();
                break;
        }
    }

    private void PrintBindingTarget(Node target)
    {
        switch (target)
        {
            case Identifier id: Write(id.Name); break;
            case ObjectExpression obj: PrintObject(obj); break;
            case ArrayExpression arr: PrintArray(arr); break;
            case AssignmentExpression assign:
                PrintExpression(assign.Left, Prec.Call);
                Space(); Write('='); Space();
                PrintExpression(assign.Right, Prec.Assignment);
                break;
            case Expression e: PrintExpression(e, Prec.Assignment); break;
        }
    }

    // -- templates / strings ----------------------------------------------

    private void PrintTemplate(TemplateLiteral e)
    {
        Write('`');
        for (var i = 0; i < e.Quasis.Count; i++)
        {
            Write(e.Quasis[i].Cooked is { } cooked ? EscapeTemplateChunk(cooked) : string.Empty);
            if (i < e.Expressions.Count)
            {
                Write("${");
                PrintExpression(e.Expressions[i], Prec.Sequence);
                Write('}');
            }
        }
        Write('`');
    }

    private static string EscapeTemplateChunk(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '`': sb.Append("\\`"); break;
                case '\\': sb.Append("\\\\"); break;
                case '$': sb.Append("\\$"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private void PrintString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        Write(sb.ToString());
    }

    // -- jsx (rarely reached: the bundler lowers JSX before printing) ------

    private void PrintJsxElement(JsxElement e)
    {
        Write('<');
        PrintJsxName(e.OpeningElement.Name);
        foreach (var attr in e.OpeningElement.Attributes)
        {
            Write(' ');
            PrintJsxAttribute(attr);
        }
        if (e.OpeningElement.SelfClosing)
        {
            Write(" />");
            return;
        }
        Write('>');
        foreach (var child in e.Children)
        {
            PrintJsxChild(child);
        }
        Write("</");
        if (e.ClosingElement is not null) PrintJsxName(e.ClosingElement.Name);
        Write('>');
    }

    private void PrintJsxFragment(JsxFragment e)
    {
        Write("<>");
        foreach (var child in e.Children) PrintJsxChild(child);
        Write("</>");
    }

    private void PrintJsxChild(Node child)
    {
        switch (child)
        {
            case JsxText t: Write(t.Value); break;
            case JsxExpressionContainer c:
                Write('{');
                if (c.Expression is not null) PrintExpression(c.Expression, Prec.Sequence);
                Write('}');
                break;
            case JsxElement el: PrintJsxElement(el); break;
            case JsxFragment fr: PrintJsxFragment(fr); break;
        }
    }

    private void PrintJsxAttribute(Node attr)
    {
        if (attr is JsxSpreadAttribute spread)
        {
            Write('{');
            Write("...");
            PrintExpression(spread.Argument, Prec.Assignment);
            Write('}');
            return;
        }
        if (attr is JsxAttribute a)
        {
            PrintJsxName(a.Name);
            if (a.Value is StringLiteral str) { Write('='); PrintString(str.Value); }
            else if (a.Value is JsxExpressionContainer c)
            {
                Write("={");
                if (c.Expression is not null) PrintExpression(c.Expression, Prec.Sequence);
                Write('}');
            }
        }
    }

    private void PrintJsxName(JsxName name)
    {
        switch (name)
        {
            case JsxIdentifier id: Write(id.Name); break;
            case JsxMemberExpression m: PrintJsxName(m.Object); Write('.'); Write(m.Property.Name); break;
            case JsxNamespacedName ns: Write(ns.Namespace.Name); Write(':'); Write(ns.Name.Name); break;
        }
    }

    // -- precedence & operators -------------------------------------------

    private enum Prec
    {
        Sequence = 0,
        Assignment = 1,
        Conditional = 2,
        Coalesce = 3,
        LogicalOr = 4,
        LogicalAnd = 5,
        BitOr = 6,
        BitXor = 7,
        BitAnd = 8,
        Equality = 9,
        Relational = 10,
        Shift = 11,
        Additive = 12,
        Multiplicative = 13,
        Exponent = 14,
        Unary = 15,
        Postfix = 16,
        Call = 17,
        Primary = 18,
    }

    private static Prec ExpressionPrecedence(Expression e) => e switch
    {
        SequenceExpression => Prec.Sequence,
        AssignmentExpression => Prec.Assignment,
        ArrowFunctionExpression => Prec.Assignment,
        YieldExpression => Prec.Assignment,
        ConditionalExpression => Prec.Conditional,
        LogicalExpression le => BinaryInfo(le.Operator).Precedence,
        BinaryExpression be => BinaryInfo(be.Operator).Precedence,
        UnaryExpression => Prec.Unary,
        AwaitExpression => Prec.Unary,
        UpdateExpression u => u.Prefix ? Prec.Unary : Prec.Postfix,
        CallExpression => Prec.Call,
        NewExpression ne => ne.Arguments.Count > 0 ? Prec.Call : Prec.Primary,
        MemberExpression => Prec.Call,
        ImportExpression => Prec.Call,
        TaggedTemplateExpression => Prec.Call,
        _ => Prec.Primary,
    };

    private static (Prec Precedence, bool RightAssociative) BinaryInfo(TokenKind op) => op switch
    {
        TokenKind.QuestionQuestion => (Prec.Coalesce, false),
        TokenKind.BarBar => (Prec.LogicalOr, false),
        TokenKind.AmpersandAmpersand => (Prec.LogicalAnd, false),
        TokenKind.Bar => (Prec.BitOr, false),
        TokenKind.Caret => (Prec.BitXor, false),
        TokenKind.Ampersand => (Prec.BitAnd, false),
        TokenKind.EqualsEquals or TokenKind.ExclamationEquals
            or TokenKind.EqualsEqualsEquals or TokenKind.ExclamationEqualsEquals => (Prec.Equality, false),
        TokenKind.LessThan or TokenKind.GreaterThan or TokenKind.LessThanEquals
            or TokenKind.GreaterThanEquals or TokenKind.InstanceOfKeyword or TokenKind.InKeyword => (Prec.Relational, false),
        TokenKind.LessThanLessThan or TokenKind.GreaterThanGreaterThan
            or TokenKind.GreaterThanGreaterThanGreaterThan => (Prec.Shift, false),
        TokenKind.Plus or TokenKind.Minus => (Prec.Additive, false),
        TokenKind.Asterisk or TokenKind.Slash or TokenKind.Percent => (Prec.Multiplicative, false),
        TokenKind.AsteriskAsterisk => (Prec.Exponent, true),
        _ => (Prec.Equality, false),
    };

    private static bool IsWordOperator(TokenKind op) => op switch
    {
        TokenKind.InstanceOfKeyword or TokenKind.InKeyword or TokenKind.TypeOfKeyword
            or TokenKind.VoidKeyword or TokenKind.DeleteKeyword => true,
        _ => false,
    };

    private static string OperatorText(TokenKind op) => op switch
    {
        TokenKind.Plus => "+",
        TokenKind.Minus => "-",
        TokenKind.Asterisk => "*",
        TokenKind.Slash => "/",
        TokenKind.Percent => "%",
        TokenKind.AsteriskAsterisk => "**",
        TokenKind.EqualsEquals => "==",
        TokenKind.ExclamationEquals => "!=",
        TokenKind.EqualsEqualsEquals => "===",
        TokenKind.ExclamationEqualsEquals => "!==",
        TokenKind.LessThan => "<",
        TokenKind.GreaterThan => ">",
        TokenKind.LessThanEquals => "<=",
        TokenKind.GreaterThanEquals => ">=",
        TokenKind.LessThanLessThan => "<<",
        TokenKind.GreaterThanGreaterThan => ">>",
        TokenKind.GreaterThanGreaterThanGreaterThan => ">>>",
        TokenKind.Ampersand => "&",
        TokenKind.Bar => "|",
        TokenKind.Caret => "^",
        TokenKind.AmpersandAmpersand => "&&",
        TokenKind.BarBar => "||",
        TokenKind.QuestionQuestion => "??",
        TokenKind.Exclamation => "!",
        TokenKind.Tilde => "~",
        TokenKind.TypeOfKeyword => "typeof",
        TokenKind.VoidKeyword => "void",
        TokenKind.DeleteKeyword => "delete",
        TokenKind.InstanceOfKeyword => "instanceof",
        TokenKind.InKeyword => "in",
        TokenKind.PlusPlus => "++",
        TokenKind.MinusMinus => "--",
        TokenKind.Equals => "=",
        TokenKind.PlusEquals => "+=",
        TokenKind.MinusEquals => "-=",
        TokenKind.AsteriskEquals => "*=",
        TokenKind.SlashEquals => "/=",
        TokenKind.PercentEquals => "%=",
        TokenKind.AsteriskAsteriskEquals => "**=",
        TokenKind.LessThanLessThanEquals => "<<=",
        TokenKind.GreaterThanGreaterThanEquals => ">>=",
        TokenKind.GreaterThanGreaterThanGreaterThanEquals => ">>>=",
        TokenKind.AmpersandEquals => "&=",
        TokenKind.BarEquals => "|=",
        TokenKind.CaretEquals => "^=",
        TokenKind.AmpersandAmpersandEquals => "&&=",
        TokenKind.BarBarEquals => "||=",
        TokenKind.QuestionQuestionEquals => "??=",
        _ => "?",
    };
}
