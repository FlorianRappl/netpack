import {
  downloadedBinPath,
  NETPACK_BINARY_PATH,
  isValidBinaryPath,
  pkgAndSubpathForCurrentPlatform,
} from "./platform";

import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import * as zlib from "zlib";
import * as https from "https";
import * as child_process from "child_process";

const versionFromPackageJSON: string = require(path.join(
  __dirname,
  "..",
  "package.json"
)).version;
const toPath = path.join(__dirname, "bin", "netpack");
let isToPathJS = true;

function validateBinaryVersion(...command: string[]): void {
  command.push("--version");
  let stdout: string;
  try {
    stdout = child_process
      .execFileSync(command.shift()!, command, {
        stdio: "pipe",
      })
      .toString()
      .trim();
  } catch (err) {
    if (
      os.platform() === "darwin" &&
      /_SecTrustEvaluateWithError/.test(err + "")
    ) {
      let os = "this version of macOS";
      try {
        os =
          "macOS " +
          child_process
            .execFileSync("sw_vers", ["-productVersion"])
            .toString()
            .trim();
      } catch {}
      throw new Error(`The "netpack" package cannot be installed because ${os} is too outdated.`);
    }
    throw err;
  }
  if (stdout !== versionFromPackageJSON) {
    throw new Error(
      `Expected ${JSON.stringify(
        versionFromPackageJSON
      )} but got ${JSON.stringify(stdout)}`
    );
  }
}

function isYarn(): boolean {
  const { npm_config_user_agent } = process.env;
  if (npm_config_user_agent) {
    return /\byarn\//.test(npm_config_user_agent);
  }
  return false;
}

function fetch(url: string): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    https
      .get(url, (res) => {
        if (
          (res.statusCode === 301 || res.statusCode === 302) &&
          res.headers.location
        )
          return fetch(res.headers.location).then(resolve, reject);
        if (res.statusCode !== 200)
          return reject(new Error(`Server responded with ${res.statusCode}`));
        let chunks: Buffer[] = [];
        res.on("data", (chunk) => chunks.push(chunk));
        res.on("end", () => resolve(Buffer.concat(chunks)));
      })
      .on("error", reject);
  });
}

function extractFileFromTarGzip(buffer: Buffer, subpath: string): Buffer {
  try {
    buffer = zlib.unzipSync(buffer);
  } catch (err: any) {
    throw new Error(
      `Invalid gzip data in archive: ${(err && err.message) || err}`
    );
  }
  let str = (i: number, n: number) =>
    String.fromCharCode(...buffer.subarray(i, i + n)).replace(/\0.*$/, "");
  let offset = 0;
  subpath = `package/${subpath}`;
  while (offset < buffer.length) {
    let name = str(offset, 100);
    let size = parseInt(str(offset + 124, 12), 8);
    offset += 512;
    if (!isNaN(size)) {
      if (name === subpath) return buffer.subarray(offset, offset + size);
      offset += (size + 511) & ~511;
    }
  }
  throw new Error(`Could not find ${JSON.stringify(subpath)} in archive`);
}

function installUsingNPM(pkg: string, subpath: string, binPath: string): void {
  const env = { ...process.env, npm_config_global: undefined };
  const netpackLibDir = path.dirname(require.resolve("netpack"));
  const installDir = path.join(netpackLibDir, "npm-install");
  fs.mkdirSync(installDir);
  try {
    fs.writeFileSync(path.join(installDir, "package.json"), "{}");

    // Run "npm install" in the temporary directory which should download the
    // desired package. Try to avoid unnecessary log output. This uses the "npm"
    // command instead of a HTTP request so that it hopefully works in situations
    // where HTTP requests are blocked but the "npm" command still works due to,
    // for example, a custom configured npm registry and special firewall rules.
    child_process.execSync(
      `npm install --loglevel=error --prefer-offline --no-audit --progress=false ${pkg}@${versionFromPackageJSON}`,
      { cwd: installDir, stdio: "pipe", env }
    );

    // Move the downloaded binary executable into place. The destination path
    // is the same one that the JavaScript API code uses so it will be able to
    // find the binary executable here later.
    const installedBinPath = path.join(
      installDir,
      "node_modules",
      pkg,
      subpath
    );
    fs.renameSync(installedBinPath, binPath);
  } finally {
    // Try to clean up afterward so we don't unnecessarily waste file system
    // space. Leaving nested "node_modules" directories can also be problematic
    // for certain tools that scan over the file tree and expect it to have a
    // certain structure.
    try {
      removeRecursive(installDir);
    } catch {
      // Removing a file or directory can randomly break on Windows, returning
      // EBUSY for an arbitrary length of time. I think this happens when some
      // other program has that file or directory open (e.g. an anti-virus
      // program). This is fine on Unix because the OS just unlinks the entry
      // but keeps the reference around until it's unused. There's nothing we
      // can do in this case so we just leave the directory there.
    }
  }
}

