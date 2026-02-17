#!/usr/bin/env node
import fs from 'fs'
import path from 'path'
import { spawnSync } from 'child_process'
import { fileURLToPath, pathToFileURL } from 'url'

function usage(exitCode = 0) {
  const msg = `
Usage:
  scripts/upm/sign-upm-package.mjs [options]

Options:
  --package-path <dir>   Path to folder containing package.json
                         (default: UnityCliBridge/Packages/unity-cli-bridge)
  --out-dir <dir>        Output directory for signed .tgz (default: dist/upm)
  --out-tgz <file>       Output .tgz path (overrides --out-dir)
  --org-id <id>          Unity Cloud Organization ID (or env UNITY_CLOUD_ORG_ID)
  --unity-path <path>    Unity Editor executable path (or env UNITY_EDITOR_PATH)
  --tag <vX.Y.Z>         Release tag to verify against package.json version
                         (default: env RELEASE_TAG or GITHUB_REF_NAME if set)
  --dry-run              Print planned command and exit 0
  --json                 Emit JSON to stdout
  --skip-verify          Skip checking .attestation.p7m inside the .tgz
  --verbose              Verbose logs
  -h, --help             Show help
`
  process.stdout.write(msg.trimStart())
  process.stdout.write('\n')
  process.exit(exitCode)
}

export function sanitizePackageName(name) {
  return String(name ?? '')
    .replace(/^@/, '')
    .split('/')
    .join('-')
}

export function findAttestationPath(tarListOutput) {
  const lines = String(tarListOutput ?? '')
    .split('\n')
    .map(l => l.trim())
    .filter(Boolean)
  return lines.find(l => /(^|\/)\.attestation\.p7m$/.test(l)) ?? null
}

export function buildUnityUpmPackArgs({ packageFolder, outputTgz, orgId }) {
  return [
    '-batchmode',
    '-nographics',
    '-quit',
    '-upmPack',
    packageFolder,
    outputTgz,
    '-cloudOrganization',
    orgId,
    '-logfile',
    '-'
  ]
}

export function parseArgs(argv) {
  const args = [...argv]
  const opts = {
    packagePath: 'UnityCliBridge/Packages/unity-cli-bridge',
    outDir: 'dist/upm',
    outTgz: null,
    orgId: null,
    unityPath: null,
    tag: null,
    dryRun: false,
    json: false,
    skipVerify: false,
    verbose: false
  }

  for (let i = 0; i < args.length; i++) {
    const a = args[i]
    if (a === '--package-path') {
      opts.packagePath = args[++i]
    } else if (a === '--out-dir') {
      opts.outDir = args[++i]
    } else if (a === '--out-tgz') {
      opts.outTgz = args[++i]
    } else if (a === '--org-id') {
      opts.orgId = args[++i]
    } else if (a === '--unity-path') {
      opts.unityPath = args[++i]
    } else if (a === '--tag') {
      opts.tag = args[++i]
    } else if (a === '--dry-run') {
      opts.dryRun = true
    } else if (a === '--json') {
      opts.json = true
    } else if (a === '--skip-verify') {
      opts.skipVerify = true
    } else if (a === '--verbose') {
      opts.verbose = true
    } else if (a === '-h' || a === '--help') {
      usage(0)
    } else if (a?.startsWith('-')) {
      throw new Error(`Unknown option: ${a}`)
    }
  }

  if (!opts.packagePath) throw new Error('--package-path is required')
  if (!opts.outDir && !opts.outTgz) throw new Error('Either --out-dir or --out-tgz is required')
  return opts
}

function resolveUnityExecutable({ unityPath, verbose }) {
  const fromEnv = process.env.UNITY_EDITOR_PATH
  const candidate = unityPath || fromEnv
  if (candidate) return candidate

  const linuxDefault = '/opt/unity/Editor/Unity'
  if (process.platform === 'linux' && fs.existsSync(linuxDefault)) return linuxDefault

  if (verbose) {
    console.warn(
      '[upm-sign] UNITY_EDITOR_PATH not set; falling back to `Unity` on PATH (may fail if Unity is not installed).'
    )
  }
  return 'Unity'
}

function resolveOrgId(orgId) {
  return orgId || process.env.UNITY_CLOUD_ORG_ID || process.env.UNITY_CLOUD_ORGANIZATION_ID || null
}

function resolveTag(tag) {
  return tag || process.env.RELEASE_TAG || process.env.GITHUB_REF_NAME || null
}

function readUpmPackageJson(packageDirAbs) {
  const packageJsonPath = path.join(packageDirAbs, 'package.json')
  if (!fs.existsSync(packageJsonPath)) {
    throw new Error(`package.json not found: ${packageJsonPath}`)
  }
  const json = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'))
  if (!json?.name || !json?.version) {
    throw new Error(`Invalid package.json (missing name/version): ${packageJsonPath}`)
  }
  return json
}

function ensureDir(dirAbs) {
  fs.mkdirSync(dirAbs, { recursive: true })
}

function verifyTarballHasAttestation(tgzAbs) {
  const list = spawnSync('tar', ['-tzf', tgzAbs], { encoding: 'utf8' })
  if (list.error) throw list.error
  if (list.status !== 0) {
    throw new Error(`tar -tzf failed (exit ${list.status})`)
  }
  const attestationPath = findAttestationPath(list.stdout)
  if (!attestationPath) {
    throw new Error('Missing .attestation.p7m in tarball')
  }
  return attestationPath
}

function main() {
  const __filename = fileURLToPath(import.meta.url)
  const __dirname = path.dirname(__filename)
  const repoRoot = path.resolve(__dirname, '..', '..')

  let opts
  try {
    opts = parseArgs(process.argv.slice(2))
  } catch (e) {
    console.error(`[upm-sign] ${e.message}`)
    usage(2)
  }

  const tag = resolveTag(opts.tag)
  const orgId = resolveOrgId(opts.orgId)
  if (!orgId) {
    console.error(
      '[upm-sign] Missing org id. Provide --org-id or set UNITY_CLOUD_ORG_ID / UNITY_CLOUD_ORGANIZATION_ID.'
    )
    process.exit(2)
  }

  const unityExe = resolveUnityExecutable({ unityPath: opts.unityPath, verbose: opts.verbose })

  const packageDirAbs = path.resolve(repoRoot, opts.packagePath)
  const pkg = readUpmPackageJson(packageDirAbs)

  if (tag) {
    const expected = `v${pkg.version}`
    if (tag !== expected) {
      console.error(`[upm-sign] Version mismatch: tag(${tag}) != package(${expected})`)
      process.exit(2)
    }
  }

  const outDirAbs = path.resolve(repoRoot, opts.outDir)
  ensureDir(outDirAbs)

  const tgzAbs = opts.outTgz
    ? path.resolve(repoRoot, opts.outTgz)
    : path.join(outDirAbs, `${sanitizePackageName(pkg.name)}-${pkg.version}.tgz`)
  ensureDir(path.dirname(tgzAbs))

  const unityArgs = buildUnityUpmPackArgs({
    packageFolder: packageDirAbs,
    outputTgz: tgzAbs,
    orgId
  })
  if (opts.verbose) {
    console.log('[upm-sign] package:', packageDirAbs)
    console.log('[upm-sign] out:', tgzAbs)
    console.log('[upm-sign] org:', orgId)
  }

  if (opts.dryRun) {
    const payload = {
      unityExe,
      unityArgs,
      tgz: tgzAbs,
      package: pkg.name,
      version: pkg.version,
      orgId
    }
    process.stdout.write(
      opts.json ? `${JSON.stringify(payload)}\n` : `${unityExe} ${unityArgs.join(' ')}\n`
    )
    process.exit(0)
  }

  const run = spawnSync(unityExe, unityArgs, { stdio: 'inherit' })
  if (run.error) {
    console.error(`[upm-sign] Failed to run Unity: ${run.error.message}`)
    process.exit(1)
  }
  if (typeof run.status === 'number' && run.status !== 0) {
    process.exit(run.status)
  }

  if (!fs.existsSync(tgzAbs)) {
    console.error(`[upm-sign] Signed tgz not found: ${tgzAbs}`)
    process.exit(1)
  }

  let attestationPath = null
  if (!opts.skipVerify) {
    try {
      attestationPath = verifyTarballHasAttestation(tgzAbs)
    } catch (e) {
      console.error(`[upm-sign] ${e.message}`)
      process.exit(1)
    }
  }

  const payload = {
    tgz: tgzAbs,
    attestation: attestationPath,
    package: pkg.name,
    version: pkg.version,
    tag: tag ?? undefined
  }
  process.stdout.write(opts.json ? `${JSON.stringify(payload)}\n` : `${tgzAbs}\n`)
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main()
}
