param(
    [Parameter(Mandatory = $true)]
    [string]$CafeServer
)

$ErrorActionPreference = 'Stop'
$patchRoot = Join-Path $PSScriptRoot 'cafe-server-patch\src'
$sourceRoot = Join-Path $CafeServer 'src'
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'index.ts'))) {
    throw "Cafe server source was not found: $CafeServer"
}
$backupRoot = Join-Path $CafeServer ('backup-before-momopet-community-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$backupFiles = @('repositories\momoRepo.ts','routes\momo.ts','scripts\initMomoTables.ts','index.ts','desktop-entry.ts')
foreach ($relative in $backupFiles) {
    $existing = Join-Path $sourceRoot $relative
    if (Test-Path -LiteralPath $existing) {
        $backup = Join-Path $backupRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $existing -Destination $backup
    }
}

$copies = @(
    @('repositories\momoRepo.ts', 'repositories\momoRepo.ts'),
    @('routes\momo.ts', 'routes\momo.ts'),
    @('scripts\initMomoTables.ts', 'scripts\initMomoTables.ts')
)
foreach ($copy in $copies) {
    $source = Join-Path $patchRoot $copy[0]
    $destination = Join-Path $sourceRoot $copy[1]
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

function Add-MomoRoute([string]$path) {
    $text = [IO.File]::ReadAllText($path)
    if ($text -notmatch 'routes/momo\.js') {
        $anchor = 'import { membersRouter } from "./routes/members.js";'
        if (-not $text.Contains($anchor)) { throw "Import anchor was not found: $path" }
        $text = $text.Replace($anchor, $anchor + [Environment]::NewLine + 'import { momoRouter } from "./routes/momo.js";')
    }
    if ($text -notmatch 'app\.use\("/api/momo"') {
        $anchor = 'app.use("/api/members", membersRouter);'
        if (-not $text.Contains($anchor)) { throw "Route anchor was not found: $path" }
        $text = $text.Replace($anchor, $anchor + [Environment]::NewLine + 'app.use("/api/momo", momoRouter);')
    }
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

Add-MomoRoute (Join-Path $sourceRoot 'index.ts')
Add-MomoRoute (Join-Path $sourceRoot 'desktop-entry.ts')
Write-Host "MomoPet messaging and community patch was merged. Backup: $backupRoot"
