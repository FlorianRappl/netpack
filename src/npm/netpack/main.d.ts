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
