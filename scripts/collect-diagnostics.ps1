# collect-diagnostics.ps1 - P1-08 生成最小诊断包到 artifacts\diagnostics\<stamp>。
# 只收集"定位问题"所需的环境/日志摘要，绝不含密钥/Token/聊天/用户文档正文。
# 生成前对文本做明确敏感信息过滤（Scrub）。
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$script:BlockName = 'diagnostics'

$root = $script:RepositoryRoot
$artifacts = Join-Path $root 'artifacts'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $artifacts ('diagnostics\' + $stamp)
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# 生成前的敏感信息过滤：替换常见密钥形态，避免把密钥写进诊断包。
function Scrub([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    $t = $text
    $t = [regex]::Replace($t, 'sk-[A-Za-z0-9_-]{8,}', 'sk-***[REDACTED]')
    $t = [regex]::Replace($t, '(?i)(bearer\s+)[A-Za-z0-9._-]{10,}', '$1***[REDACTED]')
    $t = [regex]::Replace($t, '(?i)((api[-_]?key|access[-_]?key|token|appsecret|clientsecret|password|secret)\s*[:=]\s*)[A-Za-z0-9._/+-]{12,}', '$1***[REDACTED]')
    return $t
}

$out = New-Object System.Collections.Generic.List[string]
$manifest = New-Object System.Collections.Generic.List[string]

$out.Add('MomoPet diagnostics pack')
$out.Add('generated: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$git = Get-CommitInfo
$out.Add('gitBranch: ' + $git.Branch)
$out.Add('gitCommit: ' + $git.Commit)
$out.Add('os: ' + $env:OS + ' / ' + [Environment]::OSVersion.VersionString)
$out.Add('ps: ' + $PSVersionTable.PSVersion.ToString())

try { $node = Resolve-NodeRuntime; $out.Add('node: ' + $node.Version) }
catch { $out.Add('node: (unavailable)') }
try { $sdk = Resolve-WindowsSdkWinmd; $out.Add('windowsSdk: ' + $sdk.Version.ToString()) }
catch { $out.Add('windowsSdk: (unavailable)') }

# 构建/版本信息
$buildInfo = Join-Path $artifacts 'build-info.json'
if (Test-Path -LiteralPath $buildInfo) {
    Copy-Item -LiteralPath $buildInfo -Destination (Join-Path $outDir 'build-info.json') -Force
    $manifest.Add('build-info.json')
}

# 最近应用日志（仅 momo-*.log，经过 Scrub）
$logRoot = Join-Path $env:LOCALAPPDATA 'MomoPet\logs'
if ($env:MOMOPET_DATA_DIR) { $logRoot = Join-Path $env:MOMOPET_DATA_DIR 'logs' }
$logsCopied = 0
if (Test-Path -LiteralPath $logRoot) {
    $recent = Get-ChildItem -LiteralPath $logRoot -Filter 'momo-*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 3
    foreach ($lf in $recent) {
        try {
            $raw = [IO.File]::ReadAllText($lf.FullName, [Text.Encoding]::UTF8)
            $dest = Join-Path $outDir ('logs-' + $lf.Name)
            [IO.File]::WriteAllText($dest, (Scrub $raw), (New-Object System.Text.UTF8Encoding($false)))
            $manifest.Add('logs-' + $lf.Name)
            $logsCopied++
        } catch { }
    }
}
$out.Add('logsCopied: ' + $logsCopied)

# 关键文件存在性（只记录是否存在，不收集内容）
foreach ($c in @('artifacts\MomoPet.exe', 'artifacts\build-info.json', '.agents\skills', '本地部署\node.exe')) {
    $out.Add('exists:' + $c + ' = ' + (Test-Path -LiteralPath (Join-Path $root $c)))
}
$dataRoot = $env:MOMOPET_DATA_DIR
if ([string]::IsNullOrWhiteSpace($dataRoot)) { $dataRoot = Join-Path $env:LOCALAPPDATA 'MomoPet' }
# 以下只记录"是否存在"，绝不复制 DPAPI 加密文件或任何密钥文件内容。
$out.Add('secretFilePresent:wind-key.dat = ' + (Test-Path -LiteralPath (Join-Path $dataRoot 'wind-key.dat')) + ' (content NOT collected)')

$out.Add('')
$out.Add('Sensitive filtering: enabled (keys/tokens must not appear here)')

$envPath = Join-Path $outDir 'environment.txt'
[IO.File]::WriteAllLines($envPath, ($out | ForEach-Object { $_ }), (New-Object System.Text.UTF8Encoding($false)))
$manifest.Add('environment.txt')
$manifest.Add('MANIFEST (see below)')

$manifestPath = Join-Path $outDir 'MANIFEST.txt'
[IO.File]::WriteAllLines($manifestPath, ("diagnostics-manifest:" + $stamp, '', ($out | ForEach-Object { '-' + $_ })), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host ("[OK]   diagnostics pack written to: " + $outDir)
Write-Host ("      collected files: " + $manifest.Count)
exit 0