import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import test from 'node:test'

const repoRoot = path.resolve(import.meta.dirname, '..', '..')
const scriptPath = path.join(repoRoot, 'scripts', 'release', 'update-versions.mjs')

function makeFixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-cli-update-versions-'))
  fs.mkdirSync(path.join(root, 'scripts', 'release'), { recursive: true })
  fs.mkdirSync(path.join(root, 'UnityCliBridge', 'Packages', 'unity-cli-bridge'), {
    recursive: true
  })
  fs.copyFileSync(scriptPath, path.join(root, 'scripts', 'release', 'update-versions.mjs'))
  fs.writeFileSync(
    path.join(root, 'package.json'),
    `${JSON.stringify({ name: 'unity-cli-workspace', version: '0.1.0' }, null, 2)}\n`
  )
  fs.writeFileSync(
    path.join(root, 'UnityCliBridge', 'Packages', 'unity-cli-bridge', 'package.json'),
    `${JSON.stringify({ name: 'com.akiojin.unity-cli-bridge', version: '0.1.0' }, null, 2)}\n`
  )
  fs.writeFileSync(
    path.join(root, 'Cargo.toml'),
    `[package]
name = "unity-cli"
version = "0.1.0"
edition = "2021"

[dependencies]
serde = "1.0"
`
  )
  return root
}

test('update-versions syncs workspace package, Unity package, and Cargo package versions', () => {
  const root = makeFixture()

  execFileSync('node', ['scripts/release/update-versions.mjs', 'v1.2.3'], {
    cwd: root,
    stdio: 'pipe'
  })

  const workspacePackage = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'))
  const unityPackage = JSON.parse(
    fs.readFileSync(
      path.join(root, 'UnityCliBridge', 'Packages', 'unity-cli-bridge', 'package.json'),
      'utf8'
    )
  )
  const cargoToml = fs.readFileSync(path.join(root, 'Cargo.toml'), 'utf8')

  assert.equal(workspacePackage.version, '1.2.3')
  assert.equal(unityPackage.version, '1.2.3')
  assert.match(cargoToml, /\[package\]\nname = "unity-cli"\nversion = "1\.2\.3"/)
  assert.match(cargoToml, /serde = "1\.0"/)
})
