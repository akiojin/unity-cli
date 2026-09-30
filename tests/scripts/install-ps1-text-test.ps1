# Check the installer's HTTP text boundary on any PowerShell platform.
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot '../../scripts/install.ps1'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($installer, [ref]$null, [ref]$null)
foreach ($name in @('Get-Text', 'Get-OptionalText')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $false)
    . ([scriptblock]::Create($definition.Extent.Text))
}

# Invoke-WebRequest returns byte[] for application/octet-stream (SHA256SUMS).
function Invoke-WebRequest {
    param($Uri, [switch]$UseBasicParsing, $Headers)
    [pscustomobject]@{ Content = $script:responseContent }
}

$expected = ('a' * 64) + "  unity-cli-win-x64`r`n" + ('b' * 64) + "  unity-cli-win-x64.exe`r`n"
foreach ($binary in @($true, $false)) {
    $script:responseContent = if ($binary) { [System.Text.Encoding]::UTF8.GetBytes($expected) } else { $expected }
    # Preserve the byte[] type in the mocked HTTP response.
    if ($binary) { $script:responseContent = [byte[]]$script:responseContent }
    $actual = Get-OptionalText 'https://example.invalid/SHA256SUMS'
    if ($actual -isnot [string] -or $actual -cne $expected) {
        throw "HTTP content must return unchanged text (binary=$binary, actual type=$($actual.GetType().FullName))"
    }
}
Write-Host 'install.ps1 HTTP text tests passed'
