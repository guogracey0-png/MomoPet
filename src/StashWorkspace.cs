using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MomoPetApp
{
    public partial class PetController
    {
        TextBox stashSearchBox;
        ComboBox stashTypeFilter,stashDateFilter,stashSourceFilter;
        CheckBox stashFavoriteOnly;
        TextBlock stashFilterSummary,stashPreviewTitle,stashPreviewMeta;
        Window stashPreviewPanel;
        Image stashPreviewImage;
        TextBox stashPreviewText;
        bool stashFilterUpdating;

        string Sha256Hex(byte[] bytes)
        {
            using(var sha=SHA256.Create()){return String.Concat(sha.ComputeHash(bytes).Select(x=>x.ToString("x2")));}
        }

        // 中转袋行内小按钮：单击执行动作，同时避免行内双击误触预览。
        Button StashRowButton(string text,Brush background,RoutedEventHandler onClick)
        {
            var button=MakeButton(text,background);
            button.FontSize=11;button.Padding=new Thickness(7,3,7,3);button.Margin=new Thickness(0,1,0,1);
            button.Click+=onClick;
            button.AddHandler(Control.PreviewMouseDoubleClickEvent,new MouseButtonEventHandler(delegate(object s,MouseButtonEventArgs e){e.Handled=true;}));
            return button;
        }

        void DownloadStashImage(StashItem item)
        {
            if(item==null||item.Kind!="image"||String.IsNullOrWhiteSpace(item.Value)||!File.Exists(item.Value)){React("图片已经不在了",false);return;}
            try
            {
                string ext=Path.GetExtension(item.Value);if(String.IsNullOrEmpty(ext))ext=".png";
                var dialog=new Microsoft.Win32.SaveFileDialog{Title="下载图片",Filter="图片文件|*"+ext+"|所有文件|*.*",DefaultExt=ext,
                    FileName=String.IsNullOrWhiteSpace(item.Name)?("图片-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")):item.Name};
                if(dialog.ShowDialog()!=true)return;
                File.Copy(item.Value,dialog.FileName,true);
                React("图片已保存："+dialog.FileName,false);
            }
            catch(Exception ex){React("下载失败："+ex.Message,false);}
        }

        void SendStashToAiPocket(StashItem item)
        {
            if(item==null)return;
            if(item.Kind=="text"){OpenAiPocketChat(item.Value??"");return;}
            if(item.Kind=="image"){OpenImageEditor(item);return;}
            React("文本和图片可以直接进 AI 口袋",false);
        }

        string ComputeStashHash(StashItem item)
        {
            if(item==null)return null;if(item.Kind=="text")return "T:"+Sha256Hex(Encoding.UTF8.GetBytes((item.Value??"").Replace("\r\n","\n").Trim()));
            if(String.IsNullOrWhiteSpace(item.Value)||!File.Exists(item.Value))return null;
            try{
                var info=new FileInfo(item.Value);using(var sha=SHA256.Create())using(var stream=new FileStream(item.Value,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
                    if(info.Length<=16L*1024*1024)return "F:"+String.Concat(sha.ComputeHash(stream).Select(x=>x.ToString("x2")));
                    int sample=256*1024;byte[] first=new byte[sample],last=new byte[sample],size=BitConverter.GetBytes(info.Length);int firstRead=stream.Read(first,0,first.Length);stream.Seek(Math.Max(0,info.Length-sample),SeekOrigin.Begin);int lastRead=stream.Read(last,0,last.Length);using(var memory=new MemoryStream()){memory.Write(size,0,size.Length);memory.Write(first,0,firstRead);memory.Write(last,0,lastRead);return "Q:"+info.Length+":"+String.Concat(sha.ComputeHash(memory.ToArray()).Select(x=>x.ToString("x2")));}
                }
            }catch{return null;}
        }

        string StashVersionKey(StashItem item)
        {
            if(item==null)return null;string name=Path.GetFileNameWithoutExtension(item.Name??"").Trim().ToLowerInvariant();name=Regex.Replace(name,@"(?:\s*[-_ ]?(?:副本|copy)|\s*\(\d+\)|\s*[-_ ]?v\d+)$","",RegexOptions.IgnoreCase);return String.IsNullOrWhiteSpace(name)?null:name;
        }

        bool AddStashItemSmart(StashItem item)
        {
            if(item==null)return false;if(String.IsNullOrWhiteSpace(item.Id))item.Id=Guid.NewGuid().ToString("N");if(String.IsNullOrWhiteSpace(item.Created))item.Created=DateTime.Now.ToString("o");if(String.IsNullOrWhiteSpace(item.SourceApp))item.SourceApp="桌面拖放";item.LastSeen=DateTime.Now.ToString("o");item.ContentHash=ComputeStashHash(item);
            StashItem duplicate=!String.IsNullOrWhiteSpace(item.ContentHash)?stashItems.FirstOrDefault(x=>String.Equals(x.ContentHash,item.ContentHash,StringComparison.OrdinalIgnoreCase)):null;
            if(duplicate!=null){lastOfficeCapturedItem=duplicate;duplicate.DuplicateCount=Math.Max(1,duplicate.DuplicateCount+1);duplicate.LastSeen=DateTime.Now.ToString("o");if(item.Owned&&!String.Equals(item.Value,duplicate.Value,StringComparison.OrdinalIgnoreCase))DeleteOwnedStashFile(item);return false;}
            string versionKey=StashVersionKey(item);var versions=String.IsNullOrWhiteSpace(versionKey)?new List<StashItem>():stashItems.Where(x=>StashVersionKey(x)==versionKey&&!String.Equals(x.ContentHash,item.ContentHash,StringComparison.OrdinalIgnoreCase)).ToList();if(versions.Count>0){string group=versions.Select(x=>x.VersionGroup).FirstOrDefault(x=>!String.IsNullOrWhiteSpace(x))??Guid.NewGuid().ToString("N");foreach(StashItem existing in versions)existing.VersionGroup=group;item.VersionGroup=group;}
            lastOfficeCapturedItem=item;stashItems.Insert(0,item);QueueStashIndex(item);return true;
        }

        string StashTypeName(StashItem item)
        {
            if(item==null)return "其他";if(item.Kind=="text")return "文本";if(item.Kind=="image"||item.Kind=="imagePending")return "图片";if(item.Kind=="folder")return "文件夹";string ext=Path.GetExtension(item.Value??item.Name??"").ToLowerInvariant();if(ext==".doc"||ext==".docx"||ext==".xls"||ext==".xlsx"||ext==".csv"||ext==".ppt"||ext==".pptx")return "Office";if(ext==".pdf")return "PDF";if(ext==".zip"||ext==".rar"||ext==".7z")return "压缩包";if(ext==".psd")return "PSD";if(ext==".exe"||ext==".msi")return "程序";return "其他";
        }

        string StashSearchable(StashItem item)
        {
            return String.Join(" ",new[]{item==null?null:item.Name,item==null?null:item.Kind,item==null?null:item.Value,item==null?null:item.Project,item==null?null:item.Company,item==null?null:item.Industry,item==null?null:item.IndexName,item==null?null:item.Client,item==null?null:item.Tags,item==null?null:item.Note,item==null?null:item.SourceApp,item==null?null:item.SearchText});
        }

        bool CanIndexStashText(StashItem item)
        {
            if(item==null||item.Kind=="image"||item.Kind=="imagePending"||item.Kind=="folder")return false;if(item.Kind=="text")return true;string ext=Path.GetExtension(item.Value??"").ToLowerInvariant();return new[]{".txt",".md",".csv",".json",".xml",".html",".htm",".log",".docx",".pptx",".xlsx"}.Contains(ext);
        }

        void QueueStashIndex(StashItem item)
        {
            if(!CanIndexStashText(item)||!String.IsNullOrWhiteSpace(item.SearchText))return;if(item.Kind=="text"){item.SearchText=(item.Value??"").Length>200000?(item.Value??"").Substring(0,200000):item.Value;return;}ThreadPool.QueueUserWorkItem(delegate{try{string reference;string text=ExtractStashPreviewText(item,out reference);if(text.Length>200000)text=text.Substring(0,200000);app.Dispatcher.BeginInvoke(new Action(delegate{if(!stashItems.Contains(item))return;item.SearchText=text;SaveStash();if(stashPanel!=null&&stashPanel.IsVisible)RefreshStash();}));}catch{}});
        }

        void QueueMissingStashIndexes()
        {
            var pending=stashItems.Where(item=>String.IsNullOrWhiteSpace(item.ContentHash)||(CanIndexStashText(item)&&String.IsNullOrWhiteSpace(item.SearchText))).ToList();if(pending.Count==0)return;ThreadPool.QueueUserWorkItem(delegate{bool changed=false;foreach(StashItem item in pending){try{if(String.IsNullOrWhiteSpace(item.ContentHash)){item.ContentHash=ComputeStashHash(item);changed=true;}if(CanIndexStashText(item)&&String.IsNullOrWhiteSpace(item.SearchText)){if(item.Kind=="text")item.SearchText=item.Value??"";else{string reference;item.SearchText=ExtractStashPreviewText(item,out reference);}if(item.SearchText!=null&&item.SearchText.Length>200000)item.SearchText=item.SearchText.Substring(0,200000);changed=true;}}catch{}}if(changed)app.Dispatcher.BeginInvoke(new Action(delegate{SaveStash();if(stashPanel!=null&&stashPanel.IsVisible)RefreshStash();}));});
        }

        IEnumerable<StashItem> FilteredStashItems()
        {
            string query=(stashSearchBox==null?"":stashSearchBox.Text??"").Trim(),type=stashTypeFilter==null?"全部类型":Convert.ToString(stashTypeFilter.SelectedItem)??"全部类型",date=stashDateFilter==null?"全部日期":Convert.ToString(stashDateFilter.SelectedItem)??"全部日期",source=stashSourceFilter==null?"全部来源":Convert.ToString(stashSourceFilter.SelectedItem)??"全部来源";DateTime now=DateTime.Now;
            IEnumerable<StashItem> result=stashItems;if(!String.IsNullOrWhiteSpace(query)){string[] terms=query.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);result=result.Where(item=>terms.All(term=>StashSearchable(item).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0));}if(type!="全部类型")result=result.Where(item=>StashTypeName(item)==type);if(source!="全部来源")result=result.Where(item=>(String.IsNullOrWhiteSpace(item.SourceApp)?"未知来源":item.SourceApp)==source);if(stashFavoriteOnly!=null&&stashFavoriteOnly.IsChecked==true)result=result.Where(item=>item.Favorite);
            if(date!="全部日期")result=result.Where(item=>{DateTime created;if(!DateTime.TryParse(item.Created,out created))return false;if(date=="今天")return created.Date==now.Date;if(date=="近7天")return created>=now.AddDays(-7);if(date=="近30天")return created>=now.AddDays(-30);return true;});
            return result.OrderByDescending(x=>x.Favorite).ThenByDescending(x=>{DateTime value;return DateTime.TryParse(x.Created,out value)?value:DateTime.MinValue;});
        }

        void RefreshStashFilterChoices()
        {
            if(stashSourceFilter==null)return;stashFilterUpdating=true;string selected=Convert.ToString(stashSourceFilter.SelectedItem)??"全部来源";var values=(new[]{"全部来源"}).Concat(stashItems.Select(x=>String.IsNullOrWhiteSpace(x.SourceApp)?"未知来源":x.SourceApp).Distinct().OrderBy(x=>x)).ToList();stashSourceFilter.ItemsSource=values;stashSourceFilter.SelectedItem=values.Contains(selected)?selected:"全部来源";stashFilterUpdating=false;
        }

        List<StashItem> SelectedStashes()
        {
            if(stashList==null)return new List<StashItem>();var selected=stashList.SelectedItems.Cast<object>().Select(x=>x as ListBoxItem).Where(x=>x!=null).Select(x=>x.Tag as StashItem).Where(x=>x!=null).ToList();if(selected.Count==0){StashItem one=SelectedStash();if(one!=null)selected.Add(one);}return selected;
        }

        DataObject MakeStashData(IEnumerable<StashItem> source)
        {
            var items=source==null?new List<StashItem>():source.Where(x=>x!=null).Distinct().ToList();if(items.Count==0)return null;var data=new DataObject();var texts=items.Where(x=>x.Kind=="text").Select(x=>x.Value??"").ToList();var paths=items.Where(x=>x.Kind!="text"&&(File.Exists(x.Value)||Directory.Exists(x.Value))).Select(x=>x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(texts.Count>0){string joined=String.Join(Environment.NewLine+Environment.NewLine,texts);data.SetData(DataFormats.UnicodeText,joined);data.SetData(DataFormats.Text,joined);}if(paths.Length>0)data.SetData(DataFormats.FileDrop,paths);if(items.Count==1&&items[0].Kind=="image"&&File.Exists(items[0].Value)){try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(items[0].Value);image.EndInit();image.Freeze();data.SetData(DataFormats.Bitmap,image);}catch{}}return data;
        }

        void BeginSelectedStashDrag(DependencyObject source)
        {
            try{var data=MakeStashData(SelectedStashes());if(data!=null)DragDrop.DoDragDrop(source,data,DragDropEffects.Copy);}catch(Exception ex){React("拖不出去："+ex.Message,false);}
        }

        void CopySelectedStashes()
        {
            try{var items=SelectedStashes();var data=MakeStashData(items);if(data!=null){Clipboard.SetDataObject(data,true);React("已复制 "+items.Count+" 项",false);}}catch(Exception ex){React("复制失败："+ex.Message,false);}
        }

        void DeleteSelectedStashes()
        {
            var items=SelectedStashes();if(items.Count==0)return;foreach(StashItem item in items){DeleteOwnedStashFile(item);stashItems.Remove(item);}SaveStash();RefreshStash();
        }

        void ToggleSelectedStashFavorite()
        {
            var items=SelectedStashes();if(items.Count==0)return;bool value=items.Any(x=>!x.Favorite);foreach(StashItem item in items)item.Favorite=value;SaveStash();RefreshStash();
        }

        string StashVersionLabel(StashItem item)
        {
            if(item==null||String.IsNullOrWhiteSpace(item.VersionGroup))return "";var versions=stashItems.Where(x=>x.VersionGroup==item.VersionGroup).OrderBy(x=>x.Created).ToList();int index=versions.IndexOf(item)+1;return versions.Count>1?"v"+index+"/"+versions.Count:"";
        }

        string StashMetadataLine(StashItem item)
        {
            var parts=new List<string>();string version=StashVersionLabel(item);if(!String.IsNullOrWhiteSpace(version))parts.Add(version);if(item.DuplicateCount>0)parts.Add("重复 "+item.DuplicateCount+" 次");if(!String.IsNullOrWhiteSpace(item.Project))parts.Add("项目 "+item.Project);if(!String.IsNullOrWhiteSpace(item.Company))parts.Add("公司 "+item.Company);if(!String.IsNullOrWhiteSpace(item.Industry))parts.Add("行业 "+item.Industry);if(!String.IsNullOrWhiteSpace(item.IndexName))parts.Add("指数 "+item.IndexName);if(!String.IsNullOrWhiteSpace(item.Client))parts.Add("客户 "+item.Client);if(!String.IsNullOrWhiteSpace(item.Tags))parts.Add("#"+item.Tags.Replace(","," #").Replace("，"," #"));if(!String.IsNullOrWhiteSpace(item.SourceApp))parts.Add(item.SourceApp);return String.Join(" · ",parts);
        }

        TextBox MetadataBox(StackPanel stack,string label,string value,bool multiline=false)
        {
            stack.Children.Add(new TextBlock
            {
                Text=label,FontWeight=FontWeights.SemiBold,FontSize=13,Foreground=Ui.Ink,
                Margin=new Thickness(0,10,0,5)
            });
            var box=new TextBox
            {
                Text=value??"",MinHeight=multiline?86:38,AcceptsReturn=multiline,
                TextWrapping=TextWrapping.Wrap,
                VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden,
                Padding=new Thickness(10,6,10,6),Background=Ui.Card,BorderBrush=Ui.InputLine
            };
            stack.Children.Add(box);return box;
        }

        void EditSelectedStashMetadata()
        {
            var items=SelectedStashes();if(items.Count==0)return;
            StashItem first=items[0];
            var window=new Window
            {
                Title="中转资料信息",Width=520,Height=690,MinWidth=440,MinHeight=540,
                WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,
                AllowsTransparency=true,Background=Brushes.Transparent,
                Topmost=stashPanel!=null&&stashPanel.Topmost,Owner=stashPanel
            };
            Ui.StyleWindow(window);
            var outer=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(22)};
            Ui.StyleCard(outer);
            var root=new Grid();
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

            var header=new Grid{Margin=new Thickness(0,0,0,12)};
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var heading=new StackPanel();
            heading.Children.Add(Ui.Title(items.Count==1?"整理中转资料":"批量整理 "+items.Count+" 项",20));
            heading.Children.Add(Ui.Subtitle(items.Count==1?"补充标签与业务归属，便于检索和复用":"填写的内容将应用到所选资料"));
            header.Children.Add(heading);
            var close=Ui.MakeCloseButton();close.Click+=delegate{window.DialogResult=false;};
            Grid.SetColumn(close,1);header.Children.Add(close);root.Children.Add(header);
            EnableWindowInteraction(window,header);

            var scroll=new ScrollViewer
            {
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                Padding=new Thickness(2,0,8,0)
            };
            var stack=new StackPanel();
            var project=MetadataBox(stack,"项目",items.Count==1?first.Project:"");
            var company=MetadataBox(stack,"公司",items.Count==1?first.Company:"");
            var industry=MetadataBox(stack,"行业",items.Count==1?first.Industry:"");
            var indexName=MetadataBox(stack,"指数",items.Count==1?first.IndexName:"");
            var client=MetadataBox(stack,"客户",items.Count==1?first.Client:"");
            var tags=MetadataBox(stack,"标签（逗号分隔）",items.Count==1?first.Tags:"");
            var note=MetadataBox(stack,"备注",items.Count==1?first.Note:"",true);
            scroll.Content=stack;Grid.SetRow(scroll,1);root.Children.Add(scroll);

            var actions=new WrapPanel{Margin=new Thickness(0,16,0,0),HorizontalAlignment=HorizontalAlignment.Right};
            var cancel=MakeButton("取消",Ui.Neutral);
            var save=MakeButton("保存",Ui.Accent);save.Foreground=Brushes.White;
            save.Click+=delegate
            {
                foreach(StashItem item in items)
                {
                    if(items.Count==1||!String.IsNullOrWhiteSpace(project.Text))item.Project=project.Text.Trim();
                    if(items.Count==1||!String.IsNullOrWhiteSpace(company.Text))item.Company=company.Text.Trim();
                    if(items.Count==1||!String.IsNullOrWhiteSpace(industry.Text))item.Industry=industry.Text.Trim();
                    if(items.Count==1||!String.IsNullOrWhiteSpace(indexName.Text))item.IndexName=indexName.Text.Trim();
                    if(items.Count==1||!String.IsNullOrWhiteSpace(client.Text))item.Client=client.Text.Trim();
                    if(items.Count==1||!String.IsNullOrWhiteSpace(tags.Text))item.Tags=tags.Text.Trim();
                    if(items.Count==1||!String.IsNullOrWhiteSpace(note.Text))item.Note=note.Text.Trim();
                }
                SaveStash();RefreshStash();window.DialogResult=true;
            };
            cancel.Click+=delegate{window.DialogResult=false;};
            actions.Children.Add(cancel);actions.Children.Add(save);
            Grid.SetRow(actions,2);root.Children.Add(actions);
            outer.Child=root;window.Content=outer;window.ShowDialog();
        }

        string XmlText(string xml,string tagPattern)
        {
            if(String.IsNullOrWhiteSpace(xml))return "";var builder=new StringBuilder();var tokens=Regex.Matches(xml,@"</(?:w:p|a:p|row|si)>|<(?:"+tagPattern+")[^>]*>(.*?)</(?:"+tagPattern+")>",RegexOptions.IgnoreCase|RegexOptions.Singleline);foreach(Match token in tokens){if(token.Value.StartsWith("</",StringComparison.Ordinal)){if(builder.Length>0&&builder[builder.Length-1]!='\n')builder.AppendLine();continue;}string value=System.Net.WebUtility.HtmlDecode(Regex.Replace(token.Groups[1].Value,"<[^>]+>",""));if(String.IsNullOrWhiteSpace(value))continue;if(builder.Length>0&&builder[builder.Length-1]!='\n')builder.Append(' ');builder.Append(value.Trim());}return builder.ToString().Trim();
        }

        string ReadZipText(string path,Func<ZipArchiveEntry,bool> select,Func<ZipArchiveEntry,string,string> format)
        {
            var sections=new List<string>();using(var archive=ZipFile.OpenRead(path)){foreach(var entry in archive.Entries.Where(select).OrderBy(x=>{var number=Regex.Match(x.Name,@"\d+");int value;return number.Success&&Int32.TryParse(number.Value,out value)?value:0;}).ThenBy(x=>x.FullName)){try{using(var reader=new StreamReader(entry.Open(),Encoding.UTF8,true)){string value=reader.ReadToEnd();sections.Add(format(entry,value));}}catch{}}}return String.Join("\n\n",sections);
        }

        string ExtractStashPreviewText(StashItem item,out string reference)
        {
            reference=item==null?"":item.Name??"";if(item==null)return "";if(item.Kind=="text")return item.Value??"";if(item.Kind=="folder"){try{return String.Join("\n",Directory.EnumerateFileSystemEntries(item.Value).Take(300).Select(Path.GetFileName));}catch(Exception ex){return "文件夹无法读取："+ex.Message;}}if(!File.Exists(item.Value))return "原文件已经不存在。";string ext=Path.GetExtension(item.Value).ToLowerInvariant();
            try{
                if(new[]{".txt",".md",".csv",".json",".xml",".html",".htm",".log"}.Contains(ext)){using(var reader=new StreamReader(item.Value,Encoding.UTF8,true)){char[] buffer=new char[1000000];int read=reader.Read(buffer,0,buffer.Length);return new String(buffer,0,read)+(reader.EndOfStream?"":"\n\n【预览已截断】");}}
                if(ext==".docx"){reference=(item.Name??"")+"（Word 页码须按原文件版式核对）";return ReadZipText(item.Value,e=>e.FullName.Equals("word/document.xml",StringComparison.OrdinalIgnoreCase),(e,xml)=>XmlText(xml,"w:t"));}
                if(ext==".pptx"){return ReadZipText(item.Value,e=>Regex.IsMatch(e.FullName,@"^ppt/slides/slide\d+\.xml$",RegexOptions.IgnoreCase),(e,xml)=>{var match=Regex.Match(e.Name,@"\d+");string slide=match.Success?match.Value:"?";return "【第"+slide+"张幻灯片】\n"+XmlText(xml,"a:t");});}
                if(ext==".xlsx"){reference=(item.Name??"")+"（按工作表引用）";return ReadZipText(item.Value,e=>e.FullName.StartsWith("xl/worksheets/sheet",StringComparison.OrdinalIgnoreCase)||e.FullName.Equals("xl/sharedStrings.xml",StringComparison.OrdinalIgnoreCase),(e,xml)=>"【"+e.Name+"】\n"+XmlText(xml,"t|v"));}
                if(ext==".zip"){using(var archive=ZipFile.OpenRead(item.Value))return String.Join("\n",archive.Entries.Take(1000).Select(e=>e.FullName+"    "+e.Length+" bytes"));}
                if(ext==".pdf")return "PDF 快速入口\n\n文件："+item.Name+"\n大小："+new FileInfo(item.Value).Length/1024.0+" KB\n\n点击“打开原文件”可使用系统 PDF 阅读器查看完整页面；合规审核时请在引用位置填写页码。";
                if(ext==".psd")return "PSD 文件\n\n"+item.Name+"\n大小："+(new FileInfo(item.Value).Length/1024.0).ToString("0.0")+" KB\n当前提供文件信息、标签、版本与打开入口，不会擅自合并图层。";
                if(ext==".exe"||ext==".msi")return "程序文件（安全模式）\n\n不会自动运行。\n文件："+item.Name+"\n大小："+(new FileInfo(item.Value).Length/1024.0).ToString("0.0")+" KB\n指纹："+(item.ContentHash??ComputeStashHash(item));
                return "文件："+item.Name+"\n类型："+ext+"\n大小："+(new FileInfo(item.Value).Length/1024.0).ToString("0.0")+" KB";
            }catch(Exception ex){return "预览失败："+ex.Message;}
        }

        void BuildStashPreviewPanel()
        {
            stashPreviewPanel=new Window{Title="中转资料预览",Width=760,Height=650,MinWidth=520,MinHeight=420,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=stashPanel!=null&&stashPanel.Topmost};Ui.StyleWindow(stashPreviewPanel);var outer=new Border{CornerRadius=new CornerRadius(16),Padding=new Thickness(16)};Ui.StyleCard(outer);var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});stashPreviewTitle=Ui.Title("资料预览",19);header.Children.Add(stashPreviewTitle);var close=Ui.MakeCloseButton();close.Click+=delegate{stashPreviewPanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);layout.Children.Add(header);EnableWindowInteraction(stashPreviewPanel,header);stashPreviewMeta=new TextBlock{Foreground=Ui.SubInk,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,9)};Grid.SetRow(stashPreviewMeta,1);layout.Children.Add(stashPreviewMeta);var previewGrid=new Grid();stashPreviewImage=new Image{Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed};stashPreviewText=new TextBox{IsReadOnly=true,IsReadOnlyCaretVisible=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12),Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};previewGrid.Children.Add(stashPreviewText);previewGrid.Children.Add(stashPreviewImage);Grid.SetRow(previewGrid,2);layout.Children.Add(previewGrid);var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};var open=MakeButton("打开原文件",Ui.Neutral);open.Click+=delegate{StashItem item=stashPreviewPanel.Tag as StashItem;if(item==null||item.Kind=="text")return;try{Process.Start(new ProcessStartInfo(item.Value){UseShellExecute=true});}catch(Exception ex){React("文件打不开："+ex.Message,false);}};var download=MakeButton("下载图片",Ui.Neutral);download.ToolTip="把这张图片另存到任意位置";download.Click+=delegate{DownloadStashImage(stashPreviewPanel.Tag as StashItem);};var toAi=MakeButton("进 AI 口袋",new SolidColorBrush(Color.FromRgb(255,232,226)));toAi.ToolTip="文本进入 AI 对话，图片进入 AI 图片编辑";toAi.Click+=delegate{SendStashToAiPocket(stashPreviewPanel.Tag as StashItem);};var metadata=MakeButton("标签与备注",Ui.Neutral);metadata.Click+=delegate{StashItem item=stashPreviewPanel.Tag as StashItem;if(item!=null){stashList.SelectedItems.Clear();foreach(ListBoxItem row in stashList.Items)if(row.Tag==item){row.IsSelected=true;break;}EditSelectedStashMetadata();ShowStashPreview(item);}};actions.Children.Add(open);actions.Children.Add(download);actions.Children.Add(toAi);actions.Children.Add(metadata);Grid.SetRow(actions,3);layout.Children.Add(actions);outer.Child=layout;stashPreviewPanel.Content=outer;stashPreviewPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;stashPreviewPanel.Hide();}};
        }

        void ShowStashPreview(StashItem item)
        {
            if(item==null)return;if(stashPreviewPanel==null)BuildStashPreviewPanel();stashPreviewPanel.Tag=item;stashPreviewTitle.Text=(item.Favorite?"★ ":"")+(item.Name??"资料预览");stashPreviewMeta.Text=StashTypeName(item)+" · "+StashMetadataLine(item);stashPreviewImage.Source=null;stashPreviewImage.Visibility=Visibility.Collapsed;stashPreviewText.Visibility=Visibility.Visible;if(item.Kind=="image"&&File.Exists(item.Value)){try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(item.Value);image.EndInit();image.Freeze();stashPreviewImage.Source=image;stashPreviewImage.Visibility=Visibility.Visible;stashPreviewText.Visibility=Visibility.Collapsed;}catch{}}if(stashPreviewText.Visibility==Visibility.Visible){string reference;stashPreviewText.Text=ExtractStashPreviewText(item,out reference);}if(!stashPreviewPanel.IsVisible){var work=SystemParameters.WorkArea;stashPreviewPanel.Left=Math.Max(work.Left+8,Math.Min((stashPanel==null?pet.Left:stashPanel.Left+stashPanel.Width+10),work.Right-stashPreviewPanel.Width-8));stashPreviewPanel.Top=Math.Max(work.Top+8,Math.Min(stashPanel==null?pet.Top:stashPanel.Top,work.Bottom-stashPreviewPanel.Height-8));stashPreviewPanel.Show();}stashPreviewPanel.Activate();
        }
    }
}
