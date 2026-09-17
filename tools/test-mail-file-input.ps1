param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))
$type=$assembly.GetType('MomoPetApp.PetController')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$env:MOMOPET_DATA_DIR=Join-Path (Split-Path $PSScriptRoot -Parent) ('test-output\mail-input-'+[Guid]::NewGuid().ToString('N'))
$app=New-Object Windows.Application
$app.ShutdownMode='OnExplicitShutdown'
$controller=$type.GetConstructor([Type[]]@([Windows.Application])).Invoke([object[]]@($app.PSObject.BaseObject))
function SetField($name,$value){$type.GetField($name,$flags).SetValue($controller,$value)}
function Field($name){return ,$type.GetField($name,$flags).GetValue($controller)}
function Call($name,$arguments){$unwrapped=[object[]]@($arguments|ForEach-Object{$_.PSObject.BaseObject});$type.GetMethod($name,$flags).Invoke($controller,$unwrapped)|Out-Null}
$window=New-Object Windows.Window
$input=New-Object Windows.Controls.TextBox
$window.Content=$input
SetField 'messengerPanel' $window
SetField 'messengerInput' $input
$status=New-Object Windows.Controls.TextBlock
SetField 'messengerStatus' $status
$identity=[Activator]::CreateInstance($assembly.GetType('MomoPetApp.MomoAccountState'))
$identity.MemberId='test-no-network'
SetField 'momoAccount' $identity
SetField 'momoToken' 'test-no-network'
Call 'InstallMomoFileInput' @()
Call 'InstallMomoFileInput' @()
if(!$window.AllowDrop){throw 'File dropping is disabled'}
$data=New-Object Windows.DataObject
$data.SetData([Windows.DataFormats]::FileDrop,[string[]]@('Z:\missing-momo-file.txt'))
$paste=[Windows.DataObjectPastingEventArgs]::new($data,$false,[Windows.DataFormats]::FileDrop)
$input.RaiseEvent($paste)
if(!$paste.CommandCancelled -or [string]::IsNullOrEmpty($status.Text)){throw 'File paste did not reach mail handler'}
$text=New-Object Windows.DataObject
$text.SetText('normal text')
$pasteText=[Windows.DataObjectPastingEventArgs]::new($text,$false,[Windows.DataFormats]::UnicodeText)
$input.RaiseEvent($pasteText)
if($pasteText.CommandCancelled){throw 'Normal text paste was intercepted'}
foreach($target in @('selectedMessengerMember','selectedMomoGroup')){
    $targetType=if($target -eq 'selectedMessengerMember'){'MomoPetApp.MomoRemoteMember'}else{'MomoPetApp.MomoGroup'}
    SetField $target ([Activator]::CreateInstance($assembly.GetType($targetType)))
    Call 'AddMomoFilesFromData' @($data)
    if((Field 'momoFileOperationBusy') -or (Field 'pendingAttachments').Count -ne 0){throw 'Invalid file started an upload'}
    SetField $target $null
}
SetField 'selectedMessengerMember' ([Activator]::CreateInstance($assembly.GetType('MomoPetApp.MomoRemoteMember')))
Call 'SetMessengerBusy' @($true)
Call 'AddMomoFilesFromData' @($data)
if(!(Field 'momoFileOperationBusy') -or $input.IsEnabled){throw 'Busy guard failed'}
Call 'SetMessengerBusy' @($false)
$folder=Join-Path $env:MOMOPET_DATA_DIR 'Original Folder'
$nested=Join-Path $folder 'nested'
[IO.Directory]::CreateDirectory($nested)|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'readme.txt'),'folder upload test')
[IO.File]::WriteAllText((Join-Path $nested 'content.txt'),'nested file')
$staticFlags=[Reflection.BindingFlags]'Static,NonPublic'
$folderInputs=New-Object 'System.Collections.Generic.List[string]'
$folderInputs.Add($folder)
$prepareArgs=[object[]]::new(1);$prepareArgs[0]=$folderInputs.PSObject.BaseObject
$prepared=$type.GetMethod('PrepareMomoUpload',$staticFlags).Invoke($null,$prepareArgs)
if($prepared.Paths.Count -ne 1 -or [IO.Path]::GetFileName($prepared.Paths[0]) -ne 'Original Folder.zip'){throw 'Folder archive did not preserve the folder name'}
$archive=[IO.Compression.ZipFile]::OpenRead($prepared.Paths[0])
try{$entries=@($archive.Entries|ForEach-Object{$_.FullName.Replace('\','/')});if($entries -notcontains 'readme.txt' -or $entries -notcontains 'nested/content.txt'){throw 'Folder archive lost files or hierarchy'}}finally{$archive.Dispose()}
$cleanupArgs=[object[]]::new(1);$cleanupArgs[0]=$prepared.TemporaryFiles.PSObject.BaseObject
$type.GetMethod('CleanupMomoTemporaryFiles',$staticFlags).Invoke($null,$cleanupArgs)|Out-Null
if(Test-Path -LiteralPath ($prepared.Paths[0])){throw 'Temporary archive was not cleaned'}
$safe=$type.GetMethod('SafeAttachmentName',$staticFlags).Invoke($null,[object[]]@('Original Name 2026.xlsx'))
if($safe -ne 'Original Name 2026.xlsx'){throw 'Valid original filename was changed'}
Write-Output 'PASS: routed file paste, normal text paste, private/group validation, busy guard, folder ZIP hierarchy and original filename preservation. No clipboard changed, files uploaded or windows shown.'
