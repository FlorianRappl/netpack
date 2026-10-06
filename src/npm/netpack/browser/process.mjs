/**
 * A minimal browser stand-in for Node's `process`, for use via
 * `--alias process=netpack/browser/process.mjs` or the bundled `node-polyfills`
 * preset. It covers the fields libraries commonly touch at load time; it does not
 * emulate real process state. `process.env.NODE_ENV` is better set with `--define`.
 */

const now = () => (typeof performance !== "undefined" ? performance.now() : Date.now());

function hrtime(previous) {
  const nanos = Math.floor(now() * 1e6);
  const seconds = Math.floor(nanos / 1e9);
  const nano = nanos % 1e9;
  if (previous) {
    let s = seconds - previous[0];
    let n = nano - previous[1];
    if (n < 0) {
      s -= 1;
      n += 1e9;
    }
    return [s, n];
  }
  return [seconds, nano];
}

hrtime.bigint = () => BigInt(Math.floor(now() * 1e6));

const process = {
  env: {},
  argv: [],
  argv0: "",
  execPath: "",
  platform: "browser",
  arch: "",
  pid: 0,
  title: "browser",
  browser: true,
  version: "",
  versions: {},
  cwd() {
    return "/";
  },
  chdir() {},
  umask() {
    return 0;
  },
  nextTick(callback, ...args) {
    Promise.resolve().then(() => callback(...args));
  },
  hrtime,
  exit() {},
  on() {
    return process;
  },
  once() {
    return process;
  },
  off() {
    return process;
  },
  addListener() {
    return process;
  },
  removeListener() {
    return process;
  },
  emit() {
    return false;
  },
};

export default process;
