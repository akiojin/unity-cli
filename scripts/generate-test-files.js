#!/usr/bin/env node
/**
 * 大規模テスト用C#ファイル生成スクリプト
 *
 * 目的: コードインデックスのスケーラビリティ検証
 * - 10万ファイル規模のC#コード生成
 * - クラス間の参照関係を持たせる
 * - シンボルリネーム等の動作検証用
 *
 * 使用方法:
 *   node scripts/generate-test-files.js [--modules N] [--files-per-module M] [--output DIR]
 *
 * デフォルト:
 *   --modules 100
 *   --files-per-module 1000
 *   --output UnityCliBridge/Assets/Scripts/Generated
 */

import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const PROJECT_ROOT = path.resolve(__dirname, '..')

// コマンドライン引数パース
function parseArgs() {
  const args = process.argv.slice(2)
  const config = {
    modules: 100,
    filesPerModule: 1000,
    output: path.join(PROJECT_ROOT, 'UnityCliBridge/Assets/Scripts/Generated'),
    dryRun: false,
    verbose: false
  }

  for (let i = 0; i < args.length; i++) {
    switch (args[i]) {
      case '--modules':
        config.modules = parseInt(args[++i], 10)
        break
      case '--files-per-module':
        config.filesPerModule = parseInt(args[++i], 10)
        break
      case '--output':
        config.output = path.resolve(args[++i])
        break
      case '--dry-run':
        config.dryRun = true
        break
      case '--verbose':
        config.verbose = true
        break
      case '--help':
        console.log(`
Usage: node generate-test-files.js [options]

Options:
  --modules N           Number of modules (default: 100)
  --files-per-module M  Files per module (default: 1000)
  --output DIR          Output directory
  --dry-run             Show what would be created without writing
  --verbose             Show detailed progress
  --help                Show this help

Examples:
  # Generate 100,000 files (100 modules × 1000 files)
  node scripts/generate-test-files.js

  # Generate 10,000 files for quick test
  node scripts/generate-test-files.js --modules 10 --files-per-module 1000

  # Generate 1,000 files for minimal test
  node scripts/generate-test-files.js --modules 10 --files-per-module 100
`)
        process.exit(0)
    }
  }

  return config
}

// Coreクラス生成
function generateCoreFiles(outputDir) {
  const coreDir = path.join(outputDir, 'Core')
  fs.mkdirSync(coreDir, { recursive: true })

  // BaseEntity.cs
  const baseEntity = `using UnityEngine;

namespace Generated.Core
{
    /// <summary>
    /// Base entity class for all generated entities.
    /// Used to test inheritance-based symbol references.
    /// </summary>
    public abstract class BaseEntity : MonoBehaviour
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }

        protected virtual void Awake()
        {
            Initialize();
        }

        protected virtual void Initialize()
        {
            IsActive = true;
        }

        public abstract void Process();

        public virtual void Reset()
        {
            Id = 0;
            Name = string.Empty;
            IsActive = false;
        }
    }
}
`

  // IService.cs
  const iService = `namespace Generated.Core
{
    /// <summary>
    /// Service interface for all generated services.
    /// Used to test interface-based symbol references.
    /// </summary>
    public interface IService
    {
        void Execute();
        bool Validate();
        void Cleanup();
    }
}
`

  // Utils.cs
  const utils = `using System;
using System.Collections.Generic;

namespace Generated.Core
{
    /// <summary>
    /// Utility class with static helper methods.
    /// Used to test static method references across the codebase.
    /// </summary>
    public static class Utils
    {
        public static int Helper(int value)
        {
            return value * 2;
        }

        public static string FormatName(string name)
        {
            return string.IsNullOrEmpty(name) ? "Unknown" : name.Trim();
        }

        public static bool ValidateId(int id)
        {
            return id > 0;
        }

        public static T GetOrDefault<T>(Dictionary<string, T> dict, string key, T defaultValue)
        {
            return dict.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public static void Log(string message)
        {
            UnityEngine.Debug.Log($"[Generated] {message}");
        }
    }
}
`

  fs.writeFileSync(path.join(coreDir, 'BaseEntity.cs'), baseEntity)
  fs.writeFileSync(path.join(coreDir, 'IService.cs'), iService)
  fs.writeFileSync(path.join(coreDir, 'Utils.cs'), utils)

  return 3
}

