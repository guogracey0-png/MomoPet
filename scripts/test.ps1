# test.ps1 — unified regression gate: compliance + office-comfort.
# Writes results to artifacts\test-results.txt and exits non-zero on any failure.
param([string]$Exe)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$script:BlockName = 'test'

$root = $script:RepositoryRoot
$artifactsDir = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

if (-not $Exe) { $Exe = Join-Path $artifactsDir 'MomoPet.exe' }
if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host "[FAIL] test: executable not found at $Exe (run build first)."
    exit 1
}
$exeFull = (Resolve-Path -LiteralPath $Exe).Path
Write-Host ("Testing executable: " + $exeFull)

$resultsPath = Join-Path $artifactsDir 'test-results.txt'
$log = New-Object System.Text.StringBuilder
[void]$log.AppendLine('MomoPet regression test results')
[void]$log.AppendLine('Run time: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
[void]$log.AppendLine('Executable: ' + $exeFull)
[void]$log.AppendLine('')

$failures = 0

function Run-Test {
    param([string]$ScriptPath, [string]$Label)
    Write-Banner $Label
    [void]$log.AppendLine('===== ' + $Label + ' =====')
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Exe $exeFull 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $out) { $s = "$line"; Write-Host "  $s"; [void]$log.AppendLine($s) }
    # P1-06: every result row records name, PASS/FAIL and the child exit code.
    if ($code -ne 0) {
        $script:failures++
        Log-Fail $Label "exit code $code"
        [void]$log.AppendLine('RESULT: ' + $Label + ' = FAIL (exit ' + $code + ')')
    } else {
        Log-Ok $Label "passed (exit $code)"
        [void]$log.AppendLine('RESULT: ' + $Label + ' = PASS (exit ' + $code + ')')
    }
    [void]$log.AppendLine('')
}

Run-Test (Join-Path $root 'test-compliance.ps1') 'Compliance regression tests'
Run-Test (Join-Path $root 'test-office-comfort.ps1') 'Office comfort regression tests'
Run-Test (Join-Path $root 'test-engineering-guardrails.ps1') 'Engineering guardrail tests'

[void]$log.AppendLine(('OVERALL: ' + $(if ($failures -eq 0) { 'PASS' } else { "FAIL ($failures)" })))
$log.ToString() | Set-Content -LiteralPath $resultsPath -Encoding UTF8
Write-Host ''
Write-Host ("Test results written to: " + $resultsPath)

# Reflect the test outcome into build-info.json (testsPassed).
$infoPath = Join-Path $artifactsDir 'build-info.json'
if (Test-Path -LiteralPath $infoPath) {
    try {
        $info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
        $info | Add-Member -NotePropertyName testsPassed -NotePropertyValue ($failures -eq 0) -Force
        ($info | ConvertTo-Json) | Set-Content -LiteralPath $infoPath -Encoding UTF8
    } catch {
        Write-Host "[WARN] could not update build-info.json testsPassed: $($_.Exception.Message)"
    }
}

if ($failures -ne 0) {
    Write-Host '[FAIL] test: one or more regression tests failed.'
    exit 1
}
Write-Host '[OK]   test: all regression tests passed.'
exit 0