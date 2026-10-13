# Check the installer's HTTP text and retry boundary on any PowerShell platform.
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot '../../scripts/install.ps1'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($installer, [ref]$null, [ref]$null)
foreach ($name in @('Fail', 'Get-HttpStatus', 'Test-TransientError', 'Invoke-WithRetry', 'Get-Text', 'Get-OptionalText')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $false)
    if (-not $definition) { throw "install.ps1 must define $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

# Keep the retry backoff out of the test run.
function Start-Sleep { param($Seconds, $Milliseconds) $script:sleeps++ }

# Invoke-WebRequest returns byte[] for application/octet-stream (SHA256SUMS).
# $script:failures holds the exceptions to throw before the response succeeds.
function Invoke-WebRequest {
    param($Uri, [switch]$UseBasicParsing, $Headers, $OutFile)
    $script:calls++
    if ($script:failures.Count -gt 0) {
        $next = $script:failures[0]
        $script:failures = @($script:failures | Select-Object -Skip 1)
        throw $next
    }
    [pscustomobject]@{ Content = $script:responseContent }
}

function Reset-Http([object[]]$Failures) {
    $script:calls = 0
    $script:sleeps = 0
    $script:failures = @($Failures)
}

# Same shape as WebException (PS 5.1) and HttpResponseException (PS 7): the
# HTTP status is on Exception.Response.StatusCode.
class FakeHttpException : System.Exception {
    [object]$Response
    FakeHttpException([string]$Message, [int]$Status) : base($Message) {
        $this.Response = [pscustomobject]@{ StatusCode = $Status }
    }
}

function New-HttpError([int]$Status) {
    [FakeHttpException]::new("Response status code does not indicate success: $Status", $Status)
}

function New-NetworkError {
    [System.Net.Http.HttpRequestException]::new('No connection could be made because the target machine actively refused it.')
}

$url = 'https://example.invalid/SHA256SUMS'
$expected = ('a' * 64) + "  unity-cli-win-x64`r`n" + ('b' * 64) + "  unity-cli-win-x64.exe`r`n"
foreach ($binary in @($true, $false)) {
    Reset-Http @()
    $script:responseContent = if ($binary) { [System.Text.Encoding]::UTF8.GetBytes($expected) } else { $expected }
    # Preserve the byte[] type in the mocked HTTP response.
    if ($binary) { $script:responseContent = [byte[]]$script:responseContent }
    $actual = Get-OptionalText $url
    if ($actual -isnot [string] -or $actual -cne $expected) {
        throw "HTTP content must return unchanged text (binary=$binary, actual type=$($actual.GetType().FullName))"
    }
}

# Transient failures (no response, 5xx, 429) are retried with backoff (#514).
$script:responseContent = [byte[]][System.Text.Encoding]::UTF8.GetBytes($expected)
Reset-Http @((New-NetworkError), (New-HttpError 503), (New-HttpError 429))
$actual = Get-Text $url
if ($actual -cne $expected) { throw "transient errors must be retried until the download succeeds (actual: $actual)" }
if ($script:calls -ne 4) { throw "expected 4 HTTP attempts, got $($script:calls)" }
if ($script:sleeps -ne 3) { throw "expected a backoff before each retry, got $($script:sleeps)" }

# A missing optional file (404) is not retried and selects the fallback.
Reset-Http @((New-HttpError 404))
$actual = Get-OptionalText $url
if ($null -ne $actual) { throw "404 must return null so the installer can fall back to the manifest" }
if ($script:calls -ne 1) { throw "404 must not be retried, got $($script:calls) attempts" }

# A persistent network failure is reported with its cause, never as a missing file.
Reset-Http @(1..10 | ForEach-Object { New-NetworkError })
$message = $null
try { Get-OptionalText $url | Out-Null } catch { $message = $_.Exception.Message }
if (-not $message) { throw 'a persistent network failure must abort, not fall back to the manifest' }
if ($message -notlike "unity-cli install failed: failed to download ${url}:*refused*") {
    throw "unexpected network failure message: $message"
}
if ($script:calls -ne 4) { throw "expected 4 HTTP attempts before giving up, got $($script:calls)" }

Write-Host 'install.ps1 HTTP text tests passed'
