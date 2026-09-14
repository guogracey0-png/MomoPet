$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = @((Join-Path $root 'src\Ui.cs'), (Join-Path $root 'src\OfficeComfort.cs'), (Join-Path $root 'src\MomoPet.cs'), (Join-Path $root 'src\SkinWardrobe.cs'), (Join-Path $root 'src\StashWorkspace.cs'), (Join-Path $root 'src\PetExperience.cs'), (Join-Path $root 'src\AiSearch.cs'), (Join-Path $root 'src\ImageEditor.cs'), (Join-Path $root 'src\AiPocket.cs'), (Join-Path $root 'src\AiCommunity.cs'), (Join-Path $root 'src\Messenger.cs'), (Join-Path $root 'src\Launcher.cs'), (Join-Path $root 'src\ComplianceUpgrade.cs'), (Join-Path $root 'src\OcrContracts.cs'), (Join-Path $root 'src\EmbeddedRuntime.cs'))
# 注意：本目录被外部 safe-delete 钩子保护，任何"覆盖/删除已存在文件"都会被拦截且脚本内接不住。
# 因此所有编译产物一律写到全新的带时间戳文件名，永不覆盖旧文件。
$stamp = Get-Date -Format 'MMdd-HHmmss'
$output = Join-Path $root 'MomoPet.exe'
if (Test-Path -LiteralPath $output) {
    # 不覆盖旧版：交付一个可在退出旧版后启动的更新文件。
    $output = Join-Path $root ('MomoPet.next-' + $stamp + '.exe')
}
$ocrOutput = Join-Path $root ('MomoOcr.build-' + $stamp + '.exe')
function FirstAssembly($folder,$name) { Get-ChildItem -LiteralPath $folder -Filter $name -Recurse -File | Select-Object -First 1 -ExpandProperty FullName }
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
# 先编译 OCR 助手：主程序会把它作为嵌入资源打进单文件 exe。
$ocrReferences = @('C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd',(FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime' 'System.Runtime.WindowsRuntime.dll'),'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll')
$ocrSources = @((Join-Path $root 'src\OcrContracts.cs'),(Join-Path $root 'src\LocalOcr.cs'),(Join-Path $root 'src\LocalOcrRunner.cs'))
$ocrArguments = @('/nologo','/target:exe',('/out:'+ $ocrOutput)) + ($ocrReferences | ForEach-Object { '/reference:'+$_ }) + $ocrSources
& $compiler @ocrArguments
if ($LASTEXITCODE -ne 0) { throw "OCR helper compiler failed with exit code $LASTEXITCODE" }
# 便携运行时：把 Node 与 .agents 技能库压缩后嵌入，使 exe 在全新电脑上零安装即可运行。
# Node 体积较大，先用 gzip 压缩（体积约为原来的三分之一），运行时释放时解压还原。
$payloadDir = Join-Path $env:LOCALAPPDATA 'MomoPet\payload'
if (-not (Test-Path -LiteralPath $payloadDir)) { New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null }
$nodeSource = Join-Path $root '本地部署\node.exe'
if (-not (Test-Path -LiteralPath $nodeSource)) {
    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if (-not $nodeCommand) { throw '未找到 node.exe：请安装 Node.js，或把便携版放到 本地部署\node.exe' }
    $nodeSource = $nodeCommand.Source
}
$nodeGzip = Join-Path $payloadDir 'node.exe.gz'
if (-not (Test-Path -LiteralPath $nodeGzip) -or (Get-Item -LiteralPath $nodeGzip).LastWriteTimeUtc -lt (Get-Item -LiteralPath $nodeSource).LastWriteTimeUtc) {
    $nodeInput = [System.IO.File]::OpenRead($nodeSource)
    try {
        $nodeOutput = [System.IO.File]::Create($nodeGzip)
        try {
            $nodeCompressor = New-Object System.IO.Compression.GZipStream($nodeOutput, [System.IO.Compression.CompressionMode]::Compress)
            try { $nodeInput.CopyTo($nodeCompressor) } finally { $nodeCompressor.Dispose() }
        } finally { $nodeOutput.Dispose() }
    } finally { $nodeInput.Dispose() }
}
$skillsArchive = Join-Path $payloadDir 'agents-skills.zip'
if (Test-Path -LiteralPath $skillsArchive) { Remove-Item -LiteralPath $skillsArchive -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root '.agents'), $skillsArchive, [System.IO.Compression.CompressionLevel]::Optimal, $true)
# 收集嵌入资源：动画帧 + OCR 助手 + AI 桥接脚本 + 合规词库 + 便携 Node + 技能库，全部打进 exe。
$references = @(
    (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework' 'PresentationFramework.dll'),
    (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore' 'PresentationCore.dll'),
    (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase' 'WindowsBase.dll'),
    (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml' 'System.Xaml.dll'),
    (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression' 'System.IO.Compression.dll'),
    (FirstAssembly 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression.FileSystem' 'System.IO.Compression.FileSystem.dll')
) | Where-Object { $_ }
$assetsRoot = Join-Path $root 'assets\normalized'
$resources = @(Get-ChildItem -LiteralPath $assetsRoot -Recurse -File -Filter '*.png' | Where-Object { $_.Name -ne 'edge-peek-new.png' -and $_.FullName -notlike '*\skin-actions\*' } | ForEach-Object {
    $relative = $_.FullName.Substring($assetsRoot.Length + 1) -replace '\\','.'
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
$arguments = @('/nologo','/target:winexe',('/out:'+ $output)) + ($references | ForEach-Object { '/reference:'+$_ }) + $resources + $source
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE" }
Write-Host "Built: $output"
