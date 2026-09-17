param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))
$ui=$assembly.GetType('MomoPetApp.Ui');$selector=$assembly.GetType('MomoPetApp.TextSelection')
$flags=[Reflection.BindingFlags]'Static,Public'
function InvokeStatic($type,$name,[object[]]$values){$type.GetMethod($name,$flags).Invoke($null,$values)}
function Check($condition,$label){if(!$condition){throw $label};Write-Output ('PASS: '+$label)}
$window=New-Object Windows.Window
InvokeStatic $ui 'StyleWindow' @($window.PSObject.BaseObject)|Out-Null
$decorator=New-Object Windows.Documents.AdornerDecorator
$stack=New-Object Windows.Controls.StackPanel
$stack.Background=[Windows.Media.Brushes]::White
$decorator.Child=$stack;$window.Content=$decorator
$text=New-Object Windows.Controls.TextBlock
$text.Text="这是一封中文来信，包含换行和 emoji 🐾。`n第二行内容可以完整选中，and English 123。"
$text.TextWrapping='Wrap';$text.FontSize=18;$text.Width=240
$stack.Children.Add($text)|Out-Null
$text.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::LoadedEvent))
Check $text.Focusable 'Dynamically loaded labels become selectable'
Check ($text.ContextMenu.Items.Count -eq 3) 'Copy selection, select all and copy-all commands exist'
$decorator.Measure([Windows.Size]::new(420,320));$decorator.Arrange([Windows.Rect]::new(0,0,420,320));$decorator.UpdateLayout()
InvokeStatic $selector 'SelectAll' @($text.PSObject.BaseObject)|Out-Null
$selected=$selector.GetProperty('SelectedText').GetValue($null,$null)
Check ($selected.Replace("`r`n","`n") -eq $text.Text) 'Wrapped Chinese, English, emoji and line breaks select exactly'
Check (InvokeStatic $selector 'HasSelectionWithin' @($window.PSObject.BaseObject)) 'Selection is detected for refresh protection'
$before=$text.Text
$decorator.Measure([Windows.Size]::new(420,320));$decorator.Arrange([Windows.Rect]::new(0,0,420,320));$decorator.UpdateLayout()
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(420,320,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($decorator)
$out=Join-Path (Split-Path $PSScriptRoot -Parent) 'test-output\text-selection.png'
[IO.Directory]::CreateDirectory((Split-Path $out -Parent))|Out-Null
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream=[IO.File]::Create($out);try{$encoder.Save($stream)}finally{$stream.Dispose()}
Check ($text.Text -eq $before) 'Selection rendering does not modify source text'
InvokeStatic $selector 'Clear' @()|Out-Null
Check (!(InvokeStatic $selector 'HasSelectionWithin' @($window.PSObject.BaseObject))) 'Clearing selection resumes refresh eligibility'
$button=New-Object Windows.Controls.Button
$label=New-Object Windows.Controls.TextBlock;$label.Text='发送消息';$button.Content=$label;$stack.Children.Add($button)|Out-Null
$label.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::LoadedEvent))
Check (!$label.Focusable) 'Button labels retain normal button interaction'
$native=InvokeStatic $ui 'ReadOnlyText' @('正文保持只读，选取中间内容。',14.0)
$native.Select(7,6)
Check ($native.SelectedText -eq '选取中间内容') 'Native message editor supports partial selection'
Check $native.IsReadOnly 'Message contents cannot be edited'
Check ($native.ContextMenu.Items.Count -eq 3) 'Native message copy menu is available'
Write-Output $out
Write-Output 'No clipboard contents were changed and no app windows or accounts were opened.'
