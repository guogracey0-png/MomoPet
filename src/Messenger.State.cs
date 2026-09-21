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

        Window messengerPanel,courierWindow;

        Grid messengerBody;

        ListBox messengerContacts,messengerGroups;

        StackPanel messengerConversation;

        TextBox messengerInput;

        WrapPanel messengerAttachmentPanel;

        Button messengerSendButton;

        TextBlock messengerTitle,messengerStatus;

        readonly List<MomoRemoteMember> messengerMembers=new List<MomoRemoteMember>();

        readonly List<MomoLetter> messengerLetters=new List<MomoLetter>();

        readonly List<MomoGroup> momoGroups=new List<MomoGroup>();

        readonly List<MomoGroupMessage> momoGroupMessages=new List<MomoGroupMessage>();

        readonly List<MomoAttachment> pendingAttachments=new List<MomoAttachment>();

        readonly Queue<MomoLetter> courierQueue=new Queue<MomoLetter>();

        readonly HashSet<string> knownLetterIds=new HashSet<string>();

        MomoRemoteMember selectedMessengerMember;

        MomoGroup selectedMomoGroup;

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

        int messengerViewRequestId;

        bool messengerSendBusy;

        Button courierReceiveButton;

        Window receiptWindow;

        Grid receiptRoot;

        TextBlock receiptTitleText,receiptSubText,receiptIcon;

        DispatcherTimer receiptTimer;

        MomoLetter activeReceipt;

        DateTime receiptPhaseStarted;

        string receiptPhase;

        double receiptTargetTop,receiptStartLeft,receiptTargetLeft,receiptExitLeft;

        readonly Queue<MomoLetter> receiptQueue=new Queue<MomoLetter>();

        readonly Dictionary<string,string> knownLetterStatus=new Dictionary<string,string>();

        readonly List<MomoLetter> awaitingReceipts=new List<MomoLetter>();


        // 把选中的文件以 multipart/form-data 一次性上传，服务端返回可直接放进信里的元数据。

        void SetMessengerBusy(bool busy)
        {
            momoFileOperationBusy=busy;
            if(messengerInput!=null)messengerInput.IsEnabled=!busy;if(messengerSendButton!=null)messengerSendButton.IsEnabled=!busy;
            if(messengerContacts!=null)messengerContacts.IsEnabled=!busy;if(messengerGroups!=null)messengerGroups.IsEnabled=!busy;
        }

        bool momoFileOperationBusy;

        Window momoFileInputWindow;
}
}