// エンティティクラス生成
function generateEntityFile(moduleNum, entityNum, totalModules, filesPerModule) {
  const modulePadded = String(moduleNum).padStart(3, '0')
  const entityPadded = String(entityNum).padStart(4, '0')
  const className = `Entity${modulePadded}_${entityPadded}`

  // 同一モジュール内の他エンティティへの参照（3-5個）
  const sameModuleRefs = []
  const refCount = 3 + Math.floor(Math.random() * 3)
  for (let i = 0; i < refCount && i < filesPerModule; i++) {
    const refNum = (entityNum + i + 1) % filesPerModule
    if (refNum !== entityNum) {
      sameModuleRefs.push(`Entity${modulePadded}_${String(refNum).padStart(4, '0')}`)
    }
  }

  // 隣接モジュールへの参照（1-2個）
  const crossModuleRefs = []
  if (moduleNum > 0) {
    const prevModule = String(moduleNum - 1).padStart(3, '0')
    crossModuleRefs.push(`Entity${prevModule}_0000`)
  }
  if (moduleNum < totalModules - 1) {
    const nextModule = String(moduleNum + 1).padStart(3, '0')
    crossModuleRefs.push(`Entity${nextModule}_0000`)
  }

  // IServiceを実装するかどうか（50%の確率）
  const implementsService = entityNum % 2 === 0

  const serviceImpl = implementsService
    ? `
        public void Execute()
        {
            Process();
        }

        public bool Validate()
        {
            return Utils.ValidateId(Id);
        }

        public void Cleanup()
        {
            Reset();
        }`
    : ''

  const serviceInterface = implementsService ? ', IService' : ''

  // 同一モジュール参照のフィールドとメソッド
  const sameModuleFields = sameModuleRefs
    .map((ref, i) => `        private ${ref} _ref${i};`)
    .join('\n')

  const sameModuleInit = sameModuleRefs
    .map((ref, i) => `            _ref${i} = GetComponent<${ref}>();`)
    .join('\n')

  // クロスモジュール参照のusing
  const crossModuleUsings =
    crossModuleRefs.length > 0
      ? crossModuleRefs
          .map(ref => {
            const modNum = ref.substring(6, 9)
            return `using Generated.Module${modNum};`
          })
          .filter((v, i, a) => a.indexOf(v) === i)
          .join('\n')
      : ''

  return `using UnityEngine;
using Generated.Core;
${crossModuleUsings}

namespace Generated.Module${modulePadded}
{
    /// <summary>
    /// Generated entity class ${className}.
    /// Module: ${moduleNum}, Entity: ${entityNum}
    /// </summary>
    public class ${className} : BaseEntity${serviceInterface}
    {
        public float Value${entityNum} { get; set; }

${sameModuleFields}

        protected override void Initialize()
        {
            base.Initialize();
            Name = Utils.FormatName("${className}");
            Value${entityNum} = Utils.Helper(${entityNum});
${sameModuleInit}
        }

        public override void Process()
        {
            var result = Utils.Helper((int)Value${entityNum});
            Utils.Log($"${className} processed: {result}");
        }
${serviceImpl}
    }
}
`
}

// メイン生成処理
async function generate(config) {
  const { modules, filesPerModule, output, dryRun, verbose } = config
  const totalFiles = modules * filesPerModule + 3 // +3 for Core files

  console.log('\n📁 Test File Generation')
  console.log(`   Modules: ${modules}`)
  console.log(`   Files per module: ${filesPerModule}`)
  console.log(`   Total files: ${totalFiles.toLocaleString()}`)
  console.log(`   Output: ${output}`)
  console.log(`   Mode: ${dryRun ? 'DRY RUN' : 'WRITE'}\n`)

  if (dryRun) {
    console.log('🔍 Dry run mode - no files will be created\n')
    return
  }

  // 出力ディレクトリ作成
  fs.mkdirSync(output, { recursive: true })

  // Coreファイル生成
  console.log('📦 Generating Core files...')
  const coreCount = generateCoreFiles(output)
  console.log(`   ✅ ${coreCount} Core files created\n`)

  // モジュールファイル生成
  console.log('📦 Generating module files...')
  const startTime = Date.now()
  let totalCreated = coreCount

  for (let m = 0; m < modules; m++) {
    const modulePadded = String(m).padStart(3, '0')
    const moduleDir = path.join(output, `Module${modulePadded}`)
    fs.mkdirSync(moduleDir, { recursive: true })

    for (let e = 0; e < filesPerModule; e++) {
      const entityPadded = String(e).padStart(4, '0')
      const fileName = `Entity${modulePadded}_${entityPadded}.cs`
      const content = generateEntityFile(m, e, modules, filesPerModule)
      fs.writeFileSync(path.join(moduleDir, fileName), content)
      totalCreated++
    }

    // 進捗表示
    const progress = (((m + 1) / modules) * 100).toFixed(1)
    const elapsed = ((Date.now() - startTime) / 1000).toFixed(1)
    const rate = ((totalCreated / (Date.now() - startTime)) * 1000).toFixed(0)

    if (verbose || (m + 1) % 10 === 0 || m === modules - 1) {
      process.stdout.write(
        `\r   Module ${m + 1}/${modules} (${progress}%) - ${totalCreated.toLocaleString()} files - ${elapsed}s - ${rate} files/s`
      )
    }
  }

  const totalTime = ((Date.now() - startTime) / 1000).toFixed(1)
  console.log('\n\n✅ Generation complete!')
  console.log(`   Total files: ${totalCreated.toLocaleString()}`)
  console.log(`   Time: ${totalTime}s`)
  console.log(`   Rate: ${((totalCreated / (Date.now() - startTime)) * 1000).toFixed(0)} files/s\n`)

  // .meta ファイル生成のヒント
  console.log('💡 Next steps:')
  console.log('   1. Open Unity Editor to generate .meta files')
  console.log('   2. Run: mcp__unity-cli__build_index')
  console.log('   3. Verify with: mcp__unity-cli__get_index_status\n')
}

// 実行
const config = parseArgs()
generate(config).catch(err => {
  console.error('❌ Error:', err.message)
  process.exit(1)
})
