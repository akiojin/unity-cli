# Build this checkout and install it as the managed unity-cli binary, the same
# place scripts/install.ps1 puts a release, so unityd and your agents run your
# build. For contributors testing changes the way users run them.
# Usage: ./scripts/install-from-source.ps1 [-DebugBuild] [-SkipBuild] [-Skills <client>]
#
# Environment (same as install.ps1):
#   UNITY_CLI_TOOLS_ROOT        managed tools root (default: ~\.unity\tools)
#   UNITY_CLI_SKIP_PATH_UPDATE  set to 1 to leave the user PATH unchanged
#
# Set UNITY_CLI_NO_AUTO_UPDATE=1 (user scope) while you run a source build:
# otherwise the next release's self-update replaces it.
param(
    [switch]$DebugBuild,
    [switch]$SkipBuild,
    [ValidateSet('claude-code', 'cursor', 'windsurf', 'vscode')][string]$Skills
)
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) { Write-Error "error: $Message"; exit 1 }

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$profileDir = if ($DebugBuild) { 'debug' } else { 'release' }
$targetDir = if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $repoRoot 'target' }
$built = Join-Path $targetDir "$profileDir\unity-cli.exe"

# 1. Build
if (-not $SkipBuild) {
    if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { Fail 'cargo not found (install Rust from https://rustup.rs)' }
    $cargoArgs = @('build', '--bin', 'unity-cli', '--locked')
    if (-not $DebugBuild) { $cargoArgs += '--release' }
    Push-Location $repoRoot
    try { & cargo @cargoArgs; if ($LASTEXITCODE -ne 0) { Fail 'cargo build failed' } }
    finally { Pop-Location }
}
if (-not (Test-Path $built)) { Fail "no build at $built (drop -SkipBuild)" }

$versionLine = (& $built --version | Out-String).Trim()
if ($versionLine -notmatch '^unity-cli (\S+)$') { Fail "unexpected version output: $versionLine" }
$version = $Matches[1]
$commit = (git -C $repoRoot rev-parse --short HEAD 2>$null)

# 2. Stop the daemon so the installed exe is not locked
$toolsRoot = if ($env:UNITY_CLI_TOOLS_ROOT) { $env:UNITY_CLI_TOOLS_ROOT.Trim() } `
    else { Join-Path $HOME '.unity\tools' }
$destDir = Join-Path $toolsRoot 'unity-cli\win-x64'
$binary = Join-Path $destDir 'unity-cli.exe'
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
if (Test-Path $binary) {
    $env:UNITY_CLI_NO_AUTO_UPDATE = '1'
    & $binary unityd stop *> $null
    # Anything else still running the old exe keeps it locked; Windows allows
    # renaming a running exe, so move it aside instead of failing.
    Get-ChildItem -Path $destDir -Filter 'unity-cli.exe.old-*' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    $locked = $true
    foreach ($i in 1..10) {
        try { [IO.File]::Open($binary, 'Open', 'ReadWrite', 'None').Close(); $locked = $false; break }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if ($locked) {
        $aside = "$binary.old-$PID"
        Move-Item -Force -Path $binary -Destination $aside
        Write-Host "unity-cli.exe is in use; moved it to $aside"
    }
}

# 3. Install and write VERSION. VERSION holds the build's own version, so
#    self-update only replaces the build once a newer release exists.
$tmp = Join-Path $destDir 'unity-cli.download'
Copy-Item -Force -Path $built -Destination $tmp
Move-Item -Force -Path $tmp -Destination $binary
Set-Content -Path (Join-Path $destDir 'VERSION') -Value $version -NoNewline:$false -Encoding ascii
Write-Host "Installed unity-cli $version ($profileDir build of $commit) -> $binary"

# 4. Add the managed directory to the user PATH (same as install.ps1)
if ($env:UNITY_CLI_SKIP_PATH_UPDATE -ne '1') {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @($userPath -split ';' | Where-Object { $_ })
    if ($entries -notcontains $destDir) {
        [Environment]::SetEnvironmentVariable('Path', (($entries + $destDir) -join ';'), 'User')
        Write-Host "Added $destDir to the user PATH. Restart your terminal to pick it up."
    }
    if (($env:Path -split ';') -notcontains $destDir) {
        $env:Path = "$env:Path;$destDir"
    }
}

# 5. Skills: refresh installed copies to this build, or install for a client
if ($Skills) {
    & $binary skills install $Skills --force
    if ($LASTEXITCODE -ne 0) { Fail "skills install $Skills failed" }
} else {
    & $binary skills refresh *> $null
    if ($LASTEXITCODE -eq 0) { Write-Host 'Refreshed installed skills to this build.' }
}

$cargoBin = Join-Path $HOME '.cargo\bin\unity-cli.exe'
if (Test-Path $cargoBin) {
    Write-Warning "$cargoBin exists and may shadow the managed binary. Consider running: cargo uninstall unity-cli"
}
$noAuto = [Environment]::GetEnvironmentVariable('UNITY_CLI_NO_AUTO_UPDATE', 'User')
if ($noAuto -ne '1' -and $env:UNITY_CLI_NO_AUTO_UPDATE -ne '1') {
    Write-Warning ("Self-update will replace this build when a newer release ships. To keep it, run: " +
        "[Environment]::SetEnvironmentVariable('UNITY_CLI_NO_AUTO_UPDATE','1','User')")
}
