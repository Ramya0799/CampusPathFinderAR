import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, "..");

// Master copy is in the Unity PathfinderAR project:
// Assets/Resources/PathFinderNavData.json
const candidateSources = [
  "D:/PathFinder/PathfinderAR/Assets/Resources/PathFinderNavData.json",
  path.resolve(rootDir, "../PathfinderAR/Assets/Resources/PathFinderNavData.json"),
  path.resolve(rootDir, "../../PathfinderAR/Assets/Resources/PathFinderNavData.json"),
];

const sourcePath = candidateSources.find((p) => fs.existsSync(p));

if (!sourcePath) {
  console.error(
    "Error: Could not locate master PathFinderNavData.json in Unity project at D:/PathFinder/PathfinderAR/Assets/Resources/PathFinderNavData.json"
  );
  process.exit(1);
}

const targets = [
  path.join(rootDir, "shared", "PathFinderNavData.json"),
  path.join(rootDir, "frontend", "src", "data", "PathFinderNavData.json"),
  path.join(rootDir, "frontend", "public", "PathFinderNavData.json"),
];

console.log(`Syncing navigation data from master copy:\n  ${sourcePath}`);

for (const target of targets) {
  const dir = path.dirname(target);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  fs.copyFileSync(sourcePath, target);
  console.log(`  -> Copied to ${path.relative(rootDir, target)}`);
}

console.log("Navigation data successfully synchronized.");
