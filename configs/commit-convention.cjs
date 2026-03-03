const COMMIT_TYPES = [
  { type: 'feat', release: 'minor' },
  { type: 'fix', release: 'patch' },
  { type: 'perf', release: 'patch' },
  { type: 'refactor' },
  { type: 'revert', release: 'patch' },
  { type: 'build' },
  { type: 'ci' },
  { type: 'docs' },
  { type: 'style' },
  { type: 'test' },
  { type: 'chore' }
]

const parserPreset = {
  name: 'conventional-changelog-conventionalcommits',
  parserOpts: {
    noteKeywords: ['BREAKING CHANGE', 'BREAKING CHANGES', 'BREAKING']
  }
}

module.exports = {
  commitTypes: COMMIT_TYPES,
  parserPreset,
  releaseRules: COMMIT_TYPES.filter(entry => Boolean(entry.release)).map(({ type, release }) => ({
    type,
    release
  }))
}
