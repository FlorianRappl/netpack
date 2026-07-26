import type { ChildProcess } from "child_process";

/**
 * Runs the netpack native binary with the given raw CLI arguments,
 * e.g. `run(["bundle", "src/index.html", "--minify"])`.
 */
export function run(argv: string[]): ChildProcess;

/**
 * Runs a single netpack command with a set of options, e.g.
 * `run("bundle", { minify: true })`.
 */
export function run(command: string, args: Record<string, string | boolean>): ChildProcess;

export type ModuleFormat = "esm" | "cjs" | "umd" | "systemjs";
export type Platform = "web" | "node" | "deno";
export type PackagesMode = "bundle" | "external";

export interface CommonOptions {
  /**
   * Aborts the underlying process. Useful for long-running commands
   * (`serve`, or `bundle` with `watch: true`): call `controller.abort()` to
   * stop the bundler.
   */
  signal?: AbortSignal;
}

export interface BundleOptions extends CommonOptions {
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
  /** Extra package.json `exports` conditions. */
  conditions?: string[];
  /** Set to "external" to keep every node_modules import external. */
  packages?: PackagesMode;
  /** Rebuild on file changes (long-running; use `signal` to stop). */
  watch?: boolean;
}

export interface ServeOptions extends CommonOptions {
  /** Port for the dev server (default 1234). */
  port?: number;
  minify?: boolean;
  external?: string[];
  shared?: string[];
  define?: Record<string, string>;
  alias?: Record<string, string>;
  loader?: Record<string, string>;
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

/**
 * Typed, Promise-based wrappers around the netpack commands. Prefer these over
 * `run` when calling the bundler from a Node.js program. Each rejects on a
 * non-zero exit code.
 */
export const netpack: {
  /** Produces a production build of `entry` into `options.outdir` (default "dist"). */
  bundle(entry: string, options?: BundleOptions): Promise<RunResult>;
  /**
   * Starts the dev server for `entry`. Long-running: the returned Promise
   * resolves once the server stops (e.g. via `options.signal`).
   */
  serve(entry: string, options?: ServeOptions): Promise<RunResult>;
  /** Builds the dependency graph for `entry` and resolves with the parsed JSON. */
  graph(entry: string, options?: GraphOptions): Promise<GraphResult>;
  /** Analyzes `entry` and resolves with the parsed analysis JSON. */
  analyze(entry: string, options?: AnalyzeOptions): Promise<AnalyzeResult>;
  /** Inspects a previously produced graph JSON file. */
  inspect(graphFile: string, options?: CommonOptions): Promise<RunResult>;
};
