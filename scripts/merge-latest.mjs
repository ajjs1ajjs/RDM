// Merges per-platform updater fragments into the final latest.json
// consumed by the Tauri updater endpoint.
//
// Usage: node scripts/merge-latest.mjs <version> <out-file> <fragment...>
//   e.g. node scripts/merge-latest.mjs 2.0.9 latest.json frag-win.json frag-linux.json frag-mac-arm.json frag-mac-x64.json
import { readFileSync, writeFileSync } from "node:fs";

const [version, outFile, ...fragmentPaths] = process.argv.slice(2);
if (!version || !outFile || fragmentPaths.length === 0) {
  console.error("Usage: node scripts/merge-latest.mjs <version> <out-file> <fragment...>");
  process.exit(1);
}

const platforms = {};
for (const path of fragmentPaths) {
  Object.assign(platforms, JSON.parse(readFileSync(path, "utf8")));
}

const latest = {
  version,
  notes: `RDM Manager ${version}`,
  pub_date: new Date().toISOString(),
  platforms,
};

writeFileSync(outFile, JSON.stringify(latest, null, 2));
console.log(`latest.json written for v${version} with platforms: ${Object.keys(platforms).join(", ")}`);
