# Install unity-cli managed binary on Windows.
# Usage: irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
#
# The binary is installed into the same managed layout that unity-cli's
# auto-update maintains (%USERPROFILE%\.unity\tools\unity-cli\<rid>), and that
# directory is added to the user PATH. The download is verified against the
# release SHA256SUMS (or the release manifest for older releases); any
# mismatch aborts the install before the existing binary is touched.
#
# Environment:
#   UNITY_CLI_VERSION           install a specific tag instead of the latest
#   UNITY_CLI_RELEASE_BASE_URL  release download base (default: GitHub Releases)
#   UNITY_CLI_TOOLS_ROOT        managed tools root (default: ~\.unity\tools)
#   UNITY_CLI_SKIP_PATH_UPDATE  set to 1 to leave the user PATH unchanged

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Repo = 'akiojin/unity-cli'

function Fail([string]$Message) {
    throw "unity-cli install failed: $Message"
}

function Get-Rid {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    switch ($arch) {
        'X64' { return 'win-x64' }
        default { Fail "unsupported Windows architecture: $arch" }
    }
}

function Get-Text([string]$Url) {
    $content = (Invoke-WebRequest -Uri $Url -UseBasicParsing -Headers @{ 'User-Agent' = 'unity-cli-installer' }).Content
    # Decode before returning: PowerShell enumerates byte[] function results
    # into Object[], which would bypass the caller's byte[] type check.
    if ($content -is [byte[]]) { return [System.Text.Encoding]::UTF8.GetString($content) }
    return $content
}

function Get-OptionalText([string]$Url) {
    try {
        Get-Text $Url
    } catch {
        $null
    }
}

$rid = Get-Rid
Write-Host "Detected platform: $rid"

# 1. Resolve release tag
if ($env:UNITY_CLI_VERSION) {
    $tag = $env:UNITY_CLI_VERSION
    if (-not $tag.StartsWith('v')) { $tag = "v$tag" }
} else {
    Write-Host 'Fetching latest release...'
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" `
        -Headers @{ 'User-Agent' = 'unity-cli-installer' }
    $tag = $release.tag_name
}
if (-not $tag) { Fail 'failed to determine release tag' }
Write-Host "Release: $tag"

$baseUrl = if ($env:UNITY_CLI_RELEASE_BASE_URL) { $env:UNITY_CLI_RELEASE_BASE_URL.TrimEnd('/') } `
    else { "https://github.com/$Repo/releases/download" }
$assetName = "unity-cli-$rid"

# 2. Resolve the expected SHA-256 and asset URL
$sums = Get-OptionalText "$baseUrl/$tag/SHA256SUMS"
if ($sums) {
    if ($sums -is [byte[]]) { $sums = [System.Text.Encoding]::UTF8.GetString($sums) }
    $expected = $null
    foreach ($line in ($sums -split "`r?`n")) {
        $parts = $line.Trim() -split '\s+', 2
        if ($parts.Count -eq 2 -and $parts[1].TrimStart('*') -eq $assetName) {
            $expected = $parts[0].ToLowerInvariant()
            break
        }
    }
    if (-not $expected) { Fail "SHA256SUMS has no entry for $assetName" }
    $assetUrl = "$baseUrl/$tag/$assetName"
} else {
    $manifestText = Get-OptionalText "$baseUrl/$tag/unity-cli-manifest.json"
    if (-not $manifestText) { Fail 'failed to download SHA256SUMS or manifest' }
    if ($manifestText -is [byte[]]) { $manifestText = [System.Text.Encoding]::UTF8.GetString($manifestText) }
    $asset = ($manifestText | ConvertFrom-Json).assets.$rid
    if (-not $asset -or -not $asset.url -or -not $asset.sha256) { Fail "manifest has no asset for RID: $rid" }
    $expected = $asset.sha256.ToLowerInvariant()
    $assetUrl = $asset.url
}

# 3. Download into the managed install directory
$toolsRoot = if ($env:UNITY_CLI_TOOLS_ROOT) { $env:UNITY_CLI_TOOLS_ROOT.Trim() } `
    else { Join-Path $HOME '.unity\tools' }
$destDir = Join-Path $toolsRoot "unity-cli\$rid"
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$tmp = Join-Path $destDir 'unity-cli.download'

Write-Host "Downloading $assetName..."
try {
    Invoke-WebRequest -Uri $assetUrl -OutFile $tmp -UseBasicParsing `
        -Headers @{ 'User-Agent' = 'unity-cli-installer' }
} catch {
    Remove-Item -Force -ErrorAction SilentlyContinue $tmp
    Fail "download failed: $($_.Exception.Message)"
}

# 4. Verify SHA-256 before touching the installed binary
$actual = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    Remove-Item -Force $tmp
    Fail "checksum mismatch for ${assetName}: expected $expected, got $actual"
}
Write-Host 'Checksum verified.'

# 5. Install and write VERSION
$binary = Join-Path $destDir 'unity-cli.exe'
Move-Item -Force -Path $tmp -Destination $binary
$version = $tag.TrimStart('v')
Set-Content -Path (Join-Path $destDir 'VERSION') -Value $version -NoNewline:$false -Encoding ascii
Write-Host "Installed unity-cli $version -> $binary"

# 6. Add the managed directory to the user PATH
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

$cargoBin = Join-Path $HOME '.cargo\bin\unity-cli.exe'
if (Test-Path $cargoBin) {
    Write-Warning "$cargoBin exists and may shadow the managed binary. Consider running: cargo uninstall unity-cli"
}

Write-Host ''
Write-Host "Done. Run 'unity-cli --version' to verify."
