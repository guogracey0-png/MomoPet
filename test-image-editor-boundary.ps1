param([string]$Exe)
# test-image-editor-boundary.ps1 — P2A-01/P2A-02 structure guardrail.
# Verifies (against the src/ tree and scripts/build.ps1) that the ImageEditor
# boundary split holds: >=5 responsibility files, PetController stays partial,
# model DTOs are each defined exactly once, no trivial PartN split, and the
# build source list covers every ImageEditor*.cs file. Accepts $Exe for harness
# compatibility but inspects source files directly.
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

function Assert($ok, $message) {
    if (-not $ok) { Write-Error "FAIL: $message"; exit 1 }
    Write-Output "PASS: $message"
}

$srcDir = Join-Path $root 'src'
Assert (Test-Path $srcDir) 'src directory exists'

$editorFiles = Get-ChildItem -LiteralPath $srcDir -File -Filter 'ImageEditor*.cs' | Sort-Object Name
$responsibilityFiles = $editorFiles | Where-Object { $_.Name -ne 'ImageEditor.cs' }
Assert ($responsibilityFiles.Count -ge 5) ("at least 5 ImageEditor responsibility files (found $($responsibilityFiles.Count))")

# No trivial PartN split.
$trivial = $responsibilityFiles | Where-Object { $_.Name -match 'Part\d' }
Assert (($trivial | Measure-Object).Count -eq 0) 'no trivial PartN files'

# PetController-host files (all except the pure-DTO Models file) must still
# declare it as a partial class, keeping the single type split across files.
$partialMissing = @()
foreach ($f in $editorFiles) {
    if ($f.Name -eq 'ImageEditor.Models.cs') { continue }
    $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
    if ($content -notmatch 'public\s+partial\s+class\s+PetController') { $partialMissing += $f.Name }
}
Assert ($partialMissing.Count -eq 0) "PetController remains a partial class in every ImageEditor file" 
if ($partialMissing.Count -gt 0) { Write-Output ("  partial missing in: " + ($partialMissing -join ', ')) }

# Model DTOs are each defined exactly once across the ImageEditor source set.
$dtos = @('ImageTextRegion','PrecisionLayer','PsdExportLayer','ImageTextCache','ImageAiConfig','ImageProviderProfile','ImageHistoryEntry')
foreach ($dto in $dtos) {
    $count = 0
    foreach ($f in $editorFiles) {
        $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
        $count += ([regex]::Matches($content, '\b(class|struct)\s+' + $dto + '\b')).Count
    }
    Assert ($count -eq 1) ("DTO " + $dto + " defined exactly once (found $count)")
}

# Build source list covers every ImageEditor*.cs file.
$build = Get-Content -LiteralPath (Join-Path $root 'scripts\build.ps1') -Encoding UTF8 -Raw
foreach ($f in $editorFiles) {
    $name = $f.Name
    Assert ($build -match "'" + $name + "'") ("build source list includes " + $name)
}

# Every responsibility file is non-trivially populated (has class-body members).
foreach ($f in $responsibilityFiles) {
    $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
    $memberCount = ([regex]::Matches($content, '(?m)^\s{8}[^/\s].*')).Count
    Assert ($memberCount -gt 0) (($f.Name) + " carries class members (lines $memberCount)")
}

Write-Output 'All ImageEditor boundary structure tests passed.'
exit 0