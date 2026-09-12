using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Input;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

namespace MomoPetApp
{
    public class PetMovementConfig
    {
        public int ExperienceVersion { get; set; }
        public string Mode { get; set; }
        public string BehaviorMode { get; set; }
        public string SkinId { get; set; }
        public bool AutoMove { get; set; }
        public bool WindowInteractions { get; set; }
        public bool TransparentClickThrough { get; set; }
        public bool GlobalHideHotkey { get; set; }
        public double AnchorX { get; set; }
        public double AnchorY { get; set; }
        public double RangeWidth { get; set; }
        public double RangeHeight { get; set; }
    }

    public partial class PetController
    {
        class ShelfState{public double Left,Top;}
        Window launcherPanel;
        Window shelfPeekPanel;
        StackPanel shelfEntries;
        readonly HashSet<Window> manuallyPlacedWindows=new HashSet<Window>();
        readonly List<Border> launcherBubbles=new List<Border>();
        Window movementSettingsPanel;
        ComboBox movementModeBox,behaviorModeBox;
        CheckBox movementAutoBox,windowInteractionBox,transparentClickThroughBox,globalHideHotkeyBox;
        TextBox movementWidthBox,movementHeightBox;
        TextBlock movementStatus;
        PetMovementConfig petMovement=new PetMovementConfig { ExperienceVersion=1,Mode="整个桌面",BehaviorMode="正常",SkinId="default",AutoMove=true,WindowInteractions=true,TransparentClickThrough=true,GlobalHideHotkey=true,RangeWidth=520,RangeHeight=300 };
        DispatcherTimer launcherClickTimer;
        DispatcherTimer shelfHoverOpenTimer,shelfHoverCloseTimer;
        readonly Dictionary<Window,ShelfState> shelvedWindows=new Dictionary<Window,ShelfState>();

        void ScheduleLauncherOpen()
        {
            CancelShelfPeekTimers();HideShelfPeek();
            if(launcherClickTimer==null){launcherClickTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(230)};launcherClickTimer.Tick+=delegate{launcherClickTimer.Stop();ToggleLauncherPanel();};}
            launcherClickTimer.Stop();launcherClickTimer.Start();
        }

        void CancelLauncherOpen(){if(launcherClickTimer!=null)launcherClickTimer.Stop();}

