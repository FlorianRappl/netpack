import { generateBinPath } from "./platform";

import * as child_process from "child_process";

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
