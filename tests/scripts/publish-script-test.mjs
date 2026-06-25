import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import test from 'node:test'

const repoRoot = path.resolve(import.meta.dirname, '..', '..')

test('publish script delegates release version sync to update-versions', () => {
  const publishScript = fs.readFileSync(path.join(repoRoot, 'scripts', 'publish.sh'), 'utf8')

  assert.match(
    publishScript,
    /node scripts\/release\/update-versions\.mjs "\$NEW_VER"/,
    'publish.sh must sync Cargo, workspace package, and Unity package versions before tests/publish'
  )
})
