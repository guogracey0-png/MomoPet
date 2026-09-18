# restore-skills.ps1 — restore .agents/skills/<skill> from skills-lock.json sources.
# Requirement: idempotent, reusable, and it must not be committed to Git.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$script:BlockName = 'restore skills'

$root = $script:RepositoryRoot
$agentsRoot = Join-Path $root '.agents'
$skillsRoot = Join-Path $agentsRoot 'skills'
$lock = Read-SkillsLock

New-Item -ItemType Directory -Path $skillsRoot -Force | Out-Null

$anyFailure = $false
$restored = New-Object System.Collections.Generic.List[string]
$reused = 0

foreach ($entry in $lock.skills.PSObject.Properties) {
    $name = $entry.Name
    $info = $entry.Value
    $target = Join-Path $skillsRoot $name
    $sk = Join-Path $target 'SKILL.md'

    # Reuse an already-complete skill without re-downloading.
    if (-not $Force -and (Test-Path -LiteralPath $sk) -and (Get-Item -LiteralPath $sk).Length -gt 0) {
        $reused++
        Log-Ok "Skill: $name" 'already present, reused'
        continue
    }

    $url = $null
    switch ($info.sourceType) {
        'github' { $url = 'https://github.com/' + $info.source + '.git' }
        'git'    { $url = [string]$info.source }
        default  { Log-Fail "Skill: $name" "unsupported sourceType '$($info.sourceType)'"; $anyFailure = $true; continue }
    }
    if (-not $url) { Log-Fail "Skill: $name" 'could not determine clone URL'; $anyFailure = $true; continue }

    $staging = Join-Path ([IO.Path]::GetTempPath()) ('MomoPet-skills-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        & git clone --depth 1 --quiet $url (Join-Path $staging 'repo') 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Log-Fail "Skill: $name" "clone failed (source: $url)"; $anyFailure = $true; continue
        }
        $skillSrc = Join-Path $staging 'repo'
        $skillSrc = Join-Path $skillSrc ($info.skillPath -replace '/', '\')
        if (-not (Test-Path -LiteralPath $skillSrc)) {
            Log-Fail "Skill: $name" "skillPath '$($info.skillPath)' not found in source"; $anyFailure = $true; continue
        }
        $dirToCopy = Split-Path -Parent $skillSrc
        $newTarget = Join-Path $skillsRoot $name
        if (Test-Path -LiteralPath $newTarget) { Remove-Item -LiteralPath $newTarget -Recurse -Force }
        Copy-Item -LiteralPath $dirToCopy -Destination $newTarget -Recurse
        if (-not (Test-Path -LiteralPath (Join-Path $newTarget 'SKILL.md'))) {
            Log-Fail "Skill: $name" 'restored target missing SKILL.md after copy'; $anyFailure = $true; continue
        }
        $restored.Add($name)
        Log-Ok "Skill: $name" "restored (source: $url)"
    }
    finally {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host '[WARN] computedHash algorithm is not specified in skills-lock.json, so content hashes are NOT enforced here.'
Write-Host '       TODO(P0-04): define the canonical computedHash algorithm (e.g. sha256 over SKILL.md) and enforce it.'

if ($anyFailure -or $script:PipelineFailed) {
    Write-Host '[FAIL] restore-skills: one or more skills could not be restored.'
    exit 1
}
if ($restored.Count -gt 0) {
    Write-Host ("[OK]   restore-skills: restored " + $restored.Count + " skill(s) -> " + $skillsRoot)
}
Write-Host ("[OK]   restore-skills: " + $reused + " skills reused; .agents remains git-ignored.")
exit 0