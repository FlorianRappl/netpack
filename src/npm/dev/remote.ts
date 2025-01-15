import { init as runtimeInit } from "@module-federation/runtime";

const initTokens = {};
const name = __name__;
const shareScopeName = __share_scope__;
const shareStrategy = __share_strategy__;
const exposesMap = __exposes__;
const remotes = __remotes__;
const shared = __shared__;

export async function init(allShared = {}, initScope = []) {
  const runtime = runtimeInit({
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
  runtime.initShareScopeMap(shareScopeName, allShared);

  try {
    await Promise.all(
      runtime.initializeSharing(shareScopeName, {
        strategy: shareStrategy,
        from: "build",
        initScope,
      })
    );
  } catch (e) {
    console.error(e);
  }

  return runtime;
}

export async function get(moduleName: string) {
  const moduleLoader = exposesMap[moduleName];

  if (typeof moduleLoader !== 'function') {
    throw new Error(`Module ${moduleName} does not exist in container.`);
  }

  const moduleContent = await moduleLoader();
  return () => moduleContent;
}
