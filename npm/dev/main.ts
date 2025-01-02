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
    const argv = Object.entries(args)
      .map(([name, value]) => argToString(name, value))
      .join(" ");
    return run([command, ...argv]);
  }

  return child_process.spawn(binPath, command, {
    windowsHide: true,
    stdio: ["pipe", "pipe", "inherit"],
    cwd: process.cwd(),
  });
}
