const { commitTypes, parserPreset } = require('./configs/commit-convention.cjs')

module.exports = {
  extends: ['@commitlint/config-conventional'],
  parserPreset,
  rules: {
    'type-enum': [2, 'always', commitTypes.map(({ type }) => type)],
    'subject-case': [0],
    'body-max-line-length': [0]
  }
}
