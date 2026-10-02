# Installs a locally built unity-cli.exe through scripts/install.ps1 from a
# local HTTP release and runs the installed binary by name (#459). The checksum
# gate itself is covered by install-ps1-test.ps1.
param(
    [Parameter(Mandatory = $true)][string]$Binary
)
$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$installer = Join-Path $repoRoot 'scripts\install.ps1'
$built = (Resolve-Path $Binary).Path

$builtVersion = (& $built --version | Out-String).Trim()
Assert ($LASTEXITCODE -eq 0) "built binary failed to report its version: $builtVersion"
Assert ($builtVersion -match '^unity-cli (\d+\.\d+\.\d+\S*)$') "unexpected version output: $builtVersion"
$version = $Matches[1]
$tag = "v$version"

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("unity-cli-install-ps1-binary-" + [guid]::NewGuid())
$releaseDir = Join-Path $work "release\$tag"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Copy-Item -Path $built -Destination (Join-Path $releaseDir 'unity-cli-win-x64')
$hash = (Get-FileHash -Algorithm SHA256 -Path $built).Hash.ToLowerInvariant()
Set-Content -Path (Join-Path $releaseDir 'SHA256SUMS') -Encoding ascii -Value "$hash  unity-cli-win-x64"

$port = Get-Random -Minimum 20000 -Maximum 40000
$server = Start-Process -PassThru -NoNewWindow python `
    -ArgumentList @('-m', 'http.server', "$port", '--bind', '127.0.0.1', '--directory', (Join-Path $work 'release'))

# The installer appends its directory to the user PATH; put both back afterwards.
$originalUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$originalPath = $env:Path
$toolsRoot = Join-Path $work 'tools'

try {
    $ready = $false
    foreach ($attempt in 1..50) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:$port/$tag/SHA256SUMS" -UseBasicParsing | Out-Null
            $ready = $true
            break
        } catch {
            Start-Sleep -Milliseconds 200
        }
    }
    Assert $ready 'local release server did not start'

    $env:UNITY_CLI_VERSION = $tag
    $env:UNITY_CLI_RELEASE_BASE_URL = "http://127.0.0.1:$port"
    $env:UNITY_CLI_TOOLS_ROOT = $toolsRoot
    Remove-Item Env:UNITY_CLI_SKIP_PATH_UPDATE -ErrorAction SilentlyContinue
    & $installer

    $installed = Join-Path $toolsRoot 'unity-cli\win-x64\unity-cli.exe'
    Assert (Test-Path $installed) 'binary must be installed into the managed layout'
    Assert ((Get-FileHash -Algorithm SHA256 -Path $installed).Hash.ToLowerInvariant() -eq $hash) 'installed binary must match the built binary'
    Assert ((Get-Content -Raw (Join-Path $toolsRoot 'unity-cli\win-x64\VERSION')).Trim() -eq $version) 'VERSION must be written'

    $resolved = (Get-Command unity-cli -CommandType Application | Select-Object -First 1).Source
    Assert ($resolved -eq $installed) "unity-cli must resolve to the installed binary, got: $resolved"
    $installedVersion = (unity-cli --version | Out-String).Trim()
    Assert ($LASTEXITCODE -eq 0) "installed unity-cli --version exited with $LASTEXITCODE"
    Assert ($installedVersion -eq $builtVersion) "installed binary reported '$installedVersion', expected '$builtVersion'"

    Write-Host "install.ps1 binary install test passed ($installedVersion)"
} finally {
    [Environment]::SetEnvironmentVariable('Path', $originalUserPath, 'User')
    $env:Path = $originalPath
    Stop-Process -Id $server.Id -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $work
}
