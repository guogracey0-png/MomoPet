using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MomoPetApp
{
    public partial class PetController
    {
        const int WM_NCHITTEST=0x0084,WM_HOTKEY=0x0312,HTTRANSPARENT=-1;
        const int HotkeyHide=0xB0D0,HotkeyLauncher=0xB0D1,HotkeyNotes=0xB0D2,HotkeyStash=0xB0D3,HotkeyAi=0xB0D4,HotkeyMarket=0xB0D5,HotkeyWind=0xB0D6,HotkeyCompliance=0xB0D7,HotkeyCapture=0xB0D8,ModAlt=0x0001,ModControl=0x0002;
        const double SpriteFootOffset=148.0;

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect{public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]
        struct PetMonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
        [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool GetMonitorInfo(IntPtr monitor,ref PetMonitorInfo info);
        delegate bool EnumWindowsProc(IntPtr hwnd,IntPtr lParam);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindowsProc callback,IntPtr lParam);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder className,int maxCount);
        [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr hwnd,int id,int modifiers,int virtualKey);
        [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);

        sealed class AlphaMask
        {
            public int Width,Height;
            public byte[] Alpha;
        }

        HwndSource petHwndSource;
        DispatcherTimer desktopSurfaceTimer;
        bool desktopSurfaceScanInFlight;
        readonly List<Rect> desktopSurfaces=new List<Rect>();
        readonly Dictionary<ImageSource,AlphaMask> petAlphaMasks=new Dictionary<ImageSource,AlphaMask>();
        readonly List<Window> globallyHiddenWindows=new List<Window>();
        bool globalPetHidden;
        readonly HashSet<int> registeredPetHotkeys=new HashSet<int>();
        bool hasActiveSurface;
        Rect activeSurface,climbSurface;
        double climbStartLeft,climbStartTop,climbTargetLeft,climbTargetTop;
        double dropStartTop,dropTargetTop;
        DateTime lastDragVisualSample=DateTime.MinValue;
        double lastDragVisualLeft,lastDragVisualTop;
        bool petPointerPressed,petPointerDragging,petPointerStartedAtEdge;
        Point petPointerStartScreen;
        double petPointerStartLeft,petPointerStartTop;
        int petPointerClickCount;
        string petPointerHitRegion,petPointerEdgeSide;

        void InitializePetExperience()
        {
            if(pet==null)return;
            pet.SourceInitialized+=delegate{
                petHwndSource=PresentationSource.FromVisual(pet) as HwndSource;
                if(petHwndSource!=null)petHwndSource.AddHook(PetWindowHook);
                ApplyPetExperienceSettings();
            };
            desktopSurfaceTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};
            desktopSurfaceTimer.Tick+=delegate{
                CheckPresentationQuiet();RefreshDesktopSurfaces();
                // 枚举桌面窗口相对昂贵：活动时保持及时，待机时降低到每 2.4 秒一次。
                bool active=petDragActive||petState=="walk"||petState=="run"||petState=="climb"||petState=="drop";
                TimeSpan next=TimeSpan.FromMilliseconds(active?650:2400);
                if(desktopSurfaceTimer.Interval!=next)desktopSurfaceTimer.Interval=next;
            };
            desktopSurfaceTimer.Start();
        }

        void ShutdownPetExperience()
        {
            if(desktopSurfaceTimer!=null)desktopSurfaceTimer.Stop();
            if(petHwndSource!=null){UnregisterPetHotkeys();petHwndSource.RemoveHook(PetWindowHook);}petHwndSource=null;
        }

        void ApplyPetExperienceSettings()
        {
            if(petMovement==null)return;
            if(petHwndSource!=null){
                UnregisterPetHotkeys();if(petMovement.GlobalHideHotkey)RegisterPetHotkeys();
            }
            if(petMovement.WindowInteractions)RefreshDesktopSurfaces();else{desktopSurfaces.Clear();hasActiveSurface=false;}
            if(petMovement.BehaviorMode=="专注"&&!edgeHidden){StartState("sleep",12);}
        }

        IntPtr PetWindowHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(message==WM_HOTKEY&&registeredPetHotkeys.Contains(wParam.ToInt32())){handled=true;HandlePetHotkey(wParam.ToInt32());return IntPtr.Zero;}
            if(message==WM_NCHITTEST&&petMovement!=null&&petMovement.TransparentClickThrough&&!petDragActive&&!petPointerPressed&&!globalPetHidden){
                int packed=unchecked((int)lParam.ToInt64());short sx=(short)(packed&0xffff),sy=(short)((packed>>16)&0xffff);
                if(!IsInteractivePetPixel(new Point(sx,sy))){handled=true;return new IntPtr(HTTRANSPARENT);}
            }
            return IntPtr.Zero;
        }

        void RegisterPetHotkey(int id,Key key)
        {
            if(petHwndSource!=null&&RegisterHotKey(petHwndSource.Handle,id,ModControl|ModAlt,KeyInterop.VirtualKeyFromKey(key)))registeredPetHotkeys.Add(id);
        }

        void RegisterPetHotkeys()
        {
            RegisterPetHotkey(HotkeyCapture,Key.S);RegisterPetHotkey(HotkeyHide,Key.B);RegisterPetHotkey(HotkeyLauncher,Key.M);RegisterPetHotkey(HotkeyNotes,Key.N);RegisterPetHotkey(HotkeyStash,Key.V);RegisterPetHotkey(HotkeyAi,Key.A);RegisterPetHotkey(HotkeyMarket,Key.P);RegisterPetHotkey(HotkeyWind,Key.W);RegisterPetHotkey(HotkeyCompliance,Key.C);
        }

        void UnregisterPetHotkeys()
        {
            if(petHwndSource!=null)foreach(int id in registeredPetHotkeys.ToList())UnregisterHotKey(petHwndSource.Handle,id);registeredPetHotkeys.Clear();
        }

        void HandlePetHotkey(int id)
        {
            if(id==HotkeyHide){ToggleGlobalPetVisibility();return;}
            if(globalPetHidden)ToggleGlobalPetVisibility();
            if(id==HotkeyCapture)CaptureClipboardForOffice();
            else if(id==HotkeyLauncher)ToggleLauncherPanel();
            else if(id==HotkeyNotes)TogglePanel();
            else if(id==HotkeyStash)ToggleStashPanel();
            else if(id==HotkeyAi)OpenAiPocketChat("");
            else if(id==HotkeyMarket)ToggleMarketPanel();
            else if(id==HotkeyWind)ToggleAiSearchPanel();
            else if(id==HotkeyCompliance)OpenComplianceReview();
        }

        bool IsInteractivePetPixel(Point screenPoint)
        {
            if(pet==null||!pet.IsVisible)return false;
            Point client;
            // WM_NCHITTEST 给的是物理屏幕像素，而 WPF 布局使用 DIP。直接 PointFromScreen 在部分
            // 非 100% 缩放环境会出现偏移，导致可见猫咪也被误判为透明。按真实 HWND 尺寸换算最稳定。
            NativeRect windowRect;
            if(petHwndSource!=null&&GetWindowRect(petHwndSource.Handle,out windowRect)&&windowRect.Right>windowRect.Left&&windowRect.Bottom>windowRect.Top){
                client=new Point((screenPoint.X-windowRect.Left)*pet.ActualWidth/(windowRect.Right-windowRect.Left),(screenPoint.Y-windowRect.Top)*pet.ActualHeight/(windowRect.Bottom-windowRect.Top));
            }else try{client=pet.PointFromScreen(screenPoint);}catch{return true;}
            if(client.X<0||client.Y<0||client.X>pet.ActualWidth||client.Y>pet.ActualHeight)return false;
            if(IsPointInsideVisual(speechBubble,client)||IsPointInsideVisual(badge,client)||IsPointInsideVisual(pocketBadge,client))return true;
            if(edgeHidden)return IsPointInsideVisual(edgePeekImage,client);
            if(spriteLayer==null||spriteLayer.Visibility!=Visibility.Visible)return false;
            Rect spriteBounds;
            try{spriteBounds=spriteLayer.TransformToAncestor(pet).TransformBounds(new Rect(0,0,spriteLayer.ActualWidth,spriteLayer.ActualHeight));}catch{return true;}
            if(spriteBounds.Width<=0||spriteBounds.Height<=0||!spriteBounds.Contains(client))return false;
            // 精灵画布本身很小。整块画布稳定接收鼠标，可避免走路、睡觉和尾巴动作改变
            // 轮廓后出现“这一帧抓得到、下一帧抓不到”；画布外的窗口空白仍然穿透。
            return true;
        }

        bool IsPointInsideVisual(FrameworkElement visual,Point client)
        {
            if(visual==null||visual.Visibility!=Visibility.Visible||visual.ActualWidth<=0||visual.ActualHeight<=0)return false;
            try{Rect bounds=visual.TransformToAncestor(pet).TransformBounds(new Rect(0,0,visual.ActualWidth,visual.ActualHeight));return bounds.Contains(client);}catch{return false;}
        }

        AlphaMask GetAlphaMask(BitmapSource bitmap)
        {
            AlphaMask cached;if(petAlphaMasks.TryGetValue(bitmap,out cached))return cached;
            try{
                BitmapSource source=bitmap;
                if(source.Format!=PixelFormats.Bgra32&&source.Format!=PixelFormats.Pbgra32)source=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
                int width=source.PixelWidth,height=source.PixelHeight,stride=width*4;byte[] pixels=new byte[stride*height],alpha=new byte[width*height];source.CopyPixels(pixels,stride,0);
                for(int i=0,j=3;i<alpha.Length;i++,j+=4)alpha[i]=pixels[j];
                cached=new AlphaMask{Width=width,Height=height,Alpha=alpha};petAlphaMasks[bitmap]=cached;return cached;
            }catch{return null;}
        }

        void ToggleGlobalPetVisibility()
        {
            if(!globalPetHidden){
                globallyHiddenWindows.Clear();foreach(Window window in app.Windows.Cast<Window>().ToList())if(window.IsVisible){globallyHiddenWindows.Add(window);window.Hide();}
                globalPetHidden=true;if(desktopSurfaceTimer!=null)desktopSurfaceTimer.Stop();StopRendering();
            }else{
                globalPetHidden=false;foreach(Window window in globallyHiddenWindows.ToList())try{window.Show();}catch{}globallyHiddenWindows.Clear();
                if(desktopSurfaceTimer!=null)desktopSurfaceTimer.Start();if(pet!=null){pet.Show();pet.Activate();}if(petState!="idle"&&petState!="sleep"&&petState!="coffee")EnsureRendering();
            }
        }

        public void ShowFromSecondLaunch()
        {
            if(exiting||pet==null)return;
            if(globalPetHidden)ToggleGlobalPetVisibility();
            else{
                if(!pet.IsVisible)pet.Show();
                pet.Topmost=false;pet.Topmost=true;pet.Activate();
            }
            React("我在这里～",false);
        }

        void AddPetExperienceMenu(ContextMenu menu)
        {
            var behaviorRoot=new MenuItem{Header="博道咪状态"};var behaviorItems=new List<MenuItem>();
            foreach(string mode in new[]{"安静","正常","活泼","专注"}){
                string selected=mode;var item=new MenuItem{Header=selected,IsCheckable=true,IsChecked=petMovement.BehaviorMode==selected};behaviorItems.Add(item);
                item.Click+=delegate{petMovement.BehaviorMode=selected;foreach(MenuItem other in behaviorItems)other.IsChecked=Object.ReferenceEquals(other,item);SavePetMovementSettings();ApplyPetExperienceSettings();React(selected=="专注"?"我会安静陪你专注～":selected+"模式开启～",false);};behaviorRoot.Items.Add(item);
            }
            var clickThrough=new MenuItem{Header="透明区域穿透",IsCheckable=true,IsChecked=petMovement.TransparentClickThrough};clickThrough.Click+=delegate{petMovement.TransparentClickThrough=clickThrough.IsChecked;SavePetMovementSettings();};
            var hide=new MenuItem{Header="立即隐藏全部（Ctrl + Alt + B）"};hide.Click+=delegate{ToggleGlobalPetVisibility();};
            menu.Items.Add(behaviorRoot);menu.Items.Add(clickThrough);menu.Items.Add(hide);
        }

        string PetHitRegion(Point point)
        {
            if(spriteLayer==null||point.X<0||point.Y<0||point.X>spriteLayer.ActualWidth||point.Y>spriteLayer.ActualHeight)return "body";
            double x=point.X/Math.Max(1,spriteLayer.ActualWidth),y=point.Y/Math.Max(1,spriteLayer.ActualHeight);
            bool tail=facing>0?(x>.73&&y>.40&&y<.78):(x<.27&&y>.40&&y<.78);
            if(tail)return "tail";
            if(x>.28&&x<.72&&y>.24&&y<.49)return "face";
            if(x>.16&&x<.84&&y<.42)return "head";
            return "body";
        }

        void ReactPetRegion(string region)
        {
            if(region=="head"){StartState("happy",1.0);React("摸摸头就有好运 ♡",true);}
            else if(region=="face"){StartState("face",.9);React("脸颊软乎乎～",false);}
            else if(region=="tail"){StartState("tail",.82);React("尾巴会自己打招呼～",false);}
            else{StartState("stretch",1.05);React("伸个懒腰，再继续～",false);}
        }

        bool IsHeadPettingPoint(Point petPoint)
        {
            if(spriteLayer==null)return false;Point origin;try{origin=spriteLayer.TranslatePoint(new Point(0,0),pet);}catch{return false;}
            return PetHitRegion(new Point(petPoint.X-origin.X,petPoint.Y-origin.Y))=="head";
        }

        Point PetPointerScreenDip(Point localPoint)
        {
            try{
                Point device=pet.PointToScreen(localPoint);var source=PresentationSource.FromVisual(pet);
                if(source!=null&&source.CompositionTarget!=null)return source.CompositionTarget.TransformFromDevice.Transform(device);
                return device;
            }catch{return new Point(pet.Left+localPoint.X,pet.Top+localPoint.Y);}
        }

        void BeginPetPointerPress(MouseButtonEventArgs e)
        {
            if(e.ChangedButton!=MouseButton.Left||petPointerPressed)return;
            // 鼠标悬停缩放必须在拖动期间冻结，否则尚未结束的 1.045 动画会让抓取点看起来滑动。
            double hoverX=petScale.ScaleX,hoverY=petScale.ScaleY;petScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);petScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);petScale.ScaleX=hoverX;petScale.ScaleY=hoverY;
            petPointerPressed=true;petPointerDragging=false;petPointerStartedAtEdge=edgeHidden;petPointerEdgeSide=edgeHideSide;
            petPointerClickCount=e.ClickCount;petPointerHitRegion=PetHitRegion(e.GetPosition(spriteLayer));
            petPointerStartLeft=pet.Left;petPointerStartTop=pet.Top;petPointerStartScreen=PetPointerScreenDip(e.GetPosition(pet));
            pet.CaptureMouse();e.Handled=true;
        }

        bool MovePetPointer(MouseEventArgs e)
        {
            if(!petPointerPressed)return false;
            if(e.LeftButton!=MouseButtonState.Pressed){CancelPetPointerPress();return true;}
            Point current=PetPointerScreenDip(e.GetPosition(pet));double dx=current.X-petPointerStartScreen.X,dy=current.Y-petPointerStartScreen.Y;
            if(!petPointerDragging){double threshold=Math.Max(7,Math.Max(SystemParameters.MinimumHorizontalDragDistance,SystemParameters.MinimumVerticalDragDistance));if(dx*dx+dy*dy<threshold*threshold)return true;
                petPointerDragging=true;CancelLauncherOpen();if(launcherPanel!=null)launcherPanel.Hide();
                // 从侧边探头状态拖出时，先还原为完整猫咪再进入拎起姿势；否则仍在
                // edgePeekImage 上移动，松开后会被再次夹回侧边，看起来像“拖不出来”。
                if(petPointerStartedAtEdge){
                    ExitEdgeHide();petPointerStartedAtEdge=false;
                    // 窗口由探头尺寸恢复到完整尺寸后重新建立拖动基点，否则第一次移动会
                    // 又套用收纳窗口的旧 Left/Top，表现为突然偏移或吸回侧边。
                    petPointerStartLeft=pet.Left-dx;petPointerStartTop=pet.Top-dy;
                }
                HideLauncherAndRestoreShelves();CancelShelfPeekTimers();HideShelfPeek();
                BeginPetDragVisual();
                StopRendering();
            }
            // 左上角始终等于按下时窗口位置 + 鼠标屏幕位移，鼠标锚点不会因窗口移动而漂移。
            pet.Left=petPointerStartLeft+dx;pet.Top=petPointerStartTop+dy;return true;
        }

        void EndPetPointerPress(MouseButtonEventArgs e)
        {
            if(!petPointerPressed||e.ChangedButton!=MouseButton.Left)return;
            bool moved=petPointerDragging,startedAtEdge=petPointerStartedAtEdge;string edgeSide=petPointerEdgeSide,region=petPointerHitRegion;int clicks=petPointerClickCount;
            petPointerPressed=false;petPointerDragging=false;petPointerStartedAtEdge=false;if(pet.IsMouseCaptured)pet.ReleaseMouseCapture();petDragActive=false;e.Handled=true;
            if(!moved){
                AnimateScale(pet.IsMouseOver?1.045:1.0);
                if(edgeHidden){ScheduleLauncherOpen();return;}
                if(clicks>=2){CancelLauncherOpen();if(launcherPanel!=null)launcherPanel.Hide();TogglePanel();React("今天要记什么？",true);}
                else{ReactPetRegion(region);ScheduleLauncherOpen();}
                return;
            }
            if(startedAtEdge){
                Rect screen=SystemParameters.WorkArea;bool stillDocked=edgeSide=="left"?pet.Left<=screen.Left+12:pet.Left+pet.Width>=screen.Right-12;
                if(stillDocked)EnterEdgeHide(edgeSide,screen);else{double centerX=pet.Left+pet.Width*.5,centerY=pet.Top+pet.Height*.5;ExitEdgeHide();pet.Left=centerX-pet.Width*.5;pet.Top=centerY-pet.Height*.5;ClampPetToMovementRange();exactLeft=pet.Left;hopBaseTop=pet.Top;}
                AnimateScale(pet.IsMouseOver?1.045:1.0);
                return;
            }
            // 先识别物理屏幕边缘，再套用用户限制范围。
            if(TryEnterEdgeHide()){CancelLauncherOpen();return;}
            if(petMovement.Mode=="固定当前位置"){petMovement.AnchorX=pet.Left;petMovement.AnchorY=pet.Top;SavePetMovementSettings();}
            ClampPetToMovementRange();bool snapped=TrySnapToDesktopSurface();hopBaseTop=pet.Top;exactLeft=pet.Left;EndPetDragVisual();React(snapped?"这里刚好可以坐～":"被轻轻放下来啦～",false);
            AnimateScale(pet.IsMouseOver?1.045:1.0);
            if(panel!=null&&panel.IsVisible)PositionPanel();if(stashPanel!=null&&stashPanel.IsVisible&&!manuallyPlacedWindows.Contains(stashPanel))PositionStashPanel();if(launcherPanel!=null&&launcherPanel.IsVisible)PositionLauncherPanel();
        }

        void CancelPetPointerPress()
        {
            if(!petPointerPressed)return;bool wasDragging=petPointerDragging;petPointerPressed=false;petPointerDragging=false;petPointerStartedAtEdge=false;if(pet!=null&&pet.IsMouseCaptured)pet.ReleaseMouseCapture();petDragActive=false;
            if(wasDragging&&!edgeHidden){ClampPetToMovementRange();hopBaseTop=pet.Top;exactLeft=pet.Left;EndPetDragVisual();}
            AnimateScale(pet!=null&&pet.IsMouseOver?1.045:1.0);
        }

        void BeginPetDragVisual()
        {
            hasActiveSurface=false;petState="dragged";petDragActive=true;lastDragVisualLeft=pet.Left;lastDragVisualTop=pet.Top;lastDragVisualSample=DateTime.Now;
            stateStarted=DateTime.Now;stateUntil=DateTime.MaxValue;
            // 拖动全程显示完整、不透明的拎起姿势，包括从侧边拖出来。
            // 清理侧边退出动画，避免父图层的淡入仍覆盖这里的显示状态。
            spriteLayer.BeginAnimation(UIElement.OpacityProperty,null);spriteLayer.Opacity=1;
            motionScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);motionScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            petRotate.BeginAnimation(RotateTransform.AngleProperty,null);petRotate.Angle=0;
            transitionImage.BeginAnimation(UIElement.OpacityProperty,null);transitionImage.Opacity=0;transitionImage.Source=null;
            petImage.BeginAnimation(UIElement.OpacityProperty,null);petImage.Opacity=1;
            SetFrame(dragFrame);
            motionScale.ScaleX=.96;motionScale.ScaleY=1.04;motionTranslate.Y=-3;motionRotate.Angle=0;
        }

        void UpdatePetDragVisual()
        {
            if(!petDragActive||pet==null)return;DateTime now=DateTime.Now;double seconds=(now-lastDragVisualSample).TotalSeconds;
            // 高频鼠标可在一帧内产生很多 LocationChanged；视觉变换最多按 120Hz 更新，
            // 位移仍逐事件跟随鼠标，减少无意义的布局/合成失效。
            if(seconds<1.0/120.0)return;double dx=(pet.Left-lastDragVisualLeft)/seconds;
            double target=Math.Max(-4.5,Math.Min(4.5,dx*.006));motionRotate.Angle=motionRotate.Angle*.72+target*.28;motionTranslate.Y=-3;
            lastDragVisualLeft=pet.Left;lastDragVisualTop=pet.Top;lastDragVisualSample=now;
        }

        void EndPetDragVisual()
        {
            petDragActive=false;transitionImage.BeginAnimation(UIElement.OpacityProperty,null);petImage.BeginAnimation(UIElement.OpacityProperty,null);transitionImage.Opacity=0;transitionImage.Source=null;petImage.Opacity=1;
            motionScale.ScaleX=1;motionScale.ScaleY=1;motionTranslate.Y=0;motionRotate.Angle=0;StartState("landing",.44);
        }

        void AnimateLanding(double elapsed)
        {
            double t=Math.Max(0,Math.Min(1,elapsed/.44));SetFrame(t<.28?frames[2]:(t<.62?frames[1]:frames[0]));
            double decay=1-t;motionTranslate.Y=Math.Sin(t*Math.PI*2.2)*1.8*decay;motionRotate.Angle*=Math.Max(0,1-t*1.7);
            double squash=Math.Sin(Math.Min(1,t/.62)*Math.PI)*.045;motionScale.ScaleX=1+squash;motionScale.ScaleY=1-squash;
        }

        void AnimateRegionState(string state,double elapsed)
        {
            double t=Math.Max(0,Math.Min(1,elapsed/stateDuration));double wave=Math.Sin(t*Math.PI);
            if(state=="face"){SetFrame(poses[1]);motionRotate.Angle=(facing>0?-1:1)*2.2*wave;motionTranslate.Y=0;motionScale.ScaleX=1+.018*wave;motionScale.ScaleY=1+.018*wave;}
            else if(state=="tail"){SetFrame(poses[5]);motionRotate.Angle=Math.Sin(t*Math.PI*4)*1.4*(1-t);motionTranslate.Y=0;motionScale.ScaleX=1;motionScale.ScaleY=1;}
            else{SetFrame(poses[4]);motionRotate.Angle=0;motionTranslate.Y=1.2*wave;motionScale.ScaleX=1+.035*wave;motionScale.ScaleY=1-.025*wave;}
        }

        double BehaviorSpeedScale(){return petMovement==null?1:(petMovement.BehaviorMode=="安静"?.72:(petMovement.BehaviorMode=="活泼"?1.16:(petMovement.BehaviorMode=="专注"?0:1)));}

        void RefreshDesktopSurfaces()
        {
            if(pet==null||petMovement==null||!petMovement.WindowInteractions||petHwndSource==null||desktopSurfaceScanInFlight)return;
            IntPtr ownWindow=petHwndSource.Handle;Rect work=SystemParameters.WorkArea;uint own=(uint)Process.GetCurrentProcess().Id;
            Matrix fromDevice=petHwndSource.CompositionTarget==null?Matrix.Identity:petHwndSource.CompositionTarget.TransformFromDevice;
            desktopSurfaceScanInFlight=true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate{
                var found=new List<Rect>();
                try{
                    EnumWindows(delegate(IntPtr hwnd,IntPtr state){
                        if(hwnd==ownWindow||!IsWindowVisible(hwnd)||IsIconic(hwnd))return true;uint pid;GetWindowThreadProcessId(hwnd,out pid);if(pid==own)return true;
                        var className=new StringBuilder(128);GetClassName(hwnd,className,className.Capacity);string cls=className.ToString();if(cls=="Progman"||cls=="WorkerW"||cls=="Shell_TrayWnd"||cls=="Shell_SecondaryTrayWnd")return true;
                        NativeRect native;if(!GetWindowRect(hwnd,out native))return true;Point a=fromDevice.Transform(new Point(native.Left,native.Top)),b=fromDevice.Transform(new Point(native.Right,native.Bottom));Rect rect=new Rect(a,b);
                        if(rect.Width<220||rect.Height<110||rect.Right<work.Left||rect.Left>work.Right||rect.Bottom<work.Top||rect.Top>work.Bottom)return true;
                        if(rect.Top<=work.Top+34||rect.Top>=work.Bottom-80)return true;
                        found.Add(rect);return true;
                    },IntPtr.Zero);
                }catch{}
                try{app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{
                    desktopSurfaceScanInFlight=false;if(exiting||petMovement==null||!petMovement.WindowInteractions)return;
                    desktopSurfaces.Clear();desktopSurfaces.AddRange(found.OrderBy(r=>r.Top).Take(48));
                }));}catch{desktopSurfaceScanInFlight=false;}
            });
        }

        bool TrySnapToDesktopSurface()
        {
            if(petMovement==null||!petMovement.WindowInteractions||desktopSurfaces.Count==0)return false;
            double center=pet.Left+pet.Width*.5,feet=pet.Top+SpriteFootOffset;Rect best=new Rect();double bestDistance=46;bool found=false;
            foreach(Rect rect in desktopSurfaces){if(center<rect.Left+12||center>rect.Right-12)continue;double distance=Math.Abs(feet-rect.Top);if(distance<bestDistance){bestDistance=distance;best=rect;found=true;}}
            if(!found)return false;pet.Top=best.Top-SpriteFootOffset;hopBaseTop=pet.Top;activeSurface=best;hasActiveSurface=true;return true;
        }

        bool TryStartWindowClimb(double proposedLeft)
        {
            if(petMovement==null||!petMovement.WindowInteractions||petMovement.BehaviorMode=="安静"||petMovement.BehaviorMode=="专注"||hasActiveSurface)return false;
            double feet=pet.Top+SpriteFootOffset,currentFront=facing>0?pet.Left+pet.Width:pet.Left,nextFront=facing>0?proposedLeft+pet.Width:proposedLeft;
            foreach(Rect rect in desktopSurfaces){
                if(rect.Top>=feet-34||rect.Bottom<feet-8)continue;bool crosses=facing>0?(currentFront<=rect.Left+10&&nextFront>=rect.Left-10):(currentFront>=rect.Right-10&&nextFront<=rect.Right+10);if(!crosses)continue;
                climbSurface=rect;climbStartLeft=pet.Left;climbStartTop=pet.Top;climbTargetTop=rect.Top-SpriteFootOffset;climbTargetLeft=facing>0?rect.Left+10:rect.Right-pet.Width-10;
                petState="climb";stateStarted=DateTime.Now;stateDuration=Math.Max(.72,Math.Min(1.55,Math.Abs(climbTargetTop-climbStartTop)/185));stateUntil=stateStarted.AddSeconds(stateDuration);strideDistance=0;EnsureRendering();return true;
            }
            return false;
        }

        void AnimateWindowClimb(double elapsed)
        {
            double t=Math.Max(0,Math.Min(1,elapsed/stateDuration)),p=Smooth(t);SetFrame(dragFrame);pet.Left=climbStartLeft+(climbTargetLeft-climbStartLeft)*p;pet.Top=climbStartTop+(climbTargetTop-climbStartTop)*p;exactLeft=pet.Left;hopBaseTop=pet.Top;
            motionRotate.Angle=(facing>0?1:-1)*Math.Sin(t*Math.PI)*3.2;motionTranslate.Y=0;motionScale.ScaleX=1-.018*Math.Sin(t*Math.PI);motionScale.ScaleY=1+.028*Math.Sin(t*Math.PI);
        }

        void CompleteWindowClimb()
        {
            pet.Left=climbTargetLeft;pet.Top=climbTargetTop;exactLeft=pet.Left;hopBaseTop=pet.Top;activeSurface=climbSurface;hasActiveSurface=true;petState="perch";stateStarted=DateTime.Now;stateDuration=4.5;stateUntil=stateStarted.AddSeconds(stateDuration);SetFrame(poses[5]);ResetMotionPose(1);
        }

        bool KeepPetOnActiveSurface(double proposedLeft)
        {
            if(!hasActiveSurface)return false;double min=activeSurface.Left-8,max=activeSurface.Right-pet.Width+8;pet.Top=activeSurface.Top-SpriteFootOffset;hopBaseTop=pet.Top;
            if(proposedLeft>=min&&proposedLeft<=max)return false;
            if(petMovement.BehaviorMode=="活泼"&&random.NextDouble()<.38){BeginWindowDrop();return true;}
            exactLeft=Math.Max(min,Math.Min(proposedLeft,max));pet.Left=exactLeft;facing=-facing;strideDistance=0;StartState("settle",.26);return true;
        }

        void BeginWindowDrop()
        {
            Rect work=PetMovementBounds();dropStartTop=pet.Top;dropTargetTop=work.Bottom-pet.Height;hasActiveSurface=false;petState="drop";stateStarted=DateTime.Now;stateDuration=Math.Max(.55,Math.Min(1.15,Math.Sqrt(Math.Max(1,dropTargetTop-dropStartTop))/18));stateUntil=stateStarted.AddSeconds(stateDuration);EnsureRendering();
        }

        void AnimateWindowDrop(double elapsed)
        {
            double t=Math.Max(0,Math.Min(1,elapsed/stateDuration));double gravity=t*t;SetFrame(frames[0]);pet.Top=dropStartTop+(dropTargetTop-dropStartTop)*gravity;hopBaseTop=pet.Top;motionTranslate.Y=0;motionRotate.Angle=0;motionScale.ScaleX=1-.02*Math.Sin(t*Math.PI);motionScale.ScaleY=1+.035*Math.Sin(t*Math.PI);
        }

        void CompleteWindowDrop(){pet.Top=dropTargetTop;hopBaseTop=pet.Top;petState="landing";stateStarted=DateTime.Now;stateDuration=.44;stateUntil=stateStarted.AddSeconds(stateDuration);}

        Rect PetScreenBounds()
        {
            // Monitor 而非 Work：侧边任务栏不能当作物理屏幕边缘。
            if(petHwndSource!=null&&petHwndSource.CompositionTarget!=null){
                var info=new PetMonitorInfo{Size=Marshal.SizeOf(typeof(PetMonitorInfo))};
                if(GetMonitorInfo(MonitorFromWindow(petHwndSource.Handle,2),ref info)){
                    Matrix matrix=petHwndSource.CompositionTarget.TransformFromDevice;
                    return new Rect(matrix.Transform(new Point(info.Monitor.Left,info.Monitor.Top)),matrix.Transform(new Point(info.Monitor.Right,info.Monitor.Bottom)));
                }
            }
            return new Rect(0,0,SystemParameters.PrimaryScreenWidth,SystemParameters.PrimaryScreenHeight);
        }

        double EdgeGripOffset(bool left)
        {
            // edge-peek.png 的扒边竖线中心在原图 x=580/649。
            // Stretch.Uniform 的左右留白和镜像都参与计算，不能只用窗口宽度减固定值。
            double scale=Math.Min(edgePeekImage.Width/edgePeekFrame.PixelWidth,edgePeekImage.Height/edgePeekFrame.PixelHeight);
            double drawnWidth=edgePeekFrame.PixelWidth*scale;
            double grip=(edgePeekImage.Width-drawnWidth)/2+drawnWidth*(580.0/649.0);
            return (pet.Width-edgePeekImage.Width)/2+(left?edgePeekImage.Width-grip:grip);
        }

        void AnimateEdgeDockIn()
        {
            edgePeekImage.Opacity=0;edgePeekTranslate.X=edgeHideSide=="left"?-9:9;
            var fade=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(210)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}};
            var slide=new DoubleAnimation(edgePeekTranslate.X,0,TimeSpan.FromMilliseconds(240)){EasingFunction=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=.18}};
            fade.Completed+=delegate{if(edgeHidden&&edgePeekImage.Visibility==Visibility.Visible){edgePeekImage.Opacity=1;StartEdgePeekAnimation();}};edgePeekImage.BeginAnimation(UIElement.OpacityProperty,fade);edgePeekTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,slide);
        }

        void AnimateEdgeDockOut()
        {
            spriteLayer.Opacity=0;motionScale.ScaleX=.9;motionScale.ScaleY=.9;
            spriteLayer.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(220)));
            motionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,new DoubleAnimation(.9,1,TimeSpan.FromMilliseconds(260)){EasingFunction=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=.16}});
            motionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new DoubleAnimation(.9,1,TimeSpan.FromMilliseconds(260)){EasingFunction=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=.16}});
        }
    }
}
