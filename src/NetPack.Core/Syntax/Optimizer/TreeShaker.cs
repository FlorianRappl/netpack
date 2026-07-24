namespace NetPack.Syntax.Optimizer;

using System;
using System.Collections.Generic;
using NetPack.Syntax.Ast;

/// <summary>
/// Removes dead top-level code from a single module, driven by the set of
/// exports the rest of the program actually uses (<see cref="UsedExports"/>).
///
/// It is deliberately conservative: side-effecting top-level statements are
/// always kept, and a declaration is only removed when it is provably pure (a
/// function, a class without a super-class, or a <c>const/let/var</c> whose
/// initializer is side-effect-free) and neither a used export nor referenced —
/// directly or transitively — by any code that is kept.
///
/// When told (via <c>isImportPure</c>) that an import targets a side-effect-free
/// module, an unused import of that module is removed too; the removed imports
/// are returned so the caller can recompute module reachability and drop whole
/// unreferenced modules.
/// </summary>
public static class TreeShaker
{
    /// <summary>
    /// Shakes <paramref name="file"/> in place and returns the import
    /// declarations that were removed (imports of side-effect-free modules whose
    /// bindings are all unused).
    /// </summary>
    public static List<ImportDeclaration> Shake(SourceFile file, UsedExports used, Func<ImportDeclaration, bool>? isImportPure = null)
    {
        var removed = new List<ImportDeclaration>();
        var statements = file.Body;
        var infos = new List<StatementInfo>(statements.Count);
        var topNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var statement in statements)
        {
            var info = Classify(statement, isImportPure);
            infos.Add(info);
            foreach (var name in info.Declares) topNames.Add(name);
        }

        foreach (var info in infos)
        {
            info.Uses = ReferenceCollector.Collect(info.Statement, topNames);
        }

        var live = new HashSet<string>(StringComparer.Ordinal);
        var worklist = new Queue<string>();

        void MarkLive(string name)
        {
            if (topNames.Contains(name) && live.Add(name)) worklist.Enqueue(name);
        }

        foreach (var info in infos)
        {
            if (info.ForceKeep)
            {
                foreach (var use in info.Uses) MarkLive(use);
            }
            foreach (var pair in info.Exports)
            {
                if (used.Contains(pair.Key)) MarkLive(pair.Value);
            }
        }

        while (worklist.Count > 0)
        {
            var name = worklist.Dequeue();
            var decl = FindDeclarer(infos, name);
            if (decl is not null)
            {
                foreach (var use in decl.Uses) MarkLive(use);
            }
        }

        var kept = new List<Statement>(statements.Count);
        foreach (var info in infos)
        {
            switch (info.Kind)
            {
                case StatementKind.Import:
                    KeepOrPruneImport(info, live, kept, removed);
                    break;
                case StatementKind.ExportSpecifiers:
                    if (PruneExportSpecifiers(info, used)) kept.Add(info.Statement);
                    break;
                case StatementKind.ExportDefault:
                    if (info.ForceKeep || used.Contains("default")) kept.Add(info.Statement);
                    break;
                default:
                    if (info.ForceKeep || info.Declares.Count == 0 || AnyLive(info.Declares, live))
                    {
                        kept.Add(info.Statement);
                    }
                    break;
            }
        }

