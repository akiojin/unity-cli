#!/usr/bin/env pwsh
# Speckit: Create a new feature directory (SPEC-xxxxxxxx) without creating git branches.

[CmdletBinding()]
param(
    [switch]$Json,
    [switch]$Help,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$FeatureDescription
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Host "Usage: ./create-new-feature.ps1 [-Json] <feature description>"
    Write-Host ""
    Write-Host "Speckit 用の新規要件（SPEC-xxxxxxxx）を作成します。ブランチは作成しません。"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -Json   JSON形式で出力"
    Write-Host "  -Help   ヘルプを表示"
    exit 0
}

if (-not $FeatureDescription -or $FeatureDescription.Count -eq 0) {
    Write-Error "ERROR: 要件の説明が空です。Usage: ./create-new-feature.ps1 [-Json] <feature description>"
    exit 1
}

$featureDesc = ($FeatureDescription -join ' ').Trim()

function Find-RepositoryRoot {
    param(
        [string]$StartDir,
        [string[]]$Markers = @('.git', '.specify')
    )
    $current = Resolve-Path $StartDir
    while ($true) {
        foreach ($marker in $Markers) {
            if (Test-Path (Join-Path $current $marker)) {
                return $current
            }
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { return $null }
        $current = $parent
    }
}

function New-SpecId {
    # 8桁のUUID（hex）を使用
    $short = [guid]::NewGuid().ToString('N').Substring(0, 8).ToLowerInvariant()
    return "SPEC-$short"
}

function Update-SpecsReadme {
    param([string]$RepoRoot)

    $specsDir = Join-Path $RepoRoot 'specs'
    $outputFile = Join-Path $specsDir 'specs.md'
    New-Item -ItemType Directory -Path $specsDir -Force | Out-Null

    $specDirs = @()
    if (Test-Path $specsDir) {
        $specDirs = Get-ChildItem -Path $specsDir -Directory |
            Where-Object { $_.Name -match '^SPEC-[a-z0-9]{8}$' } |
            Sort-Object Name
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# 要件一覧')
    $lines.Add('')
    $lines.Add('このファイルは `.specify/scripts/bash/update-specs-readme.sh` または `.specify/scripts/powershell/create-new-feature.ps1` により自動生成されます。')
    $lines.Add('手動編集は避けてください（再生成で上書きされます）。')
    $lines.Add('')
    $lines.Add('## ステータス別一覧')
    $lines.Add('')

    $entries = @()
    $groupOrder = New-Object System.Collections.Generic.List[string]

    if (-not $specDirs -or $specDirs.Count -eq 0) {
        $entries = @([PSCustomObject]@{
            Id = '(なし)'
            Title = '-'
            Status = '未設定'
            Created = '-'
            HasSpec = $false
        })
        $groupOrder.Add('未設定') | Out-Null
    } else {
        foreach ($dir in $specDirs) {
            $id = $dir.Name
            $specFile = Join-Path $dir.FullName 'spec.md'

            $title = $id
            $status = '-'
            $created = '-'

            if (Test-Path $specFile) {
                $content = Get-Content -Path $specFile -ErrorAction SilentlyContinue
                $heading = $content | Where-Object { $_ -match '^#' } | Select-Object -First 1
                if ($heading) {
                    $title = ($heading -replace '^#\s*', '').Trim()
                    $title = ($title -replace '^(機能仕様書|Feature Specification):\s*', '').Trim()
                }

                $statusLine = $content | Where-Object { $_ -match '^\*\*(ステータス|Status)\*\*:' } | Select-Object -First 1
                if ($statusLine) {
                    $status = ($statusLine -replace '^\*\*(ステータス|Status)\*\*:\s*', '').Trim()
                }

                $createdLine = $content | Where-Object { $_ -match '^\*\*(作成日|Created)\*\*:' } | Select-Object -First 1
                if ($createdLine) {
                    $created = ($createdLine -replace '^\*\*(作成日|Created)\*\*:\s*', '').Trim()
                }
            }

            if ([string]::IsNullOrWhiteSpace($status) -or $status -eq '-') {
                $status = '未設定'
            }

            $title = $title -replace '\|', '\|'
            $status = $status -replace '\|', '\|'
            $created = $created -replace '\|', '\|'

            if (-not $groupOrder.Contains($status)) {
                $groupOrder.Add($status) | Out-Null
            }

            $entries += [PSCustomObject]@{
                Id = $id
                Title = $title
                Status = $status
                Created = $created
                HasSpec = (Test-Path $specFile)
            }
        }
    }

    $preferredOrder = @('進行中', '下書き', '実装完了', '完了', '未設定')
    $finalOrder = New-Object System.Collections.Generic.List[string]
    foreach ($status in $preferredOrder) {
        if ($groupOrder.Contains($status)) {
            $finalOrder.Add($status) | Out-Null
        }
    }
    foreach ($status in $groupOrder) {
        if (-not $finalOrder.Contains($status)) {
            $finalOrder.Add($status) | Out-Null
        }
    }

    foreach ($status in $finalOrder) {
        $groupEntries = $entries | Where-Object { $_.Status -eq $status }
        $lines.Add("### $status ($($groupEntries.Count)件)")
        $lines.Add('')
        $lines.Add('| 要件ID | タイトル | ステータス | 作成日 |')
        $lines.Add('|---|---|---|---|')
        foreach ($entry in $groupEntries) {
            if ($entry.HasSpec) {
                $lines.Add("| `$($entry.Id)` | [$($entry.Title)]($($entry.Id)/spec.md) | $($entry.Status) | $($entry.Created) |")
            } else {
                $lines.Add("| `$($entry.Id)` | $($entry.Title) | $($entry.Status) | $($entry.Created) |")
            }
        }
        $lines.Add('')
    }

    Set-Content -Path $outputFile -Value $lines -Encoding UTF8
}

$fallbackRoot = Find-RepositoryRoot -StartDir $PSScriptRoot
if (-not $fallbackRoot) {
    Write-Error "ERROR: リポジトリルートを特定できませんでした。リポジトリ内で実行してください。"
    exit 1
}

try {
    $repoRoot = (git rev-parse --show-toplevel 2>$null).Trim()
    $hasGit = ($LASTEXITCODE -eq 0)
    if (-not $hasGit) { throw "git not available" }
} catch {
    $repoRoot = (Resolve-Path $fallbackRoot).Path
    $hasGit = $false
}

Set-Location $repoRoot

$specsDir = Join-Path $repoRoot 'specs'
New-Item -ItemType Directory -Path $specsDir -Force | Out-Null

$featureId = $null
do {
    $candidate = New-SpecId
    $candidateDir = Join-Path $specsDir $candidate
    if (-not (Test-Path $candidateDir)) { $featureId = $candidate }
} while (-not $featureId)

$featureDir = Join-Path $specsDir $featureId
New-Item -ItemType Directory -Path $featureDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $featureDir 'checklists') -Force | Out-Null

$template = Join-Path $repoRoot '.specify/templates/spec-template.md'
$specFile = Join-Path $featureDir 'spec.md'
if (Test-Path $template) {
    Copy-Item -Path $template -Destination $specFile -Force
} else {
    New-Item -ItemType File -Path $specFile -Force | Out-Null
}

# Persist current feature selection (branchless workflow)
$specifyDir = Join-Path $repoRoot '.specify'
New-Item -ItemType Directory -Path $specifyDir -Force | Out-Null
Set-Content -Path (Join-Path $specifyDir 'current-feature') -Value $featureId -Encoding UTF8

Update-SpecsReadme -RepoRoot $repoRoot

$specsReadme = Join-Path $specsDir 'specs.md'

if ($hasGit) {
    Write-Host "[specify] Gitリポジトリを検出しましたが、ブランチは作成しません（設定による）。"
}
Write-Host "[specify] 要件ID: $featureId"
Write-Host "[specify] 説明: $featureDesc"

if ($Json) {
    $obj = [PSCustomObject]@{
        FEATURE_ID    = $featureId
        SPEC_FILE     = $specFile
        FEATURE_DIR   = $featureDir
        SPECS_README  = $specsReadme
    }
    $obj | ConvertTo-Json -Compress
} else {
    Write-Host "FEATURE_ID: $featureId"
    Write-Host "SPEC_FILE: $specFile"
    Write-Host "FEATURE_DIR: $featureDir"
    Write-Host "SPECS_README: $specsReadme"
}
