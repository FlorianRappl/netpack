namespace NetPack.Fragments;

using Acornima.Ast;
using NetPack.Json;

public class JsModuleFederationContainerFragment : JsFragment
{
    private JsModuleFederationContainerFragment(Graph.Node root, Module ast, Dictionary<Node, Graph.Node> replacements)
        : base(root, ast, replacements, ["init", "get"])
    {
    }

    public static string CreateContainerCode(ModuleFederation details)
    {
        var code = @"import { init as runtimeInit, loadRemote } from '@module-federation/runtime';

        const initTokens = {};
        const name = '__name__';
        const shareScopeName = '__share_scope__';
        const shareStrategy = '__share_strategy__';
        const exposesMap = { __exposes__ };
        const remotes = [ __remotes__ ];
        const shared = { __shared__ };
        
        export async function init(shared = {}, initScope = []) {
          const initRes = runtimeInit({
            name,
            remotes,
            shared,
            shareStrategy,
          });
          
          let initToken = initTokens[shareScopeName];
          
          if (!initToken) {
            initToken = initTokens[shareScopeName] = { from: name };
          }
      
          if (initScope.indexOf(initToken) >= 0) {
            return;
          }
          
          initScope.push(initToken);
          initRes.initShareScopeMap(shareScopeName, shared);

          try {
            await Promise.all(await initRes.initializeSharing(shareScopeName, {
              strategy: shareStrategy,
              from: 'build',
              initScope
            }));
          } catch (e) {
            console.error(e)
          }

          initResolve(initRes);
          return initRes;
        }
        
        export function get(moduleName) {
          if (!(moduleName in exposesMap)) throw new Error(`Module ${moduleName} does not exist in container.`);
          return (exposesMap[moduleName])().then(res => () => res);
        }
        ";

        return code
            .Replace("__name__", details.Name)
            .Replace("__share_scope__", details.ShareScope)
            .Replace("__share_strategy__", details.ShareStrategy)
            .Replace("__exposes__", string.Join(", ", details.Exposes?.Select(m => $"{m.Key}: () => import('{m.Value}')") ?? []))
            .Replace("__remotes__", string.Join(", ", details.Remotes?.Select(m => $"{{ alias: '{m.Key}', name: '{m.Value.Name}', entry: '{m.Value.Entry}', type: '{m.Value.Entry}' }}") ?? []))
            .Replace("__shared__", string.Join(", ", details.Shared?.Select(m => $"{m.Key}: {{ version: '', scope: '{m.Value.ShareScope}', lib: () => import('{m.Key}'), shareConfig: {{ singleton: {m.Value.IsSingleton}, requiredVersion: '{m.Value.RequiredVersion}' }} }}") ?? []));
    }
}
