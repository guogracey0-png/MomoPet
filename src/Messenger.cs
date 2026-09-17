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

    public class MomoAttachment
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Type { get; set; }
        public long Size { get; set; }
    }

    class MomoPreparedUpload
    {
        public List<string> Paths=new List<string>();
        public List<string> TemporaryFiles=new List<string>();
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
        public List<MomoAttachment> Attachments { get; set; }
        public string CreatedAt { get; set; }
        public string Status { get; set; }
        public string DeliveredAt { get; set; }
        public string ReadAt { get; set; }
    }

    public class MomoGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OwnerId { get; set; }
        public List<string> MemberIds { get; set; }
        public List<MomoRemoteMember> Members { get; set; }
        public string CreatedAt { get; set; }
        public override string ToString(){return "🐾  "+Name+"  · "+(Members==null?0:Members.Count)+" 人";}
    }

    public class MomoGroupMessage
    {
        public string Id { get; set; }
        public string GroupId { get; set; }
        public string SenderId { get; set; }
        public string SenderNickname { get; set; }
        public string SenderSkinId { get; set; }
        public string Content { get; set; }
        public List<MomoAttachment> Attachments { get; set; }
        public string CreatedAt { get; set; }
    }

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

        void InitializeMessenger()
        {
            messengerPollTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};messengerPollTimer.Tick+=delegate{PollMomoMessages();};if(IsMomoSignedIn()){messengerPollTimer.Start();PollMomoMessages();}
        }

        // 后台线程回调 UI 前先确认调度器还活着：窗口关闭后再 BeginInvoke 会抛
        // InvalidOperationException；异常从线程池线程逃逸出去会直接终止进程，造成“用着用着就闪退”。
        void UiPost(Action action)
        {
            if(action==null)return;
            try{var dispatcher=app==null?null:app.Dispatcher;if(dispatcher==null||dispatcher.HasShutdownStarted||dispatcher.HasShutdownFinished)return;dispatcher.BeginInvoke(action);}catch{}
        }

        void MomoApi<T>(string method,string path,object body,bool authenticated,Action<T> success,Action<string> failure)
        {
            string server=MomoServer(),token=momoToken;Task.Factory.StartNew(delegate{
                try{
                    var request=(HttpWebRequest)WebRequest.Create(server+path);request.Method=method;request.Accept="application/json";request.ContentType="application/json; charset=utf-8";request.Timeout=15000;request.ReadWriteTimeout=15000;if(authenticated&&!String.IsNullOrWhiteSpace(token))request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
                    if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(body));request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);}
                    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();T value=String.IsNullOrWhiteSpace(text)?default(T):new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(text);UiPost(new Action(delegate{if(success!=null)success(value);}));}
                }catch(WebException web){string message="无法连接云端";try{using(var response=web.Response)using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();var error=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;if(error!=null&&error.ContainsKey("error"))message=Convert.ToString(error["error"]);}}catch{}UiPost(new Action(delegate{if(failure!=null)failure(message);}));}
                catch(Exception error){UiPost(new Action(delegate{if(failure!=null)failure(error.Message);}));}
            });
        }

        // 把选中的文件以 multipart/form-data 一次性上传，服务端返回可直接放进信里的元数据。
        void UploadMomoFiles(string[] paths,Action<List<MomoAttachment>> success,Action<string> failure)
        {
            if(paths==null||paths.Length==0){if(failure!=null)failure("没有选择文件");return;}
            string server=MomoServer(),token=momoToken;Task.Factory.StartNew(delegate{
                try{
                    string boundary="----MomoPet"+Guid.NewGuid().ToString("N");var request=(HttpWebRequest)WebRequest.Create(server+"/api/momo/files");request.Method="POST";request.Accept="application/json";request.ContentType="multipart/form-data; boundary="+boundary;request.Timeout=600000;request.ReadWriteTimeout=600000;request.AllowWriteStreamBuffering=true;if(!String.IsNullOrWhiteSpace(token))request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
                    byte[] newline=Encoding.UTF8.GetBytes("\r\n");
                    using(var output=request.GetRequestStream()){
                        foreach(string path in paths){
                            string fileName=Path.GetFileName(path),mime=GuessAttachmentMime(Path.GetExtension(path));string headerFileName=fileName.Replace('"','\'').Replace('\r','_').Replace('\n','_');
                            byte[] header=Encoding.UTF8.GetBytes("--"+boundary+"\r\nContent-Disposition: form-data; name=\"files\"; filename=\""+headerFileName+"\"\r\nContent-Type: "+mime+"\r\n\r\n");output.Write(header,0,header.Length);
                            using(var fileStream=File.OpenRead(path)){byte[] buffer=new byte[81920];int read;while((read=fileStream.Read(buffer,0,buffer.Length))>0)output.Write(buffer,0,read);}
                            output.Write(newline,0,newline.Length);
                        }
                        byte[] tail=Encoding.UTF8.GetBytes("--"+boundary+"--\r\n");output.Write(tail,0,tail.Length);
                    }
                    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();var parsed=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;List<MomoAttachment> list=new List<MomoAttachment>();object raw=parsed!=null&&parsed.ContainsKey("files")?parsed["files"]:null;var items=raw is string?null:raw as System.Collections.IEnumerable;if(items!=null){int uploadedIndex=0;foreach(object item in items){var map=item as Dictionary<string,object>;if(map==null)continue;string originalName=uploadedIndex<paths.Length?Path.GetFileName(paths[uploadedIndex]):"文件";list.Add(new MomoAttachment{Name=originalName,Url=map.ContainsKey("url")?Convert.ToString(map["url"]):"",Type=map.ContainsKey("type")?Convert.ToString(map["type"]):"",Size=map.ContainsKey("size")?Convert.ToInt64(map["size"]):0});uploadedIndex++;}}UiPost(new Action(delegate{if(success!=null)success(list);}));}
                }catch(WebException web){string message="文件上传失败";try{using(var response=web.Response)using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();var error=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;if(error!=null&&error.ContainsKey("error"))message=Convert.ToString(error["error"]);}}catch{}UiPost(new Action(delegate{if(failure!=null)failure(message);}));}
                catch(Exception error){UiPost(new Action(delegate{if(failure!=null)failure(error.Message);}));}
            });
        }

        static string GuessAttachmentMime(string ext)
        {
            switch((ext??"").ToLowerInvariant()){
                case ".jpg":case ".jpeg":return "image/jpeg";
                case ".png":return "image/png";
                case ".gif":return "image/gif";
                case ".webp":return "image/webp";
                case ".bmp":return "image/bmp";
                case ".svg":return "image/svg+xml";
                case ".pdf":return "application/pdf";
                case ".txt":return "text/plain";
                case ".md":return "text/markdown";
                case ".csv":return "text/csv";
                case ".json":return "application/json";
                case ".zip":return "application/zip";
                case ".rar":return "application/vnd.rar";
                case ".7z":return "application/x-7z-compressed";
                case ".doc":return "application/msword";
                case ".docx":return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case ".xls":return "application/vnd.ms-excel";
                case ".xlsx":return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".ppt":return "application/vnd.ms-powerpoint";
                case ".pptx":return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                case ".mp3":return "audio/mpeg";
                case ".mp4":return "video/mp4";
                default:return "application/octet-stream";
            }
        }

        static string AttachmentIcon(string type)
        {
            if(String.IsNullOrWhiteSpace(type))return "📄";
            if(type.StartsWith("image/"))return "🖼️";
            if(type.StartsWith("audio/"))return "🎵";
            if(type.StartsWith("video/"))return "🎬";
            return "📄";
        }

        static string FormatFileSize(long size)
        {
            if(size<=0)return "0 B";string[] units={"B","KB","MB","GB"};double value=size;int unit=0;while(value>=1024&&unit<units.Length-1){value/=1024;unit++;}return (unit==0?((long)value).ToString():value.ToString("0.#"))+" "+units[unit];
        }

        void SetMessengerBusy(bool busy)
        {
            momoFileOperationBusy=busy;
            if(messengerInput!=null)messengerInput.IsEnabled=!busy;if(messengerSendButton!=null)messengerSendButton.IsEnabled=!busy;
            if(messengerContacts!=null)messengerContacts.IsEnabled=!busy;if(messengerGroups!=null)messengerGroups.IsEnabled=!busy;
        }

        bool momoFileOperationBusy;
        Window momoFileInputWindow;

        void InstallMomoFileInput()
        {
            if(messengerPanel==null||momoFileInputWindow==messengerPanel)return;
            momoFileInputWindow=messengerPanel;messengerPanel.AllowDrop=true;
            messengerPanel.PreviewDragOver+=delegate(object sender,DragEventArgs e){
                if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
                e.Effects=IsMomoSignedIn()&&!momoFileOperationBusy&&(selectedMessengerMember!=null||selectedMomoGroup!=null)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
            };
            messengerPanel.PreviewDrop+=delegate(object sender,DragEventArgs e){
                if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
                e.Handled=true;AddMomoFilesFromData(e.Data);
            };
            messengerPanel.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
                if(e.Key!=Key.V||(Keyboard.Modifiers&ModifierKeys.Control)==0)return;
                try{if(!Clipboard.ContainsFileDropList())return;e.Handled=true;AddMomoFiles(Clipboard.GetFileDropList().Cast<string>().ToArray());}
                catch(Exception error){if(messengerStatus!=null)messengerStatus.Text="无法读取剪贴板文件："+error.Message;}
            };
            DataObject.AddPastingHandler(messengerPanel,delegate(object sender,DataObjectPastingEventArgs e){
                if(!e.DataObject.GetDataPresent(DataFormats.FileDrop))return;
                e.CancelCommand();AddMomoFilesFromData(e.DataObject);
            });
        }

        void AddMomoFilesFromData(IDataObject data)
        {
            try{AddMomoFiles(data.GetData(DataFormats.FileDrop) as string[]);}
            catch(Exception error){if(messengerStatus!=null)messengerStatus.Text="无法读取拖入的文件："+error.Message;}
        }

        void AddMomoFiles(string[] paths)
        {
            if(!IsMomoSignedIn())return;
            if(momoFileOperationBusy){if(messengerStatus!=null)messengerStatus.Text="正在上传或发送，请稍后再添加文件";return;}
            if(selectedMessengerMember==null&&selectedMomoGroup==null){if(messengerStatus!=null)messengerStatus.Text="先选择一位朋友或一个小组";return;}
            var inputs=(paths??new string[0]).Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(inputs.Length==0)return;
            if(inputs.Any(x=>!File.Exists(x)&&!Directory.Exists(x))){if(messengerStatus!=null)messengerStatus.Text="其中有文件或文件夹已经不存在，请重新选择";return;}
            if(inputs.Length+pendingAttachments.Count>9){if(messengerStatus!=null)messengerStatus.Text="一封信最多带 9 个项目，还可添加 "+(9-pendingAttachments.Count)+" 个；请减少所选内容";return;}
            SetMessengerBusy(true);if(messengerStatus!=null)messengerStatus.Text=inputs.Any(Directory.Exists)?"正在整理文件夹并准备上传…":"正在上传 "+inputs.Length+" 个文件…";
            Task.Factory.StartNew(delegate{
                try{
                    var prepared=PrepareMomoUpload(inputs);
                    UiPost(new Action(delegate{UploadMomoFiles(prepared.Paths.ToArray(),delegate(List<MomoAttachment> uploaded){CleanupMomoTemporaryFiles(prepared.TemporaryFiles);SetMessengerBusy(false);pendingAttachments.AddRange(uploaded);RefreshAttachmentChips();if(messengerStatus!=null)messengerStatus.Text=uploaded.Count>0?"已上传 "+uploaded.Count+" 个项目，点击“送出”发送":"文件没有上传成功";},delegate(string error){CleanupMomoTemporaryFiles(prepared.TemporaryFiles);SetMessengerBusy(false);if(messengerStatus!=null)messengerStatus.Text=error+"；可重新粘贴或拖入重试";});}));
                }catch(Exception error){UiPost(new Action(delegate{SetMessengerBusy(false);if(messengerStatus!=null)messengerStatus.Text="文件夹整理失败："+error.Message;}));}
            });
        }

        static MomoPreparedUpload PrepareMomoUpload(IEnumerable<string> inputs)
        {
            var prepared=new MomoPreparedUpload();
            try{
                foreach(string input in inputs){if(File.Exists(input)){prepared.Paths.Add(input);continue;}string folderName=new DirectoryInfo(input).Name;string tempFolder=Path.Combine(Path.GetTempPath(),"MomoPet-folder-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(tempFolder);string zipPath=Path.Combine(tempFolder,SafeAttachmentName(folderName)+".zip");prepared.TemporaryFiles.Add(zipPath);ZipFile.CreateFromDirectory(input,zipPath,CompressionLevel.Optimal,false,Encoding.UTF8);prepared.Paths.Add(zipPath);}
                return prepared;
            }catch{CleanupMomoTemporaryFiles(prepared.TemporaryFiles);throw;}
        }

        static string SafeAttachmentName(string name)
        {
            string value=Path.GetFileName(name??"").Trim();foreach(char invalid in Path.GetInvalidFileNameChars())value=value.Replace(invalid,'_');return String.IsNullOrWhiteSpace(value)?"附件":value;
        }

        static void CleanupMomoTemporaryFiles(IEnumerable<string> files)
        {
            foreach(string file in files??Enumerable.Empty<string>()){try{string folder=Path.GetDirectoryName(file);if(File.Exists(file))File.Delete(file);if(!String.IsNullOrWhiteSpace(folder)&&Directory.Exists(folder))Directory.Delete(folder,true);}catch{}}
        }

        void PickMomoFiles()
        {
            if(selectedMessengerMember==null&&selectedMomoGroup==null){if(messengerStatus!=null)messengerStatus.Text="先选择一位朋友或一个小组";return;}
            var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择要放进信里的文件",Multiselect=true,Filter="常用文件|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp;*.svg;*.pdf;*.doc;*.docx;*.ppt;*.pptx;*.xls;*.xlsx;*.txt;*.md;*.csv;*.zip;*.rar;*.7z;*.json;*.yaml;*.yml;*.mp3;*.mp4|所有文件|*.*"};
            if(dialog.ShowDialog(messengerPanel)!=true||dialog.FileNames==null||dialog.FileNames.Length==0)return;
            AddMomoFiles(dialog.FileNames);
        }

        void PickMomoFolder()
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择要发送的文件夹",CheckFileExists=false,ValidateNames=false,FileName="选择这个文件夹"};
            if(dialog.ShowDialog(messengerPanel)!=true)return;string folder=Path.GetDirectoryName(dialog.FileName);if(Directory.Exists(folder))AddMomoFiles(new[]{folder});
        }

        void RefreshAttachmentChips()
        {
            if(messengerAttachmentPanel==null)return;messengerAttachmentPanel.Children.Clear();
            for(int i=0;i<pendingAttachments.Count;i++){
                var file=pendingAttachments[i];var chip=new Border{Background=Ui.AccentSoft,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10,5,6,5),Margin=new Thickness(0,0,8,6)};var row=new StackPanel{Orientation=Orientation.Horizontal};
                row.Children.Add(new TextBlock{Text=AttachmentIcon(file.Type)+" "+file.Name+"  ·  "+FormatFileSize(file.Size),FontSize=11.5,Foreground=Ui.Ink,VerticalAlignment=VerticalAlignment.Center,MaxWidth=230,TextTrimming=TextTrimming.CharacterEllipsis});
                int index=i;var remove=new Button{Content="✕",FontSize=11,Foreground=Ui.Up,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,Margin=new Thickness(8,0,0,0),Padding=new Thickness(4,0,4,0)};
                remove.Click+=delegate{if(index>=0&&index<pendingAttachments.Count){pendingAttachments.RemoveAt(index);RefreshAttachmentChips();}};
                row.Children.Add(remove);chip.Child=row;messengerAttachmentPanel.Children.Add(chip);
            }
        }

        void OpenAttachment(MomoAttachment file)
        {
            if(file==null||String.IsNullOrWhiteSpace(file.Url))return;string target=file.Url.StartsWith("http",StringComparison.OrdinalIgnoreCase)?file.Url:MomoServer()+file.Url;string originalName=SafeAttachmentName(file.Name);
            var dialog=new Microsoft.Win32.SaveFileDialog{Title="把附件保存到电脑",FileName=originalName,OverwritePrompt=true,AddExtension=true};string ext=Path.GetExtension(originalName);if(!String.IsNullOrWhiteSpace(ext)){dialog.DefaultExt=ext;dialog.Filter=ext.TrimStart('.').ToUpperInvariant()+" 文件|*"+ext+"|所有文件|*.*";}else dialog.Filter="所有文件|*.*";
            if(dialog.ShowDialog(messengerPanel)!=true)return;string destination=dialog.FileName,token=momoToken;if(messengerStatus!=null)messengerStatus.Text="正在下载 "+originalName+"…";
            Task.Factory.StartNew(delegate{
                string partial=destination+".momopet-download";
                try{var request=(HttpWebRequest)WebRequest.Create(target);request.Method="GET";request.Timeout=600000;request.ReadWriteTimeout=600000;if(!String.IsNullOrWhiteSpace(token))request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;using(var response=(HttpWebResponse)request.GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(partial))input.CopyTo(output);File.Copy(partial,destination,true);File.Delete(partial);UiPost(new Action(delegate{if(messengerStatus!=null)messengerStatus.Text="已保存："+destination;}));}
                catch(Exception error){try{if(File.Exists(partial))File.Delete(partial);}catch{}UiPost(new Action(delegate{if(messengerStatus!=null)messengerStatus.Text="下载失败："+error.Message;}));}
            });
        }

        void AddAttachmentChips(Panel host,List<MomoAttachment> files,bool alignRight)
        {
            if(host==null||files==null||files.Count==0)return;
            foreach(var file in files){
                var chip=new Border{Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,0),Cursor=Cursors.Hand,HorizontalAlignment=alignRight?HorizontalAlignment.Right:HorizontalAlignment.Left,ToolTip="点击打开 "+file.Name};
                chip.Child=new TextBlock{Text=AttachmentIcon(file.Type)+" "+file.Name+"  ·  "+FormatFileSize(file.Size),FontSize=11.5,Foreground=Ui.Accent,MaxWidth=280,TextTrimming=TextTrimming.CharacterEllipsis};
                var captured=file;chip.MouseLeftButtonUp+=delegate{OpenAttachment(captured);};host.Children.Add(chip);
            }
        }

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

        void LoadMessengerMembers()
        {
            if(!IsMomoSignedIn())return;MomoApi<List<MomoRemoteMember>>("GET","/api/momo/members",null,true,delegate(List<MomoRemoteMember> items){messengerMembers.Clear();if(items!=null)messengerMembers.AddRange(items.Where(x=>x.Id!=momoAccount.MemberId));if(messengerContacts!=null){messengerContacts.ItemsSource=null;messengerContacts.ItemsSource=messengerMembers;}if(selectedMessengerMember==null&&selectedMomoGroup==null&&messengerMembers.Count>0){selectedMessengerMember=messengerMembers[0];if(messengerContacts!=null)messengerContacts.SelectedItem=selectedMessengerMember;LoadConversation(selectedMessengerMember);}if(messengerStatus!=null)messengerStatus.Text="已同步 "+messengerMembers.Count+" 位联系人";},delegate(string error){if(messengerStatus!=null)messengerStatus.Text=error;});
        }

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

        void SetMessengerTopmost(bool value){if(momoAccountPanel!=null)momoAccountPanel.Topmost=value;if(messengerPanel!=null)messengerPanel.Topmost=value;if(courierWindow!=null)courierWindow.Topmost=true;}
        void CloseMessengerWindows(){if(momoAccountPanel!=null)momoAccountPanel.Close();if(messengerPollTimer!=null)messengerPollTimer.Stop();if(courierTimer!=null)courierTimer.Stop();if(receiptTimer!=null)receiptTimer.Stop();if(courierWindow!=null)courierWindow.Close();if(receiptWindow!=null)receiptWindow.Close();if(messengerPanel!=null)messengerPanel.Close();}
    }
}
