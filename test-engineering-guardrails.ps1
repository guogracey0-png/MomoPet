param([string]$Exe)
$ErrorActionPreference = 'Stop'
if (-not $Exe) { throw 'Exe parameter is required' }
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))

# 校验类型存在（AppPaths 为 internal，仅做存在性检查；AppLog 为 public，可直接静态调用）。
$a = $assembly.GetType('MomoPetApp.AppLog')
if (-not $a) { throw 'AppLog type not found in assembly' }
$p = $assembly.GetType('MomoPetApp.AppPaths')
if (-not $p) { throw 'AppPaths type not found in assembly' }

function Assert($ok, $message) {
    if (-not $ok) { throw $message }
    Write-Output "PASS: $message"
}

# Isolate the log into a temp data dir so the test never touches real user data.
$tmp = Join-Path $env:TEMP ('momopet-guardrail-' + [Guid]::NewGuid().ToString('N'))
[Environment]::SetEnvironmentVariable('MOMOPET_DATA_DIR', $tmp)
try {
    $seed = 'MOMOPET_TEST_SECRET_' + [Guid]::NewGuid().ToString('N')

    # 直接静态调用（避免 GetMethod+ArugmentList 在 PowerShell 5.1 下参数绑定不稳定）。
    [MomoPetApp.AppLog]::RegisterSensitive($seed, 'test-secret')
    [MomoPetApp.AppLog]::Info('guardrail', 'bootup marker 2026')
    [MomoPetApp.AppLog]::Error('guardrail', [System.Exception]::new('guardrail boom'))
    [MomoPetApp.AppLog]::Warn('guardrail', 'warning sample')

    $logsDir = Join-Path $tmp 'logs'
    Assert (Test-Path $logsDir) 'AppLog created a logs directory'
    $logFile = Get-ChildItem $logsDir -Filter 'momo-*.log' -File | Select-Object -First 1
    Assert ($null -ne $logFile) 'AppLog created momo-YYYYMMDD.log file'

    $content = [IO.File]::ReadAllText($logFile.FullName, [Text.Encoding]::UTF8)
    Assert ($content.Contains('# version')) 'Log header carries version metadata'
    Assert ($content.Contains('bootup marker 2026')) 'Info message written'
    Assert ($content.Contains('guardrail boom')) 'Error exception message written'
    Assert ($content.Contains('warning sample')) 'Warn message written'

    [MomoPetApp.AppLog]::Info('guardrail', 'about to leak: ' + $seed)
    $content2 = [IO.File]::ReadAllText($logFile.FullName, [Text.Encoding]::UTF8)
    Assert (-not $content2.Contains($seed)) 'Secret sample NOT written verbatim'
    Assert ($content2.Contains('[REDACTED:test-secret]')) 'Registered sensitive value was masked'

    $scrubbed = [MomoPetApp.AppLog]::Scrub('prefix ' + $seed + ' suffix')
    Assert ($scrubbed.Contains('[REDACTED:test-secret]')) 'Scrub API masks sensitive tokens'
    Assert (-not $scrubbed.Contains($seed)) 'Scrub API removes the raw sensitive token'

    # Logging into an unwritable directory must not throw (P1-01: log failure never crashes).
    [Environment]::SetEnvironmentVariable('MOMOPET_DATA_DIR', (Join-Path $env:SystemRoot ('MomoPet_NoWrite_' + [Guid]::NewGuid().ToString('N'))))
    $threw = $false
    try { [MomoPetApp.AppLog]::Info('guardrail', 'should-not-crash') }
    catch { $threw = $true }
    Assert (-not $threw) 'AppLog swallows write failures (no crash)'

    Write-Output 'All engineering guardrail tests passed.'
}
finally {
    [Environment]::SetEnvironmentVariable('MOMOPET_DATA_DIR', $null)
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0