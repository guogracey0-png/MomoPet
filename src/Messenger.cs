using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MomoPetApp
{
    public class MomoAccountState
    {
        public string ServerBase { get; set; }
        public string MemberId { get; set; }
        public string Username { get; set; }
        public string Nickname { get; set; }
        public string Avatar { get; set; }
        public string SkinId { get; set; }
    }

    public class MomoRemoteMember
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Nickname { get; set; }
        public string Avatar { get; set; }
        public string Bio { get; set; }
        public string SkinId { get; set; }
        public override string ToString(){return (String.IsNullOrWhiteSpace(Avatar)?"🐾":Avatar)+"  "+Nickname+"  @"+Username;}
    }

    public class MomoLetter
    {
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string SenderNickname { get; set; }
        public string ReceiverNickname { get; set; }
        public string SenderSkinId { get; set; }
        public string Content { get; set; }
        public string CreatedAt { get; set; }
        public string Status { get; set; }
        public string DeliveredAt { get; set; }
        public string ReadAt { get; set; }
    }

    public partial class PetController
    {
        const string DefaultMomoCloud="https://cafe-api-zofigfdfto.cn-hangzhou.fcapp.run";
        Window messengerPanel,courierWindow;
        Grid messengerBody;
        ListBox messengerContacts;
        StackPanel messengerConversation;
        TextBox messengerInput;
        TextBlock messengerTitle,messengerStatus,accountStatus;
        TextBox accountServerBox,accountUsernameBox,accountNicknameBox;
        PasswordBox accountPasswordBox;
        readonly List<MomoRemoteMember> messengerMembers=new List<MomoRemoteMember>();
        readonly List<MomoLetter> messengerLetters=new List<MomoLetter>();
        readonly Queue<MomoLetter> courierQueue=new Queue<MomoLetter>();
        readonly HashSet<string> knownLetterIds=new HashSet<string>();
        MomoAccountState momoAccount;
        string momoToken;
        MomoRemoteMember selectedMessengerMember;
        DispatcherTimer messengerPollTimer,courierTimer;
        Image courierImage;
        Border courierEnvelope,courierBubble;
        TextBlock courierBubbleText;
        MomoLetter activeCourierLetter;
        DateTime courierPhaseStarted;
        string courierPhase;
        double courierStartLeft,courierTargetLeft,courierExitLeft,courierBaseTop;
        int courierDirection=1,courierFrameTick;
        bool messengerRequestBusy;

        string AccountStatePath(){return Path.Combine(dataDir,"momo-account.json");}
        string AccountTokenPath(){return Path.Combine(dataDir,"momo-account-token.dat");}
        byte[] AccountEntropy(){return Encoding.UTF8.GetBytes("MomoPet.Account.Token.v1");}

        void InitializeMessenger()
        {
            LoadMomoAccount();messengerPollTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};messengerPollTimer.Tick+=delegate{PollMomoMessages();};if(IsMomoSignedIn()){messengerPollTimer.Start();PollMomoMessages();}
        }

        void LoadMomoAccount()
        {
            try{if(File.Exists(AccountStatePath()))momoAccount=json.Deserialize<MomoAccountState>(File.ReadAllText(AccountStatePath(),Encoding.UTF8));}catch{}
            if(momoAccount==null)momoAccount=new MomoAccountState{ServerBase=DefaultMomoCloud,SkinId="default"};
            if(String.IsNullOrWhiteSpace(momoAccount.ServerBase))momoAccount.ServerBase=DefaultMomoCloud;
            try{if(File.Exists(AccountTokenPath()))momoToken=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(AccountTokenPath()),AccountEntropy(),DataProtectionScope.CurrentUser));}catch{momoToken=null;}
        }

        void SaveMomoAccount()
        {
            try{Directory.CreateDirectory(dataDir);File.WriteAllText(AccountStatePath(),json.Serialize(momoAccount),new UTF8Encoding(false));if(!String.IsNullOrWhiteSpace(momoToken)){byte[] raw=Encoding.UTF8.GetBytes(momoToken);try{File.WriteAllBytes(AccountTokenPath(),ProtectedData.Protect(raw,AccountEntropy(),DataProtectionScope.CurrentUser));}finally{Array.Clear(raw,0,raw.Length);}}}catch{}
        }

        bool IsMomoSignedIn(){return momoAccount!=null&&!String.IsNullOrWhiteSpace(momoAccount.MemberId)&&!String.IsNullOrWhiteSpace(momoToken);}
        string MomoServer(){return (momoAccount==null||String.IsNullOrWhiteSpace(momoAccount.ServerBase)?DefaultMomoCloud:momoAccount.ServerBase).Trim().TrimEnd('/');}

        void MomoApi<T>(string method,string path,object body,bool authenticated,Action<T> success,Action<string> failure)
        {
            string server=MomoServer(),token=momoToken;Task.Factory.StartNew(delegate{
                try{
                    var request=(HttpWebRequest)WebRequest.Create(server+path);request.Method=method;request.Accept="application/json";request.ContentType="application/json; charset=utf-8";request.Timeout=15000;request.ReadWriteTimeout=15000;if(authenticated&&!String.IsNullOrWhiteSpace(token))request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
                    if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(body));request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);}
                    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();T value=String.IsNullOrWhiteSpace(text)?default(T):new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(text);app.Dispatcher.BeginInvoke(new Action(delegate{if(success!=null)success(value);}));}
                }catch(WebException web){string message="无法连接云端";try{using(var response=web.Response)using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();var error=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;if(error!=null&&error.ContainsKey("error"))message=Convert.ToString(error["error"]);}}catch{}app.Dispatcher.BeginInvoke(new Action(delegate{if(failure!=null)failure(message);}));}
                catch(Exception error){app.Dispatcher.BeginInvoke(new Action(delegate{if(failure!=null)failure(error.Message);}));}
            });
        }

        void OpenMessengerPanel()
        {
            if(messengerPanel==null)BuildMessengerPanel();if(RestoreShelvedIfNeeded(messengerPanel))return;RefreshMessengerBody();PositionHubWindow(messengerPanel);messengerPanel.Show();messengerPanel.Activate();if(IsMomoSignedIn()){LoadMessengerMembers();PollMomoMessages();}
        }

        void BuildMessengerPanel()
        {
            messengerPanel=new Window{Title="账号与来信",Width=1080,Height=730,MinWidth=880,MinHeight=600,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(messengerPanel);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(22)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.Children.Add(BuildHubHeader(messengerPanel,"💌","账号与来信","让朋友的小猫亲自把消息送到你桌面",delegate{if(IsMomoSignedIn())LoadMessengerMembers();else RefreshMessengerBody();},"刷新"));messengerBody=new Grid{Background=Ui.Paper};Grid.SetRow(messengerBody,1);root.Children.Add(messengerBody);shell.Child=root;messengerPanel.Content=shell;messengerPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;messengerPanel.Hide();}};
        }

        void RefreshMessengerBody()
        {
            if(messengerBody==null)return;messengerBody.Children.Clear();messengerBody.ColumnDefinitions.Clear();messengerBody.RowDefinitions.Clear();if(!IsMomoSignedIn()){BuildAccountGate();return;}BuildConversationWorkspace();
        }

        void BuildAccountGate()
        {
            messengerBody.ColumnDefinitions.Add(new ColumnDefinition());messengerBody.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(470)});messengerBody.ColumnDefinitions.Add(new ColumnDefinition());var card=HubCard(new Grid(),new Thickness(0,26,0,26),new Thickness(28));Grid.SetColumn(card,1);messengerBody.Children.Add(card);var form=(Grid)card.Child;for(int i=0;i<8;i++)form.RowDefinitions.Add(new RowDefinition{Height=i==4?new GridLength(10):GridLength.Auto});form.Children.Add(Ui.Title("登录后，让小猫替你送信",22));var intro=new TextBlock{Text="账号沿用博知汇成员体系。令牌只加密保存在当前 Windows 用户下，阿里云密钥不会进入客户端。",TextWrapping=TextWrapping.Wrap,Foreground=Ui.SubInk,FontSize=12.5,Margin=new Thickness(0,8,0,14)};Grid.SetRow(intro,1);form.Children.Add(intro);accountServerBox=new TextBox{Text=momoAccount.ServerBase,Height=42,ToolTip="云端服务地址",Margin=new Thickness(0,0,0,8)};Grid.SetRow(accountServerBox,2);form.Children.Add(accountServerBox);accountUsernameBox=new TextBox{Height=42,ToolTip="账号（3-20 位字母、数字或下划线）",Margin=new Thickness(0,0,0,8)};Grid.SetRow(accountUsernameBox,3);form.Children.Add(accountUsernameBox);accountNicknameBox=new TextBox{Height=42,ToolTip="昵称（注册时填写）",Margin=new Thickness(0,0,0,8)};Grid.SetRow(accountNicknameBox,5);form.Children.Add(accountNicknameBox);accountPasswordBox=new PasswordBox{Height=42,ToolTip="密码",Margin=new Thickness(0,0,0,8)};Grid.SetRow(accountPasswordBox,6);form.Children.Add(accountPasswordBox);var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};var login=MakeButton("登录",Ui.Accent);login.Foreground=Brushes.White;login.Click+=delegate{SubmitMomoAccount(false);};var register=MakeButton("注册新账号",Ui.Neutral);register.Click+=delegate{SubmitMomoAccount(true);};actions.Children.Add(register);actions.Children.Add(login);Grid.SetRow(actions,7);form.Children.Add(actions);accountStatus=HubText("已有博知汇账号可直接登录。",11.5,Ui.SubInk,FontWeights.Normal);accountStatus.Margin=new Thickness(0,8,0,0);Grid.SetRow(accountStatus,8);form.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});form.Children.Add(accountStatus);
        }

        void SubmitMomoAccount(bool register)
        {
            string server=(accountServerBox.Text??"").Trim(),username=(accountUsernameBox.Text??"").Trim(),password=accountPasswordBox.Password??"",nickname=(accountNicknameBox.Text??"").Trim();if(String.IsNullOrWhiteSpace(server)||String.IsNullOrWhiteSpace(username)||String.IsNullOrWhiteSpace(password)){accountStatus.Text="请填写服务地址、账号和密码";return;}if(register&&String.IsNullOrWhiteSpace(nickname)){accountStatus.Text="注册时请填写昵称";return;}momoAccount.ServerBase=server;accountStatus.Text=register?"正在创建账号…":"正在登录…";var body=new Dictionary<string,object>{{"username",username},{"password",password},{"nickname",nickname},{"skinId",petMovement==null?"default":petMovement.SkinId}};MomoApi<Dictionary<string,object>>("POST",register?"/api/momo/auth/register":"/api/momo/auth/login",body,false,delegate(Dictionary<string,object> response){Dictionary<string,object> member=response!=null&&response.ContainsKey("member")?response["member"] as Dictionary<string,object>:null;if(response==null||member==null){accountStatus.Text="服务返回的数据不完整";return;}momoToken=Convert.ToString(response["token"]);momoAccount.MemberId=Convert.ToString(member["id"]);momoAccount.Username=Convert.ToString(member["username"]);momoAccount.Nickname=Convert.ToString(member["nickname"]);momoAccount.Avatar=member.ContainsKey("avatar")?Convert.ToString(member["avatar"]):"🐾";momoAccount.SkinId=petMovement==null?"default":petMovement.SkinId;SaveMomoAccount();accountPasswordBox.Clear();messengerPollTimer.Start();RefreshMessengerBody();LoadMessengerMembers();PollMomoMessages();React("账号连好啦，等小猫来送信～",true);},delegate(string error){accountStatus.Text=error;});
        }

        void BuildConversationWorkspace()
        {
            messengerBody.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(250)});messengerBody.ColumnDefinitions.Add(new ColumnDefinition());messengerBody.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220)});
            var left=new Grid{Margin=new Thickness(0,10,12,0)};left.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});left.RowDefinitions.Add(new RowDefinition());var leftTitle=Ui.Title("联系人",17);leftTitle.Margin=new Thickness(4,0,0,10);left.Children.Add(leftTitle);messengerContacts=new ListBox{ItemsSource=messengerMembers};messengerContacts.SelectionChanged+=delegate{selectedMessengerMember=messengerContacts.SelectedItem as MomoRemoteMember;if(selectedMessengerMember!=null)LoadConversation(selectedMessengerMember);};Grid.SetRow(messengerContacts,1);left.Children.Add(messengerContacts);messengerBody.Children.Add(left);
            var center=new Grid{Margin=new Thickness(0,10,12,0)};center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});center.RowDefinitions.Add(new RowDefinition());center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});messengerTitle=Ui.Title(selectedMessengerMember==null?"选择一位朋友":"和 "+selectedMessengerMember.Nickname+" 的来信",17);messengerTitle.Margin=new Thickness(2,0,0,10);center.Children.Add(messengerTitle);messengerConversation=new StackPanel();var scroll=new ScrollViewer{Content=messengerConversation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Ui.Inner};Grid.SetRow(scroll,1);center.Children.Add(scroll);var composer=new Grid{Margin=new Thickness(0,10,0,0)};composer.ColumnDefinitions.Add(new ColumnDefinition());composer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});messengerInput=new TextBox{Height=44,AcceptsReturn=false,ToolTip="写一封短信；回车发送"};messengerInput.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){SendMomoMessage();e.Handled=true;}};composer.Children.Add(messengerInput);var send=MakeButton("让小猫送信",Ui.Accent);send.Foreground=Brushes.White;send.Height=44;send.Click+=delegate{SendMomoMessage();};Grid.SetColumn(send,1);composer.Children.Add(send);Grid.SetRow(composer,2);center.Children.Add(composer);Grid.SetColumn(center,1);messengerBody.Children.Add(center);
            var right=new StackPanel{Margin=new Thickness(0,10,0,0)};var me=new StackPanel();me.Children.Add(HubText((String.IsNullOrWhiteSpace(momoAccount.Avatar)?"🐾":momoAccount.Avatar)+"  "+momoAccount.Nickname,16,Ui.Ink,FontWeights.SemiBold));me.Children.Add(HubText("@"+momoAccount.Username+"\n当前皮肤："+SkinName(petMovement==null?"default":petMovement.SkinId),11.5,Ui.SubInk,FontWeights.Normal));right.Children.Add(HubCard(me,new Thickness(0,0,0,10),new Thickness(16)));var rules=new StackPanel();rules.Children.Add(HubText("送信方式",13,Ui.Ink,FontWeights.SemiBold));rules.Children.Add(HubText("对方在线时，看到的是你当前皮肤的小猫。对方点击收信后，你会看到“已收信”和时间。",11.5,Ui.SubInk,FontWeights.Normal));var preview=MakeButton("预览来信动画",Ui.Neutral);preview.Click+=delegate{PreviewCourierSkin(selectedMessengerMember==null?(petMovement==null?"default":petMovement.SkinId):selectedMessengerMember.SkinId);};rules.Children.Add(preview);right.Children.Add(HubCard(rules,new Thickness(0,0,0,10),new Thickness(16)));messengerStatus=HubText("云端已连接",11.5,Ui.SubInk,FontWeights.Normal);right.Children.Add(messengerStatus);var logout=MakeButton("退出账号",Brushes.Transparent);logout.Foreground=Ui.Up;logout.Click+=delegate{LogoutMomoAccount();};right.Children.Add(logout);Grid.SetColumn(right,2);messengerBody.Children.Add(right);RefreshConversationView();
        }

        void LogoutMomoAccount()
        {
            if(IsMomoSignedIn())MomoApi<Dictionary<string,object>>("POST","/api/momo/auth/logout",null,true,delegate(Dictionary<string,object> ignored){},delegate(string ignored){});momoAccount.MemberId=null;momoAccount.Username=null;momoAccount.Nickname=null;momoAccount.Avatar=null;momoToken=null;try{if(File.Exists(AccountTokenPath()))File.Delete(AccountTokenPath());}catch{}SaveMomoAccount();messengerPollTimer.Stop();selectedMessengerMember=null;messengerMembers.Clear();messengerLetters.Clear();RefreshMessengerBody();
        }

        void LoadMessengerMembers()
        {
            if(!IsMomoSignedIn())return;MomoApi<List<MomoRemoteMember>>("GET","/api/momo/members",null,true,delegate(List<MomoRemoteMember> items){messengerMembers.Clear();if(items!=null)messengerMembers.AddRange(items.Where(x=>x.Id!=momoAccount.MemberId));if(messengerContacts!=null){messengerContacts.ItemsSource=null;messengerContacts.ItemsSource=messengerMembers;}if(selectedMessengerMember==null&&messengerMembers.Count>0){selectedMessengerMember=messengerMembers[0];if(messengerContacts!=null)messengerContacts.SelectedItem=selectedMessengerMember;LoadConversation(selectedMessengerMember);}if(messengerStatus!=null)messengerStatus.Text="已同步 "+messengerMembers.Count+" 位联系人";},delegate(string error){if(messengerStatus!=null)messengerStatus.Text=error;});
        }

        void LoadConversation(MomoRemoteMember member)
        {
            if(member==null||messengerRequestBusy)return;messengerRequestBusy=true;if(messengerTitle!=null)messengerTitle.Text="和 "+member.Nickname+" 的来信";MomoApi<List<MomoLetter>>("GET","/api/momo/messages/conversation/"+Uri.EscapeDataString(member.Id),null,true,delegate(List<MomoLetter> items){messengerRequestBusy=false;messengerLetters.Clear();if(items!=null)messengerLetters.AddRange(items);RefreshConversationView();},delegate(string error){messengerRequestBusy=false;if(messengerStatus!=null)messengerStatus.Text=error;});
        }

        void RefreshConversationView()
        {
            if(messengerConversation==null)return;messengerConversation.Children.Clear();if(selectedMessengerMember==null){messengerConversation.Children.Add(BuildEmptyState("先选一位朋友","然后写一封让小猫送去的短信。"));return;}foreach(var letter in messengerLetters.OrderBy(x=>x.CreatedAt)){bool mine=letter.SenderId==momoAccount.MemberId;var body=new StackPanel();body.Children.Add(HubText(letter.Content,13,Ui.Ink,FontWeights.Normal));string state=mine?(letter.Status=="read"?"对方已收信  "+ShortCloudTime(letter.ReadAt):(letter.Status=="delivered"?"小猫已送达":"正在送信")):"收到于 "+ShortCloudTime(letter.CreatedAt);body.Children.Add(HubText(state,10.5,letter.Status=="read"?Ui.Green:Ui.SubInk,FontWeights.Normal));var card=HubCard(body,new Thickness(mine?80:0,0,mine?0:80,8),new Thickness(14,10,14,10));card.Background=mine?Ui.AccentSoft:Ui.Card;messengerConversation.Children.Add(card);}
        }

        string ShortCloudTime(string value){DateTime time;if(DateTime.TryParse(value,out time))return time.ToLocalTime().ToString("MM-dd HH:mm");return "";}

        void SendMomoMessage()
        {
            if(selectedMessengerMember==null||messengerInput==null)return;string content=(messengerInput.Text??"").Trim();if(String.IsNullOrWhiteSpace(content))return;if(content.Length>1000){messengerStatus.Text="一封信最多 1000 个字";return;}messengerInput.IsEnabled=false;var body=new Dictionary<string,object>{{"receiverId",selectedMessengerMember.Id},{"content",content},{"skinId",petMovement==null?"default":petMovement.SkinId}};MomoApi<MomoLetter>("POST","/api/momo/messages",body,true,delegate(MomoLetter letter){messengerInput.IsEnabled=true;messengerInput.Clear();if(letter!=null)messengerLetters.Add(letter);RefreshConversationView();messengerStatus.Text="小猫已经出发";},delegate(string error){messengerInput.IsEnabled=true;messengerStatus.Text=error;});
        }

        void PollMomoMessages()
        {
            if(!IsMomoSignedIn()||messengerRequestBusy)return;MomoApi<List<MomoLetter>>("GET","/api/momo/messages/inbox?unread=1",null,true,delegate(List<MomoLetter> items){if(items==null)return;foreach(var letter in items.OrderBy(x=>x.CreatedAt)){if(String.IsNullOrWhiteSpace(letter.Id)||knownLetterIds.Contains(letter.Id))continue;knownLetterIds.Add(letter.Id);courierQueue.Enqueue(letter);MarkLetterDelivered(letter);}if(activeCourierLetter==null&&courierQueue.Count>0)StartNextCourier();if(selectedMessengerMember!=null)LoadConversation(selectedMessengerMember);},delegate(string error){if(messengerStatus!=null)messengerStatus.Text=error;});
        }

        void MarkLetterDelivered(MomoLetter letter){MomoApi<Dictionary<string,object>>("POST","/api/momo/messages/"+Uri.EscapeDataString(letter.Id)+"/delivered",null,true,delegate(Dictionary<string,object> ignored){},delegate(string ignored){});}
        void MarkLetterRead(MomoLetter letter){MomoApi<Dictionary<string,object>>("POST","/api/momo/messages/"+Uri.EscapeDataString(letter.Id)+"/read",null,true,delegate(Dictionary<string,object> ignored){},delegate(string error){if(messengerStatus!=null)messengerStatus.Text="收信回执稍后重试："+error;});}

        ImageSource CourierFrame(string skinId,bool happy)
        {
            if(String.IsNullOrWhiteSpace(skinId)||skinId=="default")return happy?poses[6]:walkFrames[(courierFrameTick/5)%walkFrames.Length];BitmapImage[] motions;if(skinMotionFrames.TryGetValue(skinId,out motions)&&motions!=null)return motions[happy?6:1];return SkinPreviewFrame(skinId);
        }

        void StartNextCourier()
        {
            if(courierQueue.Count==0||activeCourierLetter!=null)return;activeCourierLetter=courierQueue.Dequeue();BuildCourierWindowIfNeeded();courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,false);courierBubbleText.Text=activeCourierLetter.SenderNickname+" 给你送信来啦";courierBubble.Visibility=Visibility.Collapsed;courierEnvelope.Visibility=Visibility.Visible;var work=SystemParameters.WorkArea;double petCenter=pet.Left+pet.Width/2;bool enterFromLeft=petCenter>work.Left+work.Width*.58;courierDirection=enterFromLeft?1:-1;courierTargetLeft=enterFromLeft?Math.Max(work.Left+4,pet.Left-courierWindow.Width+42):Math.Min(work.Right-courierWindow.Width-4,pet.Left+pet.Width-40);courierStartLeft=enterFromLeft?work.Left-courierWindow.Width-20:work.Right+20;courierExitLeft=enterFromLeft?work.Right+30:work.Left-courierWindow.Width-30;courierBaseTop=Math.Max(work.Top+4,Math.Min(pet.Top+pet.Height-courierWindow.Height+8,work.Bottom-courierWindow.Height-4));courierWindow.Left=courierStartLeft;courierWindow.Top=courierBaseTop;courierWindow.Show();courierWindow.Activate();courierPhase="arrive";courierPhaseStarted=DateTime.Now;courierFrameTick=0;if(courierTimer==null){courierTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};courierTimer.Tick+=delegate{TickCourier();};}courierTimer.Start();
        }

        void BuildCourierWindowIfNeeded()
        {
            if(courierWindow!=null)return;courierWindow=new Window{Title="小猫来信",Width=184,Height=166,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowActivated=false};var canvas=new Grid{Background=Brushes.Transparent,Cursor=Cursors.Hand};courierImage=new Image{Width=122,Height=126,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,RenderTransformOrigin=new Point(.5,.82)};canvas.Children.Add(courierImage);courierEnvelope=new Border{Width=42,Height=31,CornerRadius=new CornerRadius(6),Background=new SolidColorBrush(Color.FromRgb(255,246,219)),BorderBrush=new SolidColorBrush(Color.FromRgb(132,88,48)),BorderThickness=new Thickness(2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,24,23),Child=new TextBlock{Text="✉",FontSize=19,Foreground=new SolidColorBrush(Color.FromRgb(132,88,48)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};canvas.Children.Add(courierEnvelope);courierBubbleText=new TextBlock{FontSize=11.5,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Margin=new Thickness(10,6,10,6)};courierBubble=new Border{Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Child=courierBubbleText};canvas.Children.Add(courierBubble);canvas.MouseLeftButtonUp+=delegate{ReceiveCourierLetter();};courierWindow.Content=canvas;
        }

        void TickCourier()
        {
            if(activeCourierLetter==null){courierTimer.Stop();return;}
            courierFrameTick++;double elapsed=(DateTime.Now-courierPhaseStarted).TotalSeconds;
            if(courierPhase=="arrive"){
                double p=Math.Min(1,elapsed/1.35),ease=1-Math.Pow(1-p,3);courierWindow.Left=courierStartLeft+(courierTargetLeft-courierStartLeft)*ease;courierWindow.Top=courierBaseTop-Math.Abs(Math.Sin(p*Math.PI*8))*3;courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,false);
                if(p>=1){courierPhase="wait";courierBubble.Visibility=Visibility.Visible;courierTimer.Stop();React(activeCourierLetter.SenderNickname+" 的小猫来送信啦～",true);}
            }else if(courierPhase=="receive"){
                double p=Math.Min(1,elapsed/.62);var scale=courierEnvelope.RenderTransform as ScaleTransform;if(scale==null){scale=new ScaleTransform(1,1);courierEnvelope.RenderTransform=scale;courierEnvelope.RenderTransformOrigin=new Point(.5,.5);}scale.ScaleX=1-p*.7;scale.ScaleY=1-p*.7;courierEnvelope.Opacity=1-p;courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,true);
                if(p>=1){courierPhase="leave";courierPhaseStarted=DateTime.Now;courierEnvelope.Visibility=Visibility.Collapsed;courierBubbleText.Text="信送到啦，再见～";}
            }else if(courierPhase=="leave"){
                double p=Math.Min(1,elapsed/1.15),ease=p*p;courierWindow.Left=courierTargetLeft+(courierExitLeft-courierTargetLeft)*ease;courierWindow.Top=courierBaseTop-Math.Abs(Math.Sin(p*Math.PI*7))*3;courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,false);if(p>=1)FinishCourier();
            }
        }

        void ReceiveCourierLetter()
        {
            if(activeCourierLetter==null||courierPhase!="wait")return;MomoLetter received=activeCourierLetter;courierPhase="receive";courierPhaseStarted=DateTime.Now;courierBubble.Visibility=Visibility.Collapsed;courierTimer.Start();if(!String.IsNullOrWhiteSpace(received.Id))MarkLetterRead(received);React("收到 "+received.SenderNickname+" 的信啦 ♡",true);ShowReceivedLetter(received);
        }

        void ShowReceivedLetter(MomoLetter letter)
        {
            bool preview=String.IsNullOrWhiteSpace(letter.Id);var dialog=new Window{Title="收到一封信",Width=470,Height=330,MinWidth=420,MinHeight=280,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true};Ui.StyleWindow(dialog);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(24)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var header=new Grid{Cursor=Cursors.SizeAll};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var title=new StackPanel();title.Children.Add(Ui.Title("来自 "+letter.SenderNickname+" 的信",20));title.Children.Add(Ui.Subtitle(preview?"送信动画预览":"点击收信回执已送出 · "+ShortCloudTime(letter.CreatedAt)));header.Children.Add(title);var close=Ui.MakeCloseButton();close.Click+=delegate{dialog.Close();};Grid.SetColumn(close,1);header.Children.Add(close);header.MouseLeftButtonDown+=delegate{try{dialog.DragMove();}catch{}};root.Children.Add(header);var content=new TextBlock{Text=letter.Content,FontSize=14,Foreground=Ui.Ink,TextWrapping=TextWrapping.Wrap,LineHeight=23,Margin=new Thickness(4,18,4,18)};Grid.SetRow(content,1);root.Children.Add(content);var reply=MakeButton(preview?"完成预览":"回复 "+letter.SenderNickname,Ui.Accent);reply.Foreground=Brushes.White;reply.HorizontalAlignment=HorizontalAlignment.Right;reply.Click+=delegate{dialog.Close();if(preview)return;OpenMessengerPanel();selectedMessengerMember=messengerMembers.FirstOrDefault(x=>x.Id==letter.SenderId);RefreshMessengerBody();if(selectedMessengerMember!=null)LoadConversation(selectedMessengerMember);};Grid.SetRow(reply,2);root.Children.Add(reply);shell.Child=root;dialog.Content=shell;var work=SystemParameters.WorkArea;dialog.Left=Math.Max(work.Left+8,Math.Min(pet.Left-dialog.Width-12,work.Right-dialog.Width-8));dialog.Top=Math.Max(work.Top+8,Math.Min(pet.Top-dialog.Height+pet.Height,work.Bottom-dialog.Height-8));dialog.Show();
        }

        void FinishCourier()
        {
            courierTimer.Stop();courierWindow.Hide();courierEnvelope.Opacity=1;courierEnvelope.RenderTransform=null;courierEnvelope.Visibility=Visibility.Visible;activeCourierLetter=null;courierPhase=null;if(courierQueue.Count>0){var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};delay.Tick+=delegate{delay.Stop();StartNextCourier();};delay.Start();}
        }

        void OnLocalSkinChanged(string skinId)
        {
            if(momoAccount==null)return;momoAccount.SkinId=skinId;SaveMomoAccount();if(IsMomoSignedIn())MomoApi<Dictionary<string,object>>("PATCH","/api/momo/profile",new Dictionary<string,object>{{"skinId",skinId}},true,delegate(Dictionary<string,object> ignored){},delegate(string ignored){});
        }

        void PreviewCourierSkin(string skinId)
        {
            courierQueue.Enqueue(new MomoLetter{Id="",SenderId="",SenderNickname=SkinName(String.IsNullOrWhiteSpace(skinId)?"default":skinId),SenderSkinId=String.IsNullOrWhiteSpace(skinId)?"default":skinId,Content="这是一封送信动画预览。真实来信会在点击后把“已收信”回执送回给对方。",CreatedAt=DateTime.Now.ToString("o"),Status="sent"});if(activeCourierLetter==null)StartNextCourier();
        }

        void SetMessengerTopmost(bool value){if(messengerPanel!=null)messengerPanel.Topmost=value;if(courierWindow!=null)courierWindow.Topmost=true;}
        void CloseMessengerWindows(){if(messengerPollTimer!=null)messengerPollTimer.Stop();if(courierTimer!=null)courierTimer.Stop();if(courierWindow!=null)courierWindow.Close();if(messengerPanel!=null)messengerPanel.Close();}
    }
}
