param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))
$t=$a.GetType('MomoPetApp.PetController')
$c=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($t)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
function Field($name){$t.GetField($name,$flags)}
function SetField($name,$value){(Field $name).SetValue($c,$value)}
function Call($name){$t.GetMethod($name,$flags).Invoke($c,@())}
foreach($name in 'skinMotionFrames','skinFrames','skinCardBorders','skinQuickMenuItems'){SetField $name ([Activator]::CreateInstance((Field $name).FieldType))}
SetField 'pet' (New-Object Windows.Window)
function Bitmap($name){
 $s=$a.GetManifestResourceStream($name)
 try{$b=New-Object Windows.Media.Imaging.BitmapImage;$b.BeginInit();$b.CacheOption='OnLoad';$b.StreamSource=$s;$b.EndInit();$b.Freeze();return $b}finally{$s.Dispose()}
}
$ids=@('detective','green-scarf-calico','tuxedo-bell','white-bowtie','orange-scarf','red-collar-calico','gentleman-monocle')
$names=@('idle','walk','run','dragged','sleep','edge','happy','coffee')
$first=Bitmap 'assets.normalized.skins.detective.png'
SetField 'frames' ([Windows.Media.Imaging.BitmapImage[]]@($first))
foreach($id in $ids){
 (Field 'skinFrames').GetValue($c).Add($id,(Bitmap "assets.normalized.skins.$id.png"))
 $values=[Windows.Media.Imaging.BitmapImage[]]@($names | ForEach-Object {Bitmap "assets.normalized.skin-motion.$id.$_.png"})
 (Field 'skinMotionFrames').GetValue($c).Add($id,$values)
}
SetField 'pendingSkinId' 'detective'
Call 'BuildSkinWardrobe' | Out-Null
Call 'RefreshSkinWardrobe' | Out-Null
$window=(Field 'skinWardrobePanel').GetValue($c)
$shell=$window.Content
$preview=(Field 'skinLargePreview').GetValue($c)
New-Item -ItemType Directory -Force test-output/wardrobe | Out-Null
foreach($size in @(@(700,520),@(860,610))){
 foreach($id in $ids){
  SetField 'pendingSkinId' $id;Call 'UpdateSkinPreview' | Out-Null
  $shell.Measure((New-Object Windows.Size($size[0],$size[1])))
  $shell.Arrange((New-Object Windows.Rect(0,0,$size[0],$size[1])))
  $shell.UpdateLayout()
  $stage=$preview.Parent
  if($preview.ActualHeight -gt $stage.ActualHeight-20 -or $preview.ActualWidth -gt $stage.ActualWidth-20){throw "Clipped preview: $id $size"}
  if($preview.ActualHeight -lt 30){throw "Preview too small: $id $size"}
 }
 $render=New-Object Windows.Media.Imaging.RenderTargetBitmap($size[0],$size[1],96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $render.Render($shell)
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($render))
 $stream=[IO.File]::Create((Join-Path (Get-Location) "test-output/wardrobe/wardrobe-$($size[0]).png"))
 try{$encoder.Save($stream)}finally{$stream.Dispose()}
 "PASS: 7 skins fit at $($size[0])x$($size[1]) without showing a window"
}
