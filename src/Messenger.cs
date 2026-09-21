using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    public partial class PetController
    {

        void InitializeMessenger()
        {
            messengerPollTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};messengerPollTimer.Tick+=delegate{PollMomoMessages();};if(IsMomoSignedIn()){messengerPollTimer.Start();PollMomoMessages();}
        }

        // 后台线程回调 UI 前先确认调度器还活着：窗口关闭后再 BeginInvoke 会抛
        // InvalidOperationException；异常从线程池线程逃逸出去会直接终止进程，造成“用着用着就闪退”。

        void OpenMessengerPanel()
        {
            if(messengerPanel==null)BuildMessengerPanel();if(RestoreShelvedIfNeeded(messengerPanel))return;RefreshMessengerBody();PositionHubWindow(messengerPanel);messengerPanel.Show();messengerPanel.Activate();if(IsMomoSignedIn()){LoadMessengerMembers();PollMomoMessages();}
        }

        void BuildMessengerPanel()
        {
            messengerPanel=new Window{Title="Momo 邮局",Width=1180,Height=780,MinWidth=960,MinHeight=640,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(messengerPanel);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(22)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.Children.Add(BuildHubHeader(messengerPanel,"💌","Momo 邮局","朋友私信由小猫送达，小组消息留在安静的信箱里",delegate{if(IsMomoSignedIn()){LoadMessengerMembers();LoadMomoGroups();}else RefreshMessengerBody();},"刷新"));messengerBody=new Grid{Background=Ui.Paper};Grid.SetRow(messengerBody,1);root.Children.Add(messengerBody);shell.Child=root;messengerPanel.Content=shell;messengerPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;messengerPanel.Hide();}};
        }

        void RefreshMessengerBody()
        {
            InstallMomoFileInput();if(momoFileOperationBusy)return;
            if(messengerBody==null)return;messengerBody.Children.Clear();messengerBody.ColumnDefinitions.Clear();messengerBody.RowDefinitions.Clear();if(!IsMomoSignedIn()){BuildAccountGate();return;}BuildConversationWorkspace();
        }

        void BuildAccountGate()
        {
            var content=new StackPanel{MaxWidth=440,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(32)};
            content.Children.Add(CommunityCat(110));content.Children.Add(Ui.Title("欢迎来到 Momo 邮局",23));
            var intro=HubText("私信、小组、文件与小猫送信，都在这里。\n登录桌宠账号后即可开始。",13,Ui.SubInk,FontWeights.Normal);intro.Margin=new Thickness(0,16,0,20);content.Children.Add(intro);
            var login=MakeButton("登录桌宠账号",Ui.Accent);login.Foreground=Brushes.White;login.Click+=delegate{OpenMomoAccountPanel();};content.Children.Add(login);messengerBody.Children.Add(content);
        }

        void BuildConversationWorkspace()
        {
            messengerBody.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(276)});messengerBody.ColumnDefinitions.Add(new ColumnDefinition());messengerBody.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(204)});
            var leftCard=HubCard(new Grid(),new Thickness(0,10,12,0),new Thickness(14));var left=(Grid)leftCard.Child;left.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});left.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});left.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});left.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            var peopleTitle=new Grid{Margin=new Thickness(2,0,2,8)};peopleTitle.ColumnDefinitions.Add(new ColumnDefinition());peopleTitle.Children.Add(Ui.Title("朋友",16));left.Children.Add(peopleTitle);messengerContacts=new ListBox{ItemsSource=messengerMembers,BorderThickness=new Thickness(0),Background=Brushes.Transparent};messengerContacts.SelectionChanged+=delegate{var picked=messengerContacts.SelectedItem as MomoRemoteMember;if(picked==null)return;selectedMessengerMember=picked;selectedMomoGroup=null;if(messengerGroups!=null)messengerGroups.SelectedItem=null;LoadConversation(picked);};Grid.SetRow(messengerContacts,1);left.Children.Add(messengerContacts);
            var groupTitle=new Grid{Margin=new Thickness(2,10,2,8)};groupTitle.ColumnDefinitions.Add(new ColumnDefinition());groupTitle.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});groupTitle.Children.Add(Ui.Title("小组",16));var createGroup=MakeButton("＋ 拉个小组",Ui.PeachSoft);createGroup.Padding=new Thickness(10,5,10,5);createGroup.Click+=delegate{ShowCreateMomoGroupDialog();};Grid.SetColumn(createGroup,1);groupTitle.Children.Add(createGroup);Grid.SetRow(groupTitle,2);left.Children.Add(groupTitle);messengerGroups=new ListBox{ItemsSource=momoGroups,BorderThickness=new Thickness(0),Background=Brushes.Transparent};messengerGroups.SelectionChanged+=delegate{var picked=messengerGroups.SelectedItem as MomoGroup;if(picked==null)return;selectedMomoGroup=picked;selectedMessengerMember=null;if(messengerContacts!=null)messengerContacts.SelectedItem=null;LoadMomoGroupConversation(picked);};Grid.SetRow(messengerGroups,3);left.Children.Add(messengerGroups);messengerBody.Children.Add(leftCard);
            var centerCard=HubCard(new Grid(),new Thickness(0,10,12,0),new Thickness(16));var center=(Grid)centerCard.Child;center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});center.RowDefinitions.Add(new RowDefinition());center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});messengerTitle=Ui.Title(selectedMomoGroup!=null?selectedMomoGroup.Name:(selectedMessengerMember==null?"Momo 信箱":"和 "+selectedMessengerMember.Nickname+" 的来信"),17);messengerTitle.Margin=new Thickness(2,0,0,12);center.Children.Add(messengerTitle);messengerConversation=new StackPanel{Margin=new Thickness(12)};var scroll=new ScrollViewer{Content=messengerConversation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Ui.Inner};Grid.SetRow(scroll,1);center.Children.Add(scroll);var composer=new Grid{Margin=new Thickness(0,12,0,0)};composer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});composer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});messengerAttachmentPanel=new WrapPanel{Margin=new Thickness(0,0,0,6)};composer.Children.Add(messengerAttachmentPanel);var composerRow=new Grid();composerRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});composerRow.ColumnDefinitions.Add(new ColumnDefinition());composerRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var attachmentActions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,8,0)};var attach=MakeButton("📎 文件",Ui.Neutral);attach.Height=44;attach.Padding=new Thickness(10,0,10,0);attach.ToolTip="选择文件，也可 Ctrl+V 粘贴或拖入窗口";attach.Click+=delegate{PickMomoFiles();};attachmentActions.Children.Add(attach);var attachFolder=MakeButton("文件夹",Ui.Neutral);attachFolder.Height=44;attachFolder.Padding=new Thickness(10,0,10,0);attachFolder.Margin=new Thickness(5,0,0,0);attachFolder.ToolTip="选择文件夹，邮局会自动打包为同名 ZIP";attachFolder.Click+=delegate{PickMomoFolder();};attachmentActions.Children.Add(attachFolder);composerRow.Children.Add(attachmentActions);messengerInput=new TextBox{Height=44,AcceptsReturn=false,ToolTip="写一封信；回车发送；Ctrl+V 粘贴文件",VerticalContentAlignment=VerticalAlignment.Center};messengerInput.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){SendMomoMessage();e.Handled=true;}};Grid.SetColumn(messengerInput,1);composerRow.Children.Add(messengerInput);messengerSendButton=MakeButton("送出",Ui.Accent);messengerSendButton.Foreground=Brushes.White;messengerSendButton.Height=44;messengerSendButton.Margin=new Thickness(8,0,0,0);messengerSendButton.Click+=delegate{SendMomoMessage();};Grid.SetColumn(messengerSendButton,2);composerRow.Children.Add(messengerSendButton);Grid.SetRow(composerRow,1);composer.Children.Add(composerRow);Grid.SetRow(composer,2);center.Children.Add(composer);Grid.SetColumn(centerCard,1);messengerBody.Children.Add(centerCard);
            var right=new StackPanel{Margin=new Thickness(0,10,0,0)};var me=new StackPanel();me.Children.Add(HubText((String.IsNullOrWhiteSpace(momoAccount.Avatar)?"🐾":momoAccount.Avatar)+"  "+momoAccount.Nickname,15,Ui.Ink,FontWeights.SemiBold));me.Children.Add(HubText("@"+momoAccount.Username,11.5,Ui.SubInk,FontWeights.Normal));right.Children.Add(HubCard(me,new Thickness(0,0,0,10),new Thickness(15)));var tip=new StackPanel();tip.Children.Add(HubText("小猫邮局",13,Ui.Ink,FontWeights.SemiBold));tip.Children.Add(HubText("私信会由对方的小猫送到桌面；小组消息安静留在信箱里。\n\n添加附件：复制文件或文件夹后按 Ctrl+V，也可直接拖入邮局。文件夹会自动打包为同名 ZIP；上传完成后点击“送出”。",11.5,Ui.SubInk,FontWeights.Normal));var preview=MakeButton("预览送信",Ui.Neutral);preview.Margin=new Thickness(0,10,0,0);preview.Click+=delegate{PreviewCourierSkin(selectedMessengerMember==null?(petMovement==null?"default":petMovement.SkinId):selectedMessengerMember.SkinId);};tip.Children.Add(preview);right.Children.Add(HubCard(tip,new Thickness(0,0,0,10),new Thickness(15)));messengerStatus=HubText("云端已连接",11.5,Ui.SubInk,FontWeights.Normal);right.Children.Add(messengerStatus);var logout=MakeButton("管理桌宠账号",Brushes.Transparent);logout.Margin=new Thickness(0,12,0,0);logout.Click+=delegate{OpenMomoAccountPanel();};right.Children.Add(logout);Grid.SetColumn(right,2);messengerBody.Children.Add(right);RefreshConversationView();RefreshAttachmentChips();LoadMomoGroups();
        }

        void SetMessengerTopmost(bool value){if(momoAccountPanel!=null)momoAccountPanel.Topmost=value;if(messengerPanel!=null)messengerPanel.Topmost=value;if(courierWindow!=null)courierWindow.Topmost=true;}

        void CloseMessengerWindows(){if(momoAccountPanel!=null)momoAccountPanel.Close();if(messengerPollTimer!=null)messengerPollTimer.Stop();if(courierTimer!=null)courierTimer.Stop();if(receiptTimer!=null)receiptTimer.Stop();if(courierWindow!=null)courierWindow.Close();if(receiptWindow!=null)receiptWindow.Close();if(messengerPanel!=null)messengerPanel.Close();}
}
}
