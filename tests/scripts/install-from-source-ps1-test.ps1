# Installs a locally built unity-cli.exe through scripts/install-from-source.ps1
# (-SkipBuild) into a temporary UNITY_CLI_TOOLS_ROOT and checks the managed
# layout: fresh install, reinstall, a locked exe, a missing build, and the
# auto-update warning.
param(
    [Parameter(Mandatory = $true)][string]$Binary
)
$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$installer = Join-Path $repoRoot 'scripts\install-from-source.ps1'
$built = (Resolve-Path $Binary).Path
$shell = (Get-Process -Id $PID).Path

$builtVersion = (& $built --version | Out-String).Trim()
Assert ($builtVersion -match '^unity-cli (\S+)$') "unexpected version output: $builtVersion"
$version = $Matches[1]
$builtHash = (Get-FileHash -Algorithm SHA256 -Path $built).Hash

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("unity-cli-install-from-source-" + [guid]::NewGuid())
$targetDir = Join-Path $work 'target'
$emptyTarget = Join-Path $work 'empty-target'
$toolsRoot = Join-Path $work 'tools'
New-Item -ItemType Directory -Force -Path (Join-Path $targetDir 'debug'), $emptyTarget | Out-Null
Copy-Item -Path $built -Destination (Join-Path $targetDir 'debug\unity-cli.exe')

$destDir = Join-Path $toolsRoot 'unity-cli\win-x64'
$installed = Join-Path $destDir 'unity-cli.exe'
$saved = @{}
foreach ($name in 'CARGO_TARGET_DIR', 'UNITY_CLI_TOOLS_ROOT', 'UNITY_CLI_SKIP_PATH_UPDATE', 'UNITY_CLI_NO_AUTO_UPDATE') {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$originalUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$userOptOut = [Environment]::GetEnvironmentVariable('UNITY_CLI_NO_AUTO_UPDATE', 'User')

# Runs the installer in a child shell so its `exit 1` can't end this test.
# Continue: Windows PowerShell turns the child's stderr into terminating errors.
function Invoke-Installer {
    $ErrorActionPreference = 'Continue'
    $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $installer -DebugBuild -SkipBuild -NoSkills 2>&1 | Out-String
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
}

try {
    $env:CARGO_TARGET_DIR = $targetDir
    $env:UNITY_CLI_TOOLS_ROOT = $toolsRoot
    $env:UNITY_CLI_SKIP_PATH_UPDATE = '1'
    $env:UNITY_CLI_NO_AUTO_UPDATE = '1'

    # Fresh install
    $run = Invoke-Installer
    Assert ($run.Code -eq 0) "fresh install exited with $($run.Code): $($run.Output)"
    Assert (Test-Path $installed) 'binary must be installed into the managed layout'
    Assert ((Get-FileHash -Algorithm SHA256 -Path $installed).Hash -eq $builtHash) 'installed binary must match the build'
    Assert ((Get-Content -Raw (Join-Path $destDir 'VERSION')).Trim() -eq $version) 'VERSION must hold the build version'
    Assert ((& $installed --version | Out-String).Trim() -eq $builtVersion) 'installed binary must run'
    Assert ([Environment]::GetEnvironmentVariable('Path', 'User') -eq $originalUserPath) 'UNITY_CLI_SKIP_PATH_UPDATE=1 must leave the user PATH alone'
    Assert ($run.Output -notmatch 'Self-update will replace') 'no auto-update warning when UNITY_CLI_NO_AUTO_UPDATE=1'

    # Reinstall replaces an existing binary
    Set-Content -Path $installed -Value 'stale' -Encoding ascii
    $run = Invoke-Installer
    Assert ($run.Code -eq 0) "reinstall exited with $($run.Code): $($run.Output)"
    Assert ((Get-FileHash -Algorithm SHA256 -Path $installed).Hash -eq $builtHash) 'reinstall must replace the binary'

    # A held-open exe (like one a running daemon was started from) is moved aside
    $handle = [System.IO.File]::Open($installed, 'Open', 'Read', [System.IO.FileShare]'Read, Delete')
    try {
        $run = Invoke-Installer
    } finally {
        $handle.Dispose()
    }
    Assert ($run.Code -eq 0) "install over a locked exe exited with $($run.Code): $($run.Output)"
    Assert (@(Get-ChildItem -Path $destDir -Filter 'unity-cli.exe.old-*').Count -eq 1) 'the locked exe must be moved aside'
    Assert ((Get-FileHash -Algorithm SHA256 -Path $installed).Hash -eq $builtHash) 'the build must replace the locked exe'
    $run = Invoke-Installer
    Assert ($run.Code -eq 0) "install after a locked exe exited with $($run.Code)"
    Assert (@(Get-ChildItem -Path $destDir -Filter 'unity-cli.exe.old-*').Count -eq 0) 'the next run must remove the moved-aside exe'

    # A missing build fails and leaves the install alone
    $env:CARGO_TARGET_DIR = $emptyTarget
    $run = Invoke-Installer
    Assert ($run.Code -ne 0) 'a missing build must fail'
    Assert ($run.Output -match 'no build at') "missing build must say so: $($run.Output)"
    Assert ((Get-FileHash -Algorithm SHA256 -Path $installed).Hash -eq $builtHash) 'a failed run must leave the install alone'
    $env:CARGO_TARGET_DIR = $targetDir

    # Auto-update warning when the opt-out is not set (skipped when the
    # machine sets it at user scope, which the installer also honours)
    if ($userOptOut -ne '1') {
        Remove-Item Env:UNITY_CLI_NO_AUTO_UPDATE
        $run = Invoke-Installer
        Assert ($run.Code -eq 0) "install without the opt-out exited with $($run.Code)"
        Assert ($run.Output -match 'Self-update will replace') 'must warn when UNITY_CLI_NO_AUTO_UPDATE is unset'
    } else {
        Write-Host 'Skipping the warning check: UNITY_CLI_NO_AUTO_UPDATE=1 is set at user scope.'
    }

    Write-Host "install-from-source.ps1 test passed ($builtVersion)"
} finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
    }
    [Environment]::SetEnvironmentVariable('Path', $originalUserPath, 'User')
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $work
}
