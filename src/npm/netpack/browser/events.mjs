/**
 * A dependency-free `events` (EventEmitter) implementation for the browser, for use
 * via `--alias events=netpack/browser/events.mjs` or the bundled `node-polyfills`
 * preset. Mirrors the common EventEmitter surface; default export is the class, as
 * in Node (`const EventEmitter = require("events")`).
 */

export class EventEmitter {
  constructor() {
    this._events = Object.create(null);
    this._maxListeners = EventEmitter.defaultMaxListeners;
  }

  setMaxListeners(n) {
    this._maxListeners = n;
    return this;
  }

  getMaxListeners() {
    return this._maxListeners;
  }

  _add(type, listener, prepend) {
    if (typeof listener !== "function") throw new TypeError("listener must be a function");
    const list = this._events[type] || (this._events[type] = []);
    if (prepend) list.unshift(listener);
    else list.push(listener);
    return this;
  }

  on(type, listener) {
    return this._add(type, listener, false);
  }

  addListener(type, listener) {
    return this.on(type, listener);
  }

  prependListener(type, listener) {
    return this._add(type, listener, true);
  }

  once(type, listener) {
    const wrapped = (...args) => {
      this.off(type, wrapped);
      listener.apply(this, args);
    };
    wrapped.listener = listener;
    return this._add(type, wrapped, false);
  }

  prependOnceListener(type, listener) {
    const wrapped = (...args) => {
      this.off(type, wrapped);
      listener.apply(this, args);
    };
    wrapped.listener = listener;
    return this._add(type, wrapped, true);
  }

  off(type, listener) {
    const list = this._events[type];
    if (list) {
      const index = list.findIndex((l) => l === listener || l.listener === listener);
      if (index >= 0) list.splice(index, 1);
      if (list.length === 0) delete this._events[type];
    }
    return this;
  }

  removeListener(type, listener) {
    return this.off(type, listener);
  }

  removeAllListeners(type) {
    if (type === undefined) this._events = Object.create(null);
    else delete this._events[type];
    return this;
  }

  listeners(type) {
    return (this._events[type] || []).map((l) => l.listener || l);
  }

  rawListeners(type) {
    return (this._events[type] || []).slice();
  }

  listenerCount(type) {
    return (this._events[type] || []).length;
  }

  eventNames() {
    return Object.keys(this._events);
  }

  emit(type, ...args) {
    const list = this._events[type];
    if (!list || list.length === 0) {
      if (type === "error") {
        const err = args[0];
        throw err instanceof Error ? err : new Error("Unhandled 'error' event");
      }
      return false;
    }
    for (const listener of list.slice()) listener.apply(this, args);
    return true;
  }
}

EventEmitter.EventEmitter = EventEmitter;
EventEmitter.defaultMaxListeners = 10;

export default EventEmitter;
