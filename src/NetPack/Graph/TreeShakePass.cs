namespace NetPack.Graph;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetPack.Fragments;
using NetPack.Syntax.Optimizer;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// The whole-program tree-shaking pass, run once before an optimizing build is
/// written out. It:
/// <list type="number">
/// <item>determines which modules are side-effect-free (a package.json
/// <c>sideEffects:false</c>, or a provably pure module whose dependencies are
/// all side-effect-free);</item>
/// <item>shakes every module — dropping dead declarations, unused exports, and
/// unused imports of side-effect-free modules;</item>
/// <item>recomputes reachability from the bundle roots over the surviving import
/// edges and removes now-unreferenced modules from each bundle.</item>
/// </list>
/// The result: importing one helper from a side-effect-free package no longer
/// pulls the rest of it into the bundle.
/// </summary>
public static class TreeShakePass
{
    public static void Run(BundlerContext context)
    {
        var fragments = context.JsFragments;
        var sideEffectFree = ComputeSideEffectFree(context);

        // 1. Shake every module; collect the imports that were dropped.
        var removedImports = new HashSet<Ast.ImportDeclaration>(ReferenceComparer<Ast.ImportDeclaration>.Instance);
        foreach (var entry in fragments)
        {
            var fragment = entry.Value;
            var used = context.GetUsedExports(entry.Key);

            bool ImportPure(Ast.ImportDeclaration import)
                => fragment.Replacements.TryGetValue(import, out var target) && IsTargetFree(target, fragments, sideEffectFree);

            foreach (var dropped in TreeShaker.Shake(fragment.Ast, used, ImportPure))
            {
                removedImports.Add(dropped);
            }
        }

        // 2. Reachability from every bundle root over surviving edges.
        var live = new HashSet<Node>();
        var queue = new Queue<Node>();
        foreach (var root in context.Bundles.Keys)
        {
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var module = queue.Dequeue();
            if (!live.Add(module))
            {
                continue;
            }
            if (fragments.TryGetValue(module, out var fragment))
            {
                foreach (var edge in fragment.Replacements)
                {
                    if (edge.Key is Ast.ImportDeclaration import && removedImports.Contains(import))
                    {
                        continue; // this import was shaken away
                    }
                    queue.Enqueue(edge.Value);
                }
            }
        }

        // 3. Drop dead modules from each bundle (never touching non-JS items).
        foreach (var bundle in context.Bundles.Values)
        {
            bundle.Items = bundle.Items.Where(node => !fragments.ContainsKey(node) || live.Contains(node)).ToArray();
        }
    }

    private static bool IsTargetFree(Node target, IReadOnlyDictionary<Node, JsFragment> fragments, HashSet<Node> sideEffectFree)
        // Non-JS targets (assets referenced by URL) run no code, so they are pure.
        => !fragments.ContainsKey(target) || sideEffectFree.Contains(target);

    private static HashSet<Node> ComputeSideEffectFree(BundlerContext context)
    {
        var fragments = context.JsFragments;
        var free = new HashSet<Node>();
        var heuristicPure = new Dictionary<Node, bool>();

        foreach (var entry in fragments)
        {
            var node = entry.Key;
            var package = FindPackage(context, node.FileName);
            var packageFree = package is not null && !package.HasSideEffects(node.FileName);
            heuristicPure[node] = !TreeShaker.HasTopLevelSideEffects(entry.Value.Ast);

            if (packageFree)
            {
                free.Add(node); // the package guarantees the whole subtree is pure
            }
        }

        // Fixpoint: a provably pure module is side-effect-free once every module it
        // pulls in is known side-effect-free too.
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var entry in fragments)
            {
                var node = entry.Key;
                if (free.Contains(node) || !heuristicPure[node])
                {
                    continue;
                }

                var allFree = true;
                foreach (var target in entry.Value.Replacements.Values)
                {
                    if (fragments.ContainsKey(target) && !free.Contains(target))
                    {
                        allFree = false;
                        break;
                    }
                }

                if (allFree)
                {
                    free.Add(node);
                    changed = true;
                }
            }
        }

        return free;
    }

    private static Dependency? FindPackage(BundlerContext context, string file)
    {
        Dependency? best = null;
        var bestLength = -1;

        foreach (var dependency in context.Dependencies)
        {
            var directory = Path.GetDirectoryName(dependency.Location);
            if (directory is null)
            {
                continue;
            }
            var prefix = directory + Path.DirectorySeparatorChar;
            if (file.StartsWith(prefix, StringComparison.Ordinal) && directory.Length > bestLength)
            {
                best = dependency;
                bestLength = directory.Length;
            }
        }

        return best;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
