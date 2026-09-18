# bootstrap.ps1 — environment discovery + skill restore for the MomoPet pipeline.
# Fails loudly (non-zero exit) whenever a required dependency is missing.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$script:BlockName = 'bootstrap'

Write-Banner 'MomoPet environment bootstrap'

# --- 1) Windows / PowerShell ------------------------------------------------
$psVer = [version]$PSVersionTable.PSVersion
Write-Host ("Windows: " + [System.Environment]::OSVersion.VersionString)
Log-Ok 'PowerShell' "version $psVer on $([System.Environment]::OSVersion.Platform)"

# --- 2) Git ------------------------------------------------------------------
$gitCmd = Get-Command git -ErrorAction SilentlyContinue
if (-not $gitCmd) { Log-Fail 'Git' 'git not found on PATH'; exit 1 }
Log-Ok 'Git' $gitCmd.Source

# --- 3) .NET Framework C# compiler -------------------------------------------
try {
    $compiler = Resolve-CSharpCompiler
    Log-Ok 'C# compiler (csc.exe)' $compiler
} catch {
    Log-Fail 'C# compiler (csc.exe)' $_.Exception.Message
    $script:PipelineFailed = $true
}

# --- 4) Windows SDK (dynamic, no fixed minor version) ------------------------
[string]$windowsSdk = ''
try {
    $sdk = Resolve-WindowsSdkWinmd
    $windowsSdk = $sdk.Version.ToString()
    Log-Ok "Windows SDK" "$($sdk.Version)  ($($sdk.Path))"
} catch {
    Log-Fail 'Windows SDK' $_.Exception.Message
    $script:PipelineFailed = $true
}

# --- 5) Node runtime -----------------------------------------------------------
[string]$nodeVersion = ''
try {
    $node = Resolve-NodeRuntime
    $nodeVersion = $node.Version
} catch {
    Log-Fail 'Node runtime' $_.Exception.Message
    $script:PipelineFailed = $true
}

# --- 6) Skill restore ----------------------------------------------------------
& (Join-Path $PSScriptRoot 'restore-skills.ps1') -Force:$Force
if ($LASTEXITCODE -ne 0) { $script:PipelineFailed = $true }

Write-Banner 'Bootstrap summary'
$states = @{
    'PowerShell'    = $psVer
    'Git'           = $gitCmd.Source
    'csc compiler'  = $compiler
    'Windows SDK'   = $windowsSdk
    'Node version'  = $nodeVersion
    'skills restore'= 'ok'
}
foreach ($k in $states.Keys) { Write-Host ('  ' + $k + ': ' + $states[$k]) }

if ($script:PipelineFailed) {
    Write-Host ''
    Write-Host '[FAIL] bootstrap: one or more required dependencies are missing.'
    exit 1
}
Write-Host '[OK]   bootstrap: all required dependencies present.'
exit 0