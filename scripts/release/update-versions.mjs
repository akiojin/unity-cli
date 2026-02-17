#!/usr/bin/env node

/**
 * update-versions.mjs
 *
 * Updates version in all package.json files:
 * - package.json
 * - UnityCliBridge/Packages/unity-cli-bridge/package.json
 *
 * Usage:
 *   node scripts/release/update-versions.mjs <new-version>
 *
 * Example:
 *   node scripts/release/update-versions.mjs 4.2.0
 */

import { readFileSync, writeFileSync, existsSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const ROOT = resolve(__dirname, '../..');

const PACKAGE_FILES = [
  'package.json',
  'UnityCliBridge/Packages/unity-cli-bridge/package.json',
];

function parseVersion(version) {
  const match = version.match(/^v?(\d+)\.(\d+)\.(\d+)$/);
  if (!match) {
    throw new Error(`Invalid version format: ${version}. Expected: X.Y.Z or vX.Y.Z`);
  }
  return {
    major: parseInt(match[1], 10),
    minor: parseInt(match[2], 10),
    patch: parseInt(match[3], 10),
    toString() {
      return `${this.major}.${this.minor}.${this.patch}`;
    },
  };
}

function readPackageJson(filePath) {
  const fullPath = resolve(ROOT, filePath);
  if (!existsSync(fullPath)) {
    return null;
  }
  const content = readFileSync(fullPath, 'utf-8');
  return JSON.parse(content);
}

function writePackageJson(filePath, pkg) {
  const fullPath = resolve(ROOT, filePath);
  const content = JSON.stringify(pkg, null, 2) + '\n';
  writeFileSync(fullPath, content);
}

function updateVersion(newVersion) {
  const version = parseVersion(newVersion);
  const versionStr = version.toString();
  const results = [];

  // Update required package files
  for (const filePath of PACKAGE_FILES) {
    const pkg = readPackageJson(filePath);
    if (!pkg) {
      throw new Error(`Required file not found: ${filePath}`);
    }
    const oldVersion = pkg.version;
    pkg.version = versionStr;
    writePackageJson(filePath, pkg);
    results.push({ file: filePath, oldVersion, newVersion: versionStr });
  }

  return results;
}

function main() {
  const args = process.argv.slice(2);

  if (args.length === 0) {
    console.error('Usage: node scripts/release/update-versions.mjs <new-version>');
    console.error('Example: node scripts/release/update-versions.mjs 4.2.0');
    process.exit(1);
  }

  const newVersion = args[0];

  try {
    const results = updateVersion(newVersion);

    console.log('Version update results:');
    for (const result of results) {
      console.log(`  ${result.file}: ${result.oldVersion} -> ${result.newVersion}`);
    }
    console.log('\nVersion update completed successfully.');
  } catch (error) {
    console.error(`Error: ${error.message}`);
    process.exit(1);
  }
}

main();
