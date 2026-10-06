/**
 * A dependency-free, browser-friendly implementation of Node's `path` (POSIX
 * semantics), for use via `--alias path=netpack/browser/path.mjs` or the bundled
 * `node-polyfills` preset. Paths are treated as POSIX (`/` separators); the current
 * working directory is `/`.
 */

export const sep = "/";
export const delimiter = ":";

function assertString(value) {
  if (typeof value !== "string") {
    throw new TypeError("Path must be a string. Received " + JSON.stringify(value));
  }
}

function normalizeSegments(parts, allowAboveRoot) {
  const result = [];
  for (const part of parts) {
    if (!part || part === ".") continue;
    if (part === "..") {
      if (result.length && result[result.length - 1] !== "..") result.pop();
      else if (allowAboveRoot) result.push("..");
    } else {
      result.push(part);
    }
  }
  return result;
}

export function normalize(path) {
  assertString(path);
  if (path.length === 0) return ".";
  const absolute = path.charCodeAt(0) === 47;
  const trailingSlash = path.charCodeAt(path.length - 1) === 47;
  let out = normalizeSegments(path.split("/"), !absolute).join("/");
  if (!out && !absolute) out = ".";
  if (out && trailingSlash) out += "/";
  return (absolute ? "/" : "") + out;
}

export function join(...parts) {
  let joined;
  for (const part of parts) {
    assertString(part);
    if (part.length > 0) joined = joined === undefined ? part : joined + "/" + part;
  }
  return joined === undefined ? "." : normalize(joined);
}

export function isAbsolute(path) {
  assertString(path);
  return path.length > 0 && path.charCodeAt(0) === 47;
}

export function resolve(...parts) {
  let resolved = "";
  let absolute = false;
  for (let i = parts.length - 1; i >= -1 && !absolute; i--) {
    const part = i >= 0 ? parts[i] : "/"; // cwd is "/" in the browser
    assertString(part);
    if (part.length === 0) continue;
    resolved = part + "/" + resolved;
    absolute = part.charCodeAt(0) === 47;
  }
  resolved = normalizeSegments(resolved.split("/"), !absolute).join("/");
  if (absolute) return "/" + resolved;
  return resolved.length > 0 ? resolved : ".";
}

export function dirname(path) {
  assertString(path);
  if (path.length === 0) return ".";
  let end = -1;
  let matched = false;
  for (let i = path.length - 1; i >= 1; i--) {
    if (path.charCodeAt(i) === 47) {
      if (matched) {
        end = i;
        break;
      }
    } else {
      matched = true;
    }
  }
  if (end === -1) return path.charCodeAt(0) === 47 ? "/" : ".";
  if (end === 0) return "/";
  return path.slice(0, end);
}

export function basename(path, ext) {
  assertString(path);
  let start = 0;
  let end = -1;
  let matched = false;
  for (let i = path.length - 1; i >= 0; i--) {
    if (path.charCodeAt(i) === 47) {
      if (matched) {
        start = i + 1;
        break;
      }
    } else {
      matched = true;
      if (end === -1) end = i + 1;
    }
  }
  if (end === -1) return "";
  let base = path.slice(start, end);
  if (ext && base !== ext && base.endsWith(ext)) base = base.slice(0, base.length - ext.length);
  return base;
}

export function extname(path) {
  assertString(path);
  const base = basename(path);
  const dot = base.lastIndexOf(".");
  return dot <= 0 ? "" : base.slice(dot);
}

export function relative(from, to) {
  const a = resolve(from);
  const b = resolve(to);
  if (a === b) return "";
  const fromParts = a.split("/").filter(Boolean);
  const toParts = b.split("/").filter(Boolean);
  let i = 0;
  while (i < fromParts.length && i < toParts.length && fromParts[i] === toParts[i]) i++;
  const up = [];
  for (let j = i; j < fromParts.length; j++) up.push("..");
  return up.concat(toParts.slice(i)).join("/");
}

export function parse(path) {
  assertString(path);
  const root = isAbsolute(path) ? "/" : "";
  const base = basename(path);
  const ext = extname(path);
  const dir = dirname(path);
  return {
    root,
    dir: dir === "." && !root ? "" : dir,
    base,
    ext,
    name: ext ? base.slice(0, base.length - ext.length) : base,
  };
}

export function format(obj) {
  const dir = obj.dir || obj.root || "";
  const base = obj.base || ((obj.name || "") + (obj.ext || ""));
  if (!dir) return base;
  return dir === obj.root ? dir + base : dir + "/" + base;
}

const posix = {
  sep,
  delimiter,
  normalize,
  join,
  isAbsolute,
  resolve,
  dirname,
  basename,
  extname,
  relative,
  parse,
  format,
};

posix.posix = posix;

export default posix;
