# Installs the latest published release the way the README tells users to
# (`irm .../main/scripts/install.ps1 | iex`) and checks that the installed
# unity-cli reports that release's version (#465). Needs network access and a
# published release, so it runs from published-install.yml, not on every PR.
$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
}

$repo = 'akiojin/unity-cli'
$installerUrl = "https://raw.githubusercontent.com/$repo/main/scripts/install.ps1"

$headers = @{ 'User-Agent' = 'unity-cli-published-install-test' }
if ($env:GH_TOKEN) { $headers['Authorization'] = "Bearer $env:GH_TOKEN" }
$tag = (Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers $headers).tag_name
Assert ($tag -match '^v\d+\.\d+\.\d+$') "unexpected latest release tag: $tag"
$expected = "unity-cli $($tag.TrimStart('v'))"

# The installer must pick the release and the install location by itself.
foreach ($name in 'UNITY_CLI_VERSION', 'UNITY_CLI_RELEASE_BASE_URL', 'UNITY_CLI_TOOLS_ROOT', 'UNITY_CLI_SKIP_PATH_UPDATE') {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}
Invoke-RestMethod -Uri $installerUrl | Invoke-Expression

$installed = Join-Path $HOME '.unity\tools\unity-cli\win-x64\unity-cli.exe'
Assert (Test-Path $installed) "binary must be installed at $installed"
$resolved = (Get-Command unity-cli -CommandType Application | Select-Object -First 1).Source
Assert ($resolved -eq $installed) "unity-cli must resolve to the installed binary, got: $resolved"
$actual = (unity-cli --version | Out-String).Trim()
Assert ($LASTEXITCODE -eq 0) "unity-cli --version exited with $LASTEXITCODE"
Assert ($actual -eq $expected) "installed binary reported '$actual', expected '$expected' (release $tag)"

Write-Host "published install test passed ($actual from release $tag)"
