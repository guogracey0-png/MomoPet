param([string]$Exe)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))
$type=$assembly.GetType('MomoPetApp.PetController')
$controller=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
function SetField($name,$value){$type.GetField($name,$flags).SetValue($controller,$value)}
function Call($name,$values){$argsList=New-Object 'System.Collections.Generic.List[object]';foreach($value in $values){$argsList.Add($value.PSObject.BaseObject)};$type.GetMethod($name,$flags).Invoke($controller,$argsList.ToArray())}
function Assert($ok,$message){if(!$ok){throw $message};Write-Output "PASS: $message"}
$folder=Join-Path $PSScriptRoot ('test-output\office-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder)|Out-Null
SetField 'dataDir' $folder
foreach($name in @('manuallyPlacedWindows','comfortBitmapFiles')){SetField $name ([Activator]::CreateInstance($type.GetField($name,$flags).FieldType))}
$window=New-Object Windows.Window
$window.Title='Office comfort regression';$window.Width=500;$window.Height=400;$window.Left=20;$window.Top=30
$stack=New-Object Windows.Controls.StackPanel
$window.Content=$stack
$text=New-Object Windows.Controls.TextBox
$text.Text='未发送的草稿：中文与 123'
$stack.Children.Add($text)|Out-Null
$rich=New-Object Windows.Controls.RichTextBox
$rich.Document.Blocks.Add((New-Object Windows.Documents.Paragraph (New-Object Windows.Documents.Run '已返回的结果')))
$stack.Children.Add($rich)|Out-Null
SetField 'aiQuestionBox' $text;SetField 'aiResultBox' $rich
$type.GetField('manuallyPlacedWindows',$flags).GetValue($controller).Add($window)|Out-Null
Call 'SaveOfficeState' @($window)|Out-Null
$path=Call 'ComfortStatePath' @($window)
Assert (Test-Path $path) 'Workspace JSON saved'
$text.Clear();$rich.Document.Blocks.Clear();$window.Width=600
Call 'RestoreOfficeState' @($window)|Out-Null
Assert ($text.Text -eq '未发送的草稿：中文与 123') 'Draft restored'
Assert ($window.Width -eq 500) 'Window size restored'
$range=New-Object Windows.Documents.TextRange $rich.Document.ContentStart,$rich.Document.ContentEnd
Assert ($range.Text.Contains('已返回的结果')) 'Rich result restored'
$text.Text='新带入的内容'
Call 'RestoreOfficeState' @($window)|Out-Null
Assert ($text.Text -eq '新带入的内容') 'Explicit incoming text preserved'
[byte[]]$pixels=@(0,0,255,0,0,255,0,128,255,0,0,255,255,255,255,255)
$bitmap=[Windows.Media.Imaging.BitmapSource]::Create(2,2,96,96,[Windows.Media.PixelFormats]::Bgra32,$null,$pixels,8)
$bitmap.Freeze()
$png=Call 'StoreComfortBitmap' @($bitmap)
Assert ((Call 'StoreComfortBitmap' @($bitmap)) -eq $png) 'Unchanged bitmap snapshot reused'
$stream=[IO.File]::OpenRead($png)
try{$decoded=[Windows.Media.Imaging.BitmapDecoder]::Create($stream,[Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad).Frames[0]}finally{$stream.Dispose()}
$converted=New-Object Windows.Media.Imaging.FormatConvertedBitmap $decoded,([Windows.Media.PixelFormats]::Bgra32),$null,0
$actual=New-Object byte[] 16
$converted.CopyPixels($actual,8,0)
Assert ($actual[3] -eq 0 -and $actual[7] -eq 128 -and $actual[11] -eq 255) 'PNG alpha preserved exactly'
Write-Output 'All background tests passed; no window was shown.'
