#!/usr/bin/env node
/**
 * 10万C#ファイル生成スクリプト
 * 100モジュール × 1000ファイル = 100,000ファイル
 *
 * 参照関係:
 * - 全クラスがBaseEntityを継承またはIServiceを実装
 * - 同一モジュール内で3-5クラスを参照
 * - 隣接モジュールから1-2クラスを参照
 * - Utils.Helperを全クラスから参照
 */

const fs = require('fs')
const path = require('path')

const PROJECT_ROOT = path.join(__dirname, '../UnityCliBridge')
const GENERATED_DIR = path.join(PROJECT_ROOT, 'Assets/Scripts/Generated')

const MODULE_COUNT = 100
const FILES_PER_MODULE = 1000
const TOTAL_FILES = MODULE_COUNT * FILES_PER_MODULE

// 進捗表示
let filesCreated = 0
let lastReport = 0
const startTime = Date.now()

function reportProgress() {
  const elapsed = ((Date.now() - startTime) / 1000).toFixed(1)
  const rate = (filesCreated / elapsed).toFixed(0)
  const percent = ((filesCreated / TOTAL_FILES) * 100).toFixed(1)
  process.stdout.write(
    `\r[${percent}%] ${filesCreated}/${TOTAL_FILES} files (${rate} files/sec, ${elapsed}s elapsed)`
  )
}

// ディレクトリ作成
function ensureDir(dir) {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true })
  }
}

// Core ファイル生成
function generateCoreFiles() {
  const coreDir = path.join(GENERATED_DIR, 'Core')
  ensureDir(coreDir)

  // BaseEntity.cs
  fs.writeFileSync(
    path.join(coreDir, 'BaseEntity.cs'),
    `using UnityEngine;

namespace Generated.Core
{
    /// <summary>
    /// 基底エンティティクラス - 全10万クラスから継承される
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

        public virtual void Update()
        {
            if (IsActive)
            {
                Process();
            }
        }
    }
}
`
  )

  // IService.cs
  fs.writeFileSync(
    path.join(coreDir, 'IService.cs'),
    `namespace Generated.Core
{
    /// <summary>
    /// サービスインターフェース - 多くのクラスが実装
    /// </summary>
    public interface IService
    {
        void Start();
        void Stop();
        bool IsRunning { get; }
    }
}
`
  )

  // Utils.cs
  fs.writeFileSync(
    path.join(coreDir, 'Utils.cs'),
    `using UnityEngine;

namespace Generated.Core
{
    /// <summary>
    /// ユーティリティクラス - 全クラスから参照される
    /// リネームテスト対象: Helper メソッド
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// ヘルパーメソッド - 10万箇所から参照される
        /// リネームテスト対象
        /// </summary>
        public static void Helper()
        {
            Debug.Log("Helper called");
        }

        public static int Calculate(int a, int b)
        {
            return a + b;
        }

        public static string Format(string template, params object[] args)
        {
            return string.Format(template, args);
        }

        public static T GetOrDefault<T>(T value, T defaultValue)
        {
            return value != null ? value : defaultValue;
        }
    }
}
`
  )

  filesCreated += 3
}

