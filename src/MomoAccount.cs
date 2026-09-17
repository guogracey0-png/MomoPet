using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    // Application identity: loaded once on desktop-pet startup, reused by every module.
    public partial class PetController
    {
        const string DefaultMomoCloud="https://cafe-api-zofigfdfto.cn-hangzhou.fcapp.run";
        MomoAccountState momoAccount;
        string momoToken;
        TextBlock accountStatus;
        TextBox accountUsernameBox;
        PasswordBox accountPasswordBox;
        string AccountStatePath(){return Path.Combine(dataDir,"momo-account.json");}
        string AccountTokenPath(){return Path.Combine(dataDir,"momo-account-token.dat");}
        byte[] AccountEntropy(){return Encoding.UTF8.GetBytes("MomoPet.Account.Token.v1");}

        void LoadMomoAccount()
        {
            try{if(File.Exists(AccountStatePath()))momoAccount=json.Deserialize<MomoAccountState>(File.ReadAllText(AccountStatePath(),Encoding.UTF8));}catch{}
            if(momoAccount==null)momoAccount=new MomoAccountState{ServerBase=DefaultMomoCloud,SkinId="default"};
            if(String.IsNullOrWhiteSpace(momoAccount.ServerBase))momoAccount.ServerBase=DefaultMomoCloud;
            try{if(File.Exists(AccountTokenPath()))momoToken=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(AccountTokenPath()),AccountEntropy(),DataProtectionScope.CurrentUser));}catch{momoToken=null;}
        }

        void SaveMomoAccount()
        {
            try{MomoStorage.WriteTextAtomic(AccountStatePath(),json.Serialize(momoAccount),new UTF8Encoding(false));if(!String.IsNullOrWhiteSpace(momoToken)){byte[] raw=Encoding.UTF8.GetBytes(momoToken);try{MomoStorage.WriteBytesAtomic(AccountTokenPath(),ProtectedData.Protect(raw,AccountEntropy(),DataProtectionScope.CurrentUser));}finally{Array.Clear(raw,0,raw.Length);}}}catch{}
        }

        bool IsMomoSignedIn(){return momoAccount!=null&&!String.IsNullOrWhiteSpace(momoAccount.MemberId)&&!String.IsNullOrWhiteSpace(momoToken);}
        string MomoServer(){return (momoAccount==null||String.IsNullOrWhiteSpace(momoAccount.ServerBase)?DefaultMomoCloud:momoAccount.ServerBase).Trim().TrimEnd('/');}

        Window momoAccountPanel;
        Grid momoAccountBody;

        void OpenMomoAccountPanel()
        {
            if(momoAccountPanel==null)BuildMomoAccountPanel();
            RefreshMomoAccountPanel();PositionHubWindow(momoAccountPanel);momoAccountPanel.Show();momoAccountPanel.Activate();
        }

        void BuildMomoAccountPanel()
        {
                momoAccountPanel=new Window{Title="桌宠账号",Width=540,Height=630,MinWidth=500,MinHeight=580,
                    WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,
                    AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};
                Ui.StyleWindow(momoAccountPanel);
                var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(20)};Ui.StyleCard(shell);
                var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
                root.Children.Add(BuildHubHeader(momoAccountPanel,"🐾","桌宠账号","一个账号，陪伴整个 MomoPet",null,null));
                momoAccountBody=new Grid();var scroll=new ScrollViewer{Content=momoAccountBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
                Grid.SetRow(scroll,1);root.Children.Add(scroll);shell.Child=root;momoAccountPanel.Content=shell;
                momoAccountPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;momoAccountPanel.Hide();}};
        }

        void RefreshMomoAccountPanel()
        {
            if(momoAccountBody==null)return;
            momoAccountBody.Children.Clear();momoAccountBody.ColumnDefinitions.Clear();momoAccountBody.RowDefinitions.Clear();
            if(!IsMomoSignedIn()){BuildSharedAccountGate(momoAccountBody);return;}
            var profile=new StackPanel{Margin=new Thickness(24)};
            profile.Children.Add(CommunityCat(110));
            profile.Children.Add(Ui.Title(momoAccount.Nickname,23));
            profile.Children.Add(HubText("@"+momoAccount.Username,13,Ui.SubInk,FontWeights.Normal));
            var info=HubText("已登录桌宠账号，各模块使用同一个身份。\n本地资料仍保存在当前电脑；更换皮肤会同步到送信小猫。",13,Ui.SubInk,FontWeights.Normal);info.Margin=new Thickness(0,24,0,24);profile.Children.Add(info);
            var logout=MakeButton("退出桌宠账号",Ui.Neutral);logout.Click+=delegate{LogoutMomoAccount();};profile.Children.Add(logout);
            profile.Children.Add(HubText("退出后，所有联网模块同步退出，本地功能仍可使用。",11,Ui.SubInk,FontWeights.Normal));
            momoAccountBody.Children.Add(profile);
        }

        void BuildSharedAccountGate(Grid host)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition());host.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(430)});host.ColumnDefinitions.Add(new ColumnDefinition());
            var card=HubCard(new Grid(),new Thickness(0,12,0,12),new Thickness(34));card.Background=Ui.Card;Grid.SetColumn(card,1);host.Children.Add(card);var form=(Grid)card.Child;for(int i=0;i<6;i++)form.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var mark=new Border{Width=64,Height=64,CornerRadius=new CornerRadius(22),Background=Ui.PeachSoft,HorizontalAlignment=HorizontalAlignment.Center,Child=HubText("🐾",28,Ui.AccentDeep,FontWeights.Normal)};((TextBlock)mark.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)mark.Child).VerticalAlignment=VerticalAlignment.Center;form.Children.Add(mark);
            var heading=new StackPanel{Margin=new Thickness(0,16,0,22)};heading.Children.Add(Ui.Title("登录你的桌宠账号",22));heading.HorizontalAlignment=HorizontalAlignment.Center;heading.Children.Add(HubText("使用已有博知汇账号，只需账号和密码",12.5,Ui.SubInk,FontWeights.Normal));Grid.SetRow(heading,1);form.Children.Add(heading);
            accountUsernameBox=new TextBox{Height=44,ToolTip="账号",Margin=new Thickness(0,0,0,10),VerticalContentAlignment=VerticalAlignment.Center};var userField=new StackPanel();userField.Children.Add(HubText("账号",12,Ui.Ink,FontWeights.SemiBold));accountUsernameBox.Margin=new Thickness(0,6,0,12);userField.Children.Add(accountUsernameBox);Grid.SetRow(userField,2);form.Children.Add(userField);
            accountPasswordBox=new PasswordBox{Height=44,ToolTip="密码",Margin=new Thickness(0,0,0,14),VerticalContentAlignment=VerticalAlignment.Center};accountPasswordBox.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){SubmitMomoAccount();e.Handled=true;}};var passField=new StackPanel();passField.Children.Add(HubText("密码",12,Ui.Ink,FontWeights.SemiBold));accountPasswordBox.Margin=new Thickness(0,6,0,16);passField.Children.Add(accountPasswordBox);Grid.SetRow(passField,3);form.Children.Add(passField);
            var actions=new Grid();actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());var register=MakeButton("注册新账号",Ui.Neutral);register.Height=42;register.Margin=new Thickness(0,0,6,0);register.Click+=delegate{ShowMomoRegisterDialog();};actions.Children.Add(register);var login=MakeButton("登录",Ui.Accent);login.Foreground=Brushes.White;login.Height=42;login.Margin=new Thickness(6,0,0,0);login.Click+=delegate{SubmitMomoAccount();};Grid.SetColumn(login,1);actions.Children.Add(login);Grid.SetRow(actions,4);form.Children.Add(actions);
            accountStatus=HubText("服务地址已经内置，不需要额外配置。",11.5,Ui.SubInk,FontWeights.Normal);accountStatus.TextAlignment=TextAlignment.Center;accountStatus.Margin=new Thickness(0,12,0,0);Grid.SetRow(accountStatus,5);form.Children.Add(accountStatus);
        }

        void SubmitMomoAccount()
        {
            string username=(accountUsernameBox.Text??"").Trim(),password=accountPasswordBox.Password??"";if(String.IsNullOrWhiteSpace(username)||String.IsNullOrWhiteSpace(password)){accountStatus.Text="请填写账号和密码";return;}accountStatus.Text="正在登录…";var body=new Dictionary<string,object>{{"username",username},{"password",password},{"skinId",petMovement==null?"default":petMovement.SkinId}};MomoApi<Dictionary<string,object>>("POST","/api/momo/auth/login",body,false,delegate(Dictionary<string,object> response){CompleteMomoAuth(response,accountPasswordBox,null);},delegate(string error){accountStatus.Text=error;});
        }

        void CompleteMomoAuth(Dictionary<string,object> response,PasswordBox password,Window dialog)
        {
            Dictionary<string,object> member=response!=null&&response.ContainsKey("member")?response["member"] as Dictionary<string,object>:null;if(response==null||member==null){if(accountStatus!=null)accountStatus.Text="服务返回的数据不完整";return;}momoToken=Convert.ToString(response["token"]);momoAccount.MemberId=Convert.ToString(member["id"]);momoAccount.Username=Convert.ToString(member["username"]);momoAccount.Nickname=Convert.ToString(member["nickname"]);momoAccount.Avatar=member.ContainsKey("avatar")?Convert.ToString(member["avatar"]):"🐾";momoAccount.SkinId=petMovement==null?"default":petMovement.SkinId;SaveMomoAccount();if(password!=null)password.Clear();if(dialog!=null)dialog.Close();if(messengerPollTimer!=null)messengerPollTimer.Start();RefreshMessengerBody();RefreshMomoAccountPanel();RefreshCommunityFeed();RefreshLauncherIdentity();LoadMessengerMembers();LoadMomoGroups();PollMomoMessages();React("账号连好啦，等小猫来送信～",true);
        }

        void ShowMomoRegisterDialog()
        {
            var dialog=new Window{Title="注册桌宠账号",Width=430,Height=440,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Owner=momoAccountPanel,Topmost=momoAccountPanel.Topmost};Ui.StyleWindow(dialog);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(26)};Ui.StyleCard(shell);var root=new StackPanel();var header=new Grid{Cursor=Cursors.SizeAll};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(Ui.Title("创建账号",21));var close=Ui.MakeCloseButton();close.Click+=delegate{dialog.Close();};Grid.SetColumn(close,1);header.Children.Add(close);header.MouseLeftButtonDown+=delegate{try{dialog.DragMove();}catch{}};root.Children.Add(header);root.Children.Add(HubText("注册时填写一次昵称，之后登录仍然只用账号和密码。",12,Ui.SubInk,FontWeights.Normal));var username=new TextBox{Height=42,ToolTip="账号（3-20 位字母、数字或下划线）",Margin=new Thickness(0,18,0,9)};var nickname=new TextBox{Height=42,ToolTip="昵称（朋友看到的名字）",Margin=new Thickness(0,0,0,9)};var password=new PasswordBox{Height=42,ToolTip="密码（至少 6 位）",Margin=new Thickness(0,0,0,12)};root.Children.Add(username);root.Children.Add(nickname);root.Children.Add(password);var status=HubText("",11.5,Ui.Up,FontWeights.Normal);status.MinHeight=28;root.Children.Add(status);var submit=MakeButton("创建并登录",Ui.Accent);submit.Foreground=Brushes.White;submit.Height=42;submit.HorizontalAlignment=HorizontalAlignment.Stretch;submit.Click+=delegate{string u=(username.Text??"").Trim(),n=(nickname.Text??"").Trim(),p=password.Password??"";if(String.IsNullOrWhiteSpace(u)||String.IsNullOrWhiteSpace(n)||String.IsNullOrWhiteSpace(p)){status.Text="请填写账号、昵称和密码";return;}status.Text="正在创建…";var body=new Dictionary<string,object>{{"username",u},{"nickname",n},{"password",p},{"skinId",petMovement==null?"default":petMovement.SkinId}};MomoApi<Dictionary<string,object>>("POST","/api/momo/auth/register",body,false,delegate(Dictionary<string,object> response){CompleteMomoAuth(response,password,dialog);},delegate(string error){status.Text=error;});};root.Children.Add(submit);shell.Child=root;dialog.Content=shell;dialog.ShowDialog();
        }

        void LogoutMomoAccount()
        {
            messengerViewRequestId++;messengerRequestBusy=false;messengerSendBusy=false;if(IsMomoSignedIn())MomoApi<Dictionary<string,object>>("POST","/api/momo/auth/logout",null,true,delegate(Dictionary<string,object> ignored){},delegate(string ignored){});momoAccount.MemberId=null;momoAccount.Username=null;momoAccount.Nickname=null;momoAccount.Avatar=null;momoToken=null;try{if(File.Exists(AccountTokenPath()))File.Delete(AccountTokenPath());}catch{}SaveMomoAccount();if(messengerPollTimer!=null)messengerPollTimer.Stop();selectedMessengerMember=null;selectedMomoGroup=null;messengerMembers.Clear();messengerLetters.Clear();momoGroups.Clear();momoGroupMessages.Clear();pendingAttachments.Clear();RefreshMessengerBody();RefreshMomoAccountPanel();RefreshCommunityFeed();RefreshLauncherIdentity();
        }

    }
}
