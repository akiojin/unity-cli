# Exercises scripts/install.ps1 against a local HTTP release so the checksum
# gate is verified on Windows without touching GitHub.
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$installer = Join-Path $repoRoot 'scripts\install.ps1'
$tag = 'v9.9.9'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("unity-cli-install-ps1-" + [guid]::NewGuid())
$releaseDir = Join-Path $work "release\$tag"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    (($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
}

$genuine = [System.Text.Encoding]::ASCII.GetBytes('genuine binary')
$tampered = [System.Text.Encoding]::ASCII.GetBytes('tampered binary')
Set-Content -Path (Join-Path $releaseDir 'SHA256SUMS') -Encoding ascii `
    -Value "$(Get-Sha256Hex $genuine)  unity-cli-win-x64"

$port = Get-Random -Minimum 20000 -Maximum 40000
$server = Start-Process -PassThru -NoNewWindow python `
    -ArgumentList @('-m', 'http.server', "$port", '--bind', '127.0.0.1', '--directory', (Join-Path $work 'release'))
Start-Sleep -Seconds 2

function Invoke-Installer([string]$ToolsRoot) {
    $env:UNITY_CLI_VERSION = $tag
    $env:UNITY_CLI_RELEASE_BASE_URL = "http://127.0.0.1:$port"
    $env:UNITY_CLI_TOOLS_ROOT = $ToolsRoot
    $env:UNITY_CLI_SKIP_PATH_UPDATE = '1'
    try {
        & $installer
        return $null
    } catch {
        return $_.Exception.Message
    }
}

try {
    # Tampered binary: install must abort and leave nothing behind.
    [System.IO.File]::WriteAllBytes((Join-Path $releaseDir 'unity-cli-win-x64'), $tampered)
    $tamperedRoot = Join-Path $work 'tools-tampered'
    $err = Invoke-Installer $tamperedRoot
    Assert ($null -ne $err) 'tampered install must fail'
    Assert ($err -like '*checksum mismatch*') "unexpected error: $err"
    Assert (-not (Test-Path (Join-Path $tamperedRoot 'unity-cli\win-x64\unity-cli.exe'))) 'tampered binary must not be installed'
    Assert (-not (Test-Path (Join-Path $tamperedRoot 'unity-cli\win-x64\unity-cli.download'))) 'temporary download must be removed'

    # Genuine binary: install succeeds into the managed layout.
    [System.IO.File]::WriteAllBytes((Join-Path $releaseDir 'unity-cli-win-x64'), $genuine)
    $genuineRoot = Join-Path $work 'tools-genuine'
    $err = Invoke-Installer $genuineRoot
    Assert ($null -eq $err) "genuine install failed: $err"
    $binary = Join-Path $genuineRoot 'unity-cli\win-x64\unity-cli.exe'
    Assert (Test-Path $binary) 'binary must be installed'
    Assert ((Get-Content -Raw (Join-Path $genuineRoot 'unity-cli\win-x64\VERSION')).Trim() -eq '9.9.9') 'VERSION must be written'

    Write-Host 'install.ps1 checksum tests passed'
} finally {
    Stop-Process -Id $server.Id -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $work
}
