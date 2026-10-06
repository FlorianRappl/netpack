/**
 * A dependency-free `querystring` implementation for the browser, for use via
 * `--alias querystring=netpack/browser/querystring.mjs` or the bundled
 * `node-polyfills` preset. Covers the parse/stringify surface most code uses.
 */

export function parse(input, sep = "&", eq = "=") {
  const result = {};
  if (typeof input !== "string" || input.length === 0) return result;

  for (const pair of input.split(sep)) {
    if (pair.length === 0) continue;
    const index = pair.indexOf(eq);
    const rawKey = index < 0 ? pair : pair.slice(0, index);
    const rawValue = index < 0 ? "" : pair.slice(index + eq.length);
    const key = decode(rawKey);
    const value = decode(rawValue);

    if (Object.prototype.hasOwnProperty.call(result, key)) {
      if (Array.isArray(result[key])) result[key].push(value);
      else result[key] = [result[key], value];
    } else {
      result[key] = value;
    }
  }

  return result;
}

export function stringify(obj, sep = "&", eq = "=") {
  if (obj === null || typeof obj !== "object") return "";
  const pairs = [];
  for (const key of Object.keys(obj)) {
    const encodedKey = encode(key);
    const value = obj[key];
    if (Array.isArray(value)) {
      for (const item of value) pairs.push(encodedKey + eq + encode(item));
    } else {
      pairs.push(encodedKey + eq + encode(value));
    }
  }
  return pairs.join(sep);
}

function decode(value) {
  try {
    return decodeURIComponent(value.replace(/\+/g, " "));
  } catch {
    return value;
  }
}

function encode(value) {
  return encodeURIComponent(value === undefined || value === null ? "" : String(value));
}

export const escape = encodeURIComponent;
export const unescape = decodeURIComponent;

const querystring = { parse, stringify, encode: stringify, decode: parse, escape, unescape };

export default querystring;
