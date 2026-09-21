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

        void LoadMomoGroups()
        {
            if(!IsMomoSignedIn())return;MomoApi<List<MomoGroup>>("GET","/api/momo/groups",null,true,delegate(List<MomoGroup> items){momoGroups.Clear();if(items!=null)momoGroups.AddRange(items);if(messengerGroups!=null){messengerGroups.ItemsSource=null;messengerGroups.ItemsSource=momoGroups;}if(messengerStatus!=null)messengerStatus.Text="已连接 · "+messengerMembers.Count+" 位朋友 · "+momoGroups.Count+" 个小组";},delegate(string error){if(messengerStatus!=null)messengerStatus.Text=error;});
        }

        void ShowCreateMomoGroupDialog()
        {
            if(messengerMembers.Count==0){if(messengerStatus!=null)messengerStatus.Text="还没有可邀请的联系人";return;}var dialog=new Window{Title="拉个小组",Width=470,Height=570,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Owner=messengerPanel,Topmost=messengerPanel.Topmost};Ui.StyleWindow(dialog);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(24)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var header=new Grid{Cursor=Cursors.SizeAll};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(Ui.Title("拉个小组",21));var close=Ui.MakeCloseButton();close.Click+=delegate{dialog.Close();};Grid.SetColumn(close,1);header.Children.Add(close);header.MouseLeftButtonDown+=delegate{try{dialog.DragMove();}catch{}};root.Children.Add(header);var intro=new StackPanel{Margin=new Thickness(0,12,0,12)};intro.Children.Add(HubText("给小组起个名字",11.5,Ui.SubInk,FontWeights.SemiBold));var name=new TextBox{Height=42,ToolTip="例如：项目协作组",Margin=new Thickness(0,5,0,10),VerticalContentAlignment=VerticalAlignment.Center};intro.Children.Add(name);intro.Children.Add(HubText("邀请这些朋友",11.5,Ui.SubInk,FontWeights.SemiBold));Grid.SetRow(intro,1);root.Children.Add(intro);var choices=new StackPanel();foreach(var member in messengerMembers){var check=new CheckBox{Content=member.ToString(),Tag=member,FontSize=13,Foreground=Ui.Ink,Margin=new Thickness(4,7,4,7)};choices.Children.Add(check);}var scroll=new ScrollViewer{Content=choices,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Ui.Inner,Padding=new Thickness(12)};Grid.SetRow(scroll,2);root.Children.Add(scroll);var footer=new Grid{Margin=new Thickness(0,14,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var status=HubText("至少邀请 1 位朋友",11.5,Ui.SubInk,FontWeights.Normal);status.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(status);var create=MakeButton("创建小组",Ui.Accent);create.Foreground=Brushes.White;create.Click+=delegate{var picked=choices.Children.OfType<CheckBox>().Where(x=>x.IsChecked==true).Select(x=>((MomoRemoteMember)x.Tag).Id).ToList();string groupName=(name.Text??"").Trim();if(groupName.Length<2){status.Text="小组名称至少 2 个字";return;}if(picked.Count==0){status.Text="请至少选择 1 位朋友";return;}status.Text="正在邀请…";var body=new Dictionary<string,object>{{"name",groupName},{"memberIds",picked}};MomoApi<MomoGroup>("POST","/api/momo/groups",body,true,delegate(MomoGroup group){if(group!=null){momoGroups.Add(group);selectedMomoGroup=group;selectedMessengerMember=null;}dialog.Close();LoadMomoGroups();if(messengerGroups!=null)messengerGroups.SelectedItem=group;LoadMomoGroupConversation(group);},delegate(string error){status.Text=error;});};Grid.SetColumn(create,1);footer.Children.Add(create);Grid.SetRow(footer,3);root.Children.Add(footer);shell.Child=root;dialog.Content=shell;dialog.ShowDialog();
        }

        void LoadMomoGroupConversation(MomoGroup group)
        {
            if(group==null)return;int requestId=++messengerViewRequestId;messengerRequestBusy=true;string groupId=group.Id;if(messengerTitle!=null)messengerTitle.Text=group.Name+"  ·  "+(group.Members==null?0:group.Members.Count)+" 人";MomoApi<List<MomoGroupMessage>>("GET","/api/momo/groups/"+Uri.EscapeDataString(groupId)+"/messages",null,true,delegate(List<MomoGroupMessage> items){if(requestId!=messengerViewRequestId)return;messengerRequestBusy=false;if(selectedMomoGroup==null||selectedMomoGroup.Id!=groupId||TextSelection.HasSelectionWithin(messengerPanel))return;momoGroupMessages.Clear();if(items!=null)momoGroupMessages.AddRange(items);RefreshConversationView();},delegate(string error){if(requestId!=messengerViewRequestId)return;messengerRequestBusy=false;if(messengerStatus!=null)messengerStatus.Text=error;});
        }
}
}
