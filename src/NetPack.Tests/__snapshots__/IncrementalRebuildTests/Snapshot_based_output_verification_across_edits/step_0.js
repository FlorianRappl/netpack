const __m = { 1: (module, exports, require) => {
  const { a: a } = require(0);
  exports.default = a;
}, 0: (module, exports, require) => {
  const a = 1;
  exports.a = a;
} };
var __c = {};
function __r(id) {
  var mod = __c[id];
  if (mod)
    return mod.exports;
  mod = __c[id] = { exports: {} };
  __m[id](mod, mod.exports, __r);
  var e = mod.exports;
  if (e && (typeof e == "object" || typeof e == "function") && e.default === void 0)
    e.default = e;
  return e;
}
const { default: _default } = __r(1);
export default _default;