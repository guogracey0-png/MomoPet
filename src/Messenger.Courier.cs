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

        void MarkLetterDelivered(MomoLetter letter){MomoApi<Dictionary<string,object>>("POST","/api/momo/messages/"+Uri.EscapeDataString(letter.Id)+"/delivered",null,true,delegate(Dictionary<string,object> ignored){},delegate(string ignored){});}

        void MarkLetterRead(MomoLetter letter){MomoApi<Dictionary<string,object>>("POST","/api/momo/messages/"+Uri.EscapeDataString(letter.Id)+"/read",null,true,delegate(Dictionary<string,object> ignored){},delegate(string error){if(messengerStatus!=null)messengerStatus.Text="收信回执稍后重试："+error;});}

        // 会话接口里既有别人发来的信，也有自己送出的信；只挑自己送出、状态刚翻成 read 的那封，
        // 触发“对方已收到”动画。首次看到的信（字典里没有记录）只登记不播动画，避免开面板时刷屏。

        void TrackLetterStatuses(List<MomoLetter> items)
        {
            if(items==null)return;
            foreach(var letter in items){
                if(letter==null||String.IsNullOrWhiteSpace(letter.Id))continue;string next=String.IsNullOrWhiteSpace(letter.Status)?"sent":letter.Status;string previous;bool known=knownLetterStatus.TryGetValue(letter.Id,out previous);knownLetterStatus[letter.Id]=next;if(!known)continue;if(previous=="read"||next!="read")continue;if(momoAccount==null||letter.SenderId!=momoAccount.MemberId)continue;awaitingReceipts.RemoveAll(x=>x!=null&&x.Id==letter.Id);if(receiptQueue.Any(x=>x!=null&&x.Id==letter.Id))continue;receiptQueue.Enqueue(letter);
            }
            PollPendingReceipts();
        }

        void PollPendingReceipts(){if(activeReceipt==null&&receiptQueue.Count>0)StartNextReceipt();}

        // 自己送出的信可能分散在多个联系人下；只对还没收到回执的那几封，补拉一次对应会话，
        // 这样即使发送方没停留在那个聊天窗口，也能及时看到“对方已收到”。

        void PollReceiptConversations()
        {
            if(awaitingReceipts.Count==0)return;var peers=awaitingReceipts.Where(x=>x!=null&&!String.IsNullOrWhiteSpace(x.ReceiverId)).Select(x=>x.ReceiverId).Distinct().ToList();foreach(string peer in peers){if(selectedMessengerMember!=null&&selectedMessengerMember.Id==peer)continue;MomoApi<List<MomoLetter>>("GET","/api/momo/messages/conversation/"+Uri.EscapeDataString(peer),null,true,delegate(List<MomoLetter> items){TrackLetterStatuses(items);},delegate(string error){});}
        }

        void BuildReceiptWindowIfNeeded()
        {
            if(receiptWindow!=null)return;receiptWindow=new Window{Title="收信回执",Width=286,Height=88,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowActivated=false,IsHitTestVisible=false};
            receiptRoot=new Grid{Background=Brushes.Transparent};var card=new Border{Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Padding=new Thickness(14,12,16,12),Effect=Ui.NewCardShadow()};
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());var badge=new Border{Width=40,Height=40,CornerRadius=new CornerRadius(20),Background=Ui.GreenSoft,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            receiptIcon=new TextBlock{Text="💌",FontSize=18,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=new Point(.5,.5)};badge.Child=receiptIcon;row.Children.Add(badge);
            var text=new StackPanel{Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};receiptTitleText=new TextBlock{Text="对方已收到",FontSize=14.5,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink};receiptSubText=new TextBlock{Text="",FontSize=11.5,Foreground=Ui.SubInk,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,0),MaxWidth=190,TextTrimming=TextTrimming.CharacterEllipsis};text.Children.Add(receiptTitleText);text.Children.Add(receiptSubText);Grid.SetColumn(text,1);row.Children.Add(text);
            card.Child=row;receiptRoot.Children.Add(card);receiptWindow.Content=receiptRoot;if(receiptTimer==null){receiptTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};receiptTimer.Tick+=delegate{TickReceipt();};}
        }

        void StartNextReceipt()
        {
            if(receiptQueue.Count==0||activeReceipt!=null)return;activeReceipt=receiptQueue.Dequeue();BuildReceiptWindowIfNeeded();string name=String.IsNullOrWhiteSpace(activeReceipt.ReceiverNickname)?"对方":activeReceipt.ReceiverNickname;receiptTitleText.Text="对方已收到";receiptSubText.Text=name+" 已经读了你的信"+(String.IsNullOrWhiteSpace(activeReceipt.ReadAt)?"":" · "+ShortCloudTime(activeReceipt.ReadAt));
            var work=SystemParameters.WorkArea;double width=receiptWindow.Width,height=receiptWindow.Height;receiptTargetTop=Math.Max(work.Top+10,Math.Min(pet.Top+18,work.Bottom-height-10));double targetLeft=Math.Min(work.Right-width-10,pet.Left+pet.Width+12);if(targetLeft<work.Left+10)targetLeft=Math.Max(work.Left+10,pet.Left-width-12);receiptTargetLeft=targetLeft;receiptStartLeft=targetLeft+34;receiptExitLeft=targetLeft+34;
            receiptWindow.Left=receiptStartLeft;receiptWindow.Top=receiptTargetTop;receiptRoot.Opacity=0;receiptWindow.Show();Ui.Pulse(receiptIcon);receiptPhase="in";receiptPhaseStarted=DateTime.Now;receiptTimer.Start();
        }

        void TickReceipt()
        {
            if(activeReceipt==null){receiptTimer.Stop();return;}double elapsed=(DateTime.Now-receiptPhaseStarted).TotalSeconds;
            if(receiptPhase=="in"){double p=Math.Min(1,elapsed/.34),ease=1-Math.Pow(1-p,3);receiptWindow.Left=receiptStartLeft+(receiptTargetLeft-receiptStartLeft)*ease;receiptRoot.Opacity=p;if(p>=1){receiptPhase="hold";receiptPhaseStarted=DateTime.Now;receiptRoot.Opacity=1;}}
            else if(receiptPhase=="hold"){if(elapsed>=2.4){receiptPhase="out";receiptPhaseStarted=DateTime.Now;}}
            else if(receiptPhase=="out"){double p=Math.Min(1,elapsed/.34),ease=p*p;receiptWindow.Left=receiptTargetLeft+(receiptExitLeft-receiptTargetLeft)*ease;receiptRoot.Opacity=1-p;if(p>=1)FinishReceipt();}
        }

        void FinishReceipt()
        {
            receiptTimer.Stop();receiptWindow.Hide();receiptRoot.Opacity=1;activeReceipt=null;receiptPhase=null;if(receiptQueue.Count>0){var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(320)};delay.Tick+=delegate{delay.Stop();StartNextReceipt();};delay.Start();}
        }

        // 送信文案优先显示用户名（@账号），收件人一眼能认出是谁；查不到时回退到昵称。

        string CourierSenderName(MomoLetter letter)
        {
            if(letter==null)return "朋友";
            var member=messengerMembers.FirstOrDefault(x=>x.Id==letter.SenderId&&!String.IsNullOrWhiteSpace(x.Username));
            if(member!=null)return "@"+member.Username;
            return String.IsNullOrWhiteSpace(letter.SenderNickname)?"朋友":letter.SenderNickname;
        }

        ImageSource CourierFrame(string skinId,bool happy)
        {
            if(String.IsNullOrWhiteSpace(skinId)||skinId=="default")return happy?poses[6]:walkFrames[(courierFrameTick/5)%walkFrames.Length];BitmapImage[] motions;if(skinMotionFrames.TryGetValue(skinId,out motions)&&motions!=null)return motions[happy?6:1];return SkinPreviewFrame(skinId);
        }

        void StartNextCourier()
        {
            if(courierQueue.Count==0||activeCourierLetter!=null)return;activeCourierLetter=courierQueue.Dequeue();BuildCourierWindowIfNeeded();courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,false);courierBubbleText.Text=CourierSenderName(activeCourierLetter)+" 给你送信来啦"+(activeCourierLetter.Attachments!=null&&activeCourierLetter.Attachments.Count>0?"（带了 "+activeCourierLetter.Attachments.Count+" 个文件）":"");courierBubble.Visibility=Visibility.Collapsed;if(courierReceiveButton!=null)courierReceiveButton.Visibility=Visibility.Collapsed;courierEnvelope.Visibility=Visibility.Visible;var work=SystemParameters.WorkArea;double petCenter=pet.Left+pet.Width/2;bool enterFromLeft=petCenter>work.Left+work.Width*.58;courierDirection=enterFromLeft?1:-1;((ScaleTransform)courierImage.RenderTransform).ScaleX=enterFromLeft?1:-1;courierTargetLeft=enterFromLeft?Math.Max(work.Left+4,pet.Left-courierWindow.Width+42):Math.Min(work.Right-courierWindow.Width-4,pet.Left+pet.Width-40);courierStartLeft=enterFromLeft?work.Left-courierWindow.Width-20:work.Right+20;courierExitLeft=enterFromLeft?work.Right+30:work.Left-courierWindow.Width-30;courierBaseTop=Math.Max(work.Top+4,Math.Min(pet.Top+pet.Height-courierWindow.Height+8,work.Bottom-courierWindow.Height-4));courierWindow.Left=courierStartLeft;courierWindow.Top=courierBaseTop;courierWindow.Show();courierWindow.Activate();courierPhase="arrive";courierPhaseStarted=DateTime.Now;courierFrameTick=0;if(courierTimer==null){courierTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};courierTimer.Tick+=delegate{TickCourier();};}courierTimer.Start();
        }

        // 信使小猫到达后，主宠转身面向它并蹦跳打招呼，两只小猫完成“对接”。

        void GreetCourierCat()
        {
            React(CourierSenderName(activeCourierLetter)+" 的小猫来送信啦～",true);
            if(edgeHidden||petDragActive||globalPetHidden)return;
            double courierCenter=courierWindow.Left+courierWindow.Width/2;
            facing=courierCenter<pet.Left+pet.Width/2?-1:1;
            StartState("happy",2.4);
        }

        void BuildCourierWindowIfNeeded()
        {
            if(courierWindow!=null)return;courierWindow=new Window{Title="小猫来信",Width=192,Height=214,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowActivated=false};var canvas=new Grid{Background=Brushes.Transparent,Cursor=Cursors.Hand};canvas.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});canvas.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});canvas.RowDefinitions.Add(new RowDefinition());Grid.SetRow(courierImage=new Image{Width=122,Height=126,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,RenderTransformOrigin=new Point(.5,.82),RenderTransform=new ScaleTransform(1,1)},2);canvas.Children.Add(courierImage);courierEnvelope=new Border{Width=42,Height=31,CornerRadius=new CornerRadius(6),Background=new SolidColorBrush(Color.FromRgb(255,246,219)),BorderBrush=new SolidColorBrush(Color.FromRgb(132,88,48)),BorderThickness=new Thickness(2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,24,23),Child=new TextBlock{Text="✉",FontSize=19,Foreground=new SolidColorBrush(Color.FromRgb(132,88,48)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};Grid.SetRow(courierEnvelope,2);canvas.Children.Add(courierEnvelope);courierBubbleText=new TextBlock{FontSize=11.5,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Margin=new Thickness(10,6,10,6)};courierBubble=new Border{Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Child=courierBubbleText};Grid.SetRow(courierBubble,0);canvas.Children.Add(courierBubble);courierReceiveButton=MakeButton("收到",Ui.Accent);courierReceiveButton.Foreground=Brushes.White;courierReceiveButton.FontSize=11.5;courierReceiveButton.Height=28;courierReceiveButton.Padding=new Thickness(16,0,16,0);courierReceiveButton.HorizontalAlignment=HorizontalAlignment.Center;courierReceiveButton.VerticalAlignment=VerticalAlignment.Top;courierReceiveButton.Margin=new Thickness(0,4,0,0);courierReceiveButton.Visibility=Visibility.Collapsed;courierReceiveButton.Click+=delegate{ReceiveCourierLetter();};Grid.SetRow(courierReceiveButton,1);canvas.Children.Add(courierReceiveButton);canvas.MouseLeftButtonUp+=delegate{ReceiveCourierLetter();};courierWindow.Content=canvas;
        }

        void TickCourier()
        {
            if(activeCourierLetter==null){courierTimer.Stop();return;}
            courierFrameTick++;double elapsed=(DateTime.Now-courierPhaseStarted).TotalSeconds;
            if(courierPhase=="arrive"){
                double p=Math.Min(1,elapsed/1.35),ease=1-Math.Pow(1-p,3);courierWindow.Left=courierStartLeft+(courierTargetLeft-courierStartLeft)*ease;courierWindow.Top=courierBaseTop-Math.Abs(Math.Sin(p*Math.PI*8))*3;courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,false);
                if(p>=1){courierPhase="wait";courierBubble.Visibility=Visibility.Visible;if(courierReceiveButton!=null)courierReceiveButton.Visibility=Visibility.Visible;courierTimer.Stop();GreetCourierCat();}
            }else if(courierPhase=="receive"){
                double p=Math.Min(1,elapsed/.62);var scale=courierEnvelope.RenderTransform as ScaleTransform;if(scale==null){scale=new ScaleTransform(1,1);courierEnvelope.RenderTransform=scale;courierEnvelope.RenderTransformOrigin=new Point(.5,.5);}scale.ScaleX=1-p*.7;scale.ScaleY=1-p*.7;courierEnvelope.Opacity=1-p;courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,true);
                if(p>=1){courierPhase="leave";courierPhaseStarted=DateTime.Now;courierEnvelope.Visibility=Visibility.Collapsed;courierBubbleText.Text="信送到啦，再见～";}
            }else if(courierPhase=="leave"){
                double p=Math.Min(1,elapsed/1.15),ease=p*p;courierWindow.Left=courierTargetLeft+(courierExitLeft-courierTargetLeft)*ease;courierWindow.Top=courierBaseTop-Math.Abs(Math.Sin(p*Math.PI*7))*3;courierImage.Source=CourierFrame(activeCourierLetter.SenderSkinId,false);if(p>=1)FinishCourier();
            }
        }

        void ReceiveCourierLetter()
        {
            if(activeCourierLetter==null||courierPhase!="wait")return;MomoLetter received=activeCourierLetter;courierPhase="receive";courierPhaseStarted=DateTime.Now;courierBubble.Visibility=Visibility.Collapsed;if(courierReceiveButton!=null)courierReceiveButton.Visibility=Visibility.Collapsed;courierTimer.Start();if(!String.IsNullOrWhiteSpace(received.Id))MarkLetterRead(received);React("收到 "+CourierSenderName(received)+" 的信啦 ♡",true);ShowReceivedLetter(received);
        }

        void ShowReceivedLetter(MomoLetter letter)
        {
            bool preview=String.IsNullOrWhiteSpace(letter.Id);var dialog=new Window{Title="收到一封信",Width=470,Height=330,MinWidth=420,MinHeight=280,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true};Ui.StyleWindow(dialog);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(24)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var header=new Grid{Cursor=Cursors.SizeAll};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var title=new StackPanel();title.Children.Add(Ui.Title("来自 "+letter.SenderNickname+" 的信",20));title.Children.Add(Ui.Subtitle(preview?"送信动画预览":"点击收信回执已送出 · "+ShortCloudTime(letter.CreatedAt)));header.Children.Add(title);var close=Ui.MakeCloseButton();close.Click+=delegate{dialog.Close();};Grid.SetColumn(close,1);header.Children.Add(close);header.MouseLeftButtonDown+=delegate{try{dialog.DragMove();}catch{}};root.Children.Add(header);var letterBody=new StackPanel{Margin=new Thickness(4,18,4,18)};if(!String.IsNullOrWhiteSpace(letter.Content))letterBody.Children.Add(Ui.ReadOnlyText(letter.Content,14));AddAttachmentChips(letterBody,letter.Attachments,false);var letterScroll=new ScrollViewer{Content=letterBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(letterScroll,1);root.Children.Add(letterScroll);var reply=MakeButton(preview?"完成预览":"回复 "+letter.SenderNickname,Ui.Accent);reply.Foreground=Brushes.White;reply.HorizontalAlignment=HorizontalAlignment.Right;reply.Click+=delegate{dialog.Close();if(preview)return;OpenMessengerPanel();selectedMessengerMember=messengerMembers.FirstOrDefault(x=>x.Id==letter.SenderId);RefreshMessengerBody();if(selectedMessengerMember!=null)LoadConversation(selectedMessengerMember);};Grid.SetRow(reply,2);root.Children.Add(reply);shell.Child=root;dialog.Content=shell;var work=SystemParameters.WorkArea;dialog.Left=Math.Max(work.Left+8,Math.Min(pet.Left-dialog.Width-12,work.Right-dialog.Width-8));dialog.Top=Math.Max(work.Top+8,Math.Min(pet.Top-dialog.Height+pet.Height,work.Bottom-dialog.Height-8));dialog.Show();
        }

        void FinishCourier()
        {
            courierTimer.Stop();courierWindow.Hide();courierEnvelope.Opacity=1;courierEnvelope.RenderTransform=null;courierEnvelope.Visibility=Visibility.Visible;if(courierReceiveButton!=null)courierReceiveButton.Visibility=Visibility.Collapsed;activeCourierLetter=null;courierPhase=null;if(courierQueue.Count>0){var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};delay.Tick+=delegate{delay.Stop();StartNextCourier();};delay.Start();}
        }

        void OnLocalSkinChanged(string skinId)
        {
            if(momoAccount==null)return;momoAccount.SkinId=skinId;SaveMomoAccount();if(IsMomoSignedIn())MomoApi<Dictionary<string,object>>("POST","/api/momo/profile",new Dictionary<string,object>{{"skinId",skinId}},true,delegate(Dictionary<string,object> ignored){},delegate(string ignored){});
        }

        void PreviewCourierSkin(string skinId)
        {
            courierQueue.Enqueue(new MomoLetter{Id="",SenderId="",SenderNickname=SkinName(String.IsNullOrWhiteSpace(skinId)?"default":skinId),SenderSkinId=String.IsNullOrWhiteSpace(skinId)?"default":skinId,Content="这是一封送信动画预览。真实来信会在点击后把“已收信”回执送回给对方。",CreatedAt=DateTime.Now.ToString("o"),Status="sent"});if(activeCourierLetter==null)StartNextCourier();
        }
}
}