        file.Body = kept;
        return removed;
    }

    private static void KeepOrPruneImport(StatementInfo info, HashSet<string> live, List<Statement> kept, List<ImportDeclaration> removed)
    {
        var import = (ImportDeclaration)info.Statement;

        // Import of a side-effectful module: keep it so the module still runs.
        if (info.ForceKeep)
        {
            kept.Add(import);
            return;
        }

        // Side-effect-free bare import (`import './x'`) that does nothing → drop.
        if (import.Specifiers.Count == 0)
        {
            removed.Add(import);
            return;
        }

        var remaining = new List<ImportSpecifierBase>();
        foreach (var specifier in import.Specifiers)
        {
            if (live.Contains(specifier.Local.Name)) remaining.Add(specifier);
        }

        if (remaining.Count == 0)
        {
            removed.Add(import);
            return;
        }

        if (remaining.Count != import.Specifiers.Count)
        {
            import.Specifiers = remaining;
        }
        kept.Add(import);
    }

    private static bool AnyLive(List<string> names, HashSet<string> live)
    {
        foreach (var name in names)
        {
            if (live.Contains(name)) return true;
        }
        return false;
    }

    private static StatementInfo? FindDeclarer(List<StatementInfo> infos, string name)
    {
        foreach (var info in infos)
        {
            if (info.Declares.Contains(name)) return info;
        }
        return null;
    }

    private static bool PruneExportSpecifiers(StatementInfo info, UsedExports used)
    {
        var export = (ExportNamedDeclaration)info.Statement;
        var remaining = new List<ExportSpecifier>();
        foreach (var specifier in export.Specifiers)
        {
            if (used.Contains(ExportedName(specifier))) remaining.Add(specifier);
        }
        export.Specifiers = remaining;
        return remaining.Count > 0;
    }

    // -- module-level purity (for the whole-program pass) -------------------

    /// <summary>True when running this module (its top-level statements, ignoring
    /// what its imports transitively do) would have no observable side effect.</summary>
    internal static bool HasTopLevelSideEffects(SourceFile file)
    {
        foreach (var statement in file.Body)
        {
            if (!IsPureStatement(statement)) return true;
        }
        return false;
    }

    private static bool IsPureStatement(Statement statement) => statement switch
    {
        ImportDeclaration or ExportAllDeclaration or TypeOnlyDeclaration
            or EmptyStatement or FunctionDeclaration => true,
        ClassDeclaration cls => cls.SuperClass is null || IsPure(cls.SuperClass),
        VariableStatement variable => IsPureVariableStatement(variable),
        ExportNamedDeclaration { Declaration: { } declaration } => IsPureStatement(declaration),
        ExportNamedDeclaration => true,
        ExportDefaultDeclaration { Declaration: Expression expr } => IsPure(expr),
        ExportDefaultDeclaration => true,
        _ => false,
    };

    // -- classification ----------------------------------------------------

    private enum StatementKind
    {
        Import,
        Declaration,
        ExportDeclaration,
        ExportSpecifiers,
        ExportDefault,
        ForceKeep,
    }

    private sealed class StatementInfo
    {
        public required Statement Statement;
        public StatementKind Kind;
        public bool ForceKeep;
        public readonly List<string> Declares = new();
        public readonly Dictionary<string, string> Exports = new(StringComparer.Ordinal);
        public HashSet<string> Uses = new(StringComparer.Ordinal);
    }

    private static StatementInfo Classify(Statement statement, Func<ImportDeclaration, bool>? isImportPure)
    {
        var info = new StatementInfo { Statement = statement };

        switch (statement)
        {
            case ImportDeclaration import:
                info.Kind = StatementKind.Import;
                foreach (var specifier in import.Specifiers) info.Declares.Add(specifier.Local.Name);
                // Impure (side-effectful target) imports must always be kept.
                info.ForceKeep = !(isImportPure?.Invoke(import) ?? false);
                break;

            case FunctionDeclaration func:
                info.Kind = StatementKind.Declaration;
                if (func.Id is not null) info.Declares.Add(func.Id.Name);
                break;

            case ClassDeclaration cls:
                info.Kind = StatementKind.Declaration;
                if (cls.Id is not null) info.Declares.Add(cls.Id.Name);
                if (cls.SuperClass is not null && !IsPure(cls.SuperClass)) info.ForceKeep = true;
                break;

            case VariableStatement variable:
                info.Kind = StatementKind.Declaration;
                CollectPatternNames(variable, info.Declares);
                if (!IsPureVariableStatement(variable)) info.ForceKeep = true;
                break;

            case ExportNamedDeclaration { Declaration: { } declaration }:
                info.Kind = StatementKind.ExportDeclaration;
                var innerInfo = Classify(declaration, isImportPure);
                info.Declares.AddRange(innerInfo.Declares);
                info.ForceKeep = innerInfo.ForceKeep;
                foreach (var name in innerInfo.Declares) info.Exports[name] = name;
                break;

            case ExportNamedDeclaration { Source: not null }:
                info.Kind = StatementKind.ForceKeep;
                info.ForceKeep = true;
                break;

            case ExportNamedDeclaration named:
                info.Kind = StatementKind.ExportSpecifiers;
                foreach (var specifier in named.Specifiers)
                {
                    info.Exports[ExportedName(specifier)] = LocalName(specifier);
                }
                break;

            case ExportDefaultDeclaration def:
                info.Kind = StatementKind.ExportDefault;
                if (def.Declaration is FunctionDeclaration { Id: { } fid })
                {
                    info.Declares.Add(fid.Name);
                    info.Exports["default"] = fid.Name;
                }
                else if (def.Declaration is ClassDeclaration { Id: { } cid })
                {
                    info.Declares.Add(cid.Name);
                    info.Exports["default"] = cid.Name;
                }
                else if (def.Declaration is Expression expr && !IsPure(expr))
                {
                    info.ForceKeep = true;
                }
                break;

            case ExportAllDeclaration:
            case TypeOnlyDeclaration:
                info.Kind = StatementKind.ForceKeep;
                info.ForceKeep = true;
                break;

            case EmptyStatement:
                info.Kind = StatementKind.Declaration;
                break;

            default:
                info.Kind = StatementKind.ForceKeep;
                info.ForceKeep = true;
                break;
        }

        return info;
    }

    private static string ExportedName(ExportSpecifier specifier) => specifier.Exported switch
    {
        Identifier id => id.Name,
        StringLiteral s => s.Value,
        _ => string.Empty,
    };

    private static string LocalName(ExportSpecifier specifier) => specifier.Local switch
    {
        Identifier id => id.Name,
        StringLiteral s => s.Value,
        _ => string.Empty,
    };

    private static void CollectPatternNames(VariableStatement variable, List<string> names)
    {
        foreach (var declarator in variable.Declarations)
        {
            CollectPatternNames(declarator.Id, names);
        }
    }

    private static void CollectPatternNames(Node pattern, List<string> names)
    {
        switch (pattern)
        {
            case Identifier id:
                names.Add(id.Name);
                break;
            case ObjectExpression obj:
                foreach (var member in obj.Properties)
                {
                    if (member is SpreadElement spread) CollectPatternNames(spread.Argument, names);
                    else if (member is Property property && property.Value is not null) CollectPatternNames(property.Value, names);
                }
                break;
            case ArrayExpression arr:
                foreach (var element in arr.Elements)
                {
                    if (element is SpreadElement spread) CollectPatternNames(spread.Argument, names);
                    else if (element is not null) CollectPatternNames(element, names);
                }
                break;
            case AssignmentExpression assign:
                CollectPatternNames(assign.Left, names);
                break;
        }
    }

    private static bool IsPureVariableStatement(VariableStatement variable)
    {
        foreach (var declarator in variable.Declarations)
        {
            if (declarator.Init is not null && !IsPure(declarator.Init)) return false;
        }
        return true;
    }

    /// <summary>
    /// A conservative purity check: only expressions that clearly cannot run
    /// user code or trigger getters are treated as pure.
    /// </summary>
    internal static bool IsPure(Expression expression) => expression switch
    {
        Identifier or NumericLiteral or BigIntLiteral or StringLiteral or BooleanLiteral
            or NullLiteral or RegExpLiteral or ThisExpression or SuperExpression
            or FunctionExpression or ArrowFunctionExpression => true,
        ClassExpression cls => cls.SuperClass is null || IsPure(cls.SuperClass),
        ParenthesizedExpression paren => IsPure(paren.Expression),
        UnaryExpression { Operator: TokenKind.DeleteKeyword } => false,
        UnaryExpression unary => IsPure(unary.Argument),
        BinaryExpression binary => IsPure(binary.Left) && IsPure(binary.Right),
        LogicalExpression logical => IsPure(logical.Left) && IsPure(logical.Right),
        ConditionalExpression cond => IsPure(cond.Test) && IsPure(cond.Consequent) && IsPure(cond.Alternate),
        SequenceExpression seq => AllPure(seq.Expressions),
        ArrayExpression array => AllPureNullable(array.Elements),
        ObjectExpression obj => IsPureObject(obj),
        TemplateLiteral template => AllPure(template.Expressions),
        _ => false,
    };

    private static bool AllPure(IList<Expression> expressions)
    {
        foreach (var expression in expressions)
        {
            if (!IsPure(expression)) return false;
        }
        return true;
    }

    private static bool AllPureNullable(IList<Expression?> expressions)
    {
        foreach (var expression in expressions)
        {
            if (expression is not null && !IsPure(expression)) return false;
        }
        return true;
    }

    private static bool IsPureObject(ObjectExpression obj)
    {
        foreach (var member in obj.Properties)
        {
            switch (member)
            {
                case SpreadElement:
                    return false;
                case Property property:
                    if (property.Computed && property.Key is Expression key && !IsPure(key)) return false;
                    if (property.Value is Expression value && !IsPure(value)) return false;
                    break;
            }
        }
        return true;
    }
}
