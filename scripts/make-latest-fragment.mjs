// Generates a per-platform fragment of the Tauri updater's latest.json.
//
// Usage: node scripts/make-latest-fragment.mjs <platform-key> <bundle-dir> <out-file>
//   e.g. node scripts/make-latest-fragment.mjs windows-x86_64 \
//          src-tauri/target/release/bundle/nsis latest-fragment-windows.json
//
// Scans <bundle-dir> (recursively one level is enough — we use recursive search)
// for an updater artifact with a matching `.sig` file and emits:
//   { "<platform-key>": { "signature": "<sig contents>", "url": "<download url>" } }
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";

const [platform, bundleDir, outFile] = process.argv.slice(2);
if (!platform || !bundleDir || !outFile) {
  console.error("Usage: node scripts/make-latest-fragment.mjs <platform-key> <bundle-dir> <out-file>");
  process.exit(1);
}

const DOWNLOAD_BASE = "https://github.com/ajjs1ajjs/RDM/releases/latest/download";

function findSigFiles(dir) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...findSigFiles(full));
    else if (entry.name.endsWith(".sig")) out.push(full);
  }
  return out;
}

const sigFiles = findSigFiles(bundleDir);
if (sigFiles.length === 0) {
  console.error(`No .sig updater artifacts found under ${bundleDir}`);
  process.exit(1);
}

// Prefer the artifact types the updater consumes: NSIS setup exe / .nsis.zip,
// AppImage, macOS .app.tar.gz.
const priority = [
  /\.app\.tar\.gz\.sig$/i,
  /\.appimage\.sig$/i,
  /-setup\.exe\.sig$/i,
  /\.nsis\.zip\.sig$/i,
];
sigFiles.sort((a, b) => {
  const rank = (p) => priority.findIndex((re) => re.test(p));
  const ra = rank(a) === -1 ? priority.length : rank(a);
  const rb = rank(b) === -1 ? priority.length : rank(b);
  return ra - rb || a.localeCompare(b);
});

const sigPath = sigFiles[0];
const artifactPath = sigPath.replace(/\.sig$/i, "");
const artifactName = relative(bundleDir, artifactPath).split(/[\\/]/).pop();
if (!artifactName) {
  console.error(`Could not resolve artifact name for ${sigPath}`);
  process.exit(1);
}

// GitHub normalizes spaces in release asset names to dots, so the download
// URL must use the dotted form (e.g. "RDM.Manager_2.0.9_x64-setup.exe").
const urlName = artifactName.replace(/ /g, ".");

const fragment = {
  [platform]: {
    signature: readFileSync(sigPath, "utf8").trim(),
    url: `${DOWNLOAD_BASE}/${urlName}`,
  },
};

writeFileSync(outFile, JSON.stringify(fragment, null, 2));
console.log(`Updater fragment written to ${outFile}`);
console.log(`  platform : ${platform}`);
console.log(`  artifact : ${artifactName}`);
