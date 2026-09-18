# Compatibility entry point that forwards to scripts\build.ps1 (P0-01).
# Primary pipeline entry: scripts\verify.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'scripts\build.ps1') @args
exit $LASTEXITCODE