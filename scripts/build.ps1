# build.ps1 — compile MomoPet into artifacts\MomoPet.exe + build-info.json.
# Replaces the ad-hoc root build.ps1; that legacy file forwards here.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$script:BlockName = 'build'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $script:RepositoryRoot
$stamp = Get-Date -Format 'MMdd-HHmmss'
$artifactsDir = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

Write-Banner 'MomoPet build'

# --- resolve dependencies (P0-02 / P0-03): no fixed SDK minor, explicit node ---
$compiler = Resolve-CSharpCompiler
Log-Ok 'C# compiler' $compiler
$sdk = Resolve-WindowsSdkWinmd
Log-Ok 'Windows SDK' "$($sdk.Version) ($($sdk.Path))"
$node = Resolve-NodeRuntime
$nodeExe = $node.Path

# --- ensure skill payloads exist (P0-04) --------------------------------------
$skillsRoot = Join-Path $root '.agents'
if (-not (Test-Path -LiteralPath (Join-Path $skillsRoot 'skills'))) {
    & (Join-Path $PSScriptRoot 'restore-skills.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Skill restore failed; aborting build.' }
}

$tmpExe = Join-Path $artifactsDir ('MomoPet.' + $stamp + '.exe')
$ocrOutput = Join-Path $artifactsDir ('MomoOcr.build-' + $stamp + '.exe')

function FirstAssembly($folder, $name) {
    Get-ChildItem -LiteralPath $folder -Filter $name -Recurse -File | Select-Object -First 1 -ExpandProperty FullName
}

# --- 1) compile OCR helper -----------------------------------------------------
$ocrReferences = @(
    $sdk.Path,
    (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime' 'System.Runtime.WindowsRuntime.dll'),
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll'
) | Where-Object { $_ }

$ocrSources = @(
    (Join-Path $root 'src\OcrContracts.cs'),
    (Join-Path $root 'src\LocalOcr.cs'),
    (Join-Path $root 'src\LocalOcrRunner.cs')
)
$ocrArgs = @('/nologo', '/target:exe', ('/out:' + $ocrOutput)) + ($ocrReferences | ForEach-Object { '/reference:' + $_ }) + $ocrSources
& $compiler @ocrArgs 2>&1 | ForEach-Object { Write-Host "  [csc] $_" }
if ($LASTEXITCODE -ne 0) { throw 'OCR helper compiler failed.' }
Log-Ok 'Compile OCR helper' $ocrOutput

# --- 2) package portable runtime: node.ex gzip + skills.zip --------------------
$payloadDir = Join-Path ([IO.Path]::GetTempPath()) ('MomoPet-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
try {
    $nodeGzip = Join-Path $payloadDir 'node.exe.gz'
    $gIn = [System.IO.File]::OpenRead($nodeExe)
    try {
        $gOut = [System.IO.File]::Create($nodeGzip)
        try {
            $gz = New-Object System.IO.Compression.GZipStream($gOut, [System.IO.Compression.CompressionMode]::Compress)
            try { $gIn.CopyTo($gz) } finally { $gz.Dispose() }
        } finally { $gOut.Dispose() }
    } finally { $gIn.Dispose() }
    Log-Ok 'Package portable Node' "gzipped $nodeExe"

    $skillsArchive = Join-Path $payloadDir 'agents-skills.zip'
    $archiveStream = [IO.File]::Create($skillsArchive)
    try {
        $archive = New-Object IO.Compression.ZipArchive($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            Get-ChildItem -LiteralPath $skillsRoot -Recurse -File | Where-Object {
                $_.Name -notlike 'request-*.json' -and
                $_.Name -ne 'update-state.json' -and
                $_.FullName -notlike '*\node_modules\*' -and
                $_.FullName -notlike '*\.git\*'
            } | ForEach-Object {
                $relative = $_.FullName.Substring($skillsRoot.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, '.agents/' + $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose() }
    } finally { $archiveStream.Dispose() }
    Log-Ok 'Package skills archive' $skillsArchive

    # --- 3) compile MomoPet ------------------------------------------------------
    $references = @(
        (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework' 'PresentationFramework.dll'),
        (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore' 'PresentationCore.dll'),
        (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase' 'WindowsBase.dll'),
        (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml' 'System.Xaml.dll'),
        (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression' 'System.IO.Compression.dll'),
        (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression.FileSystem' 'System.IO.Compression.FileSystem.dll')
    ) | Where-Object { $_ }

    $sourceFiles = @(
        'AppPaths.cs','AppLog.cs','AppDiagnostics.cs','Ui.cs','TextSelection.cs','OfficeComfort.cs','MomoPet.cs','SkinWardrobe.cs','StashWorkspace.cs',
        'PetExperience.cs','AiSearch.cs','ImageEditor.cs','ImageEditor.Models.cs','ImageEditor.State.cs','ImageEditor.LocalEditing.cs','ImageEditor.AiConfig.cs','ImageEditor.AiTasks.cs','ImageEditor.Ocr.cs','ImageEditor.Precision.cs','AiPocket.cs','AiCommunity.cs','Messenger.cs',
        'MomoAccount.cs','Launcher.cs','ComplianceUpgrade.cs','OcrContracts.cs','EmbeddedRuntime.cs',
        'HealthCompanion.cs','CommunityCloud.cs'
    ) | ForEach-Object { Join-Path $root ('src\' + $_) }

    $assetsRoot = Join-Path $root 'assets\normalized'
    $resources = @(Get-ChildItem -LiteralPath $assetsRoot -Recurse -File -Filter '*.png' |
        Where-Object { $_.Name -ne 'edge-peek-new.png' -and $_.FullName -notlike '*\skin-actions\*' } |
        ForEach-Object {
            $relative = $_.FullName.Substring($assetsRoot.Length + 1) -replace '\\', '.'
            '/resource:' + $_.FullName + ',assets.normalized.' + $relative
        })
    $resources += @(
        ('/resource:' + $ocrOutput + ',MomoOcr.exe'),
        ('/resource:' + (Join-Path $root 'wind_bridge\wind_ai_search.mjs') + ',wind_bridge.wind_ai_search.mjs'),
        ('/resource:' + (Join-Path $root 'wind_bridge\image_ai.mjs') + ',wind_bridge.image_ai.mjs'),
        ('/resource:' + (Join-Path $root 'compliance-rules.txt') + ',compliance-rules.txt'),
        ('/resource:' + $nodeGzip + ',node.exe.gz'),
        ('/resource:' + $skillsArchive + ',agents-skills.zip')
    )

    # P1-07 构建元数据（version/commit/buildTime）嵌入 EXE，日志头可据此定位构建来源。
    $metaInfo = Get-CommitInfo
    $buildMetaPath = Join-Path $payloadDir 'build-meta.txt'
    $buildMetaLines = @(
        'commit=' + $metaInfo.Commit,
        'branch=' + $metaInfo.Branch,
        'buildTime=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),
        'node=' + $node.Version,
        'windowsSdk=' + $sdk.Version.ToString()
    )
    [System.IO.File]::WriteAllLines($buildMetaPath, $buildMetaLines, (New-Object System.Text.UTF8Encoding($false)))
    $resources += ('/resource:' + $buildMetaPath + ',build-meta.txt')

    $arguments = @('/nologo', '/target:winexe', ('/out:' + $tmpExe)) + ($references | ForEach-Object { '/reference:' + $_ }) + $resources + $sourceFiles
    & $compiler @arguments 2>&1 | ForEach-Object { Write-Host "  [csc] $_" }
    if ($LASTEXITCODE -ne 0) { throw 'C# compiler failed building MomoPet.' }
    Log-Ok 'Compile MomoPet' $tmpExe

    # --- 4) publish artifact (atomic; artifacts\MomoPet.exe always = current build) ---
    $finalExe = Join-Path $artifactsDir 'MomoPet.exe'
    Move-Item -LiteralPath $tmpExe -Destination $finalExe -Force
    Log-Ok 'Publish artifact' $finalExe
} finally {
    Remove-Item -LiteralPath $payloadDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Clean up OCR helper intermediates after a successful build.
Get-ChildItem -LiteralPath $artifactsDir -Filter 'MomoOcr.build-*.exe' -File -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

# --- 5) build-info.json -------------------------------------------------------
$git = Get-CommitInfo
$buildInfo = [ordered]@{
    commit       = $git.Commit
    branch       = $git.Branch
    buildTime    = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    nodeVersion  = $node.Version
    windowsSdk   = $sdk.Version.ToString()
    compiler     = $compiler
    testsPassed  = $false
}
$buildInfoPath = Join-Path $artifactsDir 'build-info.json'
$buildInfo | ConvertTo-Json | Set-Content -LiteralPath $buildInfoPath -Encoding UTF8
Log-Ok 'Write build-info.json' $buildInfoPath

Write-Host ''
Write-Host ("[OK]   build: MomoPet.exe -> " + $finalExe)
exit 0