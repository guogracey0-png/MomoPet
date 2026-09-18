# common.ps1 — shared helpers for the MomoPet engineering pipeline.
# Not meant to run standalone; dot-source from scripts under scripts/.

$ErrorActionPreference = 'Stop'

# Root of the repository (this file lives in <root>\scripts\ ).
$script:RepositoryRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

# Pipeline-global failure flag. Set by Log-Fail / Invoke-Native on failure.
$script:PipelineFailed = $false

# Human-readable name for the current phase, shown in OK/FAIL lines.
$script:BlockName = 'pipeline'

function Write-Banner {
    param([string]$Title)
    Write-Host ''
    Write-Host ('===== ' + $Title + ' =====')
}

# Min acceptable Node major for wind_bridge modules.
$script:MinNodeVersion = [version]'16.0.0'

function Log-Ok {
    param([string]$Name, [string]$Detail)
    Write-Host ('[OK]   ' + $Name + ' - ' + $Detail)
}

function Log-Fail {
    param([string]$Name, [string]$Detail)
    Write-Host ('[FAIL] ' + $Name + ' - ' + $Detail)
    $script:PipelineFailed = $true
}

function Resolve-CSharpCompiler {
    # Prefer the classic .NET Framework compiler used by the project.
    $candidates = @(
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return $c }
    }
    throw 'C# compiler (csc.exe) not found under Microsoft.NET Framework v4.0.30319.'
}

function Resolve-WindowsSdkWinmd {
    # Dynamically pick the highest installed Windows SDK Windows.winmd.
    # No fixed SDK minor version is hardcoded anywhere (see P0-02).
    $pf86 = ${env:ProgramFiles(x86)}
    $roots = @(
        (Join-Path $pf86 'Windows Kits\10\UnionMetadata'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\UnionMetadata')
    )
    $hits = @()
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($dir in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) {
            $winmd = Join-Path $dir.FullName 'Windows.winmd'
            if (Test-Path -LiteralPath $winmd) {
                $hits += [pscustomobject]@{ Version = [version]$dir.Name; Path = $winmd }
            }
        }
    }
    if ($hits.Count -eq 0) {
        throw ("Windows SDK (Windows.winmd) not found. Install the Windows 10/11 SDK or the version that " +
               "provides UnionMetadata\10.x.y.z\Windows.winmd. Searched: " + ($roots -join '; '))
    }
    $best = $hits | Sort-Object -Property { $_.Version } -Descending | Select-Object -First 1
    return $best
}

function Resolve-NodeRuntime {
    # Priority: 1) local portable node.exe, 2) system node, 3) hard failure (no silent fallback).
    $local = Join-Path $script:RepositoryRoot '本地部署\node.exe'
    if (Test-Path -LiteralPath $local) {
        $nodePath = $local
        Log-Ok $script:BlockName "Node runtime: $nodePath (本地部署)"
    } else {
        $cmd = Get-Command node -ErrorAction SilentlyContinue
        if (-not $cmd) {
            throw 'Node.js not found. Place a portable node.exe at 本地部署\node.exe or install/put node.exe on PATH.'
        }
        $nodePath = $cmd.Source
        Log-Ok $script:BlockName "Node runtime: $nodePath (system PATH)"
    }

    $versionText = & $nodePath --version 2>$null | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or -not $versionText) {
        throw "Failed to read Node version from $nodePath"
    }
    $ver = [version]($versionText.TrimStart('v'))
    if ($ver -lt $script:MinNodeVersion) {
        throw ("Node version $versionText is below the minimum supported $($script:MinNodeVersion). " +
               'Provide a newer node.exe, e.g. at 本地部署\node.exe.')
    }
    Log-Ok $script:BlockName "Node version: $versionText (min $script:MinNodeVersion)"
    return [pscustomobject]@{ Path = $nodePath; Version = $versionText }
}

function Get-CommitInfo {
    $branch = ''
    $commit = ''
    try {
        $branch = (& git -C $script:RepositoryRoot rev-parse --abbrev-ref HEAD 2>$null) -join ''
    } catch {}
    try {
        $commit = (& git -C $script:RepositoryRoot rev-parse HEAD 2>$null) -join ''
    } catch {}
    if (-not $branch) { $branch = '(detached)' }
    return [pscustomobject]@{ Branch = $branch; Commit = $commit }
}

function Read-SkillsLock {
    $lockPath = Join-Path $script:RepositoryRoot 'skills-lock.json'
    if (-not (Test-Path -LiteralPath $lockPath)) {
        throw "skills-lock.json not found at $lockPath"
    }
    return Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
}