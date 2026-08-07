import * as child_process from "child_process";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

import { generateBinPath } from "./platform";

const { binPath } = generateBinPath();

function argToString(name: string, value: string | boolean) {
  if (value === true) {
    return `--${name}`;
  } else if (value === false) {
    return `--no-${name}`;
  } else if (typeof value === "string") {
    return `--${name}=${value}`;
  } else {
    return "";
  }
}

export function run(argv: Array<string>): child_process.ChildProcess;

export function run(command: string, args: Record<string, string | boolean>): child_process.ChildProcess;

export function run(command: string | Array<string>, args?: Record<string, string | boolean>): child_process.ChildProcess {
  if (typeof command === 'string') {
    const argv = Object.entries(args ?? {})
      .map(([name, value]) => argToString(name, value))
      .filter((arg) => arg.length > 0);
    return run([command, ...argv]);
  }

  return child_process.spawn(binPath, command, {
    windowsHide: true,
    // Inherit all streams so the bundler's console output (build summary, dev
    // server URL, errors) reaches the user's terminal. Previously stdout was
    // piped, which silently swallowed everything the binary wrote to stdout.
    stdio: "inherit",
    cwd: process.cwd(),
  });
}

// ---------------------------------------------------------------------------
// Programmatic API
//
// A thin, fully-typed wrapper over `run` for calling netpack from a Node.js
// program. Each function returns a Promise that resolves when the command
// finishes (rejecting on a non-zero exit); commands that produce machine-
// readable output (graph, analyze) resolve with the parsed JSON.
// ---------------------------------------------------------------------------

export type ModuleFormat = "esm" | "cjs" | "umd" | "systemjs";
export type Platform = "web" | "node" | "deno";
export type PackagesMode = "bundle" | "external";
export type LicenseMode = "skip" | "preamble" | "json" | "spdx";

/** Details passed to {@link BuildEvents.onBuild} when a (re)build succeeds. */
export interface BuildInfo {
  /** Wall-clock build time in milliseconds, when the CLI reported it. */
  durationMs?: number;
  /** False for rebuilds triggered by a file change under `watch`/`serve`. */
  initial: boolean;
}

/**
 * Build lifecycle callbacks for the long-running commands (`serve`, and
 * `bundle` with `watch: true`). They let a program react to each (re)build —
 * a rebuild starting, finishing, or failing.
 *
 * These are wired entirely in the npm layer by watching the binary's stdout/
 * stderr for its build markers; there is no separate IPC channel. All output
 * is still forwarded to the terminal unchanged.
 */
export interface BuildEvents {
  /** Fires when a build (or rebuild) begins. */
  onStart?: () => void;
  /** Fires when a build (or rebuild) completes successfully. */
  onBuild?: (info: BuildInfo) => void;
  /** Fires when a rebuild fails (the process keeps running under watch/serve). */
  onError?: (error: Error) => void;
}

export interface CommonOptions {
  /**
   * Aborts the underlying process. Useful for long-running commands
   * (`serve`, or `bundle` with `watch: true`): call `controller.abort()` to
   * stop the bundler.
   */
  signal?: AbortSignal;
}

export interface BundleOptions extends CommonOptions, BuildEvents {
  /** Output directory (default "dist"). */
  outdir?: string;
  /** Minify + tree-shake the output. */
  minify?: boolean;
  /** Emit a source map next to each JS bundle. */
  sourcemap?: boolean;
  /** Clean the output directory first. */
  clean?: boolean;
  /** Import specifiers to keep external. */
  external?: string[];
  /** Dependencies emitted as shared bundles + import-map entries. */
  shared?: string[];
  /** Output module format (default "esm"). */
  format?: ModuleFormat;
  /** Target runtime (default "web"). */
  platform?: Platform;
  /** Compile-time constant substitutions, e.g. `{ "process.env.NODE_ENV": '"production"' }`. */
  define?: Record<string, string>;
  /** Import-specifier rewrites, e.g. `{ react: "preact/compat" }`. */
  alias?: Record<string, string>;
  /** Per-extension loader overrides, e.g. `{ ".svg": "text" }`. */
  loader?: Record<string, string>;
  /** Naming template with `[name]`/`[hash]`, e.g. "[name]-[hash]". */
  entryNames?: string;
  /** Base path/URL prepended to references to emitted files. */
  publicPath?: string;
  /** Text placed on top of the entry JS bundle, followed by a newline. */
  banner?: string;
  /** Third-party license handling (default "skip"). */
  licenses?: LicenseMode;
  /** Extra package.json `exports` conditions. */
  conditions?: string[];
  /** Set to "external" to keep every node_modules import external. */
  packages?: PackagesMode;
  /** Rebuild on file changes (long-running; use `signal` to stop). */
  watch?: boolean;
  /** Debounce delay in milliseconds for watch mode (default 200). */
  watchDelay?: number;
  /** Maximum size in bytes to inline assets as data URIs (0 = disabled). */
  inlineLimit?: number;
}

export interface ServeOptions extends CommonOptions, BuildEvents {
  /** Port for the dev server (default 1234). */
  port?: number;
  minify?: boolean;
  external?: string[];
  shared?: string[];
  define?: Record<string, string>;
  alias?: Record<string, string>;
  loader?: Record<string, string>;
  /** Text placed on top of the entry JS bundle, followed by a newline. */
  banner?: string;
  /** Maximum size in bytes to inline assets as data URIs (0 = disabled). */
  inlineLimit?: number;
}

export interface GraphOptions extends CommonOptions {
  /**
   * A file to also persist the graph JSON to. When omitted, a temporary file
   * is used internally and cleaned up (the parsed graph is still returned).
   */
  outfile?: string;
}

export interface AnalyzeOptions extends CommonOptions {
  /** A file to also persist the analysis JSON to (a temp file is used otherwise). */
  outfile?: string;
  external?: string[];
  shared?: string[];
}

export interface RunResult {
  /** The process exit code (0 on success). */
  code: number;
}

export interface GraphResult extends RunResult {
  /** The parsed dependency graph. */
  graph: unknown;
}

export interface AnalyzeResult extends RunResult {
  /** The parsed analysis data. */
  analysis: unknown;
}

function toFlag(name: string): string {
  return "--" + name.replace(/[A-Z]/g, (c) => "-" + c.toLowerCase());
}

/** Serializes a typed options object into CLI argv tokens. */
function buildArgs(options: Record<string, unknown>): string[] {
  const argv: string[] = [];

  for (const [key, value] of Object.entries(options)) {
    // Skip control/meta fields that aren't CLI flags: the abort signal and the
    // build-event callbacks (they're handled by the npm layer, not the binary).
    if (key === "signal" || value === undefined || value === null || typeof value === "function") continue;
    const flag = toFlag(key);

    if (typeof value === "boolean") {
      if (value) argv.push(flag);
    } else if (typeof value === "string" || typeof value === "number") {
      argv.push(flag, String(value));
    } else if (Array.isArray(value)) {
      if (value.length > 0) argv.push(flag, ...value.map(String));
    } else if (typeof value === "object") {
      // A record like define/alias/loader -> repeated "key=value" entries.
      const entries = Object.entries(value as Record<string, unknown>);
      if (entries.length > 0) {
        argv.push(flag, ...entries.map(([k, v]) => `${k}=${v}`));
      }
    }
  }

  return argv;
}

// ---------------------------------------------------------------------------
// Build-event detection
//
// The binary prints stable, human-readable markers on every (re)build — the
// same lines you see in the terminal. We tee its stdout/stderr through and scan
// those lines to drive the onStart/onBuild/onError callbacks. A small state
// machine collapses the redundant markers a single rebuild emits (e.g. both
// "File change detected ..." and "Bundling ...") into one onStart per build.
// ---------------------------------------------------------------------------

const MARK_START = /\[netpack\] (?:Starting build|Bundling '|File change detected)/;
const MARK_DONE = /\[netpack\] (?:Everything bundled!|Rebuild complete\.|Emitted \d|Nothing was emitted\.)/;
const MARK_FAIL = /\[netpack\] Rebuild failed:?\s*(.*)$/;
const MARK_DURATION = /\bin (\d+) ms\b/;

function hasEvents(e?: BuildEvents): boolean {
  return !!(e && (e.onStart || e.onBuild || e.onError));
}

/**
 * Splits a byte stream into lines (buffering partials across chunks) and calls
 * `onLine` for each complete line. Returns a `flush` for any trailing text.
 */
function lineScanner(onLine: (line: string) => void): (chunk: Buffer | string) => void {
  let buffer = "";
  return (chunk) => {
    buffer += chunk.toString();
    let index: number;
    while ((index = buffer.indexOf("\n")) >= 0) {
      onLine(buffer.slice(0, index));
      buffer = buffer.slice(index + 1);
    }
  };
}

/**
 * Tees the child's stdout/stderr to this process (so the terminal is unchanged)
 * while scanning for build markers and firing the lifecycle callbacks.
 */
function attachBuildEvents(child: child_process.ChildProcess, events: BuildEvents): void {
  let building = false;
  let seenFirst = false;

  const consider = (line: string) => {
    if (MARK_START.test(line)) {
      if (!building) {
        building = true;
        events.onStart?.();
      }
      return;
    }

    const fail = MARK_FAIL.exec(line);
    if (fail) {
      building = false;
      events.onError?.(new Error(fail[1]?.trim() || "netpack rebuild failed"));
      return;
    }

    if (MARK_DONE.test(line) && building) {
      building = false;
      const initial = !seenFirst;
      seenFirst = true;
      const duration = MARK_DURATION.exec(line);
      events.onBuild?.({
        durationMs: duration ? Number(duration[1]) : undefined,
        initial,
      });
    }
  };

  const scanOut = lineScanner(consider);
  const scanErr = lineScanner(consider);

  child.stdout?.on("data", (chunk: Buffer) => {
    process.stdout.write(chunk); // tee raw bytes so output is byte-accurate
    scanOut(chunk);
  });
  child.stderr?.on("data", (chunk: Buffer) => {
    process.stderr.write(chunk);
    scanErr(chunk);
  });
}

/** Runs an argv through `run` and resolves with the exit code (rejecting on failure). */
function exec(argv: string[], signal?: AbortSignal, events?: BuildEvents): Promise<RunResult> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      return reject(new Error(`netpack ${argv[0]} aborted before start`));
    }

    // When callbacks are in play we must read stdout/stderr, so pipe them (and
    // tee them back to the terminal). Otherwise inherit the streams as before.
    const child = hasEvents(events)
      ? child_process.spawn(binPath, argv, {
          windowsHide: true,
          stdio: ["inherit", "pipe", "pipe"],
          cwd: process.cwd(),
        })
      : run(argv);

    if (hasEvents(events)) {
      attachBuildEvents(child, events!);
    }

    const onAbort = () => child.kill();
    signal?.addEventListener("abort", onAbort, { once: true });

    child.on("error", (err) => {
      signal?.removeEventListener("abort", onAbort);
      reject(err);
    });

    child.on("close", (code) => {
      signal?.removeEventListener("abort", onAbort);
      // A null code means the process was terminated by a signal (e.g. an
      // intentional abort of `serve`), which we treat as a clean stop.
      if (code === 0 || code === null) {
        resolve({ code: code ?? 0 });
      } else {
        // A one-shot build that failed outright (no watch loop to report it).
        events?.onError?.(new Error(`netpack ${argv[0]} exited with code ${code}`));
        reject(new Error(`netpack ${argv[0]} exited with code ${code}`));
      }
    });
  });
}

/** Runs a command that writes JSON to `--outfile`, returning the parsed JSON. */
async function captureJson(
  command: string,
  entry: string,
  options: Record<string, unknown>
): Promise<{ code: number; json: unknown }> {
  const signal = options.signal as AbortSignal | undefined;
  const explicit = typeof options.outfile === "string" ? (options.outfile as string) : undefined;
  const outfile =
    explicit ??
    path.join(os.tmpdir(), `netpack-${command}-${process.pid}-${Date.now()}.json`);

  const args = buildArgs({ ...options, outfile });
  const result = await exec([command, entry, ...args], signal);

  let json: unknown;
  try {
    json = JSON.parse(fs.readFileSync(outfile, "utf8"));
  } catch {
    json = undefined;
  } finally {
    if (!explicit) {
      try {
        fs.unlinkSync(outfile);
      } catch {}
    }
  }

  return { code: result.code, json };
}

/**
 * Typed, Promise-based wrappers around the netpack commands. Prefer these over
 * `run` when calling the bundler from a Node.js program.
 */
export const netpack = {
  /** Produces a production build of `entry` into `options.outdir` (default "dist"). */
  bundle(entry: string, options: BundleOptions = {}): Promise<RunResult> {
    return exec(["bundle", entry, ...buildArgs(options)], options.signal, options);
  },

  /**
   * Starts the dev server for `entry`. Long-running: the returned Promise
   * resolves once the server stops (e.g. via `options.signal`).
   */
  serve(entry: string, options: ServeOptions = {}): Promise<RunResult> {
    return exec(["serve", entry, ...buildArgs(options)], options.signal, options);
  },

  /** Builds the dependency graph for `entry` and resolves with the parsed JSON. */
  async graph(entry: string, options: GraphOptions = {}): Promise<GraphResult> {
    const { code, json } = await captureJson("graph", entry, options);
    return { code, graph: json };
  },

  /** Analyzes `entry` and resolves with the parsed analysis JSON. */
  async analyze(entry: string, options: AnalyzeOptions = {}): Promise<AnalyzeResult> {
    const { code, json } = await captureJson("analyze", entry, options);
    return { code, analysis: json };
  },

  /** Inspects a previously produced graph JSON file. */
  inspect(graphFile: string, options: CommonOptions = {}): Promise<RunResult> {
    return exec(["inspect", graphFile], options.signal);
  },
};
