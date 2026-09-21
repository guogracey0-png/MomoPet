param([string]$Exe)
# test-compliance-boundary.ps1 — P2C-01..P2C-06 structure guardrail.
# Verifies (against the src/ tree and scripts/build.ps1) that the Compliance
# boundary split holds: >=6 responsibility files, PetController stays partial,
# model DTOs are each defined exactly once, no trivial PartN split, the build
# source list covers every split Compliance file, and each designated
# responsibility method lives only in its own file (move-only preserved).
# Accepts $Exe for harness compatibility but inspects source files directly.
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

function Assert($ok, $message) {
    if (-not $ok) { Write-Error "FAIL: $message"; exit 1 }
    Write-Output "PASS: $message"
}

$srcDir = Join-Path $root 'src'
Assert (Test-Path $srcDir) 'src directory exists'

# Compliance split = ComplianceUpgrade.cs (UI host) + 7 responsibility files.
# NOTE: pre-existing src/Compliance.cs (initial release, NOT part of the split
# and NOT in the build) is deliberately excluded.
$complianceFiles = Get-ChildItem -LiteralPath $srcDir -File -Filter 'Compliance*.cs' |
    Where-Object { $_.Name -ne 'Compliance.cs' } | Sort-Object Name
$expected = @('Models','State','Rules','Review','Audit','Rendering','Export')
$expectedNames = @('ComplianceUpgrade.cs') + $expected | ForEach-Object { if ($_ -like '*.cs') { $_ } else { 'Compliance.' + $_ + '.cs' } }
$missing = $expectedNames | Where-Object { -not ($complianceFiles.Name -contains $_) }
Assert ($missing.Count -eq 0) "every expected Compliance split file exists ($($expectedNames -join ', '))"
Assert ($complianceFiles.Count -ge 6) ("at least 6 Compliance responsibility files (found $($complianceFiles.Count))")

# No trivial PartN split.
$responsibilityFiles = $complianceFiles | Where-Object { $_.Name -ne 'ComplianceUpgrade.cs' }
$trivial = $responsibilityFiles | Where-Object { $_.Name -match 'Part\d' }
Assert (($trivial | Measure-Object).Count -eq 0) 'no trivial PartN files'

# PetController-host files (all except the pure-DTO Models file) must each still
# declare the same type as a partial class.
$partialMissing = @()
foreach ($f in $complianceFiles) {
    if ($f.Name -eq 'Compliance.Models.cs') { continue }
    $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
    if ($content -notmatch 'public\s+partial\s+class\s+PetController') { $partialMissing += $f.Name }
}
Assert ($partialMissing.Count -eq 0) 'PetController remains a partial class in every Compliance host file'
if ($partialMissing.Count -gt 0) { Write-Output ("  partial missing in: " + ($partialMissing -join ', ')) }

# Model DTOs are each defined exactly once across the Compliance source set.
$dtos = @('ComplianceRuleData','ComplianceFindingData','ComplianceAuditRecord','ComplianceModelReview','ComplianceModelReviewItem')
foreach ($dto in $dtos) {
    $count = 0
    foreach ($f in $complianceFiles) {
        $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
        $count += ([regex]::Matches($content, '\b(class|struct)\s+' + $dto + '\b')).Count
    }
    Assert ($count -eq 1) ("DTO " + $dto + " defined exactly once (found $count)")
}

# Build source list covers every split Compliance*.cs file.
$build = Get-Content -LiteralPath (Join-Path $root 'scripts\build.ps1') -Encoding UTF8 -Raw
foreach ($f in $complianceFiles) {
    $name = $f.Name
    Assert ($build -match "'" + $name + "'") ("build source list includes " + $name)
}

# Every responsibility file is non-trivially populated (has class-body members).
foreach ($f in $responsibilityFiles) {
    $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
    $memberCount = ([regex]::Matches($content, '(?m)^\s{8}[^/\s].*')).Count
    Assert ($memberCount -gt 0) (($f.Name) + " carries class members (lines $memberCount)")
}

# Key boundaries: named methods live only in their designated responsibility file.
$boundary = @{
    'ComplianceUpgrade.cs'   = @('BuildCompliancePanel','PolishComplianceLayout','SetComplianceImage','ShowComplianceEditor','ShowComplianceReviewTab','RunLocalComplianceScan','OpenComplianceReview')
    'Compliance.Rules.cs'    = @('ComplianceRules','LoadComplianceRuleDefinitions','IsCompliancePunctuation','NormalizeComplianceText','TryLocateCompliancePhrase','IsNegatedOrExplanatoryUse','DisclosureAnchorGroups','HasRequiredDisclosure','ScanComplianceRules')
    'Compliance.Review.cs'   = @('ComplianceInstruction','ComplianceContext','ApplyStructuredComplianceReview','MergeModelFindings','ProviderHost')
    'Compliance.Audit.cs'    = @('InitializeCompliancePaths','LoadComplianceAudits','SaveComplianceAudits','ComplianceHash','ComplianceConclusion','CloneComplianceFindings','SyncCurrentComplianceAudit','RunComplianceAudit')
    'Compliance.Rendering.cs'= @('ComplianceSeverityBrush','MarkerValue','ComplianceFindingRow','RefreshComplianceFindings','ShowSelectedComplianceFinding','SelectComplianceFinding','FocusComplianceFinding','SetComplianceDisposition','RenderComplianceOriginal')
    'Compliance.Export.cs'   = @('Html','HighlightedComplianceHtml','ExportComplianceReport','ShowComplianceHistory')
}
foreach ($entry in $boundary.GetEnumerator()) {
    foreach ($m in $entry.Value) {
        $foundIn = @()
        foreach ($f in $complianceFiles) {
            $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
            # Match the member declaration (8-space class-body indent). Negative
            # lookbehind rejects call sites: a declared method is preceded by a
            # return-type + space, whereas a call is preceded by `;`/`(`/`{`/`=`/`,`.
            # (Calls that sit on the same line as a single-line method body would
            # otherwise be miscounted as declarations.)
            if ($content -match ('(?m)^ {8}\S[^\r\n]*?(?<![;({=,])\b' + $m + '(?:<[^>]*>)?\s*\(')) { $foundIn += $f.Name }
        }
        Assert ($foundIn.Count -eq 1 -and $foundIn[0] -eq $entry.Key) ("method " + $m + " defined in " + $entry.Key + " (found in " + ($foundIn -join ',') + ")")
    }
}

Write-Output 'All Compliance boundary structure tests passed.'
exit 0