# verify.ps1 — one-command pipeline: bootstrap -> build -> test.
# Usage:  powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '########## VERIFY: bootstrap ##########'
& (Join-Path $scriptDir 'bootstrap.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host '[FAIL] verify: bootstrap failed.'
    exit 1
}

Write-Host ''
Write-Host '########## VERIFY: build ##########'
& (Join-Path $scriptDir 'build.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host '[FAIL] verify: build failed.'
    exit 1
}

Write-Host ''
Write-Host '########## VERIFY: test ##########'
& (Join-Path $scriptDir 'test.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host '[FAIL] verify: tests failed.'
    exit 1
}

Write-Host ''
Write-Host '[OK] verify: bootstrap + build + test all succeeded.'
exit 0