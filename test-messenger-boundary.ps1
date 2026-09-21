param([string]$Exe)
# test-messenger-boundary.ps1 — P2B-01..P2B-05 structure guardrail.
# Verifies (against the src/ tree and scripts/build.ps1) that the Messenger
# boundary split holds: >=6 responsibility files, PetController stays partial,
# model DTOs are each defined exactly once, no trivial PartN split, and the
# build source list covers every Messenger*.cs file. Accepts $Exe for harness
# compatibility but inspects source files directly.
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

function Assert($ok, $message) {
    if (-not $ok) { Write-Error "FAIL: $message"; exit 1 }
    Write-Output "PASS: $message"
}

$srcDir = Join-Path $root 'src'
Assert (Test-Path $srcDir) 'src directory exists'

$messengerFiles = Get-ChildItem -LiteralPath $srcDir -File -Filter 'Messenger*.cs' | Sort-Object Name
$responsibilityFiles = $messengerFiles | Where-Object { $_.Name -ne 'Messenger.cs' }
$expected = @('Models','State','Api','Attachments','Conversations','Groups','Courier')
$expectedNames = $expected | ForEach-Object { 'Messenger.' + $_ + '.cs' }
$missing = $expectedNames | Where-Object { -not ($responsibilityFiles.Name -contains $_) }
Assert ($missing.Count -eq 0) "every expected Messenger responsibility file exists ($($expectedNames -join ', '))"
Assert ($responsibilityFiles.Count -ge 6) ("at least 6 Messenger responsibility files (found $($responsibilityFiles.Count))")

# No trivial PartN split.
$trivial = $responsibilityFiles | Where-Object { $_.Name -match 'Part\d' }
Assert (($trivial | Measure-Object).Count -eq 0) 'no trivial PartN files'

# PetController-host files (all except the pure-DTO Models file) must each still
# declare the same type as a partial class.
$partialMissing = @()
foreach ($f in $messengerFiles) {
    if ($f.Name -eq 'Messenger.Models.cs') { continue }
    $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
    if ($content -notmatch 'public\s+partial\s+class\s+PetController') { $partialMissing += $f.Name }
}
Assert ($partialMissing.Count -eq 0) 'PetController remains a partial class in every Messenger host file'
if ($partialMissing.Count -gt 0) { Write-Output ("  partial missing in: " + ($partialMissing -join ', ')) }

# Model DTOs are each defined exactly once across the Messenger source set.
$dtos = @('MomoRemoteMember','MomoAttachment','MomoPreparedUpload','MomoLetter','MomoGroup','MomoGroupMessage')
foreach ($dto in $dtos) {
    $count = 0
    foreach ($f in $messengerFiles) {
        $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
        $count += ([regex]::Matches($content, '\b(class|struct)\s+' + $dto + '\b')).Count
    }
    Assert ($count -eq 1) ("DTO " + $dto + " defined exactly once (found $count)")
}

# Build source list covers every Messenger*.cs file.
$build = Get-Content -LiteralPath (Join-Path $root 'scripts\build.ps1') -Encoding UTF8 -Raw
foreach ($f in $messengerFiles) {
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
    'Messenger.cs'           = @('InitializeMessenger','OpenMessengerPanel','BuildMessengerPanel')
    'Messenger.Api.cs'         = @('MomoApi')
    'Messenger.Attachments.cs' = @('UploadMomoFiles','PrepareMomoUpload','GuessAttachmentMime')
    'Messenger.Conversations.cs'= @('SendMomoMessage','PollMomoMessages','LoadConversation')
    'Messenger.Groups.cs'      = @('LoadMomoGroups','LoadMomoGroupConversation')
    'Messenger.Courier.cs'     = @('StartNextCourier','StartNextReceipt','TickCourier')
}
foreach ($entry in $boundary.GetEnumerator()) {
    foreach ($m in $entry.Value) {
        $foundIn = @()
        foreach ($f in $messengerFiles) {
            $content = Get-Content -LiteralPath $f.FullName -Encoding UTF8 -Raw
            # Match the member declaration (8-space class-body indent), not call sites (12-space body indent).
            if ($content -match ('(?m)^ {8}\S[^\r\n]*\b' + $m + '(?:<[^>]*>)?\s*\(')) { $foundIn += $f.Name }
        }
        Assert ($foundIn.Count -eq 1 -and $foundIn[0] -eq $entry.Key) ("method " + $m + " defined in " + $entry.Key + " (found in " + ($foundIn -join ',') + ")")
    }
}

Write-Output 'All Messenger boundary structure tests passed.'
exit 0