import * as fs from "fs";
import * as os from "os";
import * as path from "path";

declare const NETPACK_VERSION: string;

export var NETPACK_BINARY_PATH: string | undefined =
  process.env.NETPACK_BINARY_PATH || NETPACK_BINARY_PATH;

export const isValidBinaryPath = (x: string | undefined): x is string =>
  !!x && x !== "/usr/bin/netpack";

const packageDarwin_arm64 = "@netpack/osx-arm64";
const packageDarwin_x64 = "@netpack/osx-x64";

export const knownWindowsPackages: Record<string, string> = {
  "win32 arm64 LE": "@netpack/win32-arm64",
  "win32 ia32 LE": "@netpack/win32-ia32",
  "win32 x64 LE": "@netpack/win32-x64",
};

export const knownUnixlikePackages: Record<string, string> = {
  "aix ppc64 BE": "@netpack/aix-ppc64",
  "android arm64 LE": "@netpack/android-arm64",
  "darwin arm64 LE": "@netpack/osx-arm64",
  "darwin x64 LE": "@netpack/osx-x64",
  "freebsd arm64 LE": "@netpack/freebsd-arm64",
  "freebsd x64 LE": "@netpack/freebsd-x64",
  "linux arm LE": "@netpack/linux-arm",
  "linux arm64 LE": "@netpack/linux-arm64",
  "linux ia32 LE": "@netpack/linux-ia32",
  "linux mips64el LE": "@netpack/linux-mips64el",
  "linux ppc64 LE": "@netpack/linux-ppc64",
  "linux riscv64 LE": "@netpack/linux-riscv64",
  "linux s390x BE": "@netpack/linux-s390x",
  "linux x64 LE": "@netpack/linux-x64",
  "linux loong64 LE": "@netpack/linux-loong64",
  "netbsd arm64 LE": "@netpack/netbsd-arm64",
  "netbsd x64 LE": "@netpack/netbsd-x64",
  "openbsd arm64 LE": "@netpack/openbsd-arm64",
  "openbsd x64 LE": "@netpack/openbsd-x64",
  "sunos x64 LE": "@netpack/sunos-x64",
};

export const knownWebAssemblyFallbackPackages: Record<string, string> = {
  "android arm LE": "@netpack/android-arm",
  "android x64 LE": "@netpack/android-x64",
};

export function pkgAndSubpathForCurrentPlatform(): {
  pkg: string;
  subpath: string;
  isWASM: boolean;
} {
  let pkg: string;
  let subpath: string;
  let isWASM = false;
  let platformKey = `${process.platform} ${os.arch()} ${os.endianness()}`;

  if (platformKey in knownWindowsPackages) {
    pkg = knownWindowsPackages[platformKey];
    subpath = "netpack.exe";
  } else if (platformKey in knownUnixlikePackages) {
    pkg = knownUnixlikePackages[platformKey];
    subpath = "bin/netpack";
  } else if (platformKey in knownWebAssemblyFallbackPackages) {
    pkg = knownWebAssemblyFallbackPackages[platformKey];
    subpath = "bin/netpack";
    isWASM = true;
  } else {
    throw new Error(`Unsupported platform: ${platformKey}`);
  }

  return { pkg, subpath, isWASM };
}

function pkgForSomeOtherPlatform(): string | null {
  const libMainJS = require.resolve("netpack");
  const nodeModulesDirectory = path.dirname(
    path.dirname(path.dirname(libMainJS))
  );

  if (path.basename(nodeModulesDirectory) === "node_modules") {
    for (const unixKey in knownUnixlikePackages) {
      try {
        const pkg = knownUnixlikePackages[unixKey];
        if (fs.existsSync(path.join(nodeModulesDirectory, pkg))) return pkg;
      } catch {}
    }

    for (const windowsKey in knownWindowsPackages) {
      try {
        const pkg = knownWindowsPackages[windowsKey];
        if (fs.existsSync(path.join(nodeModulesDirectory, pkg))) return pkg;
      } catch {}
    }
  }

  return null;
}

export function downloadedBinPath(pkg: string, subpath: string): string {
  const netpackLibDir = path.dirname(require.resolve("netpack"));
  return path.join(
    netpackLibDir,
    `downloaded-${pkg.replace("/", "-")}-${path.basename(subpath)}`
  );
}

export function generateBinPath(): { binPath: string; isWASM: boolean } {
  if (isValidBinaryPath(NETPACK_BINARY_PATH)) {
    if (!fs.existsSync(NETPACK_BINARY_PATH)) {
      console.warn(
        `[netpack] Ignoring bad configuration: NETPACK_BINARY_PATH=${NETPACK_BINARY_PATH}`
      );
    } else {
      return { binPath: NETPACK_BINARY_PATH, isWASM: false };
    }
  }

  const { pkg, subpath, isWASM } = pkgAndSubpathForCurrentPlatform();
  let binPath: string;

  try {
    binPath = require.resolve(`${pkg}/${subpath}`);
  } catch (e) {
    binPath = downloadedBinPath(pkg, subpath);
    if (!fs.existsSync(binPath)) {
      try {
        require.resolve(pkg);
      } catch {
        const otherPkg = pkgForSomeOtherPlatform();
        if (otherPkg) {
          let suggestions = `
Specifically the "${otherPkg}" package is present but this platform
needs the "${pkg}" package instead. People often get into this
situation by installing netpack on Windows or macOS and copying "node_modules"
into a Docker image that runs Linux, or by copying "node_modules" between
Windows and WSL environments.

If you are installing with npm, you can try not copying the "node_modules"
directory when you copy the files over, and running "npm ci" or "npm install"
on the destination platform after the copy. Or you could consider using yarn
instead of npm which has built-in support for installing a package on multiple
platforms simultaneously.

If you are installing with yarn, you can try listing both this platform and the
other platform in your ".yarnrc.yml" file using the "supportedArchitectures"
feature: https://yarnpkg.com/configuration/yarnrc/#supportedArchitectures
Keep in mind that this means multiple copies of netpack will be present.
`;

          // Use a custom message for macOS-specific architecture issues
          if (
            (pkg === packageDarwin_x64 && otherPkg === packageDarwin_arm64) ||
            (pkg === packageDarwin_arm64 && otherPkg === packageDarwin_x64)
          ) {
            suggestions = `
Specifically the "${otherPkg}" package is present but this platform
needs the "${pkg}" package instead. People often get into this
situation by installing netpack with npm running inside of Rosetta 2 and then
trying to use it with node running outside of Rosetta 2, or vice versa (Rosetta
2 is Apple's on-the-fly x86_64-to-arm64 translation service).

If you are installing with npm, you can try ensuring that both npm and node are
not running under Rosetta 2 and then reinstalling netpack. This likely involves
changing how you installed npm and/or node. For example, installing node with
the universal installer here should work: https://nodejs.org/en/download/. Or
you could consider using yarn instead of npm which has built-in support for
installing a package on multiple platforms simultaneously.

If you are installing with yarn, you can try listing both "arm64" and "x64"
in your ".yarnrc.yml" file using the "supportedArchitectures" feature:
https://yarnpkg.com/configuration/yarnrc/#supportedArchitectures
Keep in mind that this means multiple copies of netpack will be present.
`;
          }

          throw new Error(`
You installed netpack for another platform than the one you're currently using.
This won't work because netpack is written with native code and needs to
install a platform-specific binary executable.
${suggestions}
`);
        }

        throw new Error(`The package "${pkg}" could not be found, and is needed by netpack.

If you are installing netpack with npm, make sure that you don't specify the
"--no-optional" or "--omit=optional" flags. The "optionalDependencies" feature
of "package.json" is used by netpack to install the correct binary executable
for your current platform.`);
      }
      throw e;
    }
  }

  if (/\.zip\//.test(binPath)) {
    let pnpapi: any;
    try {
      pnpapi = require("pnpapi");
    } catch (e) {}
    if (pnpapi) {
      const root = pnpapi.getPackageInformation(
        pnpapi.topLevel
      ).packageLocation;
      const binTargetPath = path.join(
        root,
        "node_modules",
        ".cache",
        "netpack",
        `pnpapi-${pkg.replace("/", "-")}-${NETPACK_VERSION}-${path.basename(
          subpath
        )}`
      );
      if (!fs.existsSync(binTargetPath)) {
        fs.mkdirSync(path.dirname(binTargetPath), { recursive: true });
        fs.copyFileSync(binPath, binTargetPath);
        fs.chmodSync(binTargetPath, 0o755);
      }
      return { binPath: binTargetPath, isWASM };
    }
  }

  return { binPath, isWASM };
}
