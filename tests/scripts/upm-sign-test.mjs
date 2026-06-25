import assert from 'node:assert/strict'
import test from 'node:test'

import {
  buildUnityUpmPackArgs,
  findAttestationPath,
  parseArgs,
  sanitizePackageName
} from '../../scripts/upm/sign-upm-package.mjs'

test('UPM signing argument parser accepts dry-run release options', () => {
  const opts = parseArgs([
    '--package-path',
    'Packages/com.example.tool',
    '--out-dir',
    'dist/packages',
    '--org-id',
    'org-123',
    '--tag',
    'v1.2.3',
    '--dry-run',
    '--json'
  ])

  assert.equal(opts.packagePath, 'Packages/com.example.tool')
  assert.equal(opts.outDir, 'dist/packages')
  assert.equal(opts.orgId, 'org-123')
  assert.equal(opts.tag, 'v1.2.3')
  assert.equal(opts.dryRun, true)
  assert.equal(opts.json, true)
})

test('UPM signing argument parser rejects unknown options', () => {
  assert.throws(() => parseArgs(['--unknown']), /Unknown option: --unknown/)
})

test('UPM signing command builder produces Unity upmPack arguments', () => {
  assert.deepEqual(
    buildUnityUpmPackArgs({
      packageFolder: '/repo/UnityCliBridge/Packages/unity-cli-bridge',
      outputTgz: '/repo/dist/upm/com.akiojin.unity-cli-bridge-1.2.3.tgz',
      orgId: 'org-123'
    }),
    [
      '-batchmode',
      '-nographics',
      '-quit',
      '-upmPack',
      '/repo/UnityCliBridge/Packages/unity-cli-bridge',
      '/repo/dist/upm/com.akiojin.unity-cli-bridge-1.2.3.tgz',
      '-cloudOrganization',
      'org-123',
      '-logfile',
      '-'
    ]
  )
})

test('UPM signing helpers sanitize scoped package names and find attestations', () => {
  assert.equal(sanitizePackageName('@akiojin/unity-cli-bridge'), 'akiojin-unity-cli-bridge')
  assert.equal(
    findAttestationPath('package/package.json\npackage/.attestation.p7m\n'),
    'package/.attestation.p7m'
  )
})
