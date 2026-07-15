import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const root = resolve(process.cwd(), "..", "..");
const npm = resolve(root, "npm");
const natives = [
  "@netpack/linux-x64",
  "@netpack/linux-arm64",
  "@netpack/osx-arm64",
  "@netpack/win-x64",
  "@netpack/win-arm64",
];
const projects = [...natives, "netpack"];
const props = resolve(root, "Directory.Build.props");

const xml = await readFile(props, "utf8");
const result = /<VersionPrefix>(.*)<\/VersionPrefix>/g.exec(xml);
const version = result[1];

for (const project of projects) {
  const path = resolve(npm, project, "package.json");
  const content = await readFile(path, "utf8");
  const packageJson = JSON.parse(content);
  packageJson.version = version;

  // Keep the central package's references to the native packages pinned to the
  // exact version being published.
  if (packageJson.optionalDependencies) {
    for (const dependency of Object.keys(packageJson.optionalDependencies)) {
      if (natives.includes(dependency)) {
        packageJson.optionalDependencies[dependency] = version;
      }
    }
  }

  await writeFile(path, JSON.stringify(packageJson, undefined, 2) + "\n", "utf8");
}
