using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MomoPetApp
{
    public class HealthCompanionState
    {
        public bool Enabled{get;set;} public bool EyeEnabled{get;set;} public bool PostureEnabled{get;set;} public bool WaterEnabled{get;set;}
        public int EyeMinutes{get;set;} public int PostureMinutes{get;set;} public int WaterMinutes{get;set;} public int WorkStartHour{get;set;} public int WorkEndHour{get;set;}
        public double EyeSeconds{get;set;} public double PostureSeconds{get;set;} public double WaterSeconds{get;set;}
        public int EyeBreaksToday{get;set;} public int StandBreaksToday{get;set;} public int WaterToday{get;set;}
        public string Day{get;set;} public string PausedUntil{get;set;}
    }

    public partial class PetController
    {
        [StructLayout(LayoutKind.Sequential)] struct HealthLastInputInfo{public uint Size;public uint Time;}
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref HealthLastInputInfo info);
        HealthCompanionState healthState; DispatcherTimer healthTimer; Window healthPanel,healthReminderPanel; StackPanel healthDashboard; TextBlock healthStatus;
        CheckBox healthMasterBox,healthEyeBox,healthPostureBox,healthWaterBox; TextBox healthEyeMinutesBox,healthPostureMinutesBox,healthWaterMinutesBox,healthStartBox,healthEndBox;
        DateTime healthLastTick; int healthSaveTicks; bool healthReminderHandled;

        string HealthStatePath(){return Path.Combine(dataDir,"health-companion.json");}
        void InitializeHealthCompanion(){LoadHealthCompanion();healthLastTick=DateTime.Now;healthTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};healthTimer.Tick+=delegate{TickHealthCompanion();};healthTimer.Start();}
        void ShutdownHealthCompanion(){if(healthTimer!=null)healthTimer.Stop();SaveHealthCompanion();if(healthReminderPanel!=null)healthReminderPanel.Close();if(healthPanel!=null)healthPanel.Close();}
        void LoadHealthCompanion()
        {
            try{string path=HealthStatePath();if(File.Exists(path))healthState=json.Deserialize<HealthCompanionState>(File.ReadAllText(path));}catch{healthState=null;}
            if(healthState==null)healthState=new HealthCompanionState{Enabled=true,EyeEnabled=true,PostureEnabled=true,WaterEnabled=true,EyeMinutes=20,PostureMinutes=50,WaterMinutes=60,WorkStartHour=8,WorkEndHour=22};
            healthState.EyeMinutes=Math.Max(5,Math.Min(120,healthState.EyeMinutes));healthState.PostureMinutes=Math.Max(15,Math.Min(180,healthState.PostureMinutes));healthState.WaterMinutes=Math.Max(15,Math.Min(240,healthState.WaterMinutes));
            if(healthState.WorkStartHour<0||healthState.WorkStartHour>23)healthState.WorkStartHour=8;if(healthState.WorkEndHour<1||healthState.WorkEndHour>24)healthState.WorkEndHour=22;ResetHealthDayIfNeeded();
        }
        void SaveHealthCompanion(){try{MomoStorage.WriteTextAtomic(HealthStatePath(),json.Serialize(healthState),System.Text.Encoding.UTF8);}catch{}}
        void ResetHealthDayIfNeeded(){string today=DateTime.Today.ToString("yyyy-MM-dd");if(healthState.Day==today)return;healthState.Day=today;healthState.EyeBreaksToday=healthState.StandBreaksToday=healthState.WaterToday=0;}
        static double HealthIdleSeconds(){try{var info=new HealthLastInputInfo{Size=(uint)Marshal.SizeOf(typeof(HealthLastInputInfo))};if(GetLastInputInfo(ref info))return ((uint)Environment.TickCount-info.Time)/1000.0;}catch{}return 0;}
        bool HealthInWorkHours(DateTime now){int h=now.Hour;return healthState.WorkStartHour<healthState.WorkEndHour?h>=healthState.WorkStartHour&&h<healthState.WorkEndHour:h>=healthState.WorkStartHour||h<healthState.WorkEndHour;}
        bool HealthPaused(){DateTime until;return DateTime.TryParse(healthState.PausedUntil,out until)&&until>DateTime.Now;}
        void TickHealthCompanion()
        {
            if(healthState==null)return;DateTime now=DateTime.Now;double elapsed=Math.Max(0,Math.Min(30,(now-healthLastTick).TotalSeconds));healthLastTick=now;ResetHealthDayIfNeeded();
            if(HealthIdleSeconds()>=120){healthState.EyeSeconds=healthState.PostureSeconds=0;RefreshHealthDashboard();return;}
            if(!healthState.Enabled||HealthPaused()||!HealthInWorkHours(now)||presentationQuiet||globalPetHidden)return;
            healthState.EyeSeconds+=elapsed;healthState.PostureSeconds+=elapsed;healthState.WaterSeconds+=elapsed;if(++healthSaveTicks>=16){healthSaveTicks=0;SaveHealthCompanion();}
            string due=HealthDueKind();if(due!=null&&healthReminderPanel==null)ShowHealthReminder(due);RefreshHealthDashboard();
        }
        string HealthDueKind(){if(healthState.PostureEnabled&&healthState.PostureSeconds>=healthState.PostureMinutes*60)return "posture";if(healthState.EyeEnabled&&healthState.EyeSeconds>=healthState.EyeMinutes*60)return "eye";if(healthState.WaterEnabled&&healthState.WaterSeconds>=healthState.WaterMinutes*60)return "water";return null;}
        void CompleteHealthAction(string kind)
        {
            if(kind=="eye"){healthState.EyeSeconds=0;healthState.EyeBreaksToday++;React("看看远处，让眼睛歇一会儿～",true);StartState("sleep",4);}else if(kind=="posture"){healthState.PostureSeconds=0;healthState.StandBreaksToday++;React("起来舒展一下，肩膀也放松～",true);StartState("happy",2);}else{healthState.WaterSeconds=0;healthState.WaterToday++;React("补水完成，做得好～",true);StartState("coffee",4);}SaveHealthCompanion();RefreshHealthDashboard();
        }
        void PostponeHealthAction(string kind,int minutes){double s=minutes*60;if(kind=="eye")healthState.EyeSeconds=Math.Max(0,healthState.EyeMinutes*60-s);else if(kind=="posture")healthState.PostureSeconds=Math.Max(0,healthState.PostureMinutes*60-s);else healthState.WaterSeconds=Math.Max(0,healthState.WaterMinutes*60-s);SaveHealthCompanion();RefreshHealthDashboard();}
        void ShowHealthReminder(string kind)
        {
            string icon=kind=="eye"?"👀":kind=="posture"?"🧘":"💧",title=kind=="eye"?"让眼睛休息一下":kind=="posture"?"该起身活动了":"喝口水吧",detail=kind=="eye"?"看向远处 20 秒，眨眨眼睛。":kind=="posture"?"站起来走动，舒展肩颈和腰背。":"离开屏幕，慢慢喝几口水。";
            healthReminderHandled=false;healthReminderPanel=new Window{Title="健康陪伴提醒",Width=410,Height=220,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true};Ui.StyleWindow(healthReminderPanel);
            var shell=new Border{CornerRadius=new CornerRadius(22),Padding=new Thickness(20),Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(76)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.Children.Add(CommunityCat(68));var body=new StackPanel{Margin=new Thickness(15,0,0,0)};body.Children.Add(HubText(icon+"  "+title,18,Ui.Ink,FontWeights.Bold));var copy=HubText(detail,12.5,Ui.SubInk,FontWeights.Normal);copy.Margin=new Thickness(0,9,0,15);body.Children.Add(copy);var actions=new StackPanel{Orientation=Orientation.Horizontal};var done=MakeButton(kind=="water"?"已经喝水":"开始休息",Ui.Accent);done.Foreground=Brushes.White;done.Click+=delegate{healthReminderHandled=true;CompleteHealthAction(kind);healthReminderPanel.Close();};var later=MakeButton("5 分钟后",Ui.Neutral);later.Margin=new Thickness(8,0,0,0);later.Click+=delegate{healthReminderHandled=true;PostponeHealthAction(kind,5);healthReminderPanel.Close();};actions.Children.Add(done);actions.Children.Add(later);body.Children.Add(actions);Grid.SetColumn(body,1);grid.Children.Add(body);shell.Child=grid;healthReminderPanel.Content=shell;
            healthReminderPanel.Closed+=delegate{if(!healthReminderHandled)PostponeHealthAction(kind,5);healthReminderPanel=null;};var work=SystemParameters.WorkArea;healthReminderPanel.Left=Math.Max(work.Left+8,Math.Min(pet.Left-healthReminderPanel.Width+pet.Width,work.Right-healthReminderPanel.Width-8));healthReminderPanel.Top=Math.Max(work.Top+8,Math.Min(pet.Top-healthReminderPanel.Height-8,work.Bottom-healthReminderPanel.Height-8));React(title,false);healthReminderPanel.Show();
        }
        void OpenHealthCompanion(){if(healthPanel==null)BuildHealthCompanion();RefreshHealthDashboard();PositionHubWindow(healthPanel);healthPanel.Show();healthPanel.Activate();}
        void BuildHealthCompanion()
        {
            healthPanel=new Window{Title="健康陪伴",Width=900,Height=650,MinWidth=760,MinHeight=560,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(healthPanel);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(22)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.Children.Add(BuildHubHeader(healthPanel,"🌿","健康陪伴","让小猫照顾你的用眼、久坐与补水节奏",delegate{RefreshHealthDashboard();},"刷新"));healthDashboard=new StackPanel{Margin=new Thickness(0,14,0,0)};var scroll=new ScrollViewer{Content=healthDashboard,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);root.Children.Add(scroll);shell.Child=root;healthPanel.Content=shell;healthPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;healthPanel.Hide();}};
        }
        Border HealthMetricCard(string icon,string title,string detail,string count){var body=new StackPanel();body.Children.Add(HubText(icon+"  "+title,14,Ui.Ink,FontWeights.Bold));var text=HubText(detail,11.5,Ui.SubInk,FontWeights.Normal);text.Margin=new Thickness(0,7,0,8);body.Children.Add(text);body.Children.Add(HubText(count,12,Ui.AccentDeep,FontWeights.SemiBold));return HubCard(body,new Thickness(0,0,10,0),new Thickness(15));}
        string HealthRemaining(double elapsed,int minutes){int left=Math.Max(0,(int)Math.Ceiling(minutes-elapsed/60));return left==0?"现在可以休息":"约 "+left+" 分钟后";}
        void RefreshHealthDashboard()
        {
            if(healthDashboard==null||healthState==null)return;healthDashboard.Children.Clear();ResetHealthDayIfNeeded();var overview=new Grid();overview.ColumnDefinitions.Add(new ColumnDefinition());overview.ColumnDefinitions.Add(new ColumnDefinition());overview.ColumnDefinitions.Add(new ColumnDefinition());var eye=HealthMetricCard("👀","护眼",HealthRemaining(healthState.EyeSeconds,healthState.EyeMinutes),"今日完成 "+healthState.EyeBreaksToday+" 次");var posture=HealthMetricCard("🧘","活动",HealthRemaining(healthState.PostureSeconds,healthState.PostureMinutes),"今日起身 "+healthState.StandBreaksToday+" 次");var water=HealthMetricCard("💧","补水",HealthRemaining(healthState.WaterSeconds,healthState.WaterMinutes),"今日记录 "+healthState.WaterToday+" 次");overview.Children.Add(eye);Grid.SetColumn(posture,1);overview.Children.Add(posture);Grid.SetColumn(water,2);water.Margin=new Thickness(0);overview.Children.Add(water);healthDashboard.Children.Add(overview);
            var quick=new WrapPanel{Margin=new Thickness(0,14,0,14)};var eyeDone=MakeButton("我休息过眼睛了",Ui.AccentSoft);eyeDone.Click+=delegate{CompleteHealthAction("eye");};var standDone=MakeButton("我起来活动了",Ui.AccentSoft);standDone.Click+=delegate{CompleteHealthAction("posture");};var drank=MakeButton("我喝水了",Ui.AccentSoft);drank.Click+=delegate{CompleteHealthAction("water");};var pause=MakeButton(HealthPaused()?"恢复提醒":"暂停 1 小时",Ui.Neutral);pause.Click+=delegate{healthState.PausedUntil=HealthPaused()?null:DateTime.Now.AddHours(1).ToString("o");SaveHealthCompanion();RefreshHealthDashboard();};quick.Children.Add(eyeDone);quick.Children.Add(standDone);quick.Children.Add(drank);quick.Children.Add(pause);healthDashboard.Children.Add(quick);
            var settings=new StackPanel();settings.Children.Add(Ui.Title("提醒设置",16));healthMasterBox=new CheckBox{Content="开启健康陪伴",IsChecked=healthState.Enabled,Margin=new Thickness(0,12,0,8),FontWeight=FontWeights.SemiBold};settings.Children.Add(healthMasterBox);var toggles=new WrapPanel();healthEyeBox=new CheckBox{Content="护眼提醒",IsChecked=healthState.EyeEnabled,Margin=new Thickness(0,0,22,8)};healthPostureBox=new CheckBox{Content="久坐提醒",IsChecked=healthState.PostureEnabled,Margin=new Thickness(0,0,22,8)};healthWaterBox=new CheckBox{Content="补水提醒",IsChecked=healthState.WaterEnabled,Margin=new Thickness(0,0,22,8)};toggles.Children.Add(healthEyeBox);toggles.Children.Add(healthPostureBox);toggles.Children.Add(healthWaterBox);settings.Children.Add(toggles);
            var fields=new Grid{Margin=new Thickness(0,8,0,0)};for(int i=0;i<6;i++)fields.ColumnDefinitions.Add(new ColumnDefinition());fields.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});fields.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});string[] labels={"护眼（分钟）","久坐（分钟）","补水（分钟）","开始时间","结束时间"};for(int i=0;i<labels.Length;i++){var label=HubText(labels[i],10.5,Ui.SubInk,FontWeights.Normal);label.Margin=new Thickness(0,0,8,5);Grid.SetColumn(label,i);fields.Children.Add(label);}healthEyeMinutesBox=new TextBox{Text=healthState.EyeMinutes.ToString(),Margin=new Thickness(0,0,8,0)};healthPostureMinutesBox=new TextBox{Text=healthState.PostureMinutes.ToString(),Margin=new Thickness(0,0,8,0)};healthWaterMinutesBox=new TextBox{Text=healthState.WaterMinutes.ToString(),Margin=new Thickness(0,0,8,0)};healthStartBox=new TextBox{Text=healthState.WorkStartHour.ToString("00")+":00",Margin=new Thickness(0,0,8,0)};healthEndBox=new TextBox{Text=healthState.WorkEndHour.ToString("00")+":00",Margin=new Thickness(0,0,8,0)};TextBox[] boxes={healthEyeMinutesBox,healthPostureMinutesBox,healthWaterMinutesBox,healthStartBox,healthEndBox};for(int i=0;i<boxes.Length;i++){boxes[i].Height=36;boxes[i].VerticalContentAlignment=VerticalAlignment.Center;Grid.SetColumn(boxes[i],i);Grid.SetRow(boxes[i],1);fields.Children.Add(boxes[i]);}var save=MakeButton("保存设置",Ui.Accent);save.Foreground=Brushes.White;save.Height=36;save.Click+=delegate{SaveHealthSettings();};Grid.SetColumn(save,5);Grid.SetRow(save,1);fields.Children.Add(save);settings.Children.Add(fields);healthStatus=HubText("",11.5,Ui.SubInk,FontWeights.Normal);healthStatus.Margin=new Thickness(0,10,0,0);settings.Children.Add(healthStatus);var note=HubText("离开电脑满 2 分钟会被识别为自然休息，用眼和久坐计时自动重新开始；工作时段之外不提醒。此功能用于日常习惯辅助，不提供医疗判断。",11,Ui.SubInk,FontWeights.Normal);note.Margin=new Thickness(0,12,0,0);settings.Children.Add(note);healthDashboard.Children.Add(HubCard(settings,new Thickness(0),new Thickness(18)));
        }
        void SaveHealthSettings()
        {
            int eye,posture,water,start,end;if(!Int32.TryParse(healthEyeMinutesBox.Text,out eye)||!Int32.TryParse(healthPostureMinutesBox.Text,out posture)||!Int32.TryParse(healthWaterMinutesBox.Text,out water)||!Int32.TryParse((healthStartBox.Text??"").Split(':')[0],out start)||!Int32.TryParse((healthEndBox.Text??"").Split(':')[0],out end)){healthStatus.Text="请填写有效的数字和整点时间";return;}if(eye<5||eye>120||posture<15||posture>180||water<15||water>240||start<0||start>23||end<1||end>24||start==end){healthStatus.Text="间隔或工作时段超出可用范围";return;}
            healthState.Enabled=healthMasterBox.IsChecked==true;healthState.EyeEnabled=healthEyeBox.IsChecked==true;healthState.PostureEnabled=healthPostureBox.IsChecked==true;healthState.WaterEnabled=healthWaterBox.IsChecked==true;healthState.EyeMinutes=eye;healthState.PostureMinutes=posture;healthState.WaterMinutes=water;healthState.WorkStartHour=start;healthState.WorkEndHour=end;SaveHealthCompanion();RefreshHealthDashboard();healthStatus.Text="设置已保存，小猫会按新节奏陪着你";
        }
    }
}
