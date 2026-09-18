param([string]$Exe)
$ErrorActionPreference = 'Stop'
if (-not $Exe) { throw 'Exe parameter is required' }
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))

$a = $assembly.GetType('MomoPetApp.AppLog')
if (-not $a) { throw 'AppLog type not found in assembly' }
$p = $assembly.GetType('MomoPetApp.AppPaths')
if (-not $p) { throw 'AppPaths type not found in assembly' }

$flags = [Reflection.BindingFlags]'Public,Static'
function AppLogCall([string]$method, [Type[]]$types, [object[]]$argsList) {
    $mi = $a.GetMethod($method, $flags, $null, $types, $null)
    if (-not $mi) { throw "AppLog.$method not found" }
    return $mi.Invoke($null, $argsList)
}
function Assert($ok, $message) {
    if (-not $ok) { throw $message }
    Write-Output "PASS: $message"
}

# Isolate the log into a temp data dir so the test never touches real user data.
$tmp = Join-Path $env:TEMP ('momopet-guardrail-' + [Guid]::NewGuid().ToString('N'))
[Environment]::SetEnvironmentVariable('MOMOPET_DATA_DIR', $tmp)
try {
    $seed = 'MOMOPET_TEST_SECRET_' + [Guid]::NewGuid().ToString('N')

    AppLogCall 'RegisterSensitive' @([string], [string]) @($seed, 'test-secret')
    AppLogCall 'Info' @([string], [string]) @('guardrail', 'bootup marker 2026')
    AppLogCall 'Error' @([string], [System.Exception]) @('guardrail', [System.Exception]::new('guardrail boom'))
    AppLogCall 'Warn' @([string], [string]) @('guardrail', 'warning sample')

    $logsDir = Join-Path $tmp 'logs'
    Assert (Test-Path $logsDir) 'AppLog created a logs directory'
    $logFile = Get-ChildItem $logsDir -Filter 'momo-*.log' -File | Select-Object -First 1
    Assert ($null -ne $logFile) 'AppLog created momo-YYYYMMDD.log file'

    $content = [IO.File]::ReadAllText($logFile.FullName, [Text.Encoding]::UTF8)
    Assert ($content.Contains('# version')) 'Log header carries version metadata'
    Assert ($content.Contains('bootup marker 2026')) 'Info message written'
    Assert ($content.Contains('guardrail boom')) 'Error exception message written'
    Assert ($content.Contains('warning sample')) 'Warn message written'

    AppLogCall 'Info' @([string], [string]) @('guardrail', 'about to leak: ' + $seed)
    $content2 = [IO.File]::ReadAllText($logFile.FullName, [Text.Encoding]::UTF8)
    Assert (-not $content2.Contains($seed)) 'Secret sample NOT written verbatim'
    Assert ($content2.Contains('[REDACTED:test-secret]')) 'Registered sensitive value was masked'

    $scrubbed = AppLogCall 'Scrub' @([string]) @('prefix ' + $seed + ' suffix')
    Assert ($scrubbed.Contains('[REDACTED:test-secret]')) 'Scrub API masks sensitive tokens'
    Assert (-not $scrubbed.Contains($seed)) 'Scrub API removes the raw sensitive token'

    # Logging into an unwritable directory must not throw (P1-01: log failure never crashes).
    [Environment]::SetEnvironmentVariable('MOMOPET_DATA_DIR', (Join-Path $env:SystemRoot ('MomoPet_NoWrite_' + [Guid]::NewGuid().ToString('N'))))
    $threw = $false
    try { AppLogCall 'Info' @([string], [string]) @('guardrail', 'should-not-crash') }
    catch { $threw = $true }
    Assert (-not $threw) 'AppLog swallows write failures (no crash)'

    Write-Output 'All engineering guardrail tests passed.'
}
finally {
    [Environment]::SetEnvironmentVariable('MOMOPET_DATA_DIR', $null)
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0