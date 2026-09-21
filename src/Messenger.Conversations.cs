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

        void LoadMessengerMembers()
        {
            if(!IsMomoSignedIn())return;MomoApi<List<MomoRemoteMember>>("GET","/api/momo/members",null,true,delegate(List<MomoRemoteMember> items){messengerMembers.Clear();if(items!=null)messengerMembers.AddRange(items.Where(x=>x.Id!=momoAccount.MemberId));if(messengerContacts!=null){messengerContacts.ItemsSource=null;messengerContacts.ItemsSource=messengerMembers;}if(selectedMessengerMember==null&&selectedMomoGroup==null&&messengerMembers.Count>0){selectedMessengerMember=messengerMembers[0];if(messengerContacts!=null)messengerContacts.SelectedItem=selectedMessengerMember;LoadConversation(selectedMessengerMember);}if(messengerStatus!=null)messengerStatus.Text="已同步 "+messengerMembers.Count+" 位联系人";},delegate(string error){if(messengerStatus!=null)messengerStatus.Text=error;});
        }

        void LoadConversation(MomoRemoteMember member)
        {
            if(member==null)return;int requestId=++messengerViewRequestId;messengerRequestBusy=true;string memberId=member.Id;if(messengerTitle!=null)messengerTitle.Text="和 "+member.Nickname+" 的来信";MomoApi<List<MomoLetter>>("GET","/api/momo/messages/conversation/"+Uri.EscapeDataString(memberId),null,true,delegate(List<MomoLetter> items){if(requestId!=messengerViewRequestId)return;messengerRequestBusy=false;if(selectedMessengerMember==null||selectedMessengerMember.Id!=memberId||TextSelection.HasSelectionWithin(messengerPanel))return;messengerLetters.Clear();if(items!=null)messengerLetters.AddRange(items);TrackLetterStatuses(items);RefreshConversationView();},delegate(string error){if(requestId!=messengerViewRequestId)return;messengerRequestBusy=false;if(messengerStatus!=null)messengerStatus.Text=error;});
        }

        void RefreshConversationView()
        {
            if(messengerConversation==null)return;TextSelection.Clear();messengerConversation.Children.Clear();if(selectedMomoGroup!=null){foreach(var message in momoGroupMessages.OrderBy(x=>x.CreatedAt)){bool mine=message.SenderId==momoAccount.MemberId;var body=new StackPanel();body.Children.Add(HubText(mine?"我":message.SenderNickname,10.5,mine?Ui.AccentDeep:Ui.SubInk,FontWeights.SemiBold));if(!String.IsNullOrWhiteSpace(message.Content))body.Children.Add(Ui.ReadOnlyText(message.Content,13));AddAttachmentChips(body,message.Attachments,mine);body.Children.Add(HubText(ShortCloudTime(message.CreatedAt),10,Ui.SubInk,FontWeights.Normal));var card=HubCard(body,new Thickness(mine?72:0,0,mine?0:72,8),new Thickness(14,10,14,10));card.Background=mine?Ui.AccentSoft:Ui.Card;messengerConversation.Children.Add(card);}if(momoGroupMessages.Count==0)messengerConversation.Children.Add(BuildEmptyState("小组刚刚建好","发第一条消息，大家就能在这里看到。"));return;}if(selectedMessengerMember==null){messengerConversation.Children.Add(BuildEmptyState("欢迎来到 Momo 邮局","从左边选一位朋友，或点“拉个小组”。"));return;}foreach(var letter in messengerLetters.OrderBy(x=>x.CreatedAt)){bool mine=letter.SenderId==momoAccount.MemberId;var body=new StackPanel();if(!String.IsNullOrWhiteSpace(letter.Content))body.Children.Add(Ui.ReadOnlyText(letter.Content,13));AddAttachmentChips(body,letter.Attachments,mine);string state=mine?(letter.Status=="read"?"对方已收信  "+ShortCloudTime(letter.ReadAt):(letter.Status=="delivered"?"小猫已送达":"正在送信")):"收到于 "+ShortCloudTime(letter.CreatedAt);body.Children.Add(HubText(state,10.5,letter.Status=="read"?Ui.Green:Ui.SubInk,FontWeights.Normal));var card=HubCard(body,new Thickness(mine?72:0,0,mine?0:72,8),new Thickness(14,10,14,10));card.Background=mine?Ui.AccentSoft:Ui.Card;messengerConversation.Children.Add(card);}
        }

        string ShortCloudTime(string value){DateTime time;if(DateTime.TryParse(value,out time))return time.ToLocalTime().ToString("MM-dd HH:mm");return "";}

        void SendMomoMessage()
        {
            if(messengerSendBusy||momoFileOperationBusy||(selectedMessengerMember==null&&selectedMomoGroup==null)||messengerInput==null)return;string content=(messengerInput.Text??"").Trim();if(String.IsNullOrWhiteSpace(content)&&pendingAttachments.Count==0){messengerStatus.Text="写句话，或者放个文件进去";return;}if(content.Length>1000){messengerStatus.Text="消息最多 1000 个字";return;}var body=new Dictionary<string,object>{{"content",content},{"skinId",petMovement==null?"default":petMovement.SkinId}};if(pendingAttachments.Count>0)body["attachments"]=pendingAttachments.Select(x=>new Dictionary<string,object>{{"name",x.Name??""},{"url",x.Url??""},{"type",x.Type??""},{"size",x.Size}}).ToList();messengerSendBusy=true;SetMessengerBusy(true);if(selectedMomoGroup!=null){MomoApi<MomoGroupMessage>("POST","/api/momo/groups/"+Uri.EscapeDataString(selectedMomoGroup.Id)+"/messages",body,true,delegate(MomoGroupMessage message){messengerSendBusy=false;SetMessengerBusy(false);messengerInput.Clear();pendingAttachments.Clear();RefreshAttachmentChips();if(message!=null)momoGroupMessages.Add(message);RefreshConversationView();messengerStatus.Text="已发到小组";},delegate(string error){messengerSendBusy=false;SetMessengerBusy(false);messengerStatus.Text=error;});return;}body["receiverId"]=selectedMessengerMember.Id;MomoApi<MomoLetter>("POST","/api/momo/messages",body,true,delegate(MomoLetter letter){messengerSendBusy=false;SetMessengerBusy(false);messengerInput.Clear();pendingAttachments.Clear();RefreshAttachmentChips();if(letter!=null){if(!String.IsNullOrWhiteSpace(letter.Id)){knownLetterStatus[letter.Id]=letter.Status??"sent";awaitingReceipts.Add(letter);}messengerLetters.Add(letter);}RefreshConversationView();messengerStatus.Text="小猫已经出发";},delegate(string error){messengerSendBusy=false;SetMessengerBusy(false);messengerStatus.Text=error;});
        }

        void PollMomoMessages()
        {
            if(!IsMomoSignedIn()||messengerRequestBusy)return;MomoApi<List<MomoLetter>>("GET","/api/momo/messages/inbox?unread=1",null,true,delegate(List<MomoLetter> items){if(items==null)return;foreach(var letter in items.OrderBy(x=>x.CreatedAt)){if(String.IsNullOrWhiteSpace(letter.Id)||knownLetterIds.Contains(letter.Id))continue;knownLetterIds.Add(letter.Id);// 陌生发件人先刷新联系人，送信文案才能显示 @用户名
if(!messengerMembers.Any(x=>x.Id==letter.SenderId))LoadMessengerMembers();courierQueue.Enqueue(letter);MarkLetterDelivered(letter);}if(activeCourierLetter==null&&courierQueue.Count>0)StartNextCourier();PollPendingReceipts();PollReceiptConversations();if(!TextSelection.HasSelectionWithin(messengerPanel)){if(selectedMessengerMember!=null)LoadConversation(selectedMessengerMember);else if(selectedMomoGroup!=null)LoadMomoGroupConversation(selectedMomoGroup);}},delegate(string error){if(messengerStatus!=null)messengerStatus.Text=error;});
        }
}
}
