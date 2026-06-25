import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import test from 'node:test'

const repoRoot = path.resolve(import.meta.dirname, '..', '..')

test('release workflow runs skill contract lint before release publication', () => {
  const releaseWorkflow = fs.readFileSync(
    path.join(repoRoot, '.github/workflows/release.yml'),
    'utf8'
  )

  assert.match(
    releaseWorkflow,
    /skills lint --severity error/,
    'release.yml must enforce Skill Contract v1 before creating release artifacts'
  )
})

test('lint workflow keeps skill contract lint as a required gate', () => {
  const lintWorkflow = fs.readFileSync(path.join(repoRoot, '.github/workflows/lint.yml'), 'utf8')

  assert.match(lintWorkflow, /skills lint --severity error/)
})