// エンティティクラス生成
function generateEntityClass(moduleNum, entityNum) {
  const moduleName = `Module${String(moduleNum).padStart(3, '0')}`
  const className = `Entity${String(entityNum).padStart(4, '0')}`

  // 参照するクラスを決定
  const sameModuleRefs = []
  const adjacentModuleRefs = []

  // 同一モジュール内から3-5クラス参照
  const sameModuleCount = 3 + (entityNum % 3)
  for (let i = 0; i < sameModuleCount && i < entityNum; i++) {
    const refNum = (entityNum - 1 - i) % FILES_PER_MODULE
    if (refNum >= 0 && refNum !== entityNum) {
      sameModuleRefs.push(`Entity${String(refNum).padStart(4, '0')}`)
    }
  }

  // 隣接モジュールから1-2クラス参照
  if (moduleNum > 1) {
    const prevModule = `Module${String(moduleNum - 1).padStart(3, '0')}`
    adjacentModuleRefs.push({
      module: prevModule,
      entity: `Entity${String(entityNum % 100).padStart(4, '0')}`
    })
  }
  if (moduleNum < MODULE_COUNT) {
    const nextModule = `Module${String(moduleNum + 1).padStart(3, '0')}`
    adjacentModuleRefs.push({
      module: nextModule,
      entity: `Entity${String((entityNum + 50) % 100).padStart(4, '0')}`
    })
  }

  // 継承またはインターフェース実装を決定
  const implementsService = entityNum % 5 === 0
  const inheritance = implementsService ? 'BaseEntity, IService' : 'BaseEntity'

  // サービス実装コード
  const serviceImpl = implementsService
    ? `
        public bool IsRunning { get; private set; }

        public void Start()
        {
            IsRunning = true;
            Utils.Helper();
        }

        public void Stop()
        {
            IsRunning = false;
        }
`
    : ''

  // 同一モジュール参照フィールド
  const sameModuleFields = sameModuleRefs
    .map((ref, i) => `        private ${ref} _ref${i};`)
    .join('\n')

  // 同一モジュール参照メソッド
  const sameModuleUsage = sameModuleRefs
    .map((ref, i) => `            if (_ref${i} != null) _ref${i}.Process();`)
    .join('\n')

  // 隣接モジュール using
  const adjacentUsings = adjacentModuleRefs.map(r => `using Generated.${r.module};`).join('\n')

  // 隣接モジュール参照
  const adjacentFields = adjacentModuleRefs
    .map((r, i) => `        private ${r.entity} _adjacent${i};`)
    .join('\n')

  const content = `using UnityEngine;
using Generated.Core;
${adjacentUsings}

namespace Generated.${moduleName}
{
    /// <summary>
    /// ${moduleName}.${className}
    /// BaseEntity継承、Utils.Helper参照
    /// </summary>
    public class ${className} : ${inheritance}
    {
        private int _counter;
${sameModuleFields}
${adjacentFields}
${serviceImpl}
        protected override void Initialize()
        {
            base.Initialize();
            _counter = 0;
            Utils.Helper();
        }

        public override void Process()
        {
            _counter++;
            Utils.Helper();
${sameModuleUsage}
        }

        public int GetCounter()
        {
            Utils.Helper();
            return _counter;
        }
    }
}
`

  return content
}

// モジュールファイル生成
function generateModule(moduleNum) {
  const moduleName = `Module${String(moduleNum).padStart(3, '0')}`
  const moduleDir = path.join(GENERATED_DIR, moduleName)
  ensureDir(moduleDir)

  for (let i = 0; i < FILES_PER_MODULE; i++) {
    const className = `Entity${String(i).padStart(4, '0')}`
    const content = generateEntityClass(moduleNum, i)
    fs.writeFileSync(path.join(moduleDir, `${className}.cs`), content)

    filesCreated++
    if (filesCreated - lastReport >= 1000) {
      reportProgress()
      lastReport = filesCreated
    }
  }
}

// メイン処理
async function main() {
  console.log('=== 10万C#ファイル生成スクリプト ===')
  console.log(`Target: ${TOTAL_FILES} files (${MODULE_COUNT} modules × ${FILES_PER_MODULE} files)`)
  console.log(`Output: ${GENERATED_DIR}`)
  console.log()

  // 既存のGeneratedディレクトリを削除
  if (fs.existsSync(GENERATED_DIR)) {
    console.log('Removing existing Generated directory...')
    fs.rmSync(GENERATED_DIR, { recursive: true })
  }

  ensureDir(GENERATED_DIR)

  console.log('Generating Core files...')
  generateCoreFiles()

  console.log('Generating module files...')
  for (let m = 1; m <= MODULE_COUNT; m++) {
    generateModule(m)
  }

  reportProgress()
  console.log('\n')

  const elapsed = ((Date.now() - startTime) / 1000).toFixed(1)
  const rate = (filesCreated / elapsed).toFixed(0)

  console.log('=== Generation Complete ===')
  console.log(`Total files: ${filesCreated}`)
  console.log(`Total time: ${elapsed}s`)
  console.log(`Rate: ${rate} files/sec`)

  // ファイル数確認
  const actualCount = fs
    .readdirSync(GENERATED_DIR, { recursive: true })
    .filter(f => f.endsWith('.cs')).length
  console.log(`Verified file count: ${actualCount}`)
}

main().catch(console.error)
