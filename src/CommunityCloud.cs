using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MomoPetApp
{
    public class CommunityCloudPost
    {
        public string Id{get;set;} public string Author{get;set;} public string Title{get;set;} public string Body{get;set;} public string Category{get;set;} public string Tags{get;set;} public string CreatedAt{get;set;} public int Likes{get;set;} public bool Liked{get;set;}
    }
    public class CommunityCloudPackage
    {
        public string Id{get;set;} public string Kind{get;set;} public string Author{get;set;} public string Name{get;set;} public string Description{get;set;} public string Version{get;set;} public MomoAttachment File{get;set;} public string CreatedAt{get;set;} public int Downloads{get;set;}
    }

    public partial class PetController
    {
        readonly List<CommunityCloudPackage> communityCloudPackages=new List<CommunityCloudPackage>();
        bool communityCloudBusy; string communityCloudError;

        void SyncCommunityCloud()
        {
            if(!IsMomoSignedIn()){communityCloudError="登录桌宠账号后即可查看和共享云端内容";RefreshCommunityFeed();return;}
            communityCloudBusy=true;communityCloudError=null;RefreshCommunityFeed();
            int pending=2;Action finished=delegate{pending--;if(pending<=0){communityCloudBusy=false;RefreshCommunityFeed();}};
            MomoApi<List<CommunityCloudPost>>("GET","/api/momo/community/posts",null,true,delegate(List<CommunityCloudPost> rows){communityPosts.Clear();foreach(var row in rows??new List<CommunityCloudPost>())communityPosts.Add(new CommunityPost{Id=row.Id,Author=row.Author,Role="云端成员",Title=row.Title,Body=row.Body,Category=row.Category,Tags=row.Tags,Created=FormatCommunityCloudTime(row.CreatedAt),Likes=row.Likes,Liked=row.Liked});finished();},delegate(string error){communityCloudError=error;finished();});
            MomoApi<List<CommunityCloudPackage>>("GET","/api/momo/community/packages",null,true,delegate(List<CommunityCloudPackage> rows){communityCloudPackages.Clear();communityCloudPackages.AddRange(rows??new List<CommunityCloudPackage>());MergeCloudPackagesIntoLibraries();finished();},delegate(string error){communityCloudError=error;finished();});
        }

        static string FormatCommunityCloudTime(string value){DateTime time;return DateTime.TryParse(value,out time)?time.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):value;}
        void MergeCloudPackagesIntoLibraries()
        {
            communityApps.RemoveAll(x=>x.Cloud);communityResources.RemoveAll(x=>x.Cloud);
            foreach(var item in communityCloudPackages){if(item.Kind=="app")communityApps.Add(new CommunityAppItem{Id=item.Id,Name=item.Name,Description=item.Description,Category="社区共享",Version=item.Version,Author=item.Author,Url=item.File==null?null:item.File.Url,FileName=item.File==null?null:item.File.Name,Cloud=true});else communityResources.Add(new CommunityResourceItem{Id=item.Id,Kind="Skill",Name=item.Name,Description=item.Description,Tags="社区共享 · "+item.Author,Url=item.File==null?null:item.File.Url,FileName=item.File==null?null:item.File.Name,Cloud=true});}
            RefreshAppsGrid();RefreshResources();
        }

        void PublishCommunityPost(string title,string body,string category,TextBlock status,Action success)
        {
            if(!IsMomoSignedIn()){status.Text="请先登录桌宠账号";return;}status.Text="正在发布到云端…";var request=new Dictionary<string,object>{{"title",title},{"body",body},{"category",String.IsNullOrWhiteSpace(category)?"实践分享":category},{"tags",""}};
            MomoApi<CommunityCloudPost>("POST","/api/momo/community/posts",request,true,delegate(CommunityCloudPost post){communityPosts.Insert(0,new CommunityPost{Id=post.Id,Author=post.Author,Role="云端成员",Title=post.Title,Body=post.Body,Category=post.Category,Tags=post.Tags,Created=FormatCommunityCloudTime(post.CreatedAt),Likes=post.Likes,Liked=post.Liked});RefreshCommunityFeed();success();},delegate(string error){status.Text=error;});
        }

        void ToggleCommunityLike(CommunityPost post)
        {
            if(!IsMomoSignedIn())return;MomoApi<CommunityCloudPost>("POST","/api/momo/community/posts/"+Uri.EscapeDataString(post.Id)+"/like",null,true,delegate(CommunityCloudPost updated){post.Likes=updated.Likes;post.Liked=updated.Liked;RefreshCommunityFeed();},delegate(string error){communityCloudError=error;RefreshCommunityFeed();});
        }

        void ShowShareCommunityPackage(string kind)
        {
            if(!IsMomoSignedIn()){OpenMomoAccountPanel();return;}bool appKind=kind=="app";var picker=new OpenFileDialog{Title=appKind?"选择要共享的 AI 应用":"选择要共享的 Skill",Filter=appKind?"AI 应用 (*.exe)|*.exe":"Skill 文件|*.skill;*.zip;*.md;*.txt;*.json"};if(picker.ShowDialog(communityPanel)!=true)return;
            var dialog=new Window{Title="共享到 AI 社区",Width=560,Height=430,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=communityPanel,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=communityPanel.Topmost};Ui.StyleWindow(dialog);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(24),Background=CommunityCream,BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};var root=new StackPanel();root.Children.Add(HubText(appKind?"共享 AI 应用":"共享 Skill",21,CommunityInk,FontWeights.Bold));root.Children.Add(HubText(Path.GetFileName(picker.FileName),11.5,CommunityMuted,FontWeights.Normal));var name=new TextBox{Text=Path.GetFileNameWithoutExtension(picker.FileName),Height=40,Margin=new Thickness(0,18,0,10)};var description=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=130,Padding=new Thickness(10),ToolTip="介绍用途、使用方法和注意事项"};root.Children.Add(name);root.Children.Add(description);var status=HubText("文件会上传到云端，所有登录用户都能看到。",11,CommunityMuted,FontWeights.Normal);status.Margin=new Thickness(0,10,0,10);root.Children.Add(status);var actions=new WrapPanel();var cancel=CommunityButton("取消",delegate{dialog.Close();},false);Button upload=null;upload=CommunityButton("上传并共享",delegate{if(String.IsNullOrWhiteSpace(name.Text)||String.IsNullOrWhiteSpace(description.Text)){status.Text="请填写名称和说明";return;}upload.IsEnabled=false;status.Text="正在上传文件…";UploadMomoFiles(new[]{picker.FileName},delegate(List<MomoAttachment> files){if(files.Count==0){upload.IsEnabled=true;status.Text="文件上传失败";return;}var request=new Dictionary<string,object>{{"kind",kind},{"name",name.Text.Trim()},{"description",description.Text.Trim()},{"version","1.0"},{"file",new Dictionary<string,object>{{"name",files[0].Name},{"url",files[0].Url},{"type",files[0].Type},{"size",files[0].Size}}}};MomoApi<CommunityCloudPackage>("POST","/api/momo/community/packages",request,true,delegate(CommunityCloudPackage item){communityCloudPackages.Insert(0,item);MergeCloudPackagesIntoLibraries();communityMode=appKind?"AI 应用":"Skill";RefreshCommunityFeed();dialog.Close();},delegate(string error){upload.IsEnabled=true;status.Text=error;});},delegate(string error){upload.IsEnabled=true;status.Text=error;});},true);actions.Children.Add(cancel);actions.Children.Add(upload);root.Children.Add(actions);shell.Child=root;dialog.Content=shell;dialog.ShowDialog();
        }

        Border BuildCommunityPackageCard(CommunityCloudPackage item)
        {
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(64)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var icon=HubText(item.Kind=="app"?"EXE":"S",14,CommunityRust,FontWeights.Bold);icon.HorizontalAlignment=HorizontalAlignment.Center;icon.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(new Border{Width=48,Height=48,CornerRadius=new CornerRadius(14),Background=CommunityPeach,Child=icon});var copy=new StackPanel();copy.Children.Add(HubText(item.Name,16,CommunityInk,FontWeights.SemiBold));copy.Children.Add(HubText(item.Description,12,CommunityMuted,FontWeights.Normal));copy.Children.Add(HubText((item.Kind=="app"?"AI 应用":"Skill")+" · "+item.Author+" · "+FormatCommunityCloudTime(item.CreatedAt),10.5,CommunityRust,FontWeights.Normal));Grid.SetColumn(copy,1);grid.Children.Add(copy);var save=CommunityButton(item.Kind=="app"?"保存到 AI 应用":"保存到资源中心",delegate{DownloadCommunityPackage(item);},true);Grid.SetColumn(save,2);grid.Children.Add(save);return CommunityCard(grid,new Thickness(0,0,0,12));
        }

        void DownloadCommunityPackage(CommunityCloudPackage item)
        {
            if(item==null||item.File==null||String.IsNullOrWhiteSpace(item.File.Url))return;string folder=Path.Combine(dataDir,"community-downloads",item.Kind=="app"?"apps":"skills");Directory.CreateDirectory(folder);string name=SafeAttachmentName(item.File.Name);string destination=UniqueCommunityPath(folder,name),target=item.File.Url.StartsWith("http",StringComparison.OrdinalIgnoreCase)?item.File.Url:MomoServer()+item.File.Url,token=momoToken;communityCloudError="正在下载 "+name+"…";RefreshCommunityFeed();
            Task.Factory.StartNew(delegate{string partial=destination+".download";try{var request=(HttpWebRequest)WebRequest.Create(target);request.Timeout=600000;request.ReadWriteTimeout=600000;if(!String.IsNullOrWhiteSpace(token))request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;using(var response=(HttpWebResponse)request.GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(partial))input.CopyTo(output);File.Move(partial,destination);UiPost(new Action(delegate{if(item.Kind=="app")communityApps.Insert(0,new CommunityAppItem{Id="local-"+Guid.NewGuid().ToString("N"),Name=item.Name,Description=item.Description,Category="社区下载",Version=item.Version,Author=item.Author,Path=destination});else communityResources.Insert(0,new CommunityResourceItem{Id="local-"+Guid.NewGuid().ToString("N"),Kind="Skill",Name=item.Name,Description=item.Description,Tags="社区下载",Path=destination});SaveCommunityData();communityCloudError="已保存到"+(item.Kind=="app"?" AI 应用":"资源中心 Skill");React(communityCloudError,true);RefreshCommunityFeed();RefreshAppsGrid();RefreshResources();}));}catch(Exception error){try{if(File.Exists(partial))File.Delete(partial);}catch{}UiPost(new Action(delegate{communityCloudError="下载失败："+error.Message;RefreshCommunityFeed();}));}});
        }
        static string UniqueCommunityPath(string folder,string name){string candidate=Path.Combine(folder,name);if(!File.Exists(candidate))return candidate;string stem=Path.GetFileNameWithoutExtension(name),ext=Path.GetExtension(name);for(int i=2;i<1000;i++){candidate=Path.Combine(folder,stem+" ("+i+")"+ext);if(!File.Exists(candidate))return candidate;}return Path.Combine(folder,Guid.NewGuid().ToString("N")+ext);}
    }
}
