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
}
}
