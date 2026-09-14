using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace MomoPetApp
{
    public class CommunityPost
    {
        public string Id { get; set; }
        public string Author { get; set; }
        public string Role { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Category { get; set; }
        public string Tags { get; set; }
        public string Created { get; set; }
        public int Likes { get; set; }
        public int Comments { get; set; }
        public bool Liked { get; set; }
        public bool Favorite { get; set; }
    }

    public class CommunityAppItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Path { get; set; }
        public bool Favorite { get; set; }
    }

    public class CommunityResourceItem
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Tags { get; set; }
        public string Path { get; set; }
        public bool Favorite { get; set; }
    }

    public partial class PetController
    {
        Window communityPanel, aiAppsPanel, resourceCenterPanel;
        StackPanel communityFeed, communityRightRail, appsGrid, resourcesList;
        TextBox communitySearchBox, appsSearchBox, resourceSearchBox;
        TextBlock communityViewTitle, appsViewTitle, resourceViewTitle;
        string communityMode="动态", appsMode="应用广场", resourceKind="全部";
        readonly List<CommunityPost> communityPosts=new List<CommunityPost>();
        readonly List<CommunityAppItem> communityApps=new List<CommunityAppItem>();
        readonly List<CommunityResourceItem> communityResources=new List<CommunityResourceItem>();
        bool communityDataLoaded;

        string CommunityDataPath(string name){return Path.Combine(dataDir,name+".json");}

        void EnsureCommunityData()
        {
            if(communityDataLoaded)return;communityDataLoaded=true;
            LoadCommunityList(CommunityDataPath("community-posts"),communityPosts);
            LoadCommunityList(CommunityDataPath("community-apps"),communityApps);
            LoadCommunityList(CommunityDataPath("community-resources"),communityResources);
            // 只迁移删除旧版本明确写入的演示条目，绝不误删用户自己创建或以后从云端同步的内容。
            string[] fakePosts={"今天用 AI 把周报整理时间缩短了一半","一个好用的 AI 工具，应该先让人感到轻松","本周值得收藏的 3 个研究 Skill"};
            string[] fakeApps={"灵感画板","会议纪要助手","数据图表工坊","提示词调试器"};
            string[] fakeResources={"研究资料溯源","会议行动项提取","柔和办公图标包","周报版式参考"};
            int purged=communityPosts.RemoveAll(x=>fakePosts.Contains(x.Title))+communityApps.RemoveAll(x=>fakeApps.Contains(x.Name))+communityResources.RemoveAll(x=>fakeResources.Contains(x.Name));
            if(purged>0)SaveCommunityData();
        }

        void LoadCommunityList<T>(string path,List<T> target)
        {
            try{if(File.Exists(path)){var loaded=json.Deserialize<List<T>>(File.ReadAllText(path,Encoding.UTF8));if(loaded!=null)target.AddRange(loaded);}}catch(Exception error){Debug.WriteLine("Community load: "+error.Message);}
        }

        void SaveCommunityData()
        {
            try{Directory.CreateDirectory(dataDir);File.WriteAllText(CommunityDataPath("community-posts"),json.Serialize(communityPosts),new UTF8Encoding(false));File.WriteAllText(CommunityDataPath("community-apps"),json.Serialize(communityApps),new UTF8Encoding(false));File.WriteAllText(CommunityDataPath("community-resources"),json.Serialize(communityResources),new UTF8Encoding(false));}catch(Exception error){Debug.WriteLine("Community save: "+error.Message);}
        }

        TextBlock HubText(string text,double size,Brush color,FontWeight weight)
        {
            return new TextBlock{Text=text,FontSize=size,Foreground=color,FontWeight=weight,TextWrapping=TextWrapping.Wrap};
        }

        Border HubCard(UIElement child,Thickness margin,Thickness padding)
        {
            return new Border{Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Margin=margin,Padding=padding,Child=child};
        }

        Button HubNav(string label,Action action)
        {
            var button=MakeButton(label,Brushes.Transparent);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,2,0,2);button.Padding=new Thickness(14,8,14,8);button.Click+=delegate{action();};return button;
        }

        Grid BuildHubHeader(Window window,string icon,string title,string subtitle,Action primary,string primaryText)
        {
            var header=new Grid{Margin=new Thickness(2,0,2,14),Cursor=Cursors.SizeAll};
            header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var identity=new StackPanel{Orientation=Orientation.Horizontal};
            var badge=new Border{Width=42,Height=42,CornerRadius=new CornerRadius(14),Background=Ui.AccentSoft,Margin=new Thickness(0,0,12,0),Child=HubText(icon,20,Ui.AccentDeep,FontWeights.Normal)};((TextBlock)badge.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)badge.Child).VerticalAlignment=VerticalAlignment.Center;identity.Children.Add(badge);
            var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};copy.Children.Add(Ui.Title(title,22));copy.Children.Add(Ui.Subtitle(subtitle));identity.Children.Add(copy);header.Children.Add(identity);
            if(primary!=null){var create=MakeButton(primaryText,Ui.Accent);create.Foreground=Brushes.White;create.VerticalAlignment=VerticalAlignment.Center;create.Click+=delegate{primary();};Grid.SetColumn(create,1);header.Children.Add(create);}var close=Ui.MakeCloseButton();close.Click+=delegate{window.Hide();};Grid.SetColumn(close,2);header.Children.Add(close);
            AddShelfControl(window,header,close);EnableWindowInteraction(window,header);return header;
        }

        void PositionHubWindow(Window window)
        {
            if(window==null||manuallyPlacedWindows.Contains(window)||shelvedWindows.ContainsKey(window)||window.WindowState!=WindowState.Normal)return;var work=SystemParameters.WorkArea;window.Left=work.Left+(work.Width-window.Width)/2;window.Top=work.Top+(work.Height-window.Height)/2;
        }

        void OpenCommunityPanel()
        {
            EnsureCommunityData();if(communityPanel==null)BuildCommunityPanel();if(RestoreShelvedIfNeeded(communityPanel))return;RefreshCommunityFeed();PositionHubWindow(communityPanel);communityPanel.Show();communityPanel.Activate();
        }

        void BuildCommunityPanel()
        {
            communityPanel=new Window{Title="AI 社区",Width=1220,Height=790,MinWidth=980,MinHeight=650,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(communityPanel);
            var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(22)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.Children.Add(BuildHubHeader(communityPanel,"☕","AI 社区","交流经验、关注同行，也分享正在发生的好想法",ShowCreatePostDialog,"写分享"));
            var layout=new Grid{Background=Ui.Paper};layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(184)});layout.ColumnDefinitions.Add(new ColumnDefinition());layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(252)});Grid.SetRow(layout,1);root.Children.Add(layout);
            var nav=new StackPanel{Margin=new Thickness(0,12,16,0)};nav.Children.Add(Ui.SectionLabel("社区"));nav.Children.Add(HubNav("  动态",delegate{communityMode="动态";RefreshCommunityFeed();}));nav.Children.Add(HubNav("  小组",delegate{communityMode="小组";RefreshCommunityFeed();}));nav.Children.Add(HubNav("  我的收藏",delegate{communityMode="收藏";RefreshCommunityFeed();}));
            nav.Children.Add(HubNav("  💌 账号与来信",delegate{OpenMessengerPanel();}));
            var note=new StackPanel();note.Children.Add(HubText("安静交流角",13,Ui.Ink,FontWeights.SemiBold));note.Children.Add(HubText("先交换方法，再比较结果。让每次分享都能被继续使用。",11.5,Ui.SubInk,FontWeights.Normal));nav.Children.Add(HubCard(note,new Thickness(0,20,0,0),new Thickness(14)));layout.Children.Add(nav);
            var center=new Grid{Margin=new Thickness(0,12,16,0)};center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});center.RowDefinitions.Add(new RowDefinition());var centerTop=new Grid{Margin=new Thickness(0,0,0,10)};centerTop.ColumnDefinitions.Add(new ColumnDefinition());centerTop.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(270)});communityViewTitle=Ui.Title("社区动态",18);centerTop.Children.Add(communityViewTitle);communitySearchBox=new TextBox{Height=38,VerticalContentAlignment=VerticalAlignment.Center,ToolTip="搜索标题、内容或作者"};communitySearchBox.TextChanged+=delegate{RefreshCommunityFeed();};Grid.SetColumn(communitySearchBox,1);centerTop.Children.Add(communitySearchBox);center.Children.Add(centerTop);communityFeed=new StackPanel();var feedScroll=new ScrollViewer{Content=communityFeed,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(feedScroll,1);center.Children.Add(feedScroll);Grid.SetColumn(center,1);layout.Children.Add(center);
            communityRightRail=new StackPanel{Margin=new Thickness(0,12,0,0)};Grid.SetColumn(communityRightRail,2);layout.Children.Add(new ScrollViewer{Content=communityRightRail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Grid.SetColumn(layout.Children[layout.Children.Count-1],2);
            shell.Child=root;communityPanel.Content=shell;communityPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;communityPanel.Hide();}};RefreshCommunityFeed();
        }

        void RefreshCommunityFeed()
        {
            if(communityFeed==null)return;communityFeed.Children.Clear();communityRightRail.Children.Clear();communityViewTitle.Text=communityMode=="动态"?"社区动态":(communityMode=="收藏"?"我的收藏":communityMode);
            string query=communitySearchBox==null?"":(communitySearchBox.Text??"").Trim();IEnumerable<CommunityPost> items=communityPosts;
            if(communityMode=="收藏")items=items.Where(x=>x.Favorite);if(query.Length>0)items=items.Where(x=>(x.Title+" "+x.Body+" "+x.Author+" "+x.Tags).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);
            if(communityMode=="小组"){
                var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(20,62,20,62)};empty.Children.Add(HubText("🐾",28,Ui.SubInk,FontWeights.Normal));empty.Children.Add(HubText("小组已经移到来信",16,Ui.Ink,FontWeights.SemiBold));empty.Children.Add(HubText("在那里可以新建小组、邀请联系人并直接聊天。",12,Ui.SubInk,FontWeights.Normal));var open=MakeButton("打开账号与来信",Ui.Accent);open.Foreground=Brushes.White;open.HorizontalAlignment=HorizontalAlignment.Center;open.Margin=new Thickness(0,16,0,0);open.Click+=delegate{OpenMessengerPanel();};empty.Children.Add(open);communityFeed.Children.Add(HubCard(empty,new Thickness(0),new Thickness(20)));
            }
            else{foreach(var post in items)communityFeed.Children.Add(BuildCommunityPostCard(post));if(!items.Any())communityFeed.Children.Add(BuildEmptyState("这里还没有内容","社区内容保存在本机，写下第一条分享就会出现在这里。"));}
            var stats=new StackPanel();stats.Children.Add(HubText("我的社区",13,Ui.SubInk,FontWeights.SemiBold));stats.Children.Add(HubText(communityPosts.Count+"",28,Ui.Ink,FontWeights.SemiBold));stats.Children.Add(HubText("条分享 · 内容保存在本机",11.5,Ui.SubInk,FontWeights.Normal));communityRightRail.Children.Add(HubCard(stats,new Thickness(0,0,0,10),new Thickness(16)));
            var tags=communityPosts.SelectMany(x=>(x.Tags??"").Split(new[]{'·'},StringSplitOptions.RemoveEmptyEntries)).Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().Take(6).ToList();
            if(tags.Count>0){var hot=new StackPanel();hot.Children.Add(HubText("我的话题",14,Ui.Ink,FontWeights.SemiBold));foreach(string tag in tags)hot.Children.Add(HubText("# "+tag,12.5,Ui.AccentDeep,FontWeights.SemiBold));communityRightRail.Children.Add(HubCard(hot,new Thickness(0,0,0,10),new Thickness(16)));}
        }

        Border BuildCommunityPostCard(CommunityPost post)
        {
            var root=new StackPanel();var author=new Grid();author.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(44)});author.ColumnDefinitions.Add(new ColumnDefinition());author.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var avatar=new Border{Width=36,Height=36,CornerRadius=new CornerRadius(12),Background=Ui.AccentSoft,Child=HubText(String.IsNullOrWhiteSpace(post.Author)?"M":post.Author.Substring(0,1),14,Ui.AccentDeep,FontWeights.Bold)};((TextBlock)avatar.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)avatar.Child).VerticalAlignment=VerticalAlignment.Center;author.Children.Add(avatar);var identity=new StackPanel();identity.Children.Add(HubText(post.Author,13,Ui.Ink,FontWeights.SemiBold));identity.Children.Add(HubText(post.Role+" · "+post.Created,10.5,Ui.SubInk,FontWeights.Normal));Grid.SetColumn(identity,1);author.Children.Add(identity);var category=Ui.PillBadge(post.Category,Ui.AccentDeep,Ui.AccentSoft,56);Grid.SetColumn(category,2);author.Children.Add(category);root.Children.Add(author);root.Children.Add(HubText(post.Title,18,Ui.Ink,FontWeights.SemiBold));root.Children.Add(new TextBlock{Text=post.Body,FontSize=13,Foreground=Ui.SubInk,TextWrapping=TextWrapping.Wrap,LineHeight=21,Margin=new Thickness(0,7,0,7)});root.Children.Add(HubText(post.Tags,11.5,Ui.AccentDeep,FontWeights.SemiBold));var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};var like=MakeButton((post.Liked?"♥ ":"♡ ")+post.Likes,post.Liked?Ui.PeachSoft:Brushes.Transparent);like.Click+=delegate{post.Liked=!post.Liked;post.Likes+=post.Liked?1:-1;SaveCommunityData();RefreshCommunityFeed();};var comment=MakeButton("💬 "+post.Comments,Brushes.Transparent);comment.Click+=delegate{ShowPostDetail(post);};var favorite=MakeButton(post.Favorite?"★ 已收藏":"☆ 收藏",Brushes.Transparent);favorite.Click+=delegate{post.Favorite=!post.Favorite;SaveCommunityData();RefreshCommunityFeed();};actions.Children.Add(like);actions.Children.Add(comment);actions.Children.Add(favorite);root.Children.Add(actions);return HubCard(root,new Thickness(0,0,0,10),new Thickness(18));
        }

        Border BuildEmptyState(string title,string subtitle)
        {
            var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(20,70,20,70)};stack.Children.Add(HubText("🐾",28,Ui.SubInk,FontWeights.Normal));stack.Children.Add(HubText(title,16,Ui.Ink,FontWeights.SemiBold));stack.Children.Add(HubText(subtitle,12,Ui.SubInk,FontWeights.Normal));return HubCard(stack,new Thickness(0),new Thickness(20));
        }

        void ShowCreatePostDialog()
        {
            var dialog=new Window{Title="写分享",Width=620,Height=540,MinWidth=520,MinHeight=460,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=communityPanel.Topmost,Owner=communityPanel};Ui.StyleWindow(dialog);var shell=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(22)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var header=new Grid{Margin=new Thickness(0,0,0,14),Cursor=Cursors.SizeAll};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(Ui.Title("写一条可继续讨论的分享",19));var close=Ui.MakeCloseButton();close.Click+=delegate{dialog.Close();};Grid.SetColumn(close,1);header.Children.Add(close);header.MouseLeftButtonDown+=delegate{try{dialog.DragMove();}catch{}};root.Children.Add(header);var title=new TextBox{Height=42,ToolTip="标题",Margin=new Thickness(0,0,0,10)};Grid.SetRow(title,1);root.Children.Add(title);var body=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,ToolTip="写下方法、过程或问题",Padding=new Thickness(12)};Grid.SetRow(body,2);root.Children.Add(body);var footer=new Grid{Margin=new Thickness(0,12,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var hint=HubText("内容保存在本机，可随时继续编辑。",11.5,Ui.SubInk,FontWeights.Normal);hint.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(hint);var publish=MakeButton("发布到社区",Ui.Accent);publish.Foreground=Brushes.White;publish.Click+=delegate{if(String.IsNullOrWhiteSpace(title.Text)||String.IsNullOrWhiteSpace(body.Text)){hint.Text="请先补充标题和内容";return;}communityPosts.Insert(0,new CommunityPost{Id=Guid.NewGuid().ToString("N"),Author="我",Role="社区成员",Title=title.Text.Trim(),Body=body.Text.Trim(),Category="实践分享",Tags="我的分享",Created="刚刚"});SaveCommunityData();communityMode="动态";RefreshCommunityFeed();dialog.Close();};Grid.SetColumn(publish,1);footer.Children.Add(publish);Grid.SetRow(footer,3);root.Children.Add(footer);shell.Child=root;dialog.Content=shell;dialog.ShowDialog();
        }

        void ShowPostDetail(CommunityPost post)
        {
            post.Comments++;SaveCommunityData();Ui.Alert(communityPanel,post.Title,post.Body+"\n\n"+post.Author+" · "+post.Tags+"\n\n评论功能已记录一次打开；后续可在这里继续接入云端讨论。 ");RefreshCommunityFeed();
        }

        void OpenAiAppsPanel()
        {
            EnsureCommunityData();if(aiAppsPanel==null)BuildAiAppsPanel();if(RestoreShelvedIfNeeded(aiAppsPanel))return;RefreshAppsGrid();PositionHubWindow(aiAppsPanel);aiAppsPanel.Show();aiAppsPanel.Activate();
        }

        void BuildAiAppsPanel()
        {
            aiAppsPanel=new Window{Title="AI 应用",Width=1120,Height=750,MinWidth=900,MinHeight=620,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(aiAppsPanel);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(22)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.Children.Add(BuildHubHeader(aiAppsPanel,"▦","AI 应用","独立管理、添加和运行应用，不和素材混在一起",AddLocalAiApp,"添加应用"));var toolbar=new Grid{Margin=new Thickness(0,0,0,12)};toolbar.ColumnDefinitions.Add(new ColumnDefinition());toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)});var tabs=new WrapPanel();foreach(string name in new[]{"应用广场","我的应用","已收藏"}){string selected=name;tabs.Children.Add(HubNav(selected,delegate{appsMode=selected;RefreshAppsGrid();}));}toolbar.Children.Add(tabs);appsSearchBox=new TextBox{Height=38,ToolTip="搜索应用"};appsSearchBox.TextChanged+=delegate{RefreshAppsGrid();};Grid.SetColumn(appsSearchBox,1);toolbar.Children.Add(appsSearchBox);Grid.SetRow(toolbar,1);root.Children.Add(toolbar);var content=new Grid{Background=Ui.Paper};content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});content.RowDefinitions.Add(new RowDefinition());appsViewTitle=Ui.Title("应用广场",18);appsViewTitle.Margin=new Thickness(2,8,0,10);content.Children.Add(appsViewTitle);appsGrid=new StackPanel();var scroll=new ScrollViewer{Content=appsGrid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);content.Children.Add(scroll);Grid.SetRow(content,2);root.Children.Add(content);shell.Child=root;aiAppsPanel.Content=shell;aiAppsPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;aiAppsPanel.Hide();}};RefreshAppsGrid();
        }

        void RefreshAppsGrid()
        {
            if(appsGrid==null)return;appsGrid.Children.Clear();appsViewTitle.Text=appsMode;string query=appsSearchBox==null?"":(appsSearchBox.Text??"").Trim();IEnumerable<CommunityAppItem> items=communityApps;if(appsMode=="我的应用")items=items.Where(x=>!String.IsNullOrWhiteSpace(x.Path));else if(appsMode=="已收藏")items=items.Where(x=>x.Favorite);if(query.Length>0)items=items.Where(x=>(x.Name+" "+x.Description+" "+x.Category).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);var rows=items.ToList();for(int i=0;i<rows.Count;i+=3){var row=new Grid{Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());for(int j=0;j<3&&i+j<rows.Count;j++){var card=BuildAppCard(rows[i+j]);card.Margin=new Thickness(j==0?0:6,0,j==2?0:6,0);Grid.SetColumn(card,j);row.Children.Add(card);}appsGrid.Children.Add(row);}if(rows.Count==0)appsGrid.Children.Add(BuildEmptyState("没有找到应用","可以添加本地应用，或换个关键词。"));
        }

        Border BuildAppCard(CommunityAppItem item)
        {
            var root=new StackPanel{MinHeight=190};var icon=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(15),Background=Ui.AccentSoft,HorizontalAlignment=HorizontalAlignment.Left,Child=HubText("AI",15,Ui.AccentDeep,FontWeights.Bold)};((TextBlock)icon.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)icon.Child).VerticalAlignment=VerticalAlignment.Center;root.Children.Add(icon);root.Children.Add(HubText(item.Name,16,Ui.Ink,FontWeights.SemiBold));root.Children.Add(HubText(item.Description,12,Ui.SubInk,FontWeights.Normal));root.Children.Add(HubText(item.Category+" · v"+item.Version+" · "+item.Author,10.5,Ui.SubInk,FontWeights.Normal));var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};var run=MakeButton(String.IsNullOrWhiteSpace(item.Path)?"关联本地应用":"运行",String.IsNullOrWhiteSpace(item.Path)?Ui.Neutral:Ui.Accent);if(!String.IsNullOrWhiteSpace(item.Path))run.Foreground=Brushes.White;run.Click+=delegate{if(String.IsNullOrWhiteSpace(item.Path)){AttachAiApp(item);return;}try{if(!File.Exists(item.Path)){Ui.Alert(aiAppsPanel,"应用文件不存在","请重新关联本地应用文件。 ");return;}Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true});}catch(Exception error){Ui.Alert(aiAppsPanel,"无法运行应用",error.Message);}};var fav=MakeButton(item.Favorite?"★":"☆",Brushes.Transparent);fav.Click+=delegate{item.Favorite=!item.Favorite;SaveCommunityData();RefreshAppsGrid();};actions.Children.Add(run);actions.Children.Add(fav);root.Children.Add(actions);return HubCard(root,new Thickness(0),new Thickness(17));
        }

        void AddLocalAiApp()
        {
            var picker=new OpenFileDialog{Title="选择要加入的 AI 应用",Filter="应用程序 (*.exe)|*.exe|快捷方式 (*.lnk)|*.lnk|所有文件 (*.*)|*.*"};if(picker.ShowDialog(aiAppsPanel)!=true)return;communityApps.Insert(0,new CommunityAppItem{Id=Guid.NewGuid().ToString("N"),Name=Path.GetFileNameWithoutExtension(picker.FileName),Description="本地添加的 AI 应用，可从桌宠直接启动。",Category="本地应用",Version="本地",Author="我",Path=picker.FileName});SaveCommunityData();appsMode="我的应用";RefreshAppsGrid();
        }

        void AttachAiApp(CommunityAppItem item)
        {
            var picker=new OpenFileDialog{Title="为“"+item.Name+"”关联本地应用",Filter="应用程序 (*.exe)|*.exe|快捷方式 (*.lnk)|*.lnk|所有文件 (*.*)|*.*"};if(picker.ShowDialog(aiAppsPanel)!=true)return;item.Path=picker.FileName;SaveCommunityData();appsMode="我的应用";RefreshAppsGrid();
        }

        void OpenResourceCenterPanel()
        {
            EnsureCommunityData();if(resourceCenterPanel==null)BuildResourceCenterPanel();if(RestoreShelvedIfNeeded(resourceCenterPanel))return;RefreshResources();PositionHubWindow(resourceCenterPanel);resourceCenterPanel.Show();resourceCenterPanel.Activate();
        }

        void BuildResourceCenterPanel()
        {
            resourceCenterPanel=new Window{Title="资源中心",Width=1100,Height=740,MinWidth=880,MinHeight=600,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(resourceCenterPanel);var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(22)};Ui.StyleCard(shell);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.Children.Add(BuildHubHeader(resourceCenterPanel,"◇","资源中心","集中整理 Skill 与素材，保持轻量、可检索、可复用",ImportCommunityResource,"导入资源"));var layout=new Grid{Background=Ui.Paper};layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});layout.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(layout,1);root.Children.Add(layout);var side=new StackPanel{Margin=new Thickness(0,12,18,0)};side.Children.Add(Ui.SectionLabel("资源类型"));foreach(string name in new[]{"全部","Skill","素材","已收藏"}){string selected=name;side.Children.Add(HubNav("  "+selected,delegate{resourceKind=selected;RefreshResources();}));}var explanation=new StackPanel();explanation.Children.Add(HubText("为什么这样分？",13,Ui.Ink,FontWeights.SemiBold));explanation.Children.Add(HubText("Skill 是可执行能力，素材是可复用内容；它们都属于资源，但 AI 应用有自己的运行和版本管理。",11.5,Ui.SubInk,FontWeights.Normal));side.Children.Add(HubCard(explanation,new Thickness(0,20,0,0),new Thickness(14)));layout.Children.Add(side);var main=new Grid{Margin=new Thickness(0,12,0,0)};main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition());var tools=new Grid{Margin=new Thickness(0,0,0,10)};tools.ColumnDefinitions.Add(new ColumnDefinition());tools.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(290)});resourceViewTitle=Ui.Title("全部资源",18);tools.Children.Add(resourceViewTitle);resourceSearchBox=new TextBox{Height=38,ToolTip="搜索资源、标签或说明"};resourceSearchBox.TextChanged+=delegate{RefreshResources();};Grid.SetColumn(resourceSearchBox,1);tools.Children.Add(resourceSearchBox);main.Children.Add(tools);resourcesList=new StackPanel();var scroll=new ScrollViewer{Content=resourcesList,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);main.Children.Add(scroll);Grid.SetColumn(main,1);layout.Children.Add(main);shell.Child=root;resourceCenterPanel.Content=shell;resourceCenterPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;resourceCenterPanel.Hide();}};RefreshResources();
        }

        void RefreshResources()
        {
            if(resourcesList==null)return;resourcesList.Children.Clear();resourceViewTitle.Text=resourceKind=="全部"?"全部资源":resourceKind;string query=resourceSearchBox==null?"":(resourceSearchBox.Text??"").Trim();IEnumerable<CommunityResourceItem> items=communityResources;if(resourceKind=="Skill"||resourceKind=="素材")items=items.Where(x=>x.Kind==resourceKind);else if(resourceKind=="已收藏")items=items.Where(x=>x.Favorite);if(query.Length>0)items=items.Where(x=>(x.Name+" "+x.Description+" "+x.Tags).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);var found=items.ToList();foreach(var item in found)resourcesList.Children.Add(BuildResourceCard(item));if(found.Count==0)resourcesList.Children.Add(BuildEmptyState("这里还没有资源","可以导入 Skill、图片、文档或模板。"));
        }

        Border BuildResourceCard(CommunityResourceItem item)
        {
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(64)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var kind=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(15),Background=item.Kind=="Skill"?Ui.AccentSoft:Ui.PeachSoft,Child=HubText(item.Kind=="Skill"?"S":"图",15,item.Kind=="Skill"?Ui.AccentDeep:Ui.Ink,FontWeights.Bold)};((TextBlock)kind.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)kind.Child).VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(kind);var copy=new StackPanel();copy.Children.Add(HubText(item.Name,15,Ui.Ink,FontWeights.SemiBold));copy.Children.Add(HubText(item.Description,12,Ui.SubInk,FontWeights.Normal));copy.Children.Add(HubText(item.Kind+" · "+item.Tags,10.5,Ui.AccentDeep,FontWeights.SemiBold));Grid.SetColumn(copy,1);grid.Children.Add(copy);var actions=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};if(!String.IsNullOrWhiteSpace(item.Path)){var open=MakeButton("打开",Ui.Neutral);open.Click+=delegate{try{if(File.Exists(item.Path))Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true});else Ui.Alert(resourceCenterPanel,"找不到资源","这个文件可能已移动，请重新导入。 ");}catch(Exception error){Ui.Alert(resourceCenterPanel,"无法打开",error.Message);}};actions.Children.Add(open);}var favorite=MakeButton(item.Favorite?"★":"☆",Brushes.Transparent);favorite.Click+=delegate{item.Favorite=!item.Favorite;SaveCommunityData();RefreshResources();};actions.Children.Add(favorite);Grid.SetColumn(actions,2);grid.Children.Add(actions);return HubCard(grid,new Thickness(0,0,0,10),new Thickness(16));
        }

        void ImportCommunityResource()
        {
            var picker=new OpenFileDialog{Title="导入 Skill 或素材",Filter="所有支持的资源|*.md;*.txt;*.json;*.zip;*.png;*.jpg;*.jpeg;*.svg;*.pptx;*.docx;*.xlsx|Skill 与文档|*.md;*.txt;*.json;*.zip|图片与办公素材|*.png;*.jpg;*.jpeg;*.svg;*.pptx;*.docx;*.xlsx|所有文件|*.*"};if(picker.ShowDialog(resourceCenterPanel)!=true)return;string ext=Path.GetExtension(picker.FileName).ToLowerInvariant();string kind=(ext==".md"||ext==".txt"||ext==".json"||ext==".zip")?"Skill":"素材";communityResources.Insert(0,new CommunityResourceItem{Id=Guid.NewGuid().ToString("N"),Kind=kind,Name=Path.GetFileNameWithoutExtension(picker.FileName),Description="从本机导入，可在资源中心快速检索和打开。",Tags="本地导入",Path=picker.FileName});SaveCommunityData();resourceKind=kind;RefreshResources();
        }

        void SetCommunityTopmost(bool value){if(communityPanel!=null)communityPanel.Topmost=value;if(aiAppsPanel!=null)aiAppsPanel.Topmost=value;if(resourceCenterPanel!=null)resourceCenterPanel.Topmost=value;}
        void CloseCommunityWindows(){if(communityPanel!=null)communityPanel.Close();if(aiAppsPanel!=null)aiAppsPanel.Close();if(resourceCenterPanel!=null)resourceCenterPanel.Close();}
    }
}
