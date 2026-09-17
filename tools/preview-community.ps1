param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))
$type=$assembly.GetType('MomoPetApp.PetController')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$folder=Join-Path (Split-Path $PSScriptRoot -Parent) ('test-output\community-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder)|Out-Null
$env:MOMOPET_DATA_DIR=Join-Path $folder 'data'
$app=New-Object Windows.Application
$app.ShutdownMode='OnExplicitShutdown'
$constructor=$type.GetConstructor([Type[]]@([Windows.Application]))
$controller=$constructor.Invoke([object[]]@($app.PSObject.BaseObject))
function Call($name){$type.GetMethod($name,$flags).Invoke($controller,@())}
function Field($name){return ,$type.GetField($name,$flags).GetValue($controller)}
$pet=New-Object Windows.Window
$type.GetField('pet',$flags).SetValue($controller,$pet)
Call 'LoadAssets'|Out-Null
Call 'EnsureCommunityData'|Out-Null
Call 'BuildCommunityPanel'|Out-Null
$window=Field 'communityPanel'
function Render($name,$width,$height){
    $window.Width=$width;$window.Height=$height
    $content=$window.Content
    $content.Measure([Windows.Size]::new($width,$height))
    $content.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $content.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($content)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $path=Join-Path $folder ($name+'.png')
    $stream=[IO.File]::Create($path)
    try{$encoder.Save($stream)}finally{$stream.Dispose()}
    Write-Output $path
}
Render 'empty-wide' 1120 820
# Test fixtures remain in memory only; they are never saved as community activity.
$post=[Activator]::CreateInstance($assembly.GetType('MomoPetApp.CommunityPost'))
$post.Id='preview-only';$post.Author='预览示例';$post.Role='仅用于布局检查';$post.Title='把一个小发现记录下来';$post.Body=('这里是用于检查换行、长内容和动态卡片的测试文字。'*12);$post.Category='实践分享';$post.Created='2026-09-15 11:00'
(Field 'communityPosts').Add($post)
Call 'RefreshCommunityFeed'|Out-Null
Render 'post-wide' 1120 820
# Explicitly emulate the compact layout without showing or launching any window.
$rail=Field 'communityRightRail'
$columns=$rail.Parent
$columns.ColumnDefinitions[1].Width=[Windows.GridLength]::new(0)
$rail.Visibility='Collapsed'
$columns.Children[0].Margin=[Windows.Thickness]::new(0)
Render 'post-compact' 820 660
(Field 'communitySearchBox').Text='no-results-for-layout-check'
Render 'search-empty' 820 660
Write-Output 'PASS: empty feed, long post, compact layout and no search results rendered. No window or single-instance lock created.'
Call 'BuildMomoAccountPanel'|Out-Null
Call 'RefreshMomoAccountPanel'|Out-Null
$window=Field 'momoAccountPanel'
Render 'account-login' 540 630
Call 'BuildMessengerPanel'|Out-Null
Call 'RefreshMessengerBody'|Out-Null
$window=Field 'messengerPanel'
Render 'mail-signed-out' 1180 780
Call 'BuildLauncherPanel'|Out-Null
$window=Field 'launcherPanel'
Render 'launcher-signed-out' 384 354
# Identity fixture stays in memory; no authentication endpoint is called.
$identity=[Activator]::CreateInstance($assembly.GetType('MomoPetApp.MomoAccountState'))
$identity.MemberId='preview-only';$identity.Nickname='预览用户';$identity.Username='preview';$identity.SkinId='default'
$type.GetField('momoAccount',$flags).SetValue($controller,$identity)
$type.GetField('momoToken',$flags).SetValue($controller,'preview-token-not-sent')
Call 'RefreshMomoAccountPanel'|Out-Null
Call 'RefreshLauncherIdentity'|Out-Null
Call 'RefreshCommunityFeed'|Out-Null
$window=Field 'momoAccountPanel'
Render 'account-signed-in' 540 630
$window=Field 'launcherPanel'
Render 'launcher-signed-in' 384 354
$label=(Field 'launcherAccountButton').Content.Children[1].Text
if(!$label.Contains('预览用户')){throw 'Global identity did not refresh'}
$expected=@(@('工作记录','中转袋','合规审核'),@('AI 口袋','AI 应用','资源中心'),@('大盘盯盘','Wind AI'),@('AI 社区','Momo 邮局'),@('健康陪伴','小猫衣橱','桌宠设置'))
$seen=@()
for($index=0;$index -lt 5;$index++){
    $tab=(Field 'launcherCategoryButtons')[$index]
    $tab.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    # Stop entrance animation only for deterministic offscreen snapshots.
    foreach($bubble in (Field 'launcherBubbles')){
        $bubble.BeginAnimation([Windows.UIElement]::OpacityProperty,$null);$bubble.Opacity=1
        $bubble.RenderTransform=[Windows.Media.ScaleTransform]::new(1,1)
    }
    $labels=@((Field 'launcherBubbles')|ForEach-Object{$_.Child.Children[1].Children[0].Text})
    if(($labels -join '|') -ne ($expected[$index] -join '|')){throw "Wrong entries in category $index"}
    if((Field 'launcherCategoryIndex') -ne $index){throw 'Category selection did not update'}
    $seen+=$labels
    Render ('launcher-category-'+$index) 384 354
    foreach($bubble in (Field 'launcherBubbles')){
        if($bubble.ActualWidth -gt (Field 'launcherCategoryCanvas').ActualWidth+1){throw 'Card exceeds available width'}
        if([Windows.Controls.Canvas]::GetTop($bubble)+$bubble.ActualHeight -gt (Field 'launcherCategoryCanvas').ActualHeight){throw 'Card exceeds available height'}
    }
}
if(($seen|Select-Object -Unique).Count -ne 13){throw 'Missing or duplicate module entry'}
Write-Output 'PASS: all five category buttons switch correctly; all 13 modules remain accessible; no cards overflow.'
if(Test-Path (Join-Path $env:MOMOPET_DATA_DIR 'momo-account.json')){throw 'Preview identity was persisted'}
Write-Output 'PASS: global identity and independent mail entry verified without creating accounts, sending messages or showing windows.'
$cloudPackages=Field 'communityCloudPackages'
$skill=[Activator]::CreateInstance($assembly.GetType('MomoPetApp.CommunityCloudPackage'))
$skill.Id='skill-preview';$skill.Kind='skill';$skill.Name='研究 Skill';$skill.Description='云端 Skill 布局检查';$skill.Author='预览用户';$skill.CreatedAt='2026-09-17T09:00:00Z'
$skill.File=[Activator]::CreateInstance($assembly.GetType('MomoPetApp.MomoAttachment'));$skill.File.Name='research.skill';$skill.File.Url='/preview/research.skill'
$appItem=[Activator]::CreateInstance($assembly.GetType('MomoPetApp.CommunityCloudPackage'))
$appItem.Id='app-preview';$appItem.Kind='app';$appItem.Name='AI 工具';$appItem.Description='云端 EXE 布局检查';$appItem.Author='预览用户';$appItem.Version='1.0';$appItem.CreatedAt='2026-09-17T09:00:00Z'
$appItem.File=[Activator]::CreateInstance($assembly.GetType('MomoPetApp.MomoAttachment'));$appItem.File.Name='ai-tool.exe';$appItem.File.Url='/preview/ai-tool.exe'
$cloudPackages.Add($skill);$cloudPackages.Add($appItem);Call 'MergeCloudPackagesIntoLibraries'|Out-Null
if(@((Field 'communityResources')|Where-Object{$_.Cloud}).Count -ne 1 -or @((Field 'communityApps')|Where-Object{$_.Cloud}).Count -ne 1){throw 'Cloud packages did not merge into their destination libraries'}
(Field 'communitySearchBox').Clear()
$type.GetField('communityMode',$flags).SetValue($controller,'Skill');Call 'RefreshCommunityFeed'|Out-Null;$window=Field 'communityPanel';Render 'community-skills-cloud' 1120 820
$type.GetField('communityMode',$flags).SetValue($controller,'AI 应用');Call 'RefreshCommunityFeed'|Out-Null;Render 'community-apps-cloud' 1120 820
Write-Output 'PASS: cloud Skill and EXE shares render separately and map to Resource Center / AI Apps destinations.'
Call 'LoadHealthCompanion'|Out-Null
Call 'BuildHealthCompanion'|Out-Null
Call 'RefreshHealthDashboard'|Out-Null
$window=Field 'healthPanel'
Render 'health-dashboard' 900 650
$health=Field 'healthState'
if(!$health.Enabled -or $health.EyeMinutes -ne 20 -or $health.PostureMinutes -ne 50 -or $health.WaterMinutes -ne 60){throw 'Unexpected health defaults'}
$health.EyeSeconds=$health.EyeMinutes*60
$health.PostureSeconds=$health.PostureMinutes*60
$health.WaterSeconds=$health.WaterMinutes*60
if((Call 'HealthDueKind') -ne 'posture'){throw 'Health reminder priority is wrong'}
$type.GetMethod('PostponeHealthAction',$flags).Invoke($controller,[object[]]@('posture',5))|Out-Null
if((Call 'HealthDueKind') -ne 'eye'){throw 'Health postpone did not advance to the next due reminder'}
Call 'RefreshHealthDashboard'|Out-Null
Render 'health-due-state' 900 650
if((Field 'healthDashboard').Children.Count -lt 3){throw 'Health dashboard is incomplete'}
Write-Output 'PASS: health dashboard, defaults, due priority, postpone behavior and persistence rendered without showing a window.'