function removeRecursive(dir: string): void {
  for (const entry of fs.readdirSync(dir)) {
    const entryPath = path.join(dir, entry);
    let stats;
    try {
      stats = fs.lstatSync(entryPath);
    } catch {
      continue; // Guard against https://github.com/nodejs/node/issues/4760
    }
    if (stats.isDirectory()) removeRecursive(entryPath);
    else fs.unlinkSync(entryPath);
  }
  fs.rmdirSync(dir);
}

function applyManualBinaryPathOverride(overridePath: string): void {
  const pathString = JSON.stringify(overridePath);
  fs.writeFileSync(
    toPath,
    `#!/usr/bin/env node\n` +
      `require('child_process').execFileSync(${pathString}, process.argv.slice(2), { stdio: 'inherit' });\n`
  );

  // Patch the JS API use case (the "require('netpack')" workflow)
  const libMain = path.join(__dirname, "lib", "main.js");
  const code = fs.readFileSync(libMain, "utf8");
  fs.writeFileSync(
    libMain,
    `var NETPACK_BINARY_PATH = ${pathString};\n${code}`
  );
}

function maybeOptimizePackage(binPath: string): void {
  if (os.platform() !== "win32" && !isYarn()) {
    const tempPath = path.join(__dirname, "bin-netpack");
    try {
      // First link the binary with a temporary file. If this fails and throws an
      // error, then we'll just end up doing nothing. This uses a hard link to
      // avoid taking up additional space on the file system.
      fs.linkSync(binPath, tempPath);

      // Then use rename to atomically replace the target file with the temporary
      // file. If this fails and throws an error, then we'll just end up leaving
      // the temporary file there, which is harmless.
      fs.renameSync(tempPath, toPath);

      // If we get here, then we know that the target location is now a binary
      // executable instead of a JavaScript file.
      isToPathJS = false;

      // If this install script is being re-run, then "renameSync" will fail
      // since the underlying inode is the same (it just returns without doing
      // anything, and without throwing an error). In that case we should remove
      // the file manually.
      fs.unlinkSync(tempPath);
    } catch {
      // Ignore errors here since this optimization is optional
    }
  }
}

async function downloadDirectlyFromNPM(
  pkg: string,
  subpath: string,
  binPath: string
): Promise<void> {
  // If that fails, the user could have npm configured incorrectly or could not
  // have npm installed. Try downloading directly from npm as a last resort.
  const url = `https://registry.npmjs.org/${pkg}/-/${pkg.replace(
    "@netpack/",
    ""
  )}-${versionFromPackageJSON}.tgz`;
  console.error(`[netpack] Trying to download ${JSON.stringify(url)}`);
  try {
    fs.writeFileSync(
      binPath,
      extractFileFromTarGzip(await fetch(url), subpath)
    );
    fs.chmodSync(binPath, 0o755);
  } catch (e: any) {
    console.error(
      `[netpack] Failed to download ${JSON.stringify(url)}: ${
        (e && e.message) || e
      }`
    );
    throw e;
  }
}

async function checkAndPreparePackage(): Promise<void> {
  if (isValidBinaryPath(NETPACK_BINARY_PATH)) {
    if (!fs.existsSync(NETPACK_BINARY_PATH)) {
      console.warn(
        `[netpack] Ignoring bad configuration: NETPACK_BINARY_PATH=${NETPACK_BINARY_PATH}`
      );
    } else {
      applyManualBinaryPathOverride(NETPACK_BINARY_PATH);
      return;
    }
  }

  const { pkg, subpath } = pkgAndSubpathForCurrentPlatform();

  let binPath: string;
  try {
    binPath = require.resolve(`${pkg}/${subpath}`);
  } catch (e) {
    console.error(`[netpack] Failed to find package "${pkg}" on the file system

This can happen if you use the "--no-optional" flag. The "optionalDependencies"
package.json feature is used by netpack to install the correct binary executable
for your current platform. This install script will now attempt to work around
this. If that fails, you need to remove the "--no-optional" flag to use netpack.
`);

    binPath = downloadedBinPath(pkg, subpath);
    try {
      console.error(`[netpack] Trying to install package "${pkg}" using npm`);
      installUsingNPM(pkg, subpath, binPath);
    } catch (e2: any) {
      console.error(
        `[netpack] Failed to install package "${pkg}" using npm: ${
          (e2 && e2.message) || e2
        }`
      );

      try {
        await downloadDirectlyFromNPM(pkg, subpath, binPath);
      } catch (e3: any) {
        throw new Error(`Failed to install package "${pkg}"`);
      }
    }
  }

  maybeOptimizePackage(binPath);
}

checkAndPreparePackage().then(() => {
  if (isToPathJS) {
    // We need "node" before this command since it's a JavaScript file
    validateBinaryVersion(process.execPath, toPath);
  } else {
    // This is no longer a JavaScript file so don't run it using "node"
    validateBinaryVersion(toPath);
  }
});
