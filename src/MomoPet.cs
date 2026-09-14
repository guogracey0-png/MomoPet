using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MomoPetApp
{
    public class TaskItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Due { get; set; }
        public bool Done { get; set; }
        public bool Notified { get; set; }
        public string Created { get; set; }
        public string Category { get; set; }
        public string Repeat { get; set; }
        public bool SkipWeekends { get; set; }
        public bool SkipHolidays { get; set; }
        public string Weekdays { get; set; }
        public int MonthDay { get; set; }
    }

    public class StashItem
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public string Created { get; set; }
        public bool Owned { get; set; }
        public string Project { get; set; }
        public string Company { get; set; }
        public string Industry { get; set; }
        public string IndexName { get; set; }
        public string Client { get; set; }
        public string Tags { get; set; }
        public string Note { get; set; }
        public bool Favorite { get; set; }
        public string SourceApp { get; set; }
        public string ContentHash { get; set; }
        public string VersionGroup { get; set; }
        public int DuplicateCount { get; set; }
        public string LastSeen { get; set; }
        public string SearchText { get; set; }
    }

    public class MarketAlertState
    {
        public int ScheduleVersion { get; set; }
        public string Date { get; set; }
        public bool SseUp { get; set; }
        public bool SseDown { get; set; }
        public bool StarUp { get; set; }
        public bool StarDown { get; set; }
        public bool MiddaySent { get; set; }
        public bool CloseSent { get; set; }
    }

    public class MarketIndex
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public double Last { get; set; }
        public double Pct { get; set; }
        public string TradeDate { get; set; }
        public string TradeTime { get; set; }
    }

    public static class Program
    {
        static Mutex mutex;
        static EventWaitHandle activationEvent;
        static RegisteredWaitHandle activationRegistration;

        // 统一把异常落到一个日志里，方便事后定位“闪退”到底出在哪条线程上。
        static void MomoLog(string scope, Exception error)
        {
            if(error==null)return;
            try{string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MomoPet");Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"ui-errors.log"),DateTime.Now.ToString("o")+"\t["+scope+"]\t"+error+Environment.NewLine,Encoding.UTF8);}catch{}
        }

        [STAThread]
        public static void Main()
        {
            bool first;
            mutex = new Mutex(true, "MomoPet_SingleInstance_2026_Native", out first);
            bool activationEventCreated;
            activationEvent = new EventWaitHandle(false,EventResetMode.AutoReset,"MomoPet_Activate_2026_Native",out activationEventCreated);
            // 再次双击不是静默退出，而是通知已经运行的实例恢复并显示。
            if (!first) { try{activationEvent.Set();}catch{}activationEvent.Dispose();mutex.Dispose();return; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // AI 拆图是外部接口返回的多张大图，任何 UI 解码/绑定异常都不能直接结束桌宠进程。
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                MomoLog("ui", e.Exception);
                e.Handled = true;
            };
            // 后台线程（网络回调 / 本地索引 / OCR / 行情）里抛出的异常不会经过 Dispatcher，
            // 不兜住就会直接结束整个进程，这正是“用着用着就闪退”的主因：先记日志保住现场。
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                MomoLog("appdomain", e.ExceptionObject as Exception);
            };
            // .NET 4 上未被观察的 Task 异常会在 GC 时终结进程，这里标记为已观察，避免无谓闪退。
            TaskScheduler.UnobservedTaskException += delegate(object sender, UnobservedTaskExceptionEventArgs e)
            {
                MomoLog("task", e.Exception);
                e.SetObserved();
            };
            var controller = new PetController(app);
            controller.Start();
            activationRegistration=ThreadPool.RegisterWaitForSingleObject(activationEvent,delegate(object state,bool timedOut){
                try{app.Dispatcher.BeginInvoke(new Action(delegate{controller.ShowFromSecondLaunch();}));}catch{}
            },null,Timeout.Infinite,false);
            app.Run();
            if(activationRegistration!=null)activationRegistration.Unregister(null);
            activationEvent.Dispose();
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    public partial class PetController
    {
        readonly Application app;
        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MomoPet");
        readonly string dataFile;
        readonly string stashFile;
        readonly string stashDir;
        readonly string windKeyFile;
        readonly string marketStateFile;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly List<TaskItem> tasks = new List<TaskItem>();
        readonly List<StashItem> stashItems = new List<StashItem>();
        Window pet, panel, stashPanel, marketPanel;
        Image petImage, transitionImage;
        Grid spriteLayer;
        Border badge;
        TextBlock badgeText;
        Border pocketBadge;
        TextBlock pocketText;
        Border speechBubble;
        TextBlock speechText;
        ScaleTransform petScale, motionScale;
        ScaleTransform facingScale;
        RotateTransform petRotate, motionRotate;
        TranslateTransform motionTranslate;
        BitmapImage[] frames = new BitmapImage[12];
        BitmapImage[] poses = new BitmapImage[8];
        BitmapImage[] walkFrames = new BitmapImage[8];
        BitmapImage[] runFrames = new BitmapImage[8];
        BitmapImage coffeeFrame,edgePeekFrame,dragFrame;
        readonly Dictionary<string,BitmapImage> skinFrames = new Dictionary<string,BitmapImage>();
        ImageSource currentFrame;
        Image edgePeekImage;
        ScaleTransform edgePeekFacing;
        TranslateTransform edgePeekTranslate;
        TextBox titleBox;
        DatePicker dueDatePicker;
        ComboBox taskCategoryBox,taskRepeatBox,taskFilterBox,dueHourBox,dueMinuteBox,monthDayBox;
        CheckBox skipWeekendsBox,skipHolidaysBox;
        CheckBox[] weekdayBoxes;
        FrameworkElement weekdayPanel,monthDayPanel;
        Button taskSaveButton;
        string editingTaskId;
        ListBox taskList;
        ListBox stashList;
        Point pocketDragStart, stashDragStart;
        bool pocketPressed;
        StashItem pendingStashDrag;
        DispatcherTimer timer, speechTimer, idleTimer, marketTimer;
        RichTextBox marketStatusText;
        PasswordBox windKeyBox;
        bool marketFetchInFlight;
        DateTime lastMarketFetch = DateTime.MinValue;
        DateTime lastMarketSuccessfulFetch = DateTime.MinValue;
        MarketAlertState marketAlerts = new MarketAlertState();
        List<MarketIndex> latestMarket = new List<MarketIndex>();
        readonly Random random = new Random();
        Point lastPetPoint;
        DateTime pettingStarted = DateTime.Now;
        double pettingDistance;
        string petState = "idle";
        DateTime stateUntil = DateTime.MinValue;
        int facing = 1;
        double hopBaseTop;
        DateTime stateStarted = DateTime.Now;
        double stateDuration = 1;
        double strideDistance;
        double exactLeft;
        // 手动拖到活动区域左右边界后进入“小头扒边”待命，避免遮挡桌面内容。
        bool edgeHidden;
        string edgeHideSide;
        const double NormalPetWidth=142,NormalPetHeight=154;
        bool renderAttached;
        readonly Stopwatch renderClock = Stopwatch.StartNew();
        double lastRenderSeconds;
        bool petDragActive,petFollowerPositionQueued;
        bool exiting;

        public PetController(Application application)
        {
            app = application;
            dataFile = Path.Combine(dataDir, "tasks.json");
            stashFile = Path.Combine(dataDir, "stash.json");
            stashDir = Path.Combine(dataDir, "StashFiles");
            windKeyFile = Path.Combine(dataDir, "wind-key.dat");
            marketStateFile = Path.Combine(dataDir, "market-alerts.json");
            EmbeddedRuntime.Initialize(dataDir);
            InitializeAiSearchPaths();
            InitializeImageEditorPaths();
            InitializeCompliancePaths();
        }

        public void Start()
        {
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(stashDir);
            LoadPetMovementSettings();
            LoadTasks();
            LoadStash();
            QueueMissingStashIndexes();
            LoadComplianceAudits();
            LoadAssets();
            BuildPet();
            ApplySelectedSkin();
            InitializePetExperience();
            InitializeMessenger();
            UpgradeStoredImages();
            RefreshTasks();
            RefreshStash();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { CheckReminders(); };
            timer.Start();
            speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
            speechTimer.Tick += delegate { speechTimer.Stop(); speechBubble.Visibility = Visibility.Collapsed; };
            idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            idleTimer.Tick += delegate { ChooseBehavior(); };
            idleTimer.Start();
            LoadMarketAlerts();
            marketTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            marketTimer.Tick += delegate { CheckMarketSchedule(); };
            marketTimer.Start();
            var work = SystemParameters.WorkArea;
            pet.Left = work.Right - pet.Width - 22;
            pet.Top = work.Bottom - pet.Height - 18;
            ClampPetToMovementRange();
            exactLeft = pet.Left;
            pet.Show();
            React("单击聊聊 · 双击记事", false);
        }

        void LoadAssets()
        {
            for (int i=0; i<12; i++) frames[i] = EmbeddedRuntime.LoadBitmap(Path.Combine(root, "assets", "normalized", "locomotion", "frame-"+i+".png"), "assets.normalized.locomotion.frame-"+i+".png", 192);
            for (int i=0; i<8; i++) poses[i] = EmbeddedRuntime.LoadBitmap(Path.Combine(root, "assets", "normalized", "poses", "frame-"+i+".png"), "assets.normalized.poses.frame-"+i+".png", 192);
            for (int i=0; i<8; i++) walkFrames[i] = EmbeddedRuntime.LoadBitmap(Path.Combine(root, "assets", "normalized", "walk", "frame-"+i+".png"), "assets.normalized.walk.frame-"+i+".png", 192);
            for (int i=0; i<8; i++) runFrames[i] = EmbeddedRuntime.LoadBitmap(Path.Combine(root, "assets", "normalized", "run", "frame-"+i+".png"), "assets.normalized.run.frame-"+i+".png", 192);
            coffeeFrame = EmbeddedRuntime.LoadBitmap(Path.Combine(root, "assets", "normalized", "coffee", "coffee.png"), "assets.normalized.coffee.coffee.png", 192);
            edgePeekFrame = EmbeddedRuntime.LoadBitmap(Path.Combine(root, "assets", "normalized", "edge-peek.png"), "assets.normalized.edge-peek.png", 160);
            dragFrame = EmbeddedRuntime.LoadBitmap(Path.Combine(root, "assets", "normalized", "poses", "dragged.png"), "assets.normalized.poses.dragged.png", 192);
            LoadSkin("detective","detective.png");
            LoadSkin("green-scarf-calico","green-scarf-calico.png");
            LoadSkin("tuxedo-bell","tuxedo-bell.png");
            LoadSkin("white-bowtie","white-bowtie.png");
            LoadSkin("orange-scarf","orange-scarf.png");
            LoadSkin("red-collar-calico","red-collar-calico.png");
            LoadSkin("gentleman-monocle","gentleman-monocle.png");
        }

        void LoadSkin(string id,string fileName)
        {
            skinFrames[id]=EmbeddedRuntime.LoadBitmap(Path.Combine(root,"assets","normalized","skins",fileName),"assets.normalized.skins."+fileName,256);
            LoadSkinMotion(id);
        }

        ImageSource SelectedSkinFrame()
        {
            if(petMovement==null||String.IsNullOrWhiteSpace(petMovement.SkinId)||petMovement.SkinId=="default")return null;
            BitmapImage image;return skinFrames.TryGetValue(petMovement.SkinId,out image)?image:null;
        }

        void AddSkinMenu(ContextMenu menu)
        {
            var rootItem=new MenuItem{Header="更换皮肤"};
            var wardrobe=new MenuItem{Header="打开皮肤衣柜…"};wardrobe.Click+=delegate{OpenSkinWardrobe();};rootItem.Items.Add(wardrobe);rootItem.Items.Add(new Separator());
            skinQuickMenuItems.Clear();
            string[,] choices=SkinCatalog;
            for(int i=0;i<choices.GetLength(0);i++){
                string id=choices[i,0],label=choices[i,1];
                var item=new MenuItem{Header=label,Tag=id,IsCheckable=true,IsChecked=(petMovement==null?"default":petMovement.SkinId)==id};
                skinQuickMenuItems.Add(item);
                item.Click+=delegate{
                    ApplySkinChoice(id);
                };
                rootItem.Items.Add(item);
            }
            menu.Items.Add(rootItem);
        }

        void ApplySelectedSkin()
        {
            ImageSource skin=SelectedSkinFrame();
            currentFrame=null;
            if(edgePeekImage!=null)edgePeekImage.Source=SelectedSkinEdgeFrame()??skin??edgePeekFrame;
            SetFrame(frames[0]);
        }

        static Button MakeButton(string text, Brush background)
        {
            return Ui.MakeButton(text, background);
        }

        void BuildPet()
        {
            pet = new Window { Title="博道咪", Width = NormalPetWidth, Height = NormalPetHeight, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, AllowDrop = true };
            var grid = new Grid { Background = Brushes.Transparent,ClipToBounds=true };
            spriteLayer = new Grid { Width = 112, Height = 116, Margin = new Thickness(0,32,0,0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "单击聊天 · 双击记事 · 按住拖动" };
            transitionImage = new Image { Stretch = Stretch.Uniform, Opacity = 0 };
            petImage = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(transitionImage,BitmapScalingMode.Fant);
            RenderOptions.SetBitmapScalingMode(petImage,BitmapScalingMode.Fant);
            petImage.Source = frames[0]; currentFrame = frames[0];
            spriteLayer.Children.Add(transitionImage); spriteLayer.Children.Add(petImage);
            spriteLayer.CacheMode=new BitmapCache();
            edgePeekFacing=new ScaleTransform(1,1);edgePeekTranslate=new TranslateTransform();
            edgePeekImage=new Image{Source=edgePeekFrame,Width=72,Height=90,Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,RenderTransformOrigin=new Point(.5,.72)};
            edgePeekImage.CacheMode=new BitmapCache();
            var peekTransforms=new TransformGroup();peekTransforms.Children.Add(edgePeekFacing);peekTransforms.Children.Add(edgePeekTranslate);edgePeekImage.RenderTransform=peekTransforms;grid.Children.Add(edgePeekImage);
            petScale = new ScaleTransform(1, 1);
            motionScale = new ScaleTransform(1, 1);
            facingScale = new ScaleTransform(1, 1);
            petRotate = new RotateTransform(0);
            motionRotate = new RotateTransform(0);
            motionTranslate = new TranslateTransform(0, 0);
            var transforms = new TransformGroup();
            transforms.Children.Add(facingScale);
            transforms.Children.Add(motionScale);
            transforms.Children.Add(petScale);
            transforms.Children.Add(motionRotate);
            transforms.Children.Add(petRotate);
            transforms.Children.Add(motionTranslate);
            spriteLayer.RenderTransform = transforms; spriteLayer.RenderTransformOrigin = new Point(0.5, 0.82);
            grid.Children.Add(spriteLayer);
            speechText = new TextBlock { Foreground = Ui.Ink, FontSize = 11,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(8,4,8,4) };
            speechBubble = new Border { Background = Ui.Card, BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = speechText,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, MaxWidth = 138, Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.14 } };
            grid.Children.Add(speechBubble);
            badgeText = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5,0,5,0) };
            badge = new Border { Background = Ui.Badge, CornerRadius = new CornerRadius(10),
                MinWidth = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0,54,3,0), Child = badgeText, Visibility = Visibility.Collapsed,
                ToolTip = "待处理提醒" };
            grid.Children.Add(badge);

            pocketText = Ui.MakePocketTagText("");
            pocketBadge = Ui.MakePocketTag(pocketText);pocketBadge.HorizontalAlignment=HorizontalAlignment.Right;
            pocketBadge.VerticalAlignment=VerticalAlignment.Bottom;pocketBadge.Margin=new Thickness(0,0,3,2);
            pocketBadge.Visibility=Visibility.Collapsed;pocketBadge.ToolTip="点击打开中转袋；按住拖出最近一项";
            pocketBadge.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) {
                pocketPressed = true; pocketDragStart = e.GetPosition(pocketBadge); pocketBadge.CaptureMouse(); e.Handled = true;
            };
            pocketBadge.PreviewMouseMove += delegate(object s, MouseEventArgs e) {
                if (!pocketPressed || e.LeftButton != MouseButtonState.Pressed || stashItems.Count == 0) return;
                Point now = e.GetPosition(pocketBadge);
                if (Math.Abs(now.X-pocketDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(now.Y-pocketDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pocketPressed = false; pocketBadge.ReleaseMouseCapture(); e.Handled = true; BeginStashDrag(stashItems[0], pocketBadge);
            };
            pocketBadge.PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) {
                bool openPocket = pocketPressed; pocketPressed = false; pocketBadge.ReleaseMouseCapture(); e.Handled = true;
                if (openPocket) ToggleStashPanel();
            };
            grid.Children.Add(pocketBadge); pet.Content = grid;
            pet.LocationChanged += delegate { UpdatePetDragVisual();QueuePetFollowerReposition(); };

            pet.DragOver += delegate(object s, DragEventArgs e) { e.Effects = CanAcceptStash(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            pet.DragEnter += delegate(object s, DragEventArgs e) { if (CanAcceptStash(e.Data)) { petImage.Opacity = .72; speechText.Text = "放进口袋吧～"; speechBubble.Visibility = Visibility.Visible; } e.Handled = true; };
            pet.DragLeave += delegate { petImage.Opacity = 1; speechBubble.Visibility = Visibility.Collapsed; };
            pet.Drop += delegate(object s, DragEventArgs e) { petImage.Opacity = 1; e.Handled = true; AcceptStashDrop(e.Data); };

            pet.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                BeginPetPointerPress(e);
            };
            pet.MouseLeftButtonUp += delegate(object sender,MouseButtonEventArgs e){EndPetPointerPress(e);};
            pet.LostMouseCapture += delegate{if(petPointerPressed)CancelPetPointerPress();};
            pet.MouseEnter += delegate { if(!petDragActive&&!petPointerPressed)AnimateScale(1.045);ScheduleShelfPeekOpen(); };
            pet.MouseLeave += delegate { if(!petDragActive&&!petPointerPressed)AnimateScale(1.0); pettingDistance = 0;ScheduleShelfPeekClose(); };
            pet.MouseMove += delegate(object sender, MouseEventArgs e) {
                if(MovePetPointer(e)){e.Handled=true;return;}
                if(edgeHidden||petDragActive)return;
                Point now = e.GetPosition(pet);
                if(!IsHeadPettingPoint(now)){pettingDistance=0;lastPetPoint=now;return;}
                if ((DateTime.Now-pettingStarted).TotalSeconds > 1.3) { pettingStarted = DateTime.Now; pettingDistance = 0; lastPetPoint = now; }
                pettingDistance += Math.Abs(now.X-lastPetPoint.X) + Math.Abs(now.Y-lastPetPoint.Y); lastPetPoint = now;
                if (pettingDistance > 150) { pettingDistance = 0; pettingStarted = DateTime.Now; React("呼噜呼噜… ♡", true); }
            };
            var menu = new ContextMenu();
            var imageAiSettings = new MenuItem { Header = "文本 / 图像模型设置" }; imageAiSettings.Click += delegate { ShowPocketModelSettings(); };
            var windAiSettings = new MenuItem { Header = "Wind AI 模型设置" }; windAiSettings.Click += delegate { ShowWindAiModelSettings(); };
            var marketSettings = new MenuItem { Header = "盯盘与 Wind Key 设置" }; marketSettings.Click += delegate { ToggleMarketPanel(); };
            var movementSettings = new MenuItem { Header = "活动范围与自动移动" }; movementSettings.Click += delegate { OpenMovementSettings(); };
            var letters = new MenuItem { Header = "账号与来信" }; letters.Click += delegate { OpenMessengerPanel(); };
            var topmost = new MenuItem { Header = "保持最前", IsCheckable = true, IsChecked = true };
            topmost.Click += delegate { pet.Topmost = topmost.IsChecked;if(panel!=null)panel.Topmost=topmost.IsChecked;if (stashPanel != null) stashPanel.Topmost = topmost.IsChecked;if(marketPanel!=null)marketPanel.Topmost=topmost.IsChecked;if(launcherPanel!=null)launcherPanel.Topmost=topmost.IsChecked;if(movementSettingsPanel!=null)movementSettingsPanel.Topmost=topmost.IsChecked;if(skinWardrobePanel!=null)skinWardrobePanel.Topmost=topmost.IsChecked; SetAiTopmost(topmost.IsChecked);SetCommunityTopmost(topmost.IsChecked);SetMessengerTopmost(topmost.IsChecked); };
            var exit = new MenuItem { Header = "退出博道咪" }; exit.Click += delegate { Exit(); };
            menu.Items.Add(letters);menu.Items.Add(imageAiSettings);menu.Items.Add(windAiSettings);menu.Items.Add(marketSettings);menu.Items.Add(movementSettings);menu.Items.Add(new Separator());AddPetExperienceMenu(menu);AddSkinMenu(menu);menu.Items.Add(new Separator());menu.Items.Add(topmost);menu.Items.Add(new Separator());menu.Items.Add(exit);
            StylePetContextMenu(menu);
            pet.ContextMenu = menu;
            pet.Closed += delegate { if (!exiting) Exit(); };
        }

        void StylePetContextMenu(ContextMenu menu)
        {
            if(menu==null)return;
            menu.Background=Ui.Card;
            menu.Foreground=Ui.Ink;
            menu.BorderBrush=Ui.Line;
            menu.BorderThickness=new Thickness(1);
            menu.Padding=new Thickness(6);
            menu.FontFamily=new FontFamily("Microsoft YaHei UI");
            menu.FontSize=13;
            menu.SnapsToDevicePixels=true;
            menu.Effect=Ui.NewCardShadow();

            var itemStyle=new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,Ui.Ink));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,7,12,7)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,36.0));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,1,0,1)));
            var hover=new Trigger{Property=MenuItem.IsHighlightedProperty,Value=true};
            hover.Setters.Add(new Setter(Control.BackgroundProperty,Ui.AccentSoft));
            hover.Setters.Add(new Setter(Control.ForegroundProperty,Ui.AccentDeep));
            itemStyle.Triggers.Add(hover);
            var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.45));
            itemStyle.Triggers.Add(disabled);
            menu.Resources[typeof(MenuItem)]=itemStyle;

            var separatorStyle=new Style(typeof(Separator));
            separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(9,5,9,5)));
            separatorStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty,1.0));
            separatorStyle.Setters.Add(new Setter(Control.BackgroundProperty,Ui.Line));
            menu.Resources[typeof(Separator)]=separatorStyle;
        }

        void BuildPanel()
        {
            panel = new Window { Title = "博道咪工作簿", Width = 520, Height = 780,MinWidth=440,MinHeight=630, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false, AllowsTransparency = true,
                Background = Brushes.Transparent, Topmost = true };
            var outer = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(22) };
            Ui.StyleCard(outer); Ui.StyleWindow(panel);
            var rootGrid = new Grid();
            for(int row=0;row<9;row++)rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(0,0,0,14) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = new StackPanel();
            heading.Children.Add(Ui.Title("工作记录", 22));
            heading.Children.Add(Ui.Subtitle("事项、日程与重复提醒统一管理"));
            header.Children.Add(heading);
            var close = Ui.MakeCloseButton(); close.Click += delegate { panel.Hide(); };
            Grid.SetColumn(close, 1); header.Children.Add(close); Grid.SetRow(header, 0); rootGrid.Children.Add(header);AddShelfControl(panel,header,close);EnableWindowInteraction(panel,header);

            titleBox = new TextBox { Height = 40, FontSize = 15, Padding = new Thickness(11,8,11,8), BorderBrush = Ui.InputLine, ToolTip = "事项内容；不设置日期时可作为普通记录" };
            titleBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) SaveTaskFromEditor(); };
            Grid.SetRow(titleBox, 1); rootGrid.Children.Add(titleBox);

            var schedule=new Grid{Margin=new Thickness(0,8,0,0)};schedule.ColumnDefinitions.Add(new ColumnDefinition());schedule.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            dueDatePicker=new DatePicker{Height=34,ToolTip="仅当天提醒日期；留空表示普通记录"};schedule.Children.Add(dueDatePicker);var timePanel=new WrapPanel{Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};timePanel.Children.Add(new TextBlock{Text="时间",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,5,0)});dueHourBox=new ComboBox{Width=62,Height=34,Padding=new Thickness(6,4,6,4),ItemsSource=Enumerable.Range(0,24).Select(x=>x.ToString("00")).ToList()};dueMinuteBox=new ComboBox{Width=62,Height=34,Padding=new Thickness(6,4,6,4),ItemsSource=Enumerable.Range(0,60).Select(x=>x.ToString("00")).ToList()};timePanel.Children.Add(dueHourBox);timePanel.Children.Add(new TextBlock{Text=":",FontSize=18,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3,0,3,0)});timePanel.Children.Add(dueMinuteBox);Grid.SetColumn(timePanel,1);schedule.Children.Add(timePanel);Grid.SetRow(schedule,2);rootGrid.Children.Add(schedule);

            var meta=new Grid{Margin=new Thickness(0,7,0,0)};meta.ColumnDefinitions.Add(new ColumnDefinition());meta.ColumnDefinitions.Add(new ColumnDefinition());
            taskCategoryBox=new ComboBox{Height=34,IsEditable=true,Padding=new Thickness(8,4,8,4),ToolTip="选择或直接输入新分类"};taskRepeatBox=new ComboBox{Height=34,Margin=new Thickness(6,0,0,0),Padding=new Thickness(8,4,8,4),ItemsSource=new[]{"仅当天（一次）","每天","每个工作日","每周指定日","每月"},SelectedIndex=0};taskRepeatBox.SelectionChanged+=delegate{UpdateTaskScheduleEditor();};meta.Children.Add(taskCategoryBox);Grid.SetColumn(taskRepeatBox,1);meta.Children.Add(taskRepeatBox);Grid.SetRow(meta,3);rootGrid.Children.Add(meta);

            var recurrenceOptions=new StackPanel{Margin=new Thickness(0,6,0,0)};var weekdays=new WrapPanel();weekdayBoxes=new CheckBox[7];string[] weekdayNames={"一","二","三","四","五","六","日"};for(int dayIndex=0;dayIndex<7;dayIndex++){weekdayBoxes[dayIndex]=new CheckBox{Content="周"+weekdayNames[dayIndex],Margin=new Thickness(3,0,9,0)};weekdays.Children.Add(weekdayBoxes[dayIndex]);}weekdayPanel=weekdays;recurrenceOptions.Children.Add(weekdays);var monthDayRow=new WrapPanel();monthDayRow.Children.Add(new TextBlock{Text="每月",VerticalAlignment=VerticalAlignment.Center});monthDayBox=new ComboBox{Width=70,Height=32,Margin=new Thickness(6,0,6,0),ItemsSource=Enumerable.Range(1,31).ToList(),SelectedIndex=0};monthDayRow.Children.Add(monthDayBox);monthDayRow.Children.Add(new TextBlock{Text="日提醒",VerticalAlignment=VerticalAlignment.Center});monthDayPanel=monthDayRow;recurrenceOptions.Children.Add(monthDayRow);Grid.SetRow(recurrenceOptions,4);rootGrid.Children.Add(recurrenceOptions);

            var skipRow=new WrapPanel{Margin=new Thickness(0,6,0,0)};skipWeekendsBox=new CheckBox{Content="跳过周末",Margin=new Thickness(4,0,14,0),VerticalAlignment=VerticalAlignment.Center};skipHolidaysBox=new CheckBox{Content="跳过法定节假日",VerticalAlignment=VerticalAlignment.Center};skipRow.Children.Add(skipWeekendsBox);skipRow.Children.Add(skipHolidaysBox);Grid.SetRow(skipRow,5);rootGrid.Children.Add(skipRow);

            var quick = new WrapPanel { Margin = new Thickness(0,8,0,10) };
            var in15 = MakeButton("15 分钟后", new SolidColorBrush(Color.FromRgb(244,238,233))); in15.Click += delegate { SetDue(DateTime.Now.AddMinutes(15)); };
            var in60 = MakeButton("1 小时后", new SolidColorBrush(Color.FromRgb(244,238,233))); in60.Click += delegate { SetDue(DateTime.Now.AddHours(1)); };
            var tomorrow = MakeButton("明天 9:00", new SolidColorBrush(Color.FromRgb(244,238,233))); tomorrow.Click += delegate { SetDue(DateTime.Today.AddDays(1).AddHours(9)); };
            var noAlert=MakeButton("无提醒",Brushes.Transparent);noAlert.Click+=delegate{taskRepeatBox.SelectedIndex=0;dueDatePicker.SelectedDate=null;};quick.Children.Add(in15); quick.Children.Add(in60); quick.Children.Add(tomorrow);quick.Children.Add(noAlert); Grid.SetRow(quick, 6); rootGrid.Children.Add(quick);

            var editorActions=new WrapPanel();taskSaveButton=MakeButton("保存事项",new SolidColorBrush(Color.FromRgb(255,126,115)));taskSaveButton.Foreground=Brushes.White;taskSaveButton.Click+=delegate{SaveTaskFromEditor();};var cancelEdit=MakeButton("取消编辑",new SolidColorBrush(Color.FromRgb(238,234,231)));cancelEdit.Click+=delegate{ClearTaskEditor();};editorActions.Children.Add(taskSaveButton);editorActions.Children.Add(cancelEdit);Grid.SetRow(editorActions,7);rootGrid.Children.Add(editorActions);

            var filterRow=new Grid{Margin=new Thickness(0,9,0,5)};filterRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});filterRow.ColumnDefinitions.Add(new ColumnDefinition());filterRow.Children.Add(new TextBlock{Text="分类筛选",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});taskFilterBox=new ComboBox{Height=32,Padding=new Thickness(8,4,8,4)};taskFilterBox.SelectionChanged+=delegate{RefreshTasks();};Grid.SetColumn(taskFilterBox,1);filterRow.Children.Add(taskFilterBox);Grid.SetRow(filterRow,8);rootGrid.Children.Add(filterRow);

            taskList = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 14 };
            taskList.MouseDoubleClick += delegate { ToggleDone(); };
            var listBorder = new Border { Background = Ui.Inner, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = taskList };
            Grid.SetRow(listBorder, 9); rootGrid.Children.Add(listBorder);

            var actions = new Grid { Margin = new Thickness(0,10,0,0) };actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());
            var done = MakeButton("完成 / 恢复", new SolidColorBrush(Color.FromRgb(231,243,236))); done.Click += delegate { ToggleDone(); };
            var edit=MakeButton("编辑",new SolidColorBrush(Color.FromRgb(244,238,233)));edit.Click+=delegate{EditSelectedTask();};Grid.SetColumn(edit,1);var delete = MakeButton("删除", new SolidColorBrush(Color.FromRgb(248,232,230))); delete.Click += delegate { DeleteTask(); }; Grid.SetColumn(delete, 2);
            var clear = MakeButton("清理完成", new SolidColorBrush(Color.FromRgb(238,234,231))); clear.Click += delegate { tasks.RemoveAll(t => t.Done); SaveTasks(); RefreshTasks(); }; Grid.SetColumn(clear, 3);
            actions.Children.Add(done);actions.Children.Add(edit); actions.Children.Add(delete); actions.Children.Add(clear); Grid.SetRow(actions, 10); rootGrid.Children.Add(actions);
            outer.Child = rootGrid; panel.Content = outer;
            panel.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel = true; panel.Hide(); } };
            RefreshTaskCategories();ClearTaskEditor();
        }

        void BuildStashPanel()
        {
            stashPanel = new Window { Title = "博道咪中转袋", Width = 760, Height = 760,MinWidth=600,MinHeight=500, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false, AllowsTransparency = true,
                Background = Brushes.Transparent, Topmost = true };
            var outer = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(22) };
            Ui.StyleCard(outer); Ui.StyleWindow(stashPanel);
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin=new Thickness(2,0,0,14) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = new StackPanel();var title = Ui.Title("中转袋", 22);
            title.VerticalAlignment = VerticalAlignment.Center;heading.Children.Add(title);heading.Children.Add(Ui.Subtitle("资料集中暂存、检索、预览与跨应用拖放"));
            header.Children.Add(heading);
            var close = Ui.MakeCloseButton(); close.Click += delegate { stashPanel.Hide(); };
            Grid.SetColumn(close,1); header.Children.Add(close); Grid.SetRow(header,0); layout.Children.Add(header);AddShelfControl(stashPanel,header,close);EnableWindowInteraction(stashPanel,header);

            var hint = new TextBlock { Text = "文件仅保存在本机 · 支持 Office、PDF、PSD、压缩包、程序与文件夹",
                FontSize = 11.5, Foreground = Ui.SubInk,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2,0,0,10) };
            Grid.SetRow(hint,1); layout.Children.Add(hint);

            var filters=new Grid{Margin=new Thickness(0,0,0,8)};filters.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});filters.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            stashSearchBox=new TextBox{Height=36,Padding=new Thickness(10,6,10,6),ToolTip="搜索文件名、正文、项目、公司、行业、指数、客户、标签和备注"};stashSearchBox.TextChanged+=delegate{RefreshStash();};filters.Children.Add(stashSearchBox);
            var filterRow=new Grid{Margin=new Thickness(0,6,0,0)};filterRow.ColumnDefinitions.Add(new ColumnDefinition());filterRow.ColumnDefinitions.Add(new ColumnDefinition());filterRow.ColumnDefinitions.Add(new ColumnDefinition());filterRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            stashTypeFilter=new ComboBox{Height=33,Padding=new Thickness(7,4,7,4),ItemsSource=new[]{"全部类型","文本","图片","Office","PDF","压缩包","PSD","程序","文件夹","其他"},SelectedIndex=0};stashTypeFilter.SelectionChanged+=delegate{if(!stashFilterUpdating)RefreshStash();};filterRow.Children.Add(stashTypeFilter);
            stashDateFilter=new ComboBox{Height=33,Margin=new Thickness(5,0,0,0),Padding=new Thickness(7,4,7,4),ItemsSource=new[]{"全部日期","今天","近7天","近30天"},SelectedIndex=0};stashDateFilter.SelectionChanged+=delegate{if(!stashFilterUpdating)RefreshStash();};Grid.SetColumn(stashDateFilter,1);filterRow.Children.Add(stashDateFilter);
            stashSourceFilter=new ComboBox{Height=33,Margin=new Thickness(5,0,0,0),Padding=new Thickness(7,4,7,4),MinWidth=120};stashSourceFilter.SelectionChanged+=delegate{if(!stashFilterUpdating)RefreshStash();};Grid.SetColumn(stashSourceFilter,2);filterRow.Children.Add(stashSourceFilter);
            stashFavoriteOnly=new CheckBox{Content="只看收藏",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(9,0,2,0)};stashFavoriteOnly.Checked+=delegate{RefreshStash();};stashFavoriteOnly.Unchecked+=delegate{RefreshStash();};Grid.SetColumn(stashFavoriteOnly,3);filterRow.Children.Add(stashFavoriteOnly);Grid.SetRow(filterRow,1);filters.Children.Add(filterRow);Grid.SetRow(filters,2);layout.Children.Add(filters);

            stashList = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13,
                Padding = new Thickness(2), AllowDrop = false,SelectionMode=SelectionMode.Extended };
            stashList.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) {
                stashDragStart = e.GetPosition(stashList);
                var container = ItemsControl.ContainerFromElement(stashList, e.OriginalSource as DependencyObject) as ListBoxItem;
                pendingStashDrag = container == null ? null : container.Tag as StashItem;
                if (container != null) container.IsSelected = true;
            };
            stashList.PreviewMouseMove += delegate(object s, MouseEventArgs e) {
                if (pendingStashDrag == null || e.LeftButton != MouseButtonState.Pressed) return;
                Point now = e.GetPosition(stashList);
                if (Math.Abs(now.X-stashDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(now.Y-stashDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pendingStashDrag = null; BeginSelectedStashDrag(stashList);
            };
            stashList.PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) {
                StashItem clicked=pendingStashDrag; Point now=e.GetPosition(stashList);
                bool simpleClick=clicked!=null && Math.Abs(now.X-stashDragStart.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y-stashDragStart.Y)<SystemParameters.MinimumVerticalDragDistance;
                pendingStashDrag=null;
            };
            stashList.MouseDoubleClick += delegate { ShowStashPreview(SelectedStash()); };
            var listBorder = new Border { Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13), Padding = new Thickness(8), Child = stashList };
            Grid.SetRow(listBorder,3); layout.Children.Add(listBorder);

            var organize=new WrapPanel{Margin=new Thickness(0,9,0,0)};var preview=MakeButton("快速预览",Ui.Neutral);preview.Click+=delegate{ShowStashPreview(SelectedStash());};var metadata=MakeButton("标签 / 备注",Ui.Neutral);metadata.Click+=delegate{EditSelectedStashMetadata();};var favorite=MakeButton("★ 收藏 / 取消",Ui.Neutral);favorite.Click+=delegate{ToggleSelectedStashFavorite();};var audit=MakeButton("合规审核",new SolidColorBrush(Color.FromRgb(244,238,233)));audit.Click+=delegate{OpenComplianceReview(SelectedStash());};organize.Children.Add(preview);organize.Children.Add(metadata);organize.Children.Add(favorite);organize.Children.Add(audit);Grid.SetRow(organize,4);layout.Children.Add(organize);
            var actions=new Grid{Margin=new Thickness(0,7,0,0)};actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());var copy=MakeButton("复制所选",new SolidColorBrush(Color.FromRgb(231,243,236)));copy.Click+=delegate{CopySelectedStashes();};var remove=MakeButton("删除所选",new SolidColorBrush(Color.FromRgb(248,232,230)));remove.Click+=delegate{DeleteSelectedStashes();};Grid.SetColumn(remove,1);stashFilterSummary=new TextBlock{VerticalAlignment=VerticalAlignment.Center,Foreground=Ui.SubInk,Margin=new Thickness(9,0,4,0)};Grid.SetColumn(stashFilterSummary,2);var clear=MakeButton("清空全部",new SolidColorBrush(Color.FromRgb(238,234,231)));clear.Click+=delegate{ClearStash();};Grid.SetColumn(clear,3);actions.Children.Add(copy);actions.Children.Add(remove);actions.Children.Add(stashFilterSummary);actions.Children.Add(clear);Grid.SetRow(actions,5);layout.Children.Add(actions);

            outer.Child = layout; stashPanel.Content = outer;
            stashPanel.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel = true; stashPanel.Hide(); } };
        }

        void SaveTaskFromEditor()
        {
            string title=(titleBox.Text??"").Trim();if(title.Length==0){titleBox.Focus();return;}
            DateTime? due=null;TimeSpan time=SelectedTaskTime();
            string category=String.IsNullOrWhiteSpace(taskCategoryBox.Text)?"未分类":taskCategoryBox.Text.Trim();
            string repeat=Convert.ToString(taskRepeatBox.SelectedItem)??"仅当天（一次）";
            string selectedWeekdays=weekdayBoxes==null?null:String.Join(",",weekdayBoxes.Select((box,index)=>new{box,index}).Where(x=>x.box.IsChecked==true).Select(x=>(x.index+1).ToString()));
            if(repeat=="每周指定日"&&String.IsNullOrEmpty(selectedWeekdays)){Ui.Alert(panel,"还差一个选择","请选择至少一个星期几，再保存这条重复事项。");return;}
            TaskItem item=tasks.FirstOrDefault(x=>x.Id==editingTaskId);
            if(item==null){item=new TaskItem{Id=Guid.NewGuid().ToString("N"),Created=DateTime.Now.ToString("o")};tasks.Insert(0,item);}
            item.Title=title;item.Category=category;item.Repeat=repeat;item.SkipWeekends=skipWeekendsBox.IsChecked==true||repeat=="每个工作日";item.SkipHolidays=skipHolidaysBox.IsChecked==true;item.Weekdays=selectedWeekdays;item.MonthDay=monthDayBox.SelectedItem==null?1:Convert.ToInt32(monthDayBox.SelectedItem);
            if(repeat=="仅当天（一次）"){if(dueDatePicker.SelectedDate.HasValue)due=dueDatePicker.SelectedDate.Value.Date+time;else item.Due=null;}
            else due=FirstRecurringDue(item,DateTime.Now,time);
            if(due.HasValue)item.Due=AdjustAllowedDue(item,due.Value).ToString("o");item.Notified=false;item.Done=false;
            SaveTasks();RefreshTaskCategories();RefreshTasks();ClearTaskEditor();React("事项保存好啦～",true);
        }

        void UpdateTaskScheduleEditor()
        {
            if(taskRepeatBox==null||dueDatePicker==null)return;
            string repeat=Convert.ToString(taskRepeatBox.SelectedItem)??"仅当天（一次）";
            dueDatePicker.Visibility=repeat=="仅当天（一次）"?Visibility.Visible:Visibility.Collapsed;
            if(weekdayPanel!=null)weekdayPanel.Visibility=repeat=="每周指定日"?Visibility.Visible:Visibility.Collapsed;
            if(monthDayPanel!=null)monthDayPanel.Visibility=repeat=="每月"?Visibility.Visible:Visibility.Collapsed;
            if(repeat=="每个工作日"&&skipWeekendsBox!=null)skipWeekendsBox.IsChecked=true;
        }

        TimeSpan SelectedTaskTime()
        {
            int hour=9,minute=0;Int32.TryParse(Convert.ToString(dueHourBox.SelectedItem),out hour);Int32.TryParse(Convert.ToString(dueMinuteBox.SelectedItem),out minute);
            return new TimeSpan(Math.Max(0,Math.Min(23,hour)),Math.Max(0,Math.Min(59,minute)),0);
        }

        int WeekdayNumber(DayOfWeek day){return day==DayOfWeek.Sunday?7:(int)day;}
        HashSet<int> ParseWeekdays(string raw)
        {
            var result=new HashSet<int>();foreach(string part in (raw??"").Split(',')){int value;if(Int32.TryParse(part,out value)&&value>=1&&value<=7)result.Add(value);}return result;
        }

        DateTime MonthlyCandidate(int year,int month,int day,TimeSpan time){return new DateTime(year,month,Math.Min(Math.Max(1,day),DateTime.DaysInMonth(year,month)))+time;}
        DateTime FirstRecurringDue(TaskItem item,DateTime now,TimeSpan time)
        {
            string repeat=item.Repeat??"";
            if(repeat=="每周指定日")
            {
                var days=ParseWeekdays(item.Weekdays);
                for(int offset=0;offset<=7;offset++){DateTime candidate=now.Date.AddDays(offset)+time;if(candidate>now&&days.Contains(WeekdayNumber(candidate.DayOfWeek)))return AdjustAllowedDue(item,candidate);}
            }
            if(repeat=="每月")
            {
                DateTime candidate=MonthlyCandidate(now.Year,now.Month,item.MonthDay,time);if(candidate<=now){DateTime nextMonth=now.AddMonths(1);candidate=MonthlyCandidate(nextMonth.Year,nextMonth.Month,item.MonthDay,time);}return AdjustAllowedDue(item,candidate);
            }
            DateTime daily=now.Date+time;if(daily<=now)daily=daily.AddDays(1);return AdjustAllowedDue(item,daily);
        }

        TaskItem SelectedTask(){var row=taskList==null?null:taskList.SelectedItem as ListBoxItem;return row==null?null:row.Tag as TaskItem;}
        void EditSelectedTask(){TaskItem item=SelectedTask();if(item==null)return;editingTaskId=item.Id;titleBox.Text=item.Title??"";taskCategoryBox.Text=String.IsNullOrEmpty(item.Category)?"未分类":item.Category;string repeat=String.IsNullOrEmpty(item.Repeat)||item.Repeat=="仅一次"?"仅当天（一次）":item.Repeat;if(repeat=="每周")repeat="每周指定日";taskRepeatBox.SelectedItem=repeat;skipWeekendsBox.IsChecked=item.SkipWeekends;skipHolidaysBox.IsChecked=item.SkipHolidays;DateTime due;if(DateTime.TryParse(item.Due,out due)){dueDatePicker.SelectedDate=repeat=="仅当天（一次）"?(DateTime?)due.Date:null;dueHourBox.SelectedItem=due.ToString("HH");dueMinuteBox.SelectedItem=due.ToString("mm");if(repeat=="每周指定日"&&String.IsNullOrEmpty(item.Weekdays))item.Weekdays=WeekdayNumber(due.DayOfWeek).ToString();}else dueDatePicker.SelectedDate=null;var selectedDays=ParseWeekdays(item.Weekdays);for(int i=0;i<7;i++)weekdayBoxes[i].IsChecked=selectedDays.Contains(i+1);monthDayBox.SelectedItem=item.MonthDay>0?item.MonthDay:(DateTime.TryParse(item.Due,out due)?due.Day:1);UpdateTaskScheduleEditor();taskSaveButton.Content="保存修改";titleBox.Focus();}
        void ClearTaskEditor(){editingTaskId=null;if(titleBox==null)return;titleBox.Clear();dueDatePicker.SelectedDate=null;taskCategoryBox.Text="未分类";taskRepeatBox.SelectedIndex=0;dueHourBox.SelectedItem="09";dueMinuteBox.SelectedItem="00";for(int i=0;i<weekdayBoxes.Length;i++)weekdayBoxes[i].IsChecked=false;monthDayBox.SelectedIndex=0;skipWeekendsBox.IsChecked=false;skipHolidaysBox.IsChecked=false;taskSaveButton.Content="保存事项";UpdateTaskScheduleEditor();}
        void ToggleDone(){TaskItem item=SelectedTask();if(item!=null){item.Done=!item.Done;SaveTasks();RefreshTasks();}}
        void DeleteTask(){TaskItem item=SelectedTask();if(item!=null){tasks.Remove(item);SaveTasks();RefreshTaskCategories();RefreshTasks();if(editingTaskId==item.Id)ClearTaskEditor();}}
        void SetDue(DateTime value){taskRepeatBox.SelectedIndex=0;dueDatePicker.SelectedDate=value.Date;dueHourBox.SelectedItem=value.ToString("HH");dueMinuteBox.SelectedItem=value.ToString("mm");}
        string WeekdaySummary(string raw){string[] names={"周一","周二","周三","周四","周五","周六","周日"};return String.Join("、",ParseWeekdays(raw).OrderBy(x=>x).Select(x=>names[x-1]));}
        string FormatTask(TaskItem t)
        {
            string due=" · 不提醒";DateTime d;
            if(!String.IsNullOrEmpty(t.Due)&&DateTime.TryParse(t.Due,out d))
            {
                string repeat=String.IsNullOrEmpty(t.Repeat)?"仅当天（一次）":t.Repeat;
                if(repeat=="每天")due=" · 每天 "+d.ToString("HH:mm");else if(repeat=="每个工作日")due=" · 工作日 "+d.ToString("HH:mm");else if(repeat=="每周指定日"||repeat=="每周")due=" · "+WeekdaySummary(t.Weekdays)+" "+d.ToString("HH:mm");else if(repeat=="每月")due=" · 每月"+(t.MonthDay>0?t.MonthDay:d.Day)+"日 "+d.ToString("HH:mm");else due=" · "+d.ToString("MM-dd HH:mm");
            }
            return "["+(String.IsNullOrEmpty(t.Category)?"未分类":t.Category)+"] "+t.Title+due;
        }
        FrameworkElement TaskRowContent(TaskItem task)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(25)});row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon=new Grid{Width=17,Height=17,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(1,0,7,0)};
            icon.Children.Add(new System.Windows.Shapes.Ellipse{Fill=task.Done?Ui.Accent:Ui.AccentSoft,Stroke=Ui.Accent,StrokeThickness=1.5});
            if(task.Done)icon.Children.Add(new TextBlock{Text="✓",Foreground=Brushes.White,FontSize=11,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,-1,0,0)});
            else icon.Children.Add(new System.Windows.Shapes.Ellipse{Width=4,Height=4,Fill=Ui.Accent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
            row.Children.Add(icon);var label=new TextBlock{Text=FormatTask(task),Foreground=task.Done?Ui.SubInk:Ui.Ink,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};if(task.Done)label.TextDecorations=TextDecorations.Strikethrough;Grid.SetColumn(label,1);row.Children.Add(label);return row;
        }
        void RefreshTaskCategories(){if(taskCategoryBox==null||taskFilterBox==null)return;string categoryText=taskCategoryBox.Text,filter=Convert.ToString(taskFilterBox.SelectedItem)??"全部";var categories=new[]{"未分类","工作","会议","待办","生活"}.Concat(tasks.Select(x=>String.IsNullOrWhiteSpace(x.Category)?"未分类":x.Category)).Distinct().OrderBy(x=>x).ToList();taskCategoryBox.ItemsSource=categories;taskCategoryBox.Text=String.IsNullOrEmpty(categoryText)?"未分类":categoryText;taskFilterBox.ItemsSource=(new[]{"全部"}).Concat(categories).ToList();taskFilterBox.SelectedItem=((IEnumerable<string>)taskFilterBox.ItemsSource).Contains(filter)?filter:"全部";}
        void RefreshTasks(){if(taskList!=null){string filter=taskFilterBox==null?"全部":Convert.ToString(taskFilterBox.SelectedItem)??"全部";taskList.Items.Clear();foreach(var t in tasks.Where(x=>filter=="全部"||(String.IsNullOrEmpty(x.Category)?"未分类":x.Category)==filter)){var row=new ListBoxItem{Content=TaskRowContent(t),Tag=t,Padding=new Thickness(4),ToolTip=t.Title,HorizontalContentAlignment=HorizontalAlignment.Stretch};taskList.Items.Add(row);}}int active=tasks.Count(t=>!t.Done);if(badge!=null){badgeText.Text=active.ToString();badge.Visibility=!edgeHidden&&active>0?Visibility.Visible:Visibility.Collapsed;}}

        void LoadTasks() { try { if (File.Exists(dataFile)) tasks.AddRange(json.Deserialize<List<TaskItem>>(File.ReadAllText(dataFile)) ?? new List<TaskItem>()); } catch { try { File.Copy(dataFile, dataFile + ".bak", true); } catch { } } }
        void SaveTasks() { File.WriteAllText(dataFile, json.Serialize(tasks), System.Text.Encoding.UTF8); }
        void PositionPanel() { if(panel==null||shelvedWindows.ContainsKey(panel)||manuallyPlacedWindows.Contains(panel)||panel.WindowState!=WindowState.Normal)return; var work = SystemParameters.WorkArea; double left = pet.Left-panel.Width-12; if (left < work.Left) left = pet.Left+pet.Width+12; if (left+panel.Width > work.Right) left = work.Right-panel.Width-8; panel.Left = Math.Max(work.Left+8,left); panel.Top = Math.Max(work.Top+8,Math.Min(pet.Top,work.Bottom-panel.Height-8)); }
        void TogglePanel() { if(panel==null)BuildPanel();if(RestoreShelvedIfNeeded(panel)){titleBox.Focus();return;}if (panel.IsVisible) panel.Hide(); else { PositionPanel(); panel.Show(); panel.Activate(); titleBox.Focus(); } }

        bool CanAcceptStash(IDataObject data)
        {
            if (data == null) return false;
            return data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap) ||
                data.GetDataPresent(DataFormats.Html) || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);
        }

        void AcceptStashDrop(IDataObject data)
        {
            int added = 0,duplicates=0;
            try {
                if (data.GetDataPresent(DataFormats.FileDrop)) {
                    string[] paths = data.GetData(DataFormats.FileDrop) as string[];
                    if (paths != null) foreach (string path in paths) {
                        if (String.IsNullOrWhiteSpace(path)) continue;
                        string full = Path.GetFullPath(path);if(!Directory.Exists(full)&&!File.Exists(full))continue;
                        string kind = Directory.Exists(full) ? "folder" : (IsImageFile(full) ? "image" : "file");string saved=kind=="folder"?full:CopyFileIntoStash(full);
                        var item=new StashItem { Id = Guid.NewGuid().ToString("N"), Kind = kind,
                            Name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), Value = saved,
                            Owned = kind!="folder", Created = DateTime.Now.ToString("o"),SourceApp="文件拖放" };
                        if(AddStashItemSmart(item))added++;else duplicates++;
                    }
                } else if (data.GetDataPresent(DataFormats.Bitmap)) {
                    BitmapSource bitmap = data.GetData(DataFormats.Bitmap) as BitmapSource;
                    if (bitmap != null) {
                        string path = Path.Combine(stashDir, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0,6) + ".png");
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) encoder.Save(stream);
                        if(AddStashItemSmart(new StashItem { Id = Guid.NewGuid().ToString("N"), Kind = "image", Name = Path.GetFileName(path),
                            Value = path, Owned = true, Created = DateTime.Now.ToString("o"),SourceApp="剪贴板图片" }))added++;else duplicates++;
                    }
                } else if (data.GetDataPresent(DataFormats.Html) && QueueImageSource(ExtractImageSource(data.GetData(DataFormats.Html) as string))) {
                    added++;
                } else {
                    string text = data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string : data.GetData(DataFormats.Text) as string;
                    if (!String.IsNullOrWhiteSpace(text)) {
                        string candidate = text.Trim();
                        if (LooksLikeImageAddress(candidate) && QueueImageSource(candidate)) { added++; }
                        else {
                            if (text.Length > 1000000) text = text.Substring(0,1000000);
                            if(AddStashItemSmart(new StashItem { Id = Guid.NewGuid().ToString("N"), Kind = "text", Name = CompactText(text),
                                Value = text, Created = DateTime.Now.ToString("o"),SourceApp="剪贴板文字" }))added++;else duplicates++;
                        }
                    }
                }
            } catch (Exception ex) { React("这个内容没放进去：" + ex.Message, false); }

            if (added > 0) {
                SaveStash(); RefreshStash(); React("收好啦，共 " + stashItems.Count + " 项"+(duplicates>0?"；已合并 "+duplicates+" 个重复项":""), true);
            } else if(duplicates>0){SaveStash();RefreshStash();React("内容已存在，已记录重复次数",false);}else { petImage.Opacity = 1; speechBubble.Visibility = Visibility.Collapsed; }
        }

        string CompactText(string text)
        {
            string oneLine = String.Join(" ", text.Replace("\r", "").Split('\n').Where(line => line.Trim().Length > 0).Select(line => line.Trim()).Take(2));
            if (oneLine.Length == 0) oneLine = "空白文字";
            return oneLine.Length > 42 ? oneLine.Substring(0,42) + "…" : oneLine;
        }

        bool IsImageFile(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".gif" ||
                extension == ".bmp" || extension == ".tif" || extension == ".tiff" || extension == ".webp";
        }

        bool LooksLikeImageAddress(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 4096) return false;
            if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
            Uri uri; if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            if (uri.IsFile) return IsImageFile(uri.LocalPath);
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            return IsImageFile(uri.AbsolutePath);
        }

        string ExtractImageSource(string html)
        {
            if (String.IsNullOrWhiteSpace(html)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(html, "<img[^>]+src\\s*=\\s*[\\\"']([^\\\"']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : null;
        }

        string ImageExtensionFromType(string contentType, string fallback)
        {
            string type = (contentType ?? "").ToLowerInvariant();
            if (type.Contains("jpeg") || type.Contains("jpg")) return ".jpg";
            if (type.Contains("gif")) return ".gif";
            if (type.Contains("bmp")) return ".bmp";
            if (type.Contains("webp")) return ".webp";
            if (type.Contains("tiff")) return ".tif";
            string extension = Path.GetExtension(fallback ?? "").ToLowerInvariant();
            return IsImageFile("x" + extension) ? extension : ".png";
        }

        bool QueueImageSource(string source)
        {
            if (String.IsNullOrWhiteSpace(source)) return false;
            source = source.Trim();
            try {
                if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) {
                    int comma = source.IndexOf(','); if (comma < 0) return false;
                    string header = source.Substring(0,comma); byte[] bytes = Convert.FromBase64String(source.Substring(comma+1));
                    if (bytes.Length > 15*1024*1024) throw new InvalidDataException("图片超过 15 MB");
                    string extension = ImageExtensionFromType(header, null);
                    string path = Path.Combine(stashDir, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0,6) + extension);
                    File.WriteAllBytes(path,bytes);
                    AddStashItemSmart(new StashItem { Id=Guid.NewGuid().ToString("N"),Kind="image",Name=Path.GetFileName(path),Value=path,Owned=true,Created=DateTime.Now.ToString("o"),SourceApp="网页图片" });return true;
                }

                Uri uri; if (!Uri.TryCreate(source,UriKind.Absolute,out uri)) return false;
                if (uri.IsFile && File.Exists(uri.LocalPath) && IsImageFile(uri.LocalPath)) {
                    AddStashItemSmart(new StashItem { Id=Guid.NewGuid().ToString("N"),Kind="image",Name=Path.GetFileName(uri.LocalPath),Value=uri.LocalPath,Created=DateTime.Now.ToString("o"),SourceApp="本地链接" });return true;
                }
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

                var item = new StashItem { Id=Guid.NewGuid().ToString("N"),Kind="imagePending",Name="正在获取图片…",Value=source,Created=DateTime.Now.ToString("o"),SourceApp="网页图片" };
                stashItems.Insert(0,item);
                var client = new System.Net.WebClient(); client.Headers[System.Net.HttpRequestHeader.UserAgent] = "MomoPet/1.0";
                client.DownloadDataCompleted += delegate(object sender, System.Net.DownloadDataCompletedEventArgs e) {
                    UiPost(new Action(delegate {
                        try {
                            if (e.Cancelled || e.Error != null) throw e.Error ?? new IOException("下载被取消");
                            if (e.Result == null || e.Result.Length == 0 || e.Result.Length > 15*1024*1024) throw new InvalidDataException("图片为空或超过 15 MB");
                            string extension = ImageExtensionFromType(client.ResponseHeaders == null ? null : client.ResponseHeaders["Content-Type"], uri.AbsolutePath);
                            string path = Path.Combine(stashDir, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0,6) + extension);
                            File.WriteAllBytes(path,e.Result);stashItems.Remove(item);item.Kind="image";item.Name=Path.GetFileName(path);item.Value=path;item.Owned=true;AddStashItemSmart(item);
                            SaveStash(); RefreshStash(); React("图片预览准备好啦",false);
                        } catch { stashItems.Remove(item); SaveStash(); RefreshStash(); React("这张网页图片没有取到",false); }
                        finally { client.Dispose(); }
                    }));
                };
                client.DownloadDataAsync(uri); return true;
            } catch (Exception ex) { React("图片没有存进去："+ex.Message,false); return false; }
        }

        string FormatStash(StashItem item)
        {
            string icon = item.Kind == "text" ? "文" : (item.Kind == "image" || item.Kind == "imagePending" ? "图" : (item.Kind == "folder" ? "夹" : FileIcon(item.Value)));
            DateTime created; string time = DateTime.TryParse(item.Created, out created) ? created.ToString("HH:mm") : "";
            return icon + "  " + item.Name + (time.Length > 0 ? "    " + time : "");
        }

        string FileIcon(string path)
        {
            string ext=Path.GetExtension(path??"").ToLowerInvariant();if(ext==".xlsx"||ext==".xls"||ext==".csv")return "表";if(ext==".ppt"||ext==".pptx")return "演";if(ext==".doc"||ext==".docx")return "档";if(ext==".pdf")return "PDF";if(ext==".zip"||ext==".rar"||ext==".7z")return "包";if(ext==".psd")return "PSD";if(ext==".md")return "MD";if(ext==".txt")return "TXT";if(ext==".exe"||ext==".msi")return "APP";return "件";
        }

        string CopyFileIntoStash(string source)
        {
            Directory.CreateDirectory(stashDir);string extension=Path.GetExtension(source);string name=Path.GetFileNameWithoutExtension(source);string target=Path.Combine(stashDir,DateTime.Now.ToString("yyyyMMdd-HHmmss-")+Guid.NewGuid().ToString("N").Substring(0,6)+"-"+name+extension);File.Copy(source,target,false);return target;
        }

        BitmapSource LoadStashPreview(StashItem item)
        {
            if (item == null || item.Kind != "image" || !File.Exists(item.Value)) return null;
            try {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 112; image.UriSource = new Uri(item.Value); image.EndInit(); image.Freeze(); return image;
            } catch { return null; }
        }

        ListBoxItem MakeStashRow(StashItem item)
        {
            var row = new ListBoxItem { Tag = item, Padding = new Thickness(9), Margin=new Thickness(0,0,0,6), Background=Ui.Card, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            if (item.Kind == "image" || item.Kind == "imagePending") {
                var content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                content.ColumnDefinitions.Add(new ColumnDefinition());
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var previewBorder = new Border { Width = 70, Height = 58, CornerRadius = new CornerRadius(8),
                    Background = Ui.Neutral, ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Left };
                BitmapSource preview = LoadStashPreview(item);
                if (preview != null) previewBorder.Child = new Image { Source = preview, Stretch = Stretch.Uniform };
                else previewBorder.Child = new TextBlock { Text = item.Kind == "imagePending" ? "载入中…" : "无法预览",
                    Foreground = Ui.SubInk, FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(previewBorder);
                var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock { Text = (item.Favorite?"★ ":"")+item.Name, FontWeight = FontWeights.Bold, FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis });
                details.Children.Add(new TextBlock { Text = item.Kind == "imagePending" ? "正在保存网页图片" : "双击预览 · 可多选拖出",
                    FontSize = 10.5, Foreground = Ui.SubInk, Margin = new Thickness(0,4,0,0) });
                string metadata=StashMetadataLine(item);if(!String.IsNullOrWhiteSpace(metadata))details.Children.Add(new TextBlock{Text=metadata,FontSize=10,Foreground=Ui.SubInk,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,2,0,0)});
                Grid.SetColumn(details,1); content.Children.Add(details);
                // 一键直达：AI 图片直接把图带进编辑器；下载把图片另存到任意位置。
                var quickActions=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0)};
                var toAiImage=StashRowButton("AI 图片",new SolidColorBrush(Color.FromRgb(255,232,226)),delegate{OpenImageEditor(item);});
                toAiImage.ToolTip="把这张图片带入 AI 口袋的图片编辑区";
                var downloadImage=StashRowButton("下载",Ui.Neutral,delegate{DownloadStashImage(item);});
                downloadImage.ToolTip="把图片另存到桌面或任意文件夹";
                quickActions.Children.Add(toAiImage);quickActions.Children.Add(downloadImage);
                Grid.SetColumn(quickActions,2); content.Children.Add(quickActions); row.Content = content;
            } else {
                var details = new StackPanel();
                details.Children.Add(new TextBlock { Text = (item.Favorite?"★ ":"")+FormatStash(item), FontWeight = FontWeights.SemiBold,
                    FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
                if (item.Kind == "text") details.Children.Add(new TextBlock { Text = item.Value, FontSize = 11,
                    Foreground = Ui.SubInk, TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 38, Margin = new Thickness(22,3,0,0) });
                string metadata=StashMetadataLine(item);if(!String.IsNullOrWhiteSpace(metadata))details.Children.Add(new TextBlock{Text=metadata,FontSize=10,Foreground=Ui.SubInk,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(22,3,0,0)});if(!String.IsNullOrWhiteSpace(item.Note))details.Children.Add(new TextBlock{Text=item.Note,FontSize=10.5,Foreground=Ui.SubInk,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(22,2,0,0)});
                if (item.Kind == "text") {
                    // 文本一键送进 AI 对话，直接作为输入内容等待发送。
                    var textGrid=new Grid();textGrid.ColumnDefinitions.Add(new ColumnDefinition());textGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                    textGrid.Children.Add(details);
                    var toAiText=StashRowButton("AI 文字",new SolidColorBrush(Color.FromRgb(255,232,226)),delegate{OpenAiPocketChat(item.Value??"");});
                    toAiText.ToolTip="把这段文本带入 AI 口袋的对话输入框";
                    toAiText.VerticalAlignment=VerticalAlignment.Center;toAiText.Margin=new Thickness(6,0,0,0);
                    Grid.SetColumn(toAiText,1);textGrid.Children.Add(toAiText);
                    row.Content = textGrid;
                } else row.Content = details;
                row.ToolTip = item.Kind == "text" ? item.Value : item.Value;
            }
            return row;
        }

        void RefreshStash()
        {
            if (stashList != null) {
                RefreshStashFilterChoices();
                stashList.Items.Clear();
                var visible=FilteredStashItems().ToList();foreach (StashItem item in visible) {
                    stashList.Items.Add(MakeStashRow(item));
                }
                if(stashFilterSummary!=null)stashFilterSummary.Text=visible.Count==stashItems.Count?stashItems.Count+" 项":"显示 "+visible.Count+" / "+stashItems.Count;
            }
            if (pocketBadge != null) {
                pocketText.Text = "中转 " + stashItems.Count;
                // 中转入口统一放进鼠标悬停小猫时出现的快捷气泡，不再长期占据桌面。
                pocketBadge.Visibility = Visibility.Collapsed;
            }
            RefreshShelfEntries();if(ShelfPeekItemCount()==0)HideShelfPeek();
        }

        void LoadStash()
        {
            try {
                if (File.Exists(stashFile)) stashItems.AddRange(json.Deserialize<List<StashItem>>(File.ReadAllText(stashFile)) ?? new List<StashItem>());
                stashItems.RemoveAll(item => item.Kind == "imagePending");
                foreach (StashItem item in stashItems){if(item.Kind == "file" && File.Exists(item.Value) && IsImageFile(item.Value))item.Kind="image";if(String.IsNullOrWhiteSpace(item.SourceApp))item.SourceApp="历史中转";}
            }
            catch { try { File.Copy(stashFile, stashFile + ".bak", true); } catch { } }
        }

        void UpgradeStoredImages()
        {
            bool changed = false;
            foreach (StashItem item in stashItems.ToList()) {
                if (item.Kind != "text" || !LooksLikeImageAddress(item.Value)) continue;
                stashItems.Remove(item);
                if (!QueueImageSource(item.Value)) stashItems.Add(item);
                changed = true;
            }
            if (changed) SaveStash();
        }

        void SaveStash() { string temp=stashFile+".tmp";File.WriteAllText(temp,json.Serialize(stashItems),new UTF8Encoding(false));File.Copy(temp,stashFile,true);File.Delete(temp); }

        DataObject MakeStashData(StashItem item)
        {
            if (item == null) return null;
            var data = new DataObject();
            if (item.Kind == "text") {
                data.SetData(DataFormats.UnicodeText, item.Value); data.SetData(DataFormats.Text, item.Value); return data;
            }
            if (!File.Exists(item.Value) && !Directory.Exists(item.Value)) { React("原文件已经不在了", false); return null; }
            data.SetData(DataFormats.FileDrop, new string[] { item.Value });
            if (item.Kind == "image" && File.Exists(item.Value)) {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(item.Value); image.EndInit(); image.Freeze(); data.SetData(DataFormats.Bitmap, image);
            }
            return data;
        }

        void BeginStashDrag(StashItem item, DependencyObject source)
        {
            try { DataObject data = MakeStashData(item); if (data != null) DragDrop.DoDragDrop(source, data, DragDropEffects.Copy); }
            catch (Exception ex) { React("拖不出去：" + ex.Message, false); }
        }

        StashItem SelectedStash()
        {
            var row = stashList == null ? null : stashList.SelectedItem as ListBoxItem;
            return row == null ? null : row.Tag as StashItem;
        }

        void CopySelectedStash()
        {
            StashItem item = SelectedStash(); if (item == null) return;
            try { DataObject data = MakeStashData(item); if (data != null) { Clipboard.SetDataObject(data, true); React("已经复制，可以粘贴啦", false); } }
            catch (Exception ex) { React("复制失败：" + ex.Message, false); }
        }

        void DeleteOwnedStashFile(StashItem item)
        {
            if (item == null || !item.Owned || String.IsNullOrEmpty(item.Value)) return;
            try {
                string full = Path.GetFullPath(item.Value);
                string ownedRoot = Path.GetFullPath(stashDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
            } catch { }
        }

        void DeleteSelectedStash()
        {
            StashItem item = SelectedStash(); if (item == null) return;
            DeleteOwnedStashFile(item); stashItems.Remove(item); SaveStash(); RefreshStash();
        }

        void ClearStash()
        {
            if(stashItems.Count>0&&!Ui.Confirm(stashPanel,"清空中转袋","将删除全部 "+stashItems.Count+" 项中转资料，收藏项也会一起删除。此操作无法撤销。","确认清空"))return;
            foreach (StashItem item in stashItems) DeleteOwnedStashFile(item);
            stashItems.Clear(); SaveStash(); RefreshStash();if(stashPreviewPanel!=null)stashPreviewPanel.Hide();
        }

        void PositionStashPanel()
        {
            if(manuallyPlacedWindows.Contains(stashPanel)||shelvedWindows.ContainsKey(stashPanel)||stashPanel.WindowState!=WindowState.Normal)return;
            var work = SystemParameters.WorkArea; double left = pet.Left-stashPanel.Width-12;
            if (left < work.Left) left = pet.Left+pet.Width+12;
            if (left+stashPanel.Width > work.Right) left = work.Right-stashPanel.Width-8;
            stashPanel.Left = Math.Max(work.Left+8,left);
            stashPanel.Top = Math.Max(work.Top+8,Math.Min(pet.Top,work.Bottom-stashPanel.Height-8));
        }

        void ToggleStashPanel()
        {
            if(stashPanel==null)BuildStashPanel();
            if(shelvedWindows.ContainsKey(stashPanel)){RestoreWindow(stashPanel);PositionStashPanel();return;}
            if (stashPanel.IsVisible) stashPanel.Hide();
            else { RefreshStash(); PositionStashPanel(); stashPanel.Show(); stashPanel.Activate(); }
        }

        void BuildMarketPanel()
        {
            marketPanel = new Window { Title = "博道咪盯盘", Width = 500, Height = 640,MinWidth=430,MinHeight=520, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false, AllowsTransparency = true,
                Background = Brushes.Transparent, Topmost = true };
            var outer = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(22), Margin = new Thickness(3) };
            Ui.StyleCard(outer); Ui.StyleWindow(marketPanel);
            var stack = new StackPanel();
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = new StackPanel();
            heading.Children.Add(Ui.Title("大盘盯盘", 22));
            heading.Children.Add(Ui.Subtitle("每半小时更新 · 11:30 午盘 · 15:00 收盘 · 支持手动刷新"));
            header.Children.Add(heading);
            var close = Ui.MakeCloseButton(); close.Click += delegate { marketPanel.Hide(); };
            Grid.SetColumn(close,1); header.Children.Add(close); stack.Children.Add(header);AddShelfControl(marketPanel,header,close);EnableWindowInteraction(marketPanel,header);
            stack.Children.Add(Ui.Hairline(new Thickness(0,14,0,15)));
            stack.Children.Add(Ui.SectionLabel("WIND API KEY"));
            windKeyBox = new PasswordBox { Height=38, Padding=new Thickness(11,8,11,8), FontSize=13, Margin=new Thickness(0,6,0,0),
                ToolTip="Key 由 Windows 当前用户加密保存，可随时修改" };
            stack.Children.Add(windKeyBox);
            stack.Children.Add(new TextBlock { Text = File.Exists(windKeyFile) ? "已加密保存（留空表示不修改）" : "尚未配置",
                FontSize=11, Foreground=Ui.SubInk, Margin=new Thickness(1,5,0,10) });
            var buttons = new WrapPanel();
            var save = MakeButton("保存并测试", Ui.Accent); save.Foreground=Brushes.White;
            save.Click += delegate { SaveWindKeyFromPanel(); };
            var refresh = MakeButton("立即刷新", Ui.Neutral); refresh.Click += delegate { FetchMarket(true); };
            var clear = MakeButton("删除 Key", Brushes.Transparent); clear.Foreground=Ui.Up; clear.Click += delegate { ClearWindKey(); };
            buttons.Children.Add(save); buttons.Children.Add(refresh); buttons.Children.Add(clear); stack.Children.Add(buttons);
            var quoteHeader = new Grid { Margin = new Thickness(1,15,1,7) };
            quoteHeader.ColumnDefinitions.Add(new ColumnDefinition()); quoteHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            quoteHeader.Children.Add(Ui.SectionLabel("实时行情 · 五大指数"));
            var legend = new TextBlock { Text = "涨红 · 跌绿", FontSize = 10.5, Foreground = Ui.SubInk, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(legend,1); quoteHeader.Children.Add(legend); stack.Children.Add(quoteHeader);
            marketStatusText = new RichTextBox {
                IsReadOnly=true, IsReadOnlyCaretVisible=false, AcceptsReturn=true,
                FontSize=13, Height=246, Padding=new Thickness(13,8,13,8),
                BorderThickness=new Thickness(0), Background=Ui.Card, Foreground=Ui.Ink,
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto, ToolTip="Wind 行情快照" };
            marketStatusText.Style = Ui.RoundedViewerStyle(typeof(RichTextBox));
            SetMarketStatusText(File.Exists(windKeyFile) ? "等待 Wind 行情…" : "填写 Key 后开始监控");stack.Children.Add(marketStatusText); outer.Child=stack; marketPanel.Content=outer;
            marketPanel.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel=true; marketPanel.Hide(); } };
        }

        void ToggleMarketPanel()
        {
            if(marketPanel==null)BuildMarketPanel();
            if(RestoreShelvedIfNeeded(marketPanel)){RefreshMarketStatus();return;}if (marketPanel.IsVisible) marketPanel.Hide();
            else { PositionMarketPanel(); RefreshMarketStatus(); marketPanel.Show(); marketPanel.Activate(); }
        }

        void PositionMarketPanel()
        {
            if(manuallyPlacedWindows.Contains(marketPanel)||shelvedWindows.ContainsKey(marketPanel)||marketPanel.WindowState!=WindowState.Normal)return;
            var work=SystemParameters.WorkArea; double left=pet.Left-marketPanel.Width-12;
            if(left<work.Left) left=pet.Left+pet.Width+12;
            marketPanel.Left=Math.Max(work.Left+8,Math.Min(left,work.Right-marketPanel.Width-8));
            marketPanel.Top=Math.Max(work.Top+8,Math.Min(pet.Top,work.Bottom-marketPanel.Height-8));
        }

        byte[] WindEntropy() { return Encoding.UTF8.GetBytes("MomoPet.Wind.ApiKey.v1"); }

        string LoadWindKey()
        {
            try {
                if(!File.Exists(windKeyFile)) return null;
                byte[] encrypted=File.ReadAllBytes(windKeyFile);
                byte[] plain=ProtectedData.Unprotect(encrypted,WindEntropy(),DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            } catch { return null; }
        }

        void SaveWindKeyFromPanel()
        {
            string key=(windKeyBox.Password??"").Trim();
            if(String.IsNullOrEmpty(key)) { if(File.Exists(windKeyFile)) { FetchMarket(true); return; } ShowMarketToast("还差一步", "请先填写 Wind API Key。"); return; }
            try {
                byte[] plain=Encoding.UTF8.GetBytes(key);
                File.WriteAllBytes(windKeyFile,ProtectedData.Protect(plain,WindEntropy(),DataProtectionScope.CurrentUser));
                Array.Clear(plain,0,plain.Length); windKeyBox.Clear();
                SetMarketStatusText("Key 已加密保存，正在连接 Wind…"); FetchMarket(true);
            } catch(Exception ex) { SetMarketStatusText("保存失败："+ex.Message); }
        }

        void ClearWindKey()
        {
            try { if(File.Exists(windKeyFile)) File.Delete(windKeyFile); } catch { }
            windKeyBox.Clear(); latestMarket.Clear(); SetMarketStatusText("Key 已删除，监控已停止"); React("盯盘钥匙已删除",false);
        }

        void LoadMarketAlerts()
        {
            try { if(File.Exists(marketStateFile)) marketAlerts=json.Deserialize<MarketAlertState>(File.ReadAllText(marketStateFile,Encoding.UTF8)); } catch { marketAlerts=new MarketAlertState(); }
            if(marketAlerts==null) marketAlerts=new MarketAlertState();
            if(marketAlerts.ScheduleVersion<2){marketAlerts.ScheduleVersion=2;marketAlerts.MiddaySent=false;marketAlerts.CloseSent=false;SaveMarketAlerts();}
        }

        void SaveMarketAlerts()
        {
            try { File.WriteAllText(marketStateFile,json.Serialize(marketAlerts),Encoding.UTF8); } catch { }
        }

        void CheckMarketSchedule()
        {
            if(!File.Exists(windKeyFile) || marketFetchInFlight) return;
            DateTime now=DateTime.Now;
            bool weekday=now.DayOfWeek!=DayOfWeek.Saturday && now.DayOfWeek!=DayOfWeek.Sunday;
            if(!weekday)return;string today=now.ToString("yyyy-MM-dd");if(marketAlerts.Date!=today){marketAlerts=new MarketAlertState{Date=today,ScheduleVersion=2};SaveMarketAlerts();}
            TimeSpan time=now.TimeOfDay;bool retryReady=(now-lastMarketFetch).TotalSeconds>=60;
            if(time>=new TimeSpan(15,0,0)&&!marketAlerts.CloseSent&&retryReady){FetchMarket(false,"close");return;}
            if(time>=new TimeSpan(11,30,0)&&time<new TimeSpan(15,0,0)&&!marketAlerts.MiddaySent&&retryReady){FetchMarket(false,"midday");return;}
            bool morning=time>=new TimeSpan(9,30,0)&&time<new TimeSpan(11,30,0),afternoon=time>=new TimeSpan(13,0,0)&&time<new TimeSpan(15,0,0);
            if(morning||afternoon){int minute=now.Minute<30?0:30;DateTime slot=new DateTime(now.Year,now.Month,now.Day,now.Hour,minute,0);if(lastMarketFetch<slot)FetchMarket(false,"regular");}
        }

        void FetchMarket(bool userInitiated,string scheduleEvent=null)
        {
            if(marketFetchInFlight) return;
            string key=LoadWindKey();
            if(String.IsNullOrEmpty(key)) { if(userInitiated) ShowMarketToast("尚未配置", "请先在“大盘监控”里填写 Wind API Key。"); return; }
            string skillDir=EmbeddedRuntime.ResolveSkillDirectory(Path.Combine(".agents","skills","wind-mcp-skill"),root);
            string cli=Path.Combine(skillDir,"scripts","cli.mjs");
            if(!File.Exists(cli)) { SetMarketStatusText("Wind 数据 skill 未安装"); return; }
            marketFetchInFlight=true; lastMarketFetch=DateTime.Now;
            if(marketStatusText!=null) SetMarketStatusText("正在读取 Wind 行情…");
            ThreadPool.QueueUserWorkItem(delegate {
                string requestName="request-momopet-"+Guid.NewGuid().ToString("N")+".json";
                string requestPath=Path.Combine(skillDir,"scripts",requestName);
                try {
                    var args=new Dictionary<string,object>();
                    // Wind MCP 当前偶发将标准代码错误送入实体识别层；此工具同样支持指数名称，
                    // 使用明确中文名可绕开该服务端识别缺陷，解析端再稳定映射回 Wind 代码。
                    args["windcode"]="上证指数,深证成指,创业板指,中证红利,科创综指";
                    args["indexes"]="最新交易日,交易时间,中文简称,最新成交价,前收盘价,今日开盘价,今日最高价,今日最低价,涨跌幅,成交量,成交额,交易状态";
                    File.WriteAllText(requestPath,json.Serialize(args),new UTF8Encoding(false));
                    var psi=new ProcessStartInfo { WorkingDirectory=skillDir, UseShellExecute=false,
                        CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true,
                        StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8,
                        Arguments="\""+cli+"\" call index_data get_index_price_indicators \"@scripts/"+requestName+"\"" };
                    EmbeddedRuntime.UseBundledNode(psi,root);
                    psi.EnvironmentVariables["WIND_API_KEY"]=key;
                    string output, errors; int exitCode;
                    using(var process=Process.Start(psi)) { output=process.StandardOutput.ReadToEnd(); errors=process.StandardError.ReadToEnd(); process.WaitForExit(); exitCode=process.ExitCode; }
                    try { File.WriteAllText(Path.Combine(dataDir,"market-last-response.json"),output,new UTF8Encoding(false)); } catch { }
                    UiPost(new Action(delegate {
                        marketFetchInFlight=false;
                        if(exitCode!=0) { HandleMarketError(output,errors,userInitiated); return; }
                        try { latestMarket=ParseMarketOutput(output); HandleMarketSnapshot(userInitiated,scheduleEvent); }
                        catch(Exception ex) { SetMarketStatusText("Wind 行情处理失败："+ex.Message); if(userInitiated) ShowMarketToast("行情处理失败",ex.Message); }
                    }));
                } catch(Exception ex) {
                    UiPost(new Action(delegate { marketFetchInFlight=false; SetMarketStatusText("连接失败："+ex.Message); }));
                } finally { key=null; try { if(File.Exists(requestPath)) File.Delete(requestPath); } catch { } }
            });
        }

        void HandleMarketError(string output,string errors,bool userInitiated)
        {
            string message="Wind 服务暂时不可用";
            try { var e=json.Deserialize<Dictionary<string,object>>(output); if(e!=null && e.ContainsKey("message")) message=Convert.ToString(e["message"]); } catch { if(!String.IsNullOrWhiteSpace(errors)) message=errors.Trim(); }
            SetMarketStatusText(message); if(userInitiated) ShowMarketToast("Wind 连接未完成",message);
        }

        List<MarketIndex> ParseMarketOutput(string output)
        {
            object rootObject=json.DeserializeObject(output); var found=new List<MarketIndex>();
            ExtractMarketObjects(rootObject,found,null);
            var unique=new List<MarketIndex>();
            foreach(var item in found) if(!unique.Any(x=>x.Code==item.Code)) unique.Add(item);
            if(unique.Count<5) throw new Exception("没有找到五个指数的完整行情行（当前 "+unique.Count+" 个）");
            return unique;
        }

        void ExtractMarketObjects(object value,List<MarketIndex> found,List<string> columns)
        {
            var dict=value as Dictionary<string,object>;
            if(dict!=null) {
                if(dict.ContainsKey("text") && dict["text"] is string) { try { ExtractMarketObjects(json.DeserializeObject((string)dict["text"]),found,null); } catch { } }
                List<string> localColumns=columns;
                if(dict.ContainsKey("columns")) localColumns=ReadColumnNames(dict["columns"]);
                if(dict.ContainsKey("rows")) ExtractMarketObjects(dict["rows"],found,localColumns);
                MarketIndex direct=MarketFromDictionary(dict); if(direct!=null) found.Add(direct);
                foreach(var pair in dict) if(pair.Key!="text" && pair.Key!="rows") ExtractMarketObjects(pair.Value,found,localColumns);
                return;
            }
            var array=value as object[];
            if(array!=null) {
                foreach(var item in array) {
                    var row=item as object[];
                    if(row!=null && columns!=null && columns.Count==row.Length) {
                        var mapped=new Dictionary<string,object>(); for(int i=0;i<row.Length;i++) mapped[columns[i]]=row[i];
                        MarketIndex parsed=MarketFromDictionary(mapped); if(parsed!=null) found.Add(parsed);
                    }
                    ExtractMarketObjects(item,found,columns);
                }
            }
        }

        List<string> ReadColumnNames(object value)
        {
            var names=new List<string>(); var array=value as object[]; if(array==null) return names;
            foreach(var item in array) {
                if(item is string) names.Add((string)item);
                else { var d=item as Dictionary<string,object>; string name=null; if(d!=null) foreach(string key in new[]{"name","title","field","key","label"}) if(d.ContainsKey(key)) { name=Convert.ToString(d[key]); break; } names.Add(name??""); }
            }
            return names;
        }

        MarketIndex MarketFromDictionary(Dictionary<string,object> d)
        {
            string code=FindString(d,"windcode","wind_code","Wind代码","代码","证券代码","WIND_CODE");
            string name=FindString(d,"中文简称","简称","name","sec_name","证券简称");
            if(String.IsNullOrEmpty(code)) {
                string all=String.Join(" ",d.Values.Select(x=>Convert.ToString(x)).ToArray());
                foreach(string candidate in new[]{"000001.SH","399001.SZ","399006.SZ","000922.CSI","000680.SH"})if(all.Contains(candidate)){code=candidate;break;}
            }
            code=(code??"").Trim().ToUpperInvariant();if(!new[]{"000001.SH","399001.SZ","399006.SZ","000922.CSI","000680.SH"}.Contains(code)) code=MarketCodeFromName(name);
            if(String.IsNullOrEmpty(code)) return null;
            double last,pct; if(!FindDouble(d,out last,"最新成交价","最新价","last","close")) return null;
            if(!FindDouble(d,out pct,"涨跌幅","pct_chg","change_pct")) return null;
            return new MarketIndex { Code=code, Name=MarketIndexName(code),
                Last=last,Pct=pct,TradeDate=FindString(d,"最新交易日","交易日期","trade_date","date"),TradeTime=FindString(d,"交易时间","time") };
        }

        string MarketIndexName(string code)
        {
            if(code=="000001.SH")return "上证指数";if(code=="399001.SZ")return "深证成指";if(code=="399006.SZ")return "创业板指";if(code=="000922.CSI")return "中证红利";if(code=="000680.SH")return "科创综指";return code;
        }

        string MarketCodeFromName(string name)
        {
            string value=(name??"").Trim();
            if(value.Contains("上证"))return "000001.SH";
            if(value.Contains("深证"))return "399001.SZ";
            if(value.Contains("创业板"))return "399006.SZ";
            if(value.Contains("中证红利"))return "000922.CSI";
            if(value.Contains("科创"))return "000680.SH";
            return null;
        }

        string FindString(Dictionary<string,object> d,params string[] keys)
        {
            foreach(string wanted in keys) foreach(var pair in d) if(String.Equals(pair.Key,wanted,StringComparison.OrdinalIgnoreCase)) return Convert.ToString(pair.Value);
            return null;
        }

        bool FindDouble(Dictionary<string,object> d,out double number,params string[] keys)
        {
            foreach(string key in keys) foreach(var pair in d) if(String.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase) && Double.TryParse(Convert.ToString(pair.Value),out number)) return true;
            number=0; return false;
        }

        void HandleMarketSnapshot(bool userInitiated,string scheduleEvent)
        {
            lastMarketSuccessfulFetch=DateTime.Now;RefreshMarketStatus();
            MarketIndex sse=latestMarket.FirstOrDefault(x=>x.Code=="000001.SH"); MarketIndex star=latestMarket.FirstOrDefault(x=>x.Code=="000680.SH");
            if(sse==null || star==null) return;
            DateTime tradeDate; bool today=TryParseTradeDate(sse.TradeDate,out tradeDate) && tradeDate.Date==DateTime.Today;
            if(!today) {if(scheduleEvent=="midday")marketAlerts.MiddaySent=true;if(scheduleEvent=="close")marketAlerts.CloseSent=true;SaveMarketAlerts();if(userInitiated) ShowMarketToast("最近交易日行情",MarketLines(latestMarket)); return; }
            string date=tradeDate.ToString("yyyy-MM-dd");
            if(marketAlerts.Date!=date) marketAlerts=new MarketAlertState { Date=date,ScheduleVersion=2 };
            if(scheduleEvent=="midday"){marketAlerts.MiddaySent=true;ShowMarketSummaryToast(false);SaveMarketAlerts();return;}
            if(scheduleEvent=="close"){marketAlerts.CloseSent=true;ShowMarketSummaryToast(true);SaveMarketAlerts();return;}
            bool beforeClose=DateTime.Now.TimeOfDay<new TimeSpan(15,0,0);
            if(beforeClose) {
                if(sse.Pct>=.5 && !marketAlerts.SseUp) { marketAlerts.SseUp=true; ShowMarketToast("上证指数涨幅提醒",String.Format("上证指数 {0:+0.00;-0.00}%  ·  {1:0.00} 点",sse.Pct,sse.Last)); }
                if(sse.Pct<=-.5 && !marketAlerts.SseDown) { marketAlerts.SseDown=true; ShowMarketToast("上证指数跌幅提醒",String.Format("上证指数 {0:+0.00;-0.00}%  ·  {1:0.00} 点",sse.Pct,sse.Last)); }
                if(star.Pct>=1.5 && !marketAlerts.StarUp) { marketAlerts.StarUp=true; ShowMarketToast("科创综指涨幅提醒",String.Format("科创综指 {0:+0.00;-0.00}%  ·  {1:0.00} 点",star.Pct,star.Last)); }
                if(star.Pct<=-1.5 && !marketAlerts.StarDown) { marketAlerts.StarDown=true; ShowMarketToast("科创综指跌幅提醒",String.Format("科创综指 {0:+0.00;-0.00}%  ·  {1:0.00} 点",star.Pct,star.Last)); }
            } else if(userInitiated) ShowMarketToast("Wind 指数行情",MarketLines(latestMarket));
            SaveMarketAlerts();
        }

        IEnumerable<MarketIndex> OrderedMarketIndexes(IEnumerable<MarketIndex> values)
        {
            string[] codes={"000001.SH","399001.SZ","399006.SZ","000922.CSI","000680.SH"};return codes.Select(code=>values.FirstOrDefault(x=>x.Code==code)).Where(x=>x!=null);
        }

        string MarketLines(IEnumerable<MarketIndex> values)
        {
            var ordered=OrderedMarketIndexes(values).ToList();if(ordered.Count==0)return "暂无指数行情";var builder=new StringBuilder();foreach(MarketIndex item in ordered)builder.AppendLine(String.Format("{0,-6}  {1,9:0.00}  {2,8:+0.00;-0.00;0.00}%",item.Name,item.Last,item.Pct));MarketIndex first=ordered[0];DateTimeOffset quoteTime;string shownTime=first.TradeTime;if(DateTimeOffset.TryParse(first.TradeTime,out quoteTime))shownTime=quoteTime.ToString("HH:mm:ss");builder.AppendLine("行情时间："+FormatTradeDate(first.TradeDate)+" "+shownTime);builder.Append("最新抓取："+(lastMarketSuccessfulFetch==DateTime.MinValue?DateTime.Now:lastMarketSuccessfulFetch).ToString("yyyy-MM-dd HH:mm:ss"));return builder.ToString();
        }

        bool TryParseTradeDate(string value,out DateTime date)
        {
            if(!String.IsNullOrEmpty(value) && value.Length==8) {
                int year,month,day;
                if(Int32.TryParse(value.Substring(0,4),out year) && Int32.TryParse(value.Substring(4,2),out month) && Int32.TryParse(value.Substring(6,2),out day)) {
                    try { date=new DateTime(year,month,day); return true; } catch { }
                }
            }
            return DateTime.TryParse(value,out date);
        }

        string FormatTradeDate(string value)
        {
            DateTime date; return TryParseTradeDate(value,out date)?date.ToString("yyyy-MM-dd"):value;
        }

        void SetMarketStatusText(string text)
        {
            if(marketStatusText==null)return;var document=new FlowDocument{PagePadding=new Thickness(2,14,2,2),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13,Foreground=Ui.Ink};document.Blocks.Add(new Paragraph(new Run(text??"")){Margin=new Thickness(0),LineHeight=22,Foreground=Ui.SubInk});marketStatusText.Document=document;
        }

        void SetMarketStatusSnapshot(IEnumerable<MarketIndex> values)
        {
            if(marketStatusText==null)return;
            var ordered=OrderedMarketIndexes(values).ToList();if(ordered.Count==0)return;
            var document=new FlowDocument{PagePadding=new Thickness(1,2,1,2),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13,Foreground=Ui.Ink};
            for(int i=0;i<ordered.Count;i++)
            {
                MarketIndex item=ordered[i];
                var row=new Grid{Height=40};
                row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(82)});
                row.Children.Add(new TextBlock{Text=item.Name,FontSize=13.5,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink,VerticalAlignment=VerticalAlignment.Center});
                var price=new TextBlock{Text=item.Last.ToString("0.00"),FontSize=13,Foreground=Ui.Ink,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
                Grid.SetColumn(price,1);row.Children.Add(price);
                Brush changeForeground,changeBackground;
                if(item.Pct>0){changeForeground=Ui.Up;changeBackground=Ui.UpSoft;}
                else if(item.Pct<0){changeForeground=Ui.Down;changeBackground=Ui.DownSoft;}
                else{changeForeground=Ui.SubInk;changeBackground=Ui.Inner;}
                var badge=Ui.PillBadge(item.Pct.ToString("+0.00;-0.00;0.00")+"%",changeForeground,changeBackground,66);
                Grid.SetColumn(badge,2);row.Children.Add(badge);
                document.Blocks.Add(new BlockUIContainer(row){Margin=new Thickness(0)});
                if(i<ordered.Count-1)document.Blocks.Add(new BlockUIContainer(Ui.Hairline(new Thickness(0))){Margin=new Thickness(0)});
            }
            MarketIndex first=ordered[0];DateTimeOffset quoteTime;string shownTime=first.TradeTime;if(DateTimeOffset.TryParse(first.TradeTime,out quoteTime))shownTime=quoteTime.ToString("HH:mm:ss");
            document.Blocks.Add(new Paragraph(new Run("行情时间 "+FormatTradeDate(first.TradeDate)+" "+shownTime+"  ·  最新抓取 "+(lastMarketSuccessfulFetch==DateTime.MinValue?DateTime.Now:lastMarketSuccessfulFetch).ToString("MM-dd HH:mm:ss"))){Margin=new Thickness(2,10,0,2),Foreground=Ui.SubInk,FontSize=11});
            marketStatusText.Document=document;
        }

        void RefreshMarketStatus()
        {
            if(marketStatusText==null) return;
            if(latestMarket.Count<5) { SetMarketStatusText(File.Exists(windKeyFile)?"Key 已安全保存，等待五个指数行情…":"填写 Key 后开始监控"); return; }
            SetMarketStatusSnapshot(latestMarket);
        }

        void ShowMarketToast(string title,string message)
        {
            SystemSounds.Asterisk.Play(); React("大盘有动静喵！",true);
            var toast=new Window { Width=440,Height=330,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=true };
            Ui.StyleWindow(toast);
            var border=new Border { CornerRadius=new CornerRadius(14), Margin=new Thickness(4,4,10,10) }; Ui.StyleCard(border); border.Padding=new Thickness(0);
            var layout=new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(4) }); layout.ColumnDefinitions.Add(new ColumnDefinition());
            var accentBar=new Border { Background=Ui.Accent, CornerRadius=new CornerRadius(14,0,0,14) };
            layout.Children.Add(accentBar);
            var stack=new StackPanel { Margin=new Thickness(15,14,15,12) }; stack.Children.Add(new TextBlock { Text="🐾  "+title,FontSize=15,FontWeight=FontWeights.Bold,Foreground=Ui.Ink });
            stack.Children.Add(new TextBox { Text=message,IsReadOnly=true,IsReadOnlyCaretVisible=false,AcceptsReturn=true,
                TextWrapping=TextWrapping.Wrap,FontSize=14,Foreground=Ui.Ink,
                Height=215,Margin=new Thickness(0,10,0,8),Padding=new Thickness(0),BorderThickness=new Thickness(0),
                Background=Brushes.Transparent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top,
                ToolTip="可框选文字，按 Ctrl+C 或右键复制" });
            var ok=MakeButton("知道啦",Ui.Accent); ok.Foreground=Brushes.White; ok.HorizontalAlignment=HorizontalAlignment.Right; ok.Width=90; ok.Click+=delegate { toast.Close(); };
            stack.Children.Add(ok); Grid.SetColumn(stack,1); layout.Children.Add(stack); border.Child=layout; toast.Content=border; var work=SystemParameters.WorkArea; toast.Left=work.Right-toast.Width-18; toast.Top=work.Bottom-toast.Height-18; toast.Show();
            Ui.AnimateToastIn(border);
        }

        Brush MarketChangeBrush(double pct)
        {
            return pct>0?Ui.Up:(pct<0?Ui.Down:Ui.SubInk);
        }

        TextBlock MarketCell(string text,int column,Brush foreground,FontWeight weight,HorizontalAlignment alignment)
        {
            var cell=new TextBlock { Text=text,FontSize=14,Foreground=foreground,FontWeight=weight,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=alignment };
            Grid.SetColumn(cell,column);return cell;
        }

        string MarketQuoteTime(MarketIndex first)
        {
            DateTimeOffset quoteTime;string shownTime=first.TradeTime;
            if(DateTimeOffset.TryParse(first.TradeTime,out quoteTime))shownTime=quoteTime.ToString("HH:mm:ss");
            return FormatTradeDate(first.TradeDate)+" "+shownTime;
        }

        void ShowMarketSummaryToast(bool isClose)
        {
            var ordered=OrderedMarketIndexes(latestMarket).ToList();if(ordered.Count==0)return;
            SystemSounds.Asterisk.Play();React(isClose?"收盘数据到啦！":"午盘数据到啦！",true);
            var toast=new Window { Width=510,Height=475,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=true };
            Ui.StyleWindow(toast);
            var card=new Border { CornerRadius=new CornerRadius(16), Margin=new Thickness(4,4,10,10) };Ui.StyleCard(card);card.Padding=new Thickness(0);
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });

            var header=new Border { Background=Ui.Card,CornerRadius=new CornerRadius(16,16,0,0),Padding=new Thickness(22,17,22,14) };
            var headerGrid=new Grid();headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var timeBadge=new Border { Background=Ui.Ink,CornerRadius=new CornerRadius(8),Padding=new Thickness(11,5,11,5),VerticalAlignment=VerticalAlignment.Center };
            timeBadge.Child=new TextBlock { Text=isClose?"15:00":"11:30",Foreground=Brushes.White,FontSize=13,FontWeight=FontWeights.Bold };
            headerGrid.Children.Add(timeBadge);
            var heading=new StackPanel { Margin=new Thickness(13,0,0,0),VerticalAlignment=VerticalAlignment.Center };
            heading.Children.Add(new TextBlock { Text=isClose?"博道咪收盘汇总":"博道咪午盘汇总",FontSize=18,FontWeight=FontWeights.Bold,Foreground=Ui.Ink });
            heading.Children.Add(new TextBlock { Text=isClose?"今日交易结束，五个指数收盘情况":"午间休市，五个指数最新情况",FontSize=11.5,Foreground=Ui.SubInk,Margin=new Thickness(0,3,0,0) });
            Grid.SetColumn(heading,1);headerGrid.Children.Add(heading);header.Child=headerGrid;root.Children.Add(header);
            var headerLine=Ui.Hairline(new Thickness(0));Grid.SetRow(headerLine,1);root.Children.Add(headerLine);

            var body=new StackPanel { Margin=new Thickness(20,10,20,8) };
            var tableHead=new Grid { Height=28,Margin=new Thickness(11,0,11,0) };
            tableHead.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1.35,GridUnitType.Star) });tableHead.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(.85,GridUnitType.Star) });tableHead.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            tableHead.Children.Add(MarketCell("指数",0,Ui.SubInk,FontWeights.SemiBold,HorizontalAlignment.Left));
            tableHead.Children.Add(MarketCell("涨跌幅",1,Ui.SubInk,FontWeights.SemiBold,HorizontalAlignment.Right));
            tableHead.Children.Add(MarketCell(isClose?"收盘点位":"最新点位",2,Ui.SubInk,FontWeights.SemiBold,HorizontalAlignment.Right));body.Children.Add(tableHead);
            body.Children.Add(Ui.Hairline(new Thickness(0,0,0,0)));
            for(int i=0;i<ordered.Count;i++)
            {
                MarketIndex item=ordered[i];var row=new Grid{Height=42,Margin=new Thickness(11,0,11,0)};row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1.35,GridUnitType.Star) });row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(.85,GridUnitType.Star) });row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                row.Children.Add(MarketCell(item.Name,0,Ui.Ink,FontWeights.SemiBold,HorizontalAlignment.Left));
                Brush changeForeground,changeBackground;if(item.Pct>0){changeForeground=Ui.Up;changeBackground=Ui.UpSoft;}else if(item.Pct<0){changeForeground=Ui.Down;changeBackground=Ui.DownSoft;}else{changeForeground=Ui.SubInk;changeBackground=Ui.Inner;}
                var pctBadge=Ui.PillBadge(item.Pct.ToString("+0.00;-0.00;0.00")+"%",changeForeground,changeBackground,66);pctBadge.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(pctBadge,1);row.Children.Add(pctBadge);
                row.Children.Add(MarketCell(item.Last.ToString("0.00"),2,Ui.Ink,FontWeights.SemiBold,HorizontalAlignment.Right));body.Children.Add(row);
                if(i<ordered.Count-1)body.Children.Add(Ui.Hairline(new Thickness(0)));
            }
            MarketIndex first=ordered[0];var timeCard=new Border { Background=Ui.Inner,CornerRadius=new CornerRadius(10),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,9,0,0) };
            var times=new Grid();times.ColumnDefinitions.Add(new ColumnDefinition());times.ColumnDefinitions.Add(new ColumnDefinition());
            var quoteStack=new StackPanel();quoteStack.Children.Add(new TextBlock { Text="行情时间",FontSize=10.5,Foreground=Ui.SubInk });quoteStack.Children.Add(new TextBlock { Text=MarketQuoteTime(first),FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink,Margin=new Thickness(0,2,0,0) });times.Children.Add(quoteStack);
            var fetchedStack=new StackPanel { HorizontalAlignment=HorizontalAlignment.Right };fetchedStack.Children.Add(new TextBlock { Text="最新抓取",FontSize=10.5,Foreground=Ui.SubInk,HorizontalAlignment=HorizontalAlignment.Right });fetchedStack.Children.Add(new TextBlock { Text=(lastMarketSuccessfulFetch==DateTime.MinValue?DateTime.Now:lastMarketSuccessfulFetch).ToString("yyyy-MM-dd HH:mm:ss"),FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink,Margin=new Thickness(0,2,0,0) });Grid.SetColumn(fetchedStack,1);times.Children.Add(fetchedStack);timeCard.Child=times;body.Children.Add(timeCard);
            Grid.SetRow(body,2);root.Children.Add(body);

            var footer=new Grid { Margin=new Thickness(20,0,20,16) };footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var hint=new TextBlock { Text="Wind 行情 · 涨红跌绿",FontSize=10.5,Foreground=Ui.SubInk,VerticalAlignment=VerticalAlignment.Center };footer.Children.Add(hint);
            var copy=MakeButton("复制数据",Ui.Neutral);copy.Width=92;copy.Margin=new Thickness(0,0,8,0);copy.Click+=delegate { try { Clipboard.SetText(MarketLines(ordered));copy.Content="已复制"; } catch { } };Grid.SetColumn(copy,1);footer.Children.Add(copy);
            var ok=MakeButton("知道啦",Ui.Accent);ok.Foreground=Brushes.White;ok.Width=88;ok.Click+=delegate { toast.Close(); };Grid.SetColumn(ok,2);footer.Children.Add(ok);Grid.SetRow(footer,3);root.Children.Add(footer);
            card.Child=root;toast.Content=card;var work=SystemParameters.WorkArea;toast.Left=work.Right-toast.Width-18;toast.Top=work.Bottom-toast.Height-18;toast.Show();Ui.AnimateToastIn(card);
        }

        void CheckReminders()
        {
            bool changed = false; DateTime due;
            foreach (var t in tasks.Where(x => !x.Done && !x.Notified && !String.IsNullOrEmpty(x.Due)).ToList())
                if (DateTime.TryParse(t.Due, out due) && due <= DateTime.Now) {ShowReminder("["+(String.IsNullOrEmpty(t.Category)?"未分类":t.Category)+"] "+t.Title);string repeat=String.IsNullOrEmpty(t.Repeat)?"仅当天（一次）":t.Repeat;if(repeat=="仅一次"||repeat=="仅当天（一次）")t.Notified=true;else{t.Due=NextTaskDue(t,due,DateTime.Now).ToString("o");t.Notified=false;}changed=true;}
            if (changed){SaveTasks();RefreshTasks();}
        }

        DateTime NextTaskDue(TaskItem item,DateTime previous,DateTime now)
        {
            string repeat=String.IsNullOrEmpty(item.Repeat)?"仅一次":item.Repeat;TimeSpan time=previous.TimeOfDay;
            if(repeat=="每周指定日"||repeat=="每周")
            {
                var days=ParseWeekdays(item.Weekdays);if(days.Count==0)days.Add(WeekdayNumber(previous.DayOfWeek));DateTime cursor=previous.Date.AddDays(1)+time;
                for(int guard=0;guard<3700;guard++,cursor=cursor.AddDays(1))if(cursor>now&&days.Contains(WeekdayNumber(cursor.DayOfWeek))){DateTime adjusted=AdjustAllowedDue(item,cursor);if(adjusted>now)return adjusted;}
            }
            if(repeat=="每月")
            {
                int day=item.MonthDay>0?item.MonthDay:previous.Day;DateTime cursor=previous.AddMonths(1);DateTime next=MonthlyCandidate(cursor.Year,cursor.Month,day,time);while(next<=now){cursor=cursor.AddMonths(1);next=MonthlyCandidate(cursor.Year,cursor.Month,day,time);}return AdjustAllowedDue(item,next);
            }
            DateTime daily=previous;do{daily=AdjustAllowedDue(item,daily.AddDays(1));}while(daily<=now);return daily;
        }

        DateTime AdjustAllowedDue(TaskItem item,DateTime value)
        {
            int guard=0;while(guard++<370){bool weekend=value.DayOfWeek==DayOfWeek.Saturday||value.DayOfWeek==DayOfWeek.Sunday;if((item.SkipWeekends&&weekend)||(item.SkipHolidays&&IsChineseHoliday(value.Date)))value=value.AddDays(1);else break;}return value;
        }

        bool IsChineseHoliday(DateTime date)
        {
            if((date.Month==1&&date.Day==1)||(date.Month==5&&date.Day>=1&&date.Day<=5)||(date.Month==10&&date.Day>=1&&date.Day<=7)||(date.Month==4&&date.Day>=4&&date.Day<=6))return true;
            try{var calendar=new ChineseLunisolarCalendar();int lunarYear=calendar.GetYear(date),month=calendar.GetMonth(date),leap=calendar.GetLeapMonth(lunarYear);if(leap>0&&month>=leap)month--;int day=calendar.GetDayOfMonth(date);if(month==1&&day<=7)return true;if(month==5&&day==5)return true;if(month==8&&day==15)return true;}catch{}return false;
        }

        void ShowReminder(string message)
        {
            SystemSounds.Asterisk.Play();
            React("时间到啦！", true);
            var toast = new Window { Width = 330, Height = 150, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true };
            Ui.StyleWindow(toast);
            var border = new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(4,4,10,10) }; Ui.StyleCard(border); border.Padding = new Thickness(0);
            var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); layout.ColumnDefinitions.Add(new ColumnDefinition());
            var accentBar = new Border { Background = Ui.Accent, CornerRadius = new CornerRadius(14,0,0,14) };
            layout.Children.Add(accentBar);
            var stack = new StackPanel { Margin = new Thickness(14,13,14,11) }; stack.Children.Add(new TextBlock { Text = "🐾  博道咪提醒你", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Ink });
            stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Ui.Ink, Margin = new Thickness(0,9,0,9) });
            var ok = MakeButton("知道啦", Ui.Accent); ok.Foreground = Brushes.White; ok.HorizontalAlignment = HorizontalAlignment.Right; ok.Width = 90; ok.Click += delegate { toast.Close(); };
            stack.Children.Add(ok); Grid.SetColumn(stack,1); layout.Children.Add(stack); border.Child = layout; toast.Content = border; var work = SystemParameters.WorkArea; toast.Left = work.Right-toast.Width-18; toast.Top = work.Bottom-toast.Height-18; toast.Show();
            Ui.AnimateToastIn(border);
        }

        void AnimateScale(double target)
        {
            petScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(160)));
            petScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(160)));
        }

        void React(string message, bool happy)
        {
            if (speechBubble == null||presentationQuiet||globalPetHidden) return;
            speechText.Text = message; speechBubble.Visibility = Visibility.Visible;
            speechTimer.Stop(); speechTimer.Start();
            if(petDragActive)return;
            if(shelfPeekPanel!=null&&shelfPeekPanel.IsVisible&&(petState=="walk"||petState=="run")){StartState("idle",2);return;}
            var bounce = new DoubleAnimation(1.0, happy ? 1.10 : 1.045, TimeSpan.FromMilliseconds(180)) { AutoReverse = true };
            petScale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            petScale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
            var wiggle = new DoubleAnimation(-2.5, 2.5, TimeSpan.FromMilliseconds(130)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) };
            petRotate.BeginAnimation(RotateTransform.AngleProperty, wiggle);
            if (happy) StartState("happy", 1.4);
        }

        void StartState(string state, double seconds)
        {
            if(petDragActive)return;
            if(shelfPeekPanel!=null&&shelfPeekPanel.IsVisible&&(petState=="walk"||petState=="run")){StartState("idle",2);return;}
            petState = state; stateStarted = DateTime.Now; stateUntil = DateTime.Now.AddSeconds(seconds);
            stateDuration = Math.Max(.1, seconds);
            strideDistance = 0;
            hopBaseTop = pet.Top; exactLeft = pet.Left;
            EnsureRendering();
        }

        void FaceOpenSpace()
        {
            var work = PetMovementBounds();
            double leftSpace = pet.Left-work.Left;
            double rightSpace = work.Right-(pet.Left+pet.Width);
            facing = rightSpace >= leftSpace ? 1 : -1;
        }

        bool TryEnterEdgeHide()
        {
            if(pet==null||edgeHidden)return false;
            // 收纳只认电脑屏幕的物理左右边缘，不能把“活动范围边缘”误当作屏幕边缘。
            Rect bounds=PetScreenBounds();const double trigger=24;
            bool left=pet.Left<=bounds.Left+trigger;
            bool right=pet.Left+pet.Width>=bounds.Right-trigger;
            if(!left&&!right)return false;
            EnterEdgeHide(left?"left":"right",bounds);return true;
        }

        void EnterEdgeHide(string side,Rect bounds)
        {
            edgeHidden=true;edgeHideSide=side;CancelLauncherOpen();if(launcherPanel!=null)launcherPanel.Hide();if(speechBubble!=null)speechBubble.Visibility=Visibility.Collapsed;
            double centerY=pet.Top+pet.Height*.52;pet.Width=74;pet.Height=94;
            // 这是独立的“探头扒边”素材：完整身体不做缩放，而是留在屏幕外，屏幕内只出现头和爪子。
            spriteLayer.Visibility=Visibility.Collapsed;edgePeekImage.Visibility=Visibility.Visible;edgePeekFacing.ScaleX=side=="left"?-1:1;edgePeekTranslate.X=0;edgePeekTranslate.Y=0;
            // 让素材里的竖直“屏幕边线”与真实屏幕边界重合；小猫身体则在屏幕外，只留下完整探头动作。
            pet.Left=(side=="left"?bounds.Left:bounds.Right)-EdgeGripOffset(side=="left");
            pet.Top=Math.Max(bounds.Top,Math.Min(centerY-pet.Height*.52,bounds.Bottom-pet.Height));
            badge.Visibility=Visibility.Collapsed;pocketBadge.Visibility=Visibility.Collapsed;facing=side=="left"?1:-1;petState="edge";stateStarted=DateTime.Now;stateUntil=DateTime.MaxValue;stateDuration=1;exactLeft=pet.Left;hopBaseTop=pet.Top;SetFrame(poses[6]);ResetMotionPose(1);StopRendering();AnimateEdgeDockIn();
        }

        void ExitEdgeHide()
        {
            if(!edgeHidden)return;Rect bounds=PetScreenBounds();double centerY=pet.Top+pet.Height*.52;string side=edgeHideSide;edgeHidden=false;edgeHideSide=null;StopEdgePeekAnimation();
            edgePeekImage.Visibility=Visibility.Collapsed;spriteLayer.Visibility=Visibility.Visible;pet.Width=NormalPetWidth;pet.Height=NormalPetHeight;spriteLayer.Width=112;spriteLayer.Height=116;spriteLayer.HorizontalAlignment=HorizontalAlignment.Center;spriteLayer.Margin=new Thickness(0,32,0,0);
            pet.Left=side=="left"?bounds.Left+12:bounds.Right-pet.Width-12;pet.Top=Math.Max(bounds.Top,Math.Min(centerY-pet.Height*.52,bounds.Bottom-pet.Height));exactLeft=pet.Left;hopBaseTop=pet.Top;petState="landing";stateStarted=DateTime.Now;stateDuration=.44;stateUntil=stateStarted.AddSeconds(stateDuration);SetFrame(frames[0]);ResetMotionPose(1);AnimateEdgeDockOut();EnsureRendering();RefreshTasks();RefreshStash();
        }

        void EnsureRendering()
        {
            if (renderAttached) return;
            lastRenderSeconds = renderClock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRendering;
            renderAttached = true;
        }

        void QueuePetFollowerReposition()
        {
            if(petFollowerPositionQueued||pet==null)return;petFollowerPositionQueued=true;
            pet.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(delegate{
                petFollowerPositionQueued=false;if(exiting||pet==null)return;RepositionShelves();if(panel!=null&&panel.IsVisible&&!petPointerDragging)PositionPanel();if(launcherPanel!=null&&launcherPanel.IsVisible)PositionLauncherPanel();
            }));
        }

        void StartEdgePeekAnimation()
        {
            if(edgePeekTranslate==null)return;StopEdgePeekAnimation();
            var bob=new DoubleAnimation(-.75,.75,TimeSpan.FromSeconds(1.15)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
            edgePeekTranslate.BeginAnimation(TranslateTransform.YProperty,bob);
        }

        void StopEdgePeekAnimation()
        {
            if(edgePeekTranslate==null)return;edgePeekTranslate.BeginAnimation(TranslateTransform.XProperty,null);edgePeekTranslate.BeginAnimation(TranslateTransform.YProperty,null);edgePeekTranslate.X=0;edgePeekTranslate.Y=0;
            if(edgePeekImage!=null){edgePeekImage.BeginAnimation(UIElement.OpacityProperty,null);edgePeekImage.Opacity=1;}
        }

        void StopRendering()
        {
            if (!renderAttached) return;
            CompositionTarget.Rendering -= OnRendering;
            renderAttached = false;
        }

        void ChooseBehavior()
        {
            if(petDragActive||petPointerPressed||(shelfPeekPanel!=null&&shelfPeekPanel.IsVisible))return;
            if ((panel!=null&&panel.IsVisible) || (stashPanel != null && stashPanel.IsVisible) || (launcherPanel!=null&&launcherPanel.IsVisible) || DateTime.Now < stateUntil) return;
            string mode=petMovement==null||String.IsNullOrWhiteSpace(petMovement.BehaviorMode)?"正常":petMovement.BehaviorMode;
            if(mode=="专注"){if(random.NextDouble()<.68)StartState("sleep",14+random.Next(10));else StartState("idle",9);return;}
            if(mode=="安静"){double quiet=random.NextDouble();if(PetCanAutoRoam()&&quiet<.15){facing=random.Next(2)==0?-1:1;StartState("walk",3.5+random.NextDouble()*2);}else if(quiet<.57)StartState("sleep",11+random.Next(10));else if(quiet<.68)StartState("coffee",6);else StartState("idle",7);return;}
            if(!PetCanAutoRoam()){double calm=random.NextDouble();if(calm<.12)StartState("hop",1.16);else if(calm<.48)StartState("sleep",8+random.Next(8));else if(calm<.60){StartState("coffee",6);React("我不乱跑，就在这里～",false);}else StartState("idle",4);return;}
            double choice = random.NextDouble();
            bool lively=mode=="活泼";
            if (choice < (lively?.38:.35)) { facing = random.Next(2)==0 ? -1 : 1; StartState("walk", (lively?6:5)+random.Next(4)); }
            else if (choice < (lively?.66:.50)) { facing = random.Next(2)==0 ? -1 : 1; StartState("run", 2.5+random.NextDouble()*(lively?2.8:1.8)); }
            else if (choice < (lively?.79:.61)) StartState("hop", 1.16);
            else if (choice < (lively?.88:.76)) StartState("sleep", 8+random.Next(10));
            else if (choice < (lively?.93:.85)) { StartState("coffee", 7); React("咖啡时间…", false); }
            else { StartState("idle", 4); if(random.NextDouble()<.35) React("博道咪在这里守着你", false); }
        }

        void OnRendering(object sender, EventArgs args)
        {
            double nowSeconds = renderClock.Elapsed.TotalSeconds;
            if (lastRenderSeconds <= 0) { lastRenderSeconds = nowSeconds; return; }
            const double renderStep=1.0/60.0;double available=nowSeconds-lastRenderSeconds;if(available<renderStep)return;int steps=Math.Max(1,(int)(available/renderStep));double dt=Math.Min(1.0/30.0,steps*renderStep);lastRenderSeconds+=steps*renderStep;if(nowSeconds-lastRenderSeconds>.10)lastRenderSeconds=nowSeconds;
            AnimatePet(dt);
        }

        void AnimatePet(double dt)
        {
            if(petDragActive)return;
            if(shelfPeekPanel!=null&&shelfPeekPanel.IsVisible&&(petState=="walk"||petState=="run")){StartState("idle",2);return;}
            DateTime now = DateTime.Now;
            double elapsed = (now-stateStarted).TotalSeconds;
            double remaining = (stateUntil-now).TotalSeconds;
            if (now >= stateUntil && petState != "idle") {
                if(petState=="climb"){CompleteWindowClimb();elapsed=0;remaining=stateDuration;}
                else if(petState=="drop"){CompleteWindowDrop();elapsed=0;remaining=stateDuration;}
                else if (petState == "walk" || petState == "run") {
                    petState="settle"; stateStarted=now; stateUntil=now.AddSeconds(.24);
                    elapsed=0; remaining=.24;
                } else {
                    petState="idle"; elapsed=0; remaining=0;
                }
            }
            var work = PetMovementBounds();
            facingScale.ScaleX = facing > 0 ? 1 : -1; // 素材原始朝向固定向右
            switch (petState) {
                case "walk":
                    double walkSpeed = 42.0*BehaviorSpeedScale()*EaseSpeed(elapsed, remaining, .42);
                    double walkDelta = facing*walkSpeed*dt;
                    double walkNext=exactLeft+walkDelta;if(TryStartWindowClimb(walkNext)||KeepPetOnActiveSurface(walkNext))break;exactLeft=walkNext;strideDistance += Math.Abs(walkDelta);
                    SetMotionFrame(walkFrames, strideDistance/3.8);
                    motionTranslate.Y = 0;
                    motionRotate.Angle = 0;
                    motionScale.ScaleX = 1; motionScale.ScaleY = 1;
                    pet.Left = exactLeft; break;
                case "run":
                    double runSpeed = 86.0*BehaviorSpeedScale()*EaseSpeed(elapsed, remaining, .5);
                    double runDelta = facing*runSpeed*dt;
                    double runNext=exactLeft+runDelta;if(TryStartWindowClimb(runNext)||KeepPetOnActiveSurface(runNext))break;exactLeft=runNext;strideDistance += Math.Abs(runDelta);
                    SetMotionFrame(runFrames, strideDistance/6.0);
                    motionTranslate.Y = 0;
                    motionRotate.Angle = 0;
                    motionScale.ScaleX = 1; motionScale.ScaleY = 1;
                    pet.Left = exactLeft; break;
                case "settle":
                    double settle = Math.Max(0, Math.Min(1, elapsed/.24));
                    SetFrame(settle < .34 ? frames[2] : (settle < .68 ? frames[1] : frames[0]));
                    ResetMotionPose(settle); break;
                case "landing":AnimateLanding(elapsed);break;
                case "climb":AnimateWindowClimb(elapsed);break;
                case "drop":AnimateWindowDrop(elapsed);break;
                case "perch":SetFrame(poses[5]);ResetMotionPose(1);motionTranslate.Y=Math.Sin(elapsed*1.6)*.22;break;
                case "face":
                case "tail":
                case "stretch":AnimateRegionState(petState,elapsed);break;
                case "hop":
                    AnimateHop(elapsed/stateDuration); break;
                case "happy":
                    SetFrame(poses[6]);
                    double happyDecay = Math.Max(0, 1-elapsed/stateDuration);
                    motionTranslate.Y=-Math.Abs(Math.Sin(elapsed*Math.PI*3.0))*3.8*happyDecay;
                    motionRotate.Angle=Math.Sin(elapsed*Math.PI*3.0)*.8*happyDecay;
                    motionScale.ScaleX=1; motionScale.ScaleY=1; break;
                case "sleep": SetFrame(poses[7]); ResetMotionPose(1); motionScale.ScaleY=1.0+Math.Sin(elapsed*1.7)*.008; break;
                case "coffee": SetFrame(coffeeFrame); ResetMotionPose(1); break;
                case "dragged": SetFrame(dragFrame); ResetMotionPose(1); break;
                case "edge":
                    ResetMotionPose(1);
                    break;
                default:
                    SetFrame(frames[0]); ResetMotionPose(1);
                    if(hasActiveSurface){pet.Top=activeSurface.Top-SpriteFootOffset;hopBaseTop=pet.Top;}
                    motionTranslate.Y=Math.Sin(renderClock.Elapsed.TotalSeconds*2.0)*.45;
                    motionScale.ScaleX=1.0-Math.Sin(renderClock.Elapsed.TotalSeconds*2.0)*.003;
                    motionScale.ScaleY=1.0+Math.Sin(renderClock.Elapsed.TotalSeconds*2.0)*.005;
                    break;
            }
            if(!edgeHidden){
                if (pet.Left < work.Left) { pet.Left=work.Left; exactLeft=pet.Left; facing=1; strideDistance=0; }
                if (pet.Left+pet.Width > work.Right) { pet.Left=work.Right-pet.Width; exactLeft=pet.Left; facing=-1; strideDistance=0; }
                if (pet.Top < work.Top) { pet.Top=work.Top; hopBaseTop=pet.Top; }
                if (pet.Top+pet.Height > work.Bottom) { pet.Top=work.Bottom-pet.Height; hopBaseTop=pet.Top; }
            }
            if (petState == "idle" || petState == "sleep" || petState == "coffee" || petState=="perch") StopRendering();
        }

        void ResetMotionPose(double amount)
        {
            double keep = 1-Math.Max(0, Math.Min(1, amount));
            motionTranslate.Y *= keep; motionRotate.Angle *= keep;
            motionScale.ScaleX = 1; motionScale.ScaleY = 1;
        }

        void AnimateHop(double progress)
        {
            double t = Math.Max(0, Math.Min(1, progress));
            motionScale.ScaleX = 1; motionScale.ScaleY = 1;
            SetFrame(frames[0]);

            if (t < .20) {
                // 蓄力：身体下压，重心先沉下去。
                double p = Smooth(t/.20);
                motionTranslate.Y = 2.0*p;
                motionScale.ScaleX = 1+.045*p;
                motionScale.ScaleY = 1-.065*p;
                motionRotate.Angle = 0;
            } else if (t < .80) {
                // 抛物线腾空：起跳快、顶点慢、下落自然加速。
                double air = (t-.20)/.60;
                double height = 13.0*4*air*(1-air);
                motionTranslate.Y = -height;
                double stretch = Math.Sin(Math.PI*air);
                motionScale.ScaleX = 1-.018*stretch;
                motionScale.ScaleY = 1+.03*stretch;
                motionRotate.Angle = 0;
            } else if (t < .93) {
                // 落地缓冲：短促压扁，避免碰地后突然停住。
                double p = (t-.80)/.13;
                motionTranslate.Y = 1.5*(1-Smooth(p));
                motionScale.ScaleX = 1+.055*(1-p);
                motionScale.ScaleY = 1-.07*(1-p);
                motionRotate.Angle = 0;
            } else {
                double p = Smooth((t-.93)/.07);
                motionTranslate.Y = 0;
                motionScale.ScaleX = 1.0+.012*(1-p);
                motionScale.ScaleY = 1.0-.012*(1-p);
                motionRotate.Angle = 0;
            }
        }

        double Smooth(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            return value*value*(3-2*value);
        }

        void SetFrame(ImageSource frame)
        {
            ImageSource skin=SelectedSkinMotionFrame();if(skin!=null)frame=skin;
            if (Object.ReferenceEquals(currentFrame, frame)) return;
            transitionImage.BeginAnimation(UIElement.OpacityProperty, null);
            petImage.BeginAnimation(UIElement.OpacityProperty, null);
            transitionImage.Opacity = 0; transitionImage.Source = null;
            currentFrame = frame; petImage.Source = frame; petImage.Opacity = 1;
        }

        void SetMotionFrame(BitmapImage[] cycle, double position)
        {
            ImageSource skin=SelectedSkinMotionFrame();if(skin!=null){SetFrame(skin);return;}
            int index=(int)Math.Floor(position)%cycle.Length;
            if(Object.ReferenceEquals(currentFrame,cycle[index])) return;
            transitionImage.BeginAnimation(UIElement.OpacityProperty,null);
            petImage.BeginAnimation(UIElement.OpacityProperty,null);
            transitionImage.Opacity=0; transitionImage.Source=null;
            currentFrame=cycle[index]; petImage.Source=currentFrame; petImage.Opacity=1;
        }

        double EaseSpeed(double elapsed, double remaining, double ramp)
        {
            double up = Math.Max(0, Math.Min(1, elapsed/ramp));
            double down = Math.Max(0, Math.Min(1, remaining/ramp));
            double value = Math.Min(up, down);
            return value*value*(3-2*value);
        }

        void Exit() { if (exiting) return; exiting = true; if(comfortSaveTimer!=null)comfortSaveTimer.Stop();foreach(Window savedWindow in comfortLoaded.ToList())SaveOfficeState(savedWindow); timer.Stop(); if (speechTimer != null) speechTimer.Stop(); if (idleTimer != null) idleTimer.Stop(); if (marketTimer != null) marketTimer.Stop();if(launcherClickTimer!=null)launcherClickTimer.Stop();CancelShelfPeekTimers();ShutdownPetExperience(); if(renderAttached) { CompositionTarget.Rendering -= OnRendering; renderAttached=false; } CloseAiWindows();CloseCommunityWindows();CloseMessengerWindows();if(skinWardrobePanel!=null)skinWardrobePanel.Close();if(shelfPeekPanel!=null)shelfPeekPanel.Close();if(launcherPanel!=null)launcherPanel.Close(); if (marketPanel != null) marketPanel.Close(); if (stashPanel != null) stashPanel.Close(); if (panel != null) panel.Close(); if (pet != null) pet.Close(); app.Shutdown(); }
    }
}