        void BuildLauncherPanel()
        {
            launcherPanel=new Window{Title="博道咪功能入口",Width=430,Height=210,MinWidth=360,MinHeight=176,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(launcherPanel);
            var canvas=new Canvas{Width=430,Height=210};launcherBubbles.Clear();
            AddLauncherBubble(canvas,"📝","工作记录",10,10,"记录事项、重复日程与提醒",delegate{TogglePanel();});
            AddLauncherBubble(canvas,"📦","中转袋",214,10,"搜索、预览与拖出各类文件",delegate{ToggleStashPanel();});
            AddLauncherBubble(canvas,"✨","AI 口袋",10,76,"AI 对话、图片生成与编辑",delegate{OpenAiPocketChat("");});
            AddLauncherBubble(canvas,"📈","大盘盯盘",214,76,"行情监控与收盘汇总",delegate{ToggleMarketPanel();});
            AddLauncherBubble(canvas,"🔎","Wind AI",10,142,"自然语言查询金融数据",delegate{ToggleAiSearchPanel();});
            AddLauncherBubble(canvas,"🛡","合规审核",214,142,"规则初筛、模型复核与留痕",delegate{OpenComplianceReview();});
            var close=new Border{Width=22,Height=22,CornerRadius=new CornerRadius(11),Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Cursor=Cursors.Hand,ToolTip="收起"};close.Child=new TextBlock{Text="×",FontFamily=new FontFamily("Segoe UI Symbol"),FontSize=13,Foreground=Ui.SubInk,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,-1,0,0)};close.MouseLeftButtonUp+=delegate{HideLauncherAndRestoreShelves();};Canvas.SetLeft(close,404);Canvas.SetTop(close,4);Panel.SetZIndex(close,10);canvas.Children.Add(close);
            // 入口本身也能调整大小，功能卡片按比例同步缩放，不留大片空白。
            var launcherView=new Viewbox{Stretch=Stretch.Uniform,Child=canvas};launcherPanel.Content=launcherView;
            launcherPanel.Deactivated+=delegate{HideLauncherAndRestoreShelves();};launcherPanel.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){HideLauncherAndRestoreShelves();e.Handled=true;}};launcherPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;HideLauncherAndRestoreShelves();}};
        }

        void BuildShelfPeekPanel()
        {
            shelfPeekPanel=new Window{Title="快捷气泡",Width=168,Height=48,MinWidth=0,MinHeight=0,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost,ShowActivated=false};Ui.StyleWindow(shelfPeekPanel);
            shelfEntries=new StackPanel{Margin=new Thickness(7,6,7,7)};
            shelfPeekPanel.Content=new ScrollViewer{Content=shelfEntries,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Brushes.Transparent};
            shelfPeekPanel.MouseEnter+=delegate{CancelShelfPeekClose();};shelfPeekPanel.MouseLeave+=delegate{ScheduleShelfPeekClose();};
            shelfPeekPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;shelfPeekPanel.Hide();}};
        }

        void ScheduleShelfPeekOpen()
        {
            if(petPointerPressed||petDragActive||ShelfPeekItemCount()==0||(launcherPanel!=null&&launcherPanel.IsVisible))return;
            CancelShelfPeekClose();
            if(shelfHoverOpenTimer==null){shelfHoverOpenTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};shelfHoverOpenTimer.Tick+=delegate{shelfHoverOpenTimer.Stop();ShowShelfPeek();};}
            shelfHoverOpenTimer.Stop();shelfHoverOpenTimer.Start();
        }

        void ScheduleShelfPeekClose()
        {
            if(shelfHoverOpenTimer!=null)shelfHoverOpenTimer.Stop();
            if(shelfHoverCloseTimer==null){shelfHoverCloseTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(360)};shelfHoverCloseTimer.Tick+=delegate{shelfHoverCloseTimer.Stop();if(!pet.IsMouseOver&&(shelfPeekPanel==null||!shelfPeekPanel.IsMouseOver))HideShelfPeek();};}
            shelfHoverCloseTimer.Stop();shelfHoverCloseTimer.Start();
        }

        void CancelShelfPeekClose(){if(shelfHoverCloseTimer!=null)shelfHoverCloseTimer.Stop();}
        void CancelShelfPeekTimers(){if(shelfHoverOpenTimer!=null)shelfHoverOpenTimer.Stop();CancelShelfPeekClose();}
        void HideShelfPeek(){if(shelfPeekPanel!=null&&shelfPeekPanel.IsVisible)shelfPeekPanel.Hide();}

        void ShowShelfPeek()
        {
            if(ShelfPeekItemCount()==0||petPointerPressed||petDragActive||!pet.IsMouseOver||globalPetHidden||(launcherPanel!=null&&launcherPanel.IsVisible))return;
            if(shelfPeekPanel==null)BuildShelfPeekPanel();RefreshShelfEntries();PositionShelfPeekPanel();shelfPeekPanel.Show();
        }

        void PositionShelfPeekPanel()
        {
            if(shelfPeekPanel==null||pet==null)return;var work=SystemParameters.WorkArea;double left;
            if(edgeHidden)left=edgeHideSide=="left"?pet.Left+pet.Width-5:pet.Left-shelfPeekPanel.Width+5;
            else{left=pet.Left+pet.Width+8;if(left+shelfPeekPanel.Width>work.Right)left=pet.Left-shelfPeekPanel.Width-8;}
            shelfPeekPanel.Left=Math.Max(work.Left+4,Math.Min(left,work.Right-shelfPeekPanel.Width-4));shelfPeekPanel.Top=Math.Max(work.Top+4,Math.Min(pet.Top+pet.Height*.35-shelfPeekPanel.Height*.5,work.Bottom-shelfPeekPanel.Height-4));
        }

        void AddLauncherBubble(Canvas canvas,string icon,string label,double left,double top,string tooltip,Action action)
        {
            var content=new Grid{Margin=new Thickness(14,0,12,0)};content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});content.ColumnDefinitions.Add(new ColumnDefinition());var iconText=new TextBlock{Text=icon,FontSize=20,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};content.Children.Add(iconText);var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};copy.Children.Add(new TextBlock{Text=label,FontSize=12.5,FontWeight=FontWeights.Bold,Foreground=Ui.Ink});copy.Children.Add(new TextBlock{Text=tooltip,FontSize=9.5,Foreground=Ui.SubInk,Margin=new Thickness(0,2,0,0)});Grid.SetColumn(copy,1);content.Children.Add(copy);
            var bubble=new Border{Width=188,Height=58,CornerRadius=new CornerRadius(13),Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Child=content,Cursor=Cursors.Hand,ToolTip=tooltip,RenderTransformOrigin=new Point(.5,.5),Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=12,ShadowDepth=2,Opacity=.07,RenderingBias=RenderingBias.Performance}};var scale=new ScaleTransform(1,1);bubble.RenderTransform=scale;
            bubble.MouseEnter+=delegate{scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1.018,TimeSpan.FromMilliseconds(110)));scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1.018,TimeSpan.FromMilliseconds(110)));bubble.BorderBrush=Ui.Accent;};
            bubble.MouseLeave+=delegate{scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(180)));scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(180)));bubble.BorderBrush=Ui.Line;};
            bubble.MouseLeftButtonUp+=delegate{HideLauncherAndRestoreShelves();action();};Canvas.SetLeft(bubble,left);Canvas.SetTop(bubble,top);canvas.Children.Add(bubble);launcherBubbles.Add(bubble);
        }

        void PositionLauncherPanel()
        {
            if(launcherPanel==null||pet==null)return;var work=SystemParameters.WorkArea;
            double left,top;
            if(edgeHidden){
                // 侧边待命时，入口朝屏幕内部展开，不盖住探头，也不会跑到屏幕外。
                left=edgeHideSide=="left"?pet.Left+pet.Width-10:pet.Left-launcherPanel.Width+10;
                top=pet.Top+pet.Height*.5-launcherPanel.Height*.5;
            }else{
                left=pet.Left+pet.Width/2-launcherPanel.Width/2;top=pet.Top-launcherPanel.Height+52;if(top<work.Top+4)top=pet.Top+pet.Height-24;
            }
            launcherPanel.Left=Math.Max(work.Left+4,Math.Min(left,work.Right-launcherPanel.Width-4));launcherPanel.Top=Math.Max(work.Top+4,Math.Min(top,work.Bottom-launcherPanel.Height-4));
        }

        void AnimateLauncherBubbles()
        {
            for(int i=0;i<launcherBubbles.Count;i++){Border bubble=launcherBubbles[i];bubble.Opacity=0;var fade=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(125)){BeginTime=TimeSpan.FromMilliseconds(i*18),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};bubble.BeginAnimation(UIElement.OpacityProperty,fade);var scale=bubble.RenderTransform as ScaleTransform;if(scale!=null){scale.ScaleX=.96;scale.ScaleY=.96;var grow=new DoubleAnimation(.96,1,TimeSpan.FromMilliseconds(150)){BeginTime=TimeSpan.FromMilliseconds(i*18),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};scale.BeginAnimation(ScaleTransform.ScaleXProperty,grow);scale.BeginAnimation(ScaleTransform.ScaleYProperty,grow);}}
        }

        void ToggleLauncherPanel()
        {
            HideShelfPeek();if(launcherPanel==null)BuildLauncherPanel();if(shelvedWindows.ContainsKey(launcherPanel))RestoreWindow(launcherPanel);if(launcherPanel.IsVisible)HideLauncherAndRestoreShelves();else{HideShelvedTagsForLauncher();PositionLauncherPanel();launcherPanel.Show();launcherPanel.Activate();AnimateLauncherBubbles();if(!edgeHidden)React("选一个泡泡吧～",false);}
        }

        void HideShelvedTagsForLauncher(){foreach(Window window in shelvedWindows.Keys.ToList())if(window!=launcherPanel)window.Hide();}
        void HideLauncherAndRestoreShelves(){if(launcherPanel!=null&&launcherPanel.IsVisible)launcherPanel.Hide();}

        int ShelfPeekItemCount(){bool stashShelved=stashPanel!=null&&shelvedWindows.ContainsKey(stashPanel);return shelvedWindows.Count+(stashItems.Count>0&&!stashShelved?1:0);}

        void AddShelfPeekBubble(string label,string tooltip,Action action,bool featured)
        {
            var text=new TextBlock{Text=label,Foreground=featured?Ui.AccentDeep:Ui.Ink,FontSize=12.5,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,14,0)};
            var bubble=new Border{Height=36,MinWidth=112,CornerRadius=new CornerRadius(18),Background=featured?Ui.AccentSoft:Ui.Card,BorderBrush=featured?Ui.Accent:Ui.Line,BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,6),Child=text,Cursor=Cursors.Hand,ToolTip=tooltip,Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=12,ShadowDepth=2,Opacity=.10,RenderingBias=RenderingBias.Performance}};
            bubble.MouseEnter+=delegate{CancelShelfPeekClose();bubble.BorderBrush=Ui.Accent;text.Foreground=Ui.AccentDeep;};
            bubble.MouseLeave+=delegate{bubble.BorderBrush=featured?Ui.Accent:Ui.Line;text.Foreground=featured?Ui.AccentDeep:Ui.Ink;};
            bubble.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){HideShelfPeek();action();e.Handled=true;};shelfEntries.Children.Add(bubble);
        }

        void RefreshShelfEntries()
        {
            if(shelfEntries==null)return;shelfEntries.Children.Clear();
            if(stashItems.Count>0||(stashPanel!=null&&shelvedWindows.ContainsKey(stashPanel)))AddShelfPeekBubble("中转  "+stashItems.Count,"打开中转袋",delegate{if(stashPanel!=null&&shelvedWindows.ContainsKey(stashPanel))RestoreWindow(stashPanel);else ToggleStashPanel();},true);
            foreach(Window saved in shelvedWindows.Keys.ToList()){
                if(saved==stashPanel)continue;Window target=saved;AddShelfPeekBubble(ShelfLabel(target),"恢复 "+target.Title,delegate{RestoreWindow(target);},false);
            }
            if(shelfPeekPanel!=null)shelfPeekPanel.Height=Math.Min(SystemParameters.WorkArea.Height-16,13+Math.Max(1,Math.Min(7,ShelfPeekItemCount()))*42);
        }

        void ShowPocketModelSettings()
        {
            if(imageEditorPanel==null)BuildImageEditor();
            // 右键设置可以独立打开，不能让一个隐藏的 AI 图片窗口充当锚点或所有者。
            if(imageEditorPanel.IsVisible)PositionImageEditor();
            ShowImageAiSettings();
        }

        void ShowWindAiModelSettings()
        {
            if(aiSearchPanel==null)BuildAiSearchPanel();
            if(aiSearchPanel.IsVisible)PositionAiPanel();
            ShowAiSettings();
        }

        string MovementSettingsFile(){return System.IO.Path.Combine(dataDir,"pet-movement.json");}

        void LoadPetMovementSettings()
        {
            try{string path=MovementSettingsFile();if(System.IO.File.Exists(path))petMovement=json.Deserialize<PetMovementConfig>(System.IO.File.ReadAllText(path,System.Text.Encoding.UTF8));}catch{petMovement=null;}
            if(petMovement==null)petMovement=new PetMovementConfig{ExperienceVersion=1,Mode="整个桌面",BehaviorMode="正常",SkinId="default",AutoMove=true,WindowInteractions=true,TransparentClickThrough=true,GlobalHideHotkey=true,RangeWidth=520,RangeHeight=300};if(petMovement.ExperienceVersion<1){petMovement.ExperienceVersion=1;petMovement.BehaviorMode="正常";petMovement.WindowInteractions=true;petMovement.TransparentClickThrough=true;petMovement.GlobalHideHotkey=true;}if(String.IsNullOrWhiteSpace(petMovement.Mode))petMovement.Mode="整个桌面";if(String.IsNullOrWhiteSpace(petMovement.BehaviorMode))petMovement.BehaviorMode="正常";if(String.IsNullOrWhiteSpace(petMovement.SkinId))petMovement.SkinId="default";if(petMovement.RangeWidth<220)petMovement.RangeWidth=520;if(petMovement.RangeHeight<180)petMovement.RangeHeight=300;
        }

        void SavePetMovementSettings()
        {
            try{System.IO.File.WriteAllText(MovementSettingsFile(),json.Serialize(petMovement),System.Text.Encoding.UTF8);}catch{}
        }

        Rect PetMovementBounds()
        {
            Rect work=SystemParameters.WorkArea;string mode=petMovement==null?"整个桌面":petMovement.Mode;
            if(mode=="左半屏")return new Rect(work.Left,work.Top,work.Width/2,work.Height);
            if(mode=="右半屏")return new Rect(work.Left+work.Width/2,work.Top,work.Width/2,work.Height);
            if(mode=="桌面底部")return new Rect(work.Left,work.Top+work.Height*.58,work.Width,work.Height*.42);
            if(mode=="当前位置附近"){
                double width=Math.Min(work.Width,Math.Max(220,petMovement.RangeWidth)),height=Math.Min(work.Height,Math.Max(180,petMovement.RangeHeight));double centerX=petMovement.AnchorX,centerY=petMovement.AnchorY;
                if(centerX==0&&pet!=null)centerX=pet.Left+pet.Width/2;if(centerY==0&&pet!=null)centerY=pet.Top+pet.Height/2;
                double left=Math.Max(work.Left,Math.Min(centerX-width/2,work.Right-width)),top=Math.Max(work.Top,Math.Min(centerY-height/2,work.Bottom-height));return new Rect(left,top,width,height);
            }
            if(mode=="固定当前位置"){
                double left=petMovement.AnchorX,top=petMovement.AnchorY;if(left==0&&pet!=null)left=pet.Left;if(top==0&&pet!=null)top=pet.Top;left=Math.Max(work.Left,Math.Min(left,work.Right-(pet==null?142:pet.Width)));top=Math.Max(work.Top,Math.Min(top,work.Bottom-(pet==null?154:pet.Height)));return new Rect(left,top,pet==null?142:pet.Width,pet==null?154:pet.Height);
            }
            return work;
        }

        bool PetCanAutoRoam(){return petMovement!=null&&petMovement.AutoMove&&petMovement.Mode!="固定当前位置"&&petMovement.BehaviorMode!="专注";}

        void ClampPetToMovementRange()
        {
            if(pet==null)return;Rect bounds=PetMovementBounds();double maxLeft=Math.Max(bounds.Left,bounds.Right-pet.Width),maxTop=Math.Max(bounds.Top,bounds.Bottom-pet.Height);pet.Left=Math.Max(bounds.Left,Math.Min(pet.Left,maxLeft));pet.Top=Math.Max(bounds.Top,Math.Min(pet.Top,maxTop));exactLeft=pet.Left;hopBaseTop=pet.Top;
        }

        void OpenMovementSettings()
        {
            if(movementSettingsPanel==null)BuildMovementSettingsPanel();RestoreShelvedIfNeeded(movementSettingsPanel);movementModeBox.SelectedItem=petMovement.Mode;behaviorModeBox.SelectedItem=petMovement.BehaviorMode;movementAutoBox.IsChecked=petMovement.AutoMove;windowInteractionBox.IsChecked=petMovement.WindowInteractions;transparentClickThroughBox.IsChecked=petMovement.TransparentClickThrough;globalHideHotkeyBox.IsChecked=petMovement.GlobalHideHotkey;movementWidthBox.Text=Math.Round(petMovement.RangeWidth).ToString();movementHeightBox.Text=Math.Round(petMovement.RangeHeight).ToString();movementStatus.Text=MovementDescription();var work=SystemParameters.WorkArea;movementSettingsPanel.Left=Math.Max(work.Left+8,Math.Min(pet.Left-movementSettingsPanel.Width-12,work.Right-movementSettingsPanel.Width-8));movementSettingsPanel.Top=Math.Max(work.Top+8,Math.Min(pet.Top,work.Bottom-movementSettingsPanel.Height-8));movementSettingsPanel.Show();movementSettingsPanel.Activate();
        }

        string MovementDescription()
        {
            string behavior=String.IsNullOrWhiteSpace(petMovement.BehaviorMode)?"正常":petMovement.BehaviorMode;if(petMovement.Mode=="当前位置附近")return "博道咪只在以当前点为中心的 "+Math.Round(petMovement.RangeWidth)+" × "+Math.Round(petMovement.RangeHeight)+" 区域活动 · "+behavior+"模式";if(petMovement.Mode=="固定当前位置")return "固定位置 · "+behavior+"模式，仍可手动拖到新位置";return "当前范围："+petMovement.Mode+" · "+behavior+"模式"+(petMovement.AutoMove?" · 允许自动走动":" · 不自动走动");
        }

        void BuildMovementSettingsPanel()
        {
            movementSettingsPanel=new Window{Title="博道咪活动范围",Width=470,Height=690,MinWidth=420,MinHeight=570,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(movementSettingsPanel);
            var outer=new Border{CornerRadius=new CornerRadius(16),Padding=new Thickness(18)};Ui.StyleCard(outer);var stack=new StackPanel();var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(Ui.Title("🐾  活动范围与移动",19));var close=Ui.MakeCloseButton();close.Click+=delegate{movementSettingsPanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);stack.Children.Add(header);AddShelfControl(movementSettingsPanel,header,close);EnableWindowInteraction(movementSettingsPanel,header);
            var subtitle=Ui.Subtitle("限制博道咪出现的位置，避免它跑到工作区域");subtitle.Margin=new Thickness(0,3,0,16);stack.Children.Add(subtitle);stack.Children.Add(new TextBlock{Text="允许出现与活动的区域",FontWeight=FontWeights.Bold});
            movementModeBox=new ComboBox{Height=38,Margin=new Thickness(0,6,0,12),Padding=new Thickness(10,6,10,6),ItemsSource=new[]{"整个桌面","左半屏","右半屏","桌面底部","当前位置附近","固定当前位置"}};stack.Children.Add(movementModeBox);
            movementAutoBox=new CheckBox{Content="允许博道咪自己走动和跑动",Margin=new Thickness(2,0,0,14)};stack.Children.Add(movementAutoBox);
            stack.Children.Add(new TextBlock{Text="性格与工作状态",FontWeight=FontWeights.Bold});
            behaviorModeBox=new ComboBox{Height=38,Margin=new Thickness(0,6,0,8),Padding=new Thickness(10,6,10,6),ItemsSource=new[]{"安静","正常","活泼","专注"}};stack.Children.Add(behaviorModeBox);
            stack.Children.Add(new TextBlock{Text="安静会减少移动；活泼更爱探索窗边；专注只安静陪伴",Foreground=Ui.SubInk,FontSize=11.5,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,0,12)});
            windowInteractionBox=new CheckBox{Content="感知窗口边缘、窗顶和任务栏",Margin=new Thickness(2,0,0,8)};stack.Children.Add(windowInteractionBox);
            transparentClickThroughBox=new CheckBox{Content="透明区域允许鼠标穿透",Margin=new Thickness(2,0,0,8)};stack.Children.Add(transparentClickThroughBox);
            globalHideHotkeyBox=new CheckBox{Content="启用博道咪全局快捷键",Margin=new Thickness(2,0,0,8)};stack.Children.Add(globalHideHotkeyBox);
            var shortcutCard=new Border{Background=Ui.Neutral,CornerRadius=new CornerRadius(10),Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,0,0,14)};shortcutCard.Child=new TextBlock{Text="Ctrl + Alt + S  暂存剪贴板并选择处理方式\nCtrl + Alt + M  功能入口   ·   Ctrl + Alt + N  工作记录\nCtrl + Alt + V  中转袋      ·   Ctrl + Alt + A  AI 口袋\nCtrl + Alt + P  盯盘        ·   Ctrl + Alt + W  Wind AI\nCtrl + Alt + C  合规审核    ·   Ctrl + Alt + B  隐藏 / 恢复",Foreground=Ui.SubInk,FontSize=11.5,LineHeight=19,TextWrapping=TextWrapping.Wrap};stack.Children.Add(shortcutCard);
            stack.Children.Add(new TextBlock{Text="“当前位置附近”的活动尺寸",FontWeight=FontWeights.Bold});var sizeRow=new Grid{Margin=new Thickness(0,6,0,12)};sizeRow.ColumnDefinitions.Add(new ColumnDefinition());sizeRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});sizeRow.ColumnDefinitions.Add(new ColumnDefinition());movementWidthBox=new TextBox{Height=36,Padding=new Thickness(9,6,9,6),ToolTip="活动宽度"};movementHeightBox=new TextBox{Height=36,Padding=new Thickness(9,6,9,6),ToolTip="活动高度"};sizeRow.Children.Add(movementWidthBox);var multiply=new TextBlock{Text="×",Margin=new Thickness(10,0,10,0),VerticalAlignment=VerticalAlignment.Center,Foreground=Ui.SubInk};Grid.SetColumn(multiply,1);sizeRow.Children.Add(multiply);Grid.SetColumn(movementHeightBox,2);sizeRow.Children.Add(movementHeightBox);stack.Children.Add(sizeRow);
            movementStatus=new TextBlock{Foreground=Ui.SubInk,FontSize=11.5,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};stack.Children.Add(movementStatus);var actions=new WrapPanel();var save=MakeButton("保存并立即应用",Ui.Accent);save.Foreground=Brushes.White;save.Click+=delegate{SaveMovementSettingsFromPanel();};var cancel=MakeButton("取消",Ui.Neutral);cancel.Click+=delegate{movementSettingsPanel.Hide();};actions.Children.Add(save);actions.Children.Add(cancel);stack.Children.Add(actions);outer.Child=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};movementSettingsPanel.Content=outer;movementSettingsPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;movementSettingsPanel.Hide();}};
        }

        void SaveMovementSettingsFromPanel()
        {
            string mode=Convert.ToString(movementModeBox.SelectedItem);if(String.IsNullOrWhiteSpace(mode))mode="整个桌面";string behavior=Convert.ToString(behaviorModeBox.SelectedItem);if(String.IsNullOrWhiteSpace(behavior))behavior="正常";double width,height;if(!Double.TryParse(movementWidthBox.Text,out width)||width<220)width=520;if(!Double.TryParse(movementHeightBox.Text,out height)||height<180)height=300;petMovement.Mode=mode;petMovement.BehaviorMode=behavior;petMovement.AutoMove=movementAutoBox.IsChecked==true;petMovement.WindowInteractions=windowInteractionBox.IsChecked==true;petMovement.TransparentClickThrough=transparentClickThroughBox.IsChecked==true;petMovement.GlobalHideHotkey=globalHideHotkeyBox.IsChecked==true;petMovement.RangeWidth=width;petMovement.RangeHeight=height;
            if(mode=="当前位置附近"){petMovement.AnchorX=pet.Left+pet.Width/2;petMovement.AnchorY=pet.Top+pet.Height/2;}else if(mode=="固定当前位置"){petMovement.AnchorX=pet.Left;petMovement.AnchorY=pet.Top;petMovement.AutoMove=false;}
            SavePetMovementSettings();ApplyPetExperienceSettings();ClampPetToMovementRange();movementStatus.Text=MovementDescription();movementSettingsPanel.Hide();React(behavior=="专注"?"专注模式，我会安静陪着你～":(mode=="固定当前位置"?"我就在这里待着～":"活动方式记住啦～"),true);
        }

        void EnableWindowInteraction(Window window,UIElement dragHandle)
        {
            if(window==null||dragHandle==null)return;bool pressed=false,moving=false,resizing=false,resizeHover=false;Point pressPoint=new Point(),dragScreenStart=new Point(),resizeScreenStart=new Point();double leftStart=0,topStart=0,widthStart=0,heightStart=0;int resizeEdge=0;
            Border movingBorder=null;Effect movingEffect=null;bool motionOptimized=false;
            Action beginMotionOptimization=delegate{
                if(motionOptimized)return;motionOptimized=true;
                // 透明无边框窗口的大面积模糊阴影在移动/缩放时会反复重绘。交互期间暂时关闭，结束后原样恢复。
                movingBorder=window.Content as Border;if(movingBorder!=null){movingEffect=movingBorder.Effect;movingBorder.Effect=null;}
            };
            Action endMotionOptimization=delegate{
                if(!motionOptimized)return;motionOptimized=false;
                if(movingBorder!=null)movingBorder.Effect=movingEffect;movingBorder=null;movingEffect=null;
            };
            Func<Point,Point> screenDip=delegate(Point local){
                Point device=window.PointToScreen(local);var source=PresentationSource.FromVisual(window);
                return source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformFromDevice.Transform(device):device;
            };
            Func<Point,int> edgeAt=delegate(Point point){const double edge=9;int value=0;if(point.X<=edge)value|=1;else if(point.X>=window.ActualWidth-edge)value|=2;if(point.Y<=edge)value|=4;else if(point.Y>=window.ActualHeight-edge)value|=8;return value;};
            Action<int> setResizeCursor=delegate(int edge){if((edge&3)!=0&&(edge&12)!=0)window.Cursor=((edge==5)||(edge==10))?Cursors.SizeNWSE:Cursors.SizeNESW;else if((edge&3)!=0)window.Cursor=Cursors.SizeWE;else if((edge&12)!=0)window.Cursor=Cursors.SizeNS;};
            // 无边框窗口不会自动提供缩放命中区；在四边和四角显式实现，使用屏幕坐标避免移动左/上边时漂移。
            window.PreviewMouseMove+=delegate(object sender,MouseEventArgs e){
                if(resizing){Point screen=window.PointToScreen(e.GetPosition(window));var source=PresentationSource.FromVisual(window);if(source==null||source.CompositionTarget==null)return;Point current=source.CompositionTarget.TransformFromDevice.Transform(screen);double dx=current.X-resizeScreenStart.X,dy=current.Y-resizeScreenStart.Y,newLeft=leftStart,newTop=topStart,newWidth=widthStart,newHeight=heightStart;
                    if((resizeEdge&1)!=0){newWidth=widthStart-dx;newLeft=leftStart+dx;}if((resizeEdge&2)!=0)newWidth=widthStart+dx;if((resizeEdge&4)!=0){newHeight=heightStart-dy;newTop=topStart+dy;}if((resizeEdge&8)!=0)newHeight=heightStart+dy;
                    double minWidth=Math.Max(120,window.MinWidth),minHeight=Math.Max(90,window.MinHeight);if(newWidth<minWidth){if((resizeEdge&1)!=0)newLeft=leftStart+widthStart-minWidth;newWidth=minWidth;}if(newHeight<minHeight){if((resizeEdge&4)!=0)newTop=topStart+heightStart-minHeight;newHeight=minHeight;}
                    window.Left=newLeft;window.Top=newTop;window.Width=newWidth;window.Height=newHeight;e.Handled=true;return;
                }int hoverEdge=edgeAt(e.GetPosition(window));if(hoverEdge!=0){resizeHover=true;setResizeCursor(hoverEdge);}else if(resizeHover){resizeHover=false;window.Cursor=null;}};
            window.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(shelvedWindows.ContainsKey(window)||window.WindowState==WindowState.Maximized)return;int edge=edgeAt(e.GetPosition(window));if(edge==0)return;resizeEdge=edge;resizing=true;resizeHover=true;leftStart=window.Left;topStart=window.Top;widthStart=window.ActualWidth;heightStart=window.ActualHeight;Point screen=window.PointToScreen(e.GetPosition(window));var source=PresentationSource.FromVisual(window);resizeScreenStart=source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformFromDevice.Transform(screen):e.GetPosition(window);beginMotionOptimization();window.CaptureMouse();e.Handled=true;};
            window.PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){if(!resizing)return;resizing=false;resizeEdge=0;if(window.IsMouseCaptured)window.ReleaseMouseCapture();window.Cursor=null;resizeHover=false;endMotionOptimization();e.Handled=true;};
            window.LostMouseCapture+=delegate{resizing=false;resizeEdge=0;resizeHover=false;window.Cursor=null;endMotionOptimization();};
            dragHandle.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ChangedButton!=MouseButton.Left||e.ClickCount!=1||shelvedWindows.ContainsKey(window)||window.WindowState==WindowState.Maximized)return;pressed=true;moving=false;pressPoint=e.GetPosition(window);dragScreenStart=screenDip(pressPoint);leftStart=window.Left;topStart=window.Top;dragHandle.CaptureMouse();e.Handled=true;};
            dragHandle.MouseMove+=delegate(object sender,MouseEventArgs e){
                if(!pressed)return;if(e.LeftButton!=MouseButtonState.Pressed){pressed=false;moving=false;if(dragHandle.IsMouseCaptured)dragHandle.ReleaseMouseCapture();endMotionOptimization();return;}
                Point current=screenDip(e.GetPosition(window));double dx=current.X-dragScreenStart.X,dy=current.Y-dragScreenStart.Y;
                if(!moving){if(dx*dx+dy*dy<64){e.Handled=true;return;}moving=true;manuallyPlacedWindows.Add(window);beginMotionOptimization();}
                // 全程只采用同一组屏幕坐标和起始位置，窗口会与鼠标一比一移动，
                // 避免“先手动挪一次、再进入 DragMove”造成的二次偏移和系统拖动卡顿。
                window.Left=leftStart+dx;window.Top=topStart+dy;e.Handled=true;
            };
            dragHandle.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){if(!pressed)return;pressed=false;moving=false;if(dragHandle.IsMouseCaptured)dragHandle.ReleaseMouseCapture();endMotionOptimization();e.Handled=true;};
            dragHandle.LostMouseCapture+=delegate{pressed=false;moving=false;endMotionOptimization();};
        }

        void AddShelfControl(Window window,Grid header,Button closeButton)
        {
            window.Closed+=delegate{shelvedWindows.Remove(window);manuallyPlacedWindows.Remove(window);RefreshShelfEntries();};
            TrackOfficeWindow(window);int column=Grid.GetColumn(closeButton);header.Children.Remove(closeButton);var actions=new WrapPanel();
            // 标准标题栏顺序：最小化、最大化/还原、关闭。双矩形不用依赖易缺字的符号字体。
            var resize=MakeButton("",Brushes.Transparent);resize.Width=32;resize.Height=32;resize.Padding=new Thickness(0);resize.ToolTip="最大化 / 还原窗口；也可拖拽窗口四边或四角调整大小";
            var resizeIcon=new Grid{Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var rear=new Border{Width=10,Height=10,BorderBrush=Ui.Ink,BorderThickness=new Thickness(1.5),CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top};
            var front=new Border{Width=10,Height=10,Background=Ui.Card,BorderBrush=Ui.Ink,BorderThickness=new Thickness(1.5),CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};
            resizeIcon.Children.Add(rear);resizeIcon.Children.Add(front);resize.Content=resizeIcon;resize.Click+=delegate{window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;};
            var shelf=MakeButton("—",Brushes.Transparent);shelf.Width=32;shelf.FontSize=17;shelf.ToolTip="收纳到小猫的功能面板";shelf.Click+=delegate{ShelfWindow(window);};actions.Children.Add(shelf);actions.Children.Add(resize);actions.Children.Add(closeButton);Grid.SetColumn(actions,column);header.Children.Add(actions);
        }

        void ShelfWindow(Window window)
        {
            if(window==null||shelvedWindows.ContainsKey(window))return;
            shelvedWindows[window]=new ShelfState{Left=window.Left,Top=window.Top};
            window.Hide();RefreshShelfEntries();if(pet!=null&&pet.IsMouseOver)ScheduleShelfPeekOpen();
        }

        string ShelfLabel(Window window)
        {
            if(window==stashPanel)return "中转 "+stashItems.Count;if(window==panel)return "工作簿";if(window==launcherPanel)return "功能";if(window==marketPanel)return "盯盘";if(window==aiSearchPanel)return "Wind AI";if(window==imageEditorPanel)return "AI 口袋";if(window==movementSettingsPanel)return "活动范围";if(window==aiSettingsPanel||window==imageAiSettingsPanel)return "模型设置";return String.IsNullOrWhiteSpace(window.Title)?"临时窗口":window.Title.Replace("博道咪","").Trim();
        }

        void RestoreWindow(Window window)
        {
            ShelfState state;if(!shelvedWindows.TryGetValue(window,out state))return;shelvedWindows.Remove(window);
            var work=SystemParameters.WorkArea;
            if(window.WindowState==WindowState.Normal){window.Left=Math.Max(work.Left,Math.Min(state.Left,work.Right-window.Width));window.Top=Math.Max(work.Top,Math.Min(state.Top,work.Bottom-window.Height));}
            if(window==panel&&!manuallyPlacedWindows.Contains(window))PositionPanel();
            RefreshShelfEntries();if(ShelfPeekItemCount()==0)HideShelfPeek();if(!window.IsVisible)window.Show();window.Activate();
        }

        bool RestoreShelvedIfNeeded(Window window)
        {
            if(window==null||!shelvedWindows.ContainsKey(window))return false;RestoreWindow(window);return true;
        }

        void RepositionShelves()
        {
            // 收纳窗口保持隐藏，不再生成或定位独立标签。
        }

        List<string> ModelNamesFromResponse(Dictionary<string,object> response)
        {
            var result=new List<string>();object raw;if(response==null||!response.TryGetValue("models",out raw)||raw==null)return result;var enumerable=raw as IEnumerable;if(enumerable==null||raw is string)return result;
            foreach(object item in enumerable){string name=item as string;var map=item as Dictionary<string,object>;if(map!=null){object value;if(map.TryGetValue("id",out value)||map.TryGetValue("name",out value))name=Convert.ToString(value);}if(!String.IsNullOrWhiteSpace(name))result.Add(name.Trim());}
            return result.Distinct().OrderBy(x=>x).ToList();
        }

        void EnableModelSearch(ComboBox combo,Func<IEnumerable<string>> modelProvider)
        {
            if(combo==null||modelProvider==null)return;combo.IsEditable=true;combo.IsTextSearchEnabled=false;combo.StaysOpenOnEdit=true;bool updating=false;
            combo.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler(delegate(object sender,TextChangedEventArgs e){
                if(updating||!combo.IsKeyboardFocusWithin)return;string query=combo.Text??"";var all=(modelProvider()??Enumerable.Empty<string>()).Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct().ToList();var filtered=String.IsNullOrWhiteSpace(query)?all:all.Where(x=>x.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToList();
                updating=true;combo.ItemsSource=filtered;combo.Text=query;combo.ApplyTemplate();var editor=combo.Template.FindName("PART_EditableTextBox",combo) as TextBox;if(editor!=null){editor.Text=query;editor.CaretIndex=query.Length;}combo.IsDropDownOpen=filtered.Count>0;updating=false;
            }),true);
        }
    }
}
