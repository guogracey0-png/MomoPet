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
        public string Url { get; set; }
        public string FileName { get; set; }
        public bool Cloud { get; set; }
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
        public string Url { get; set; }
        public string FileName { get; set; }
        public bool Cloud { get; set; }
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
            try{MomoStorage.WriteTextAtomic(CommunityDataPath("community-posts"),json.Serialize(communityPosts),new UTF8Encoding(false));MomoStorage.WriteTextAtomic(CommunityDataPath("community-apps"),json.Serialize(communityApps),new UTF8Encoding(false));MomoStorage.WriteTextAtomic(CommunityDataPath("community-resources"),json.Serialize(communityResources),new UTF8Encoding(false));}catch(Exception error){Debug.WriteLine("Community save: "+error.Message);}
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
            EnsureCommunityData();if(communityPanel==null)BuildCommunityPanel();if(RestoreShelvedIfNeeded(communityPanel))return;RefreshCommunityFeed();PositionHubWindow(communityPanel);communityPanel.Show();communityPanel.Activate();SyncCommunityCloud();
        }

        static readonly Brush CommunityCream=new SolidColorBrush(Color.FromRgb(250,247,241));
        static readonly Brush CommunityPeach=new SolidColorBrush(Color.FromRgb(246,229,213));
        static readonly Brush CommunityRust=new SolidColorBrush(Color.FromRgb(158,84,59));
        static readonly Brush CommunityInk=new SolidColorBrush(Color.FromRgb(67,53,46));
        static readonly Brush CommunityMuted=new SolidColorBrush(Color.FromRgb(126,111,99));
        readonly Dictionary<string,Button> communityTabs=new Dictionary<string,Button>();

        Border CommunityCard(UIElement child,Thickness margin)
        {
            return new Border{Child=child,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(235,227,216)),
                BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(22),Margin=margin};
        }

        Button CommunityButton(string label,Action action,bool primary)
        {
            var button=MakeButton(label,primary?CommunityRust:Brushes.Transparent);
            button.Foreground=primary?Brushes.White:CommunityInk;button.FontSize=12;
            button.Padding=new Thickness(16,10,16,10);button.Click+=delegate{action();};return button;
        }

        Image CommunityCat(double size)
        {
            return new Image{Source=SelectedSkinFrame()??frames[0],Width=size,Height=size,
                Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center};
        }

        void BuildCommunityPanel()
        {
            communityPanel=new Window{Title="AI 社区",Width=1120,Height=820,MinWidth=820,MinHeight=620,
                WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,
                AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};
            Ui.StyleWindow(communityPanel);
            var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(24),Background=CommunityCream,
                BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
            root.Children.Add(BuildHubHeader(communityPanel,"☕","AI 社区","博知汇 · 和小猫一起，交换好想法",ShowCreatePostDialog,"＋ 写分享"));
            var page=new StackPanel{MaxWidth=1160,HorizontalAlignment=HorizontalAlignment.Stretch};
            var hero=new Grid();hero.ColumnDefinitions.Add(new ColumnDefinition());hero.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(170)});
            var greeting=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,2,12,2)};
            greeting.Children.Add(HubText("MOMO  /  一起慢慢探索",10.5,CommunityRust,FontWeights.SemiBold));
            var hello=HubText("好想法，从一句分享开始。",27,CommunityInk,FontWeights.Bold);hello.Margin=new Thickness(0,12,0,8);greeting.Children.Add(hello);
            greeting.Children.Add(HubText("一个新发现、一点小经验，或是还没解开的问题。\n带着你的小猫，来这里坐坐。",12.5,CommunityMuted,FontWeights.Normal));
            hero.Children.Add(greeting);var cat=CommunityCat(126);Grid.SetColumn(cat,1);hero.Children.Add(cat);
            var heroCard=CommunityCard(hero,new Thickness(0,4,0,20));heroCard.Background=CommunityPeach;page.Children.Add(heroCard);

            var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition());
            var railColumn=new ColumnDefinition{Width=new GridLength(264)};columns.ColumnDefinitions.Add(railColumn);
            var center=new StackPanel{Margin=new Thickness(0,0,20,0)};
            var compose=new Grid();compose.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});compose.ColumnDefinitions.Add(new ColumnDefinition());
            var avatar=CommunityCat(38);compose.Children.Add(avatar);
            var composeBody=new StackPanel{Margin=new Thickness(10,0,0,0)};
            var invite=CommunityButton("今天，有什么想和大家聊聊？",ShowCreatePostDialog,false);
            invite.HorizontalContentAlignment=HorizontalAlignment.Left;invite.Background=CommunityCream;invite.Foreground=CommunityMuted;composeBody.Children.Add(invite);
            var hints=new WrapPanel{Margin=new Thickness(0,8,0,0)};
            foreach(string label in new[]{"分享经验","发起提问","记录灵感"}){
                string topic=label;var chip=CommunityButton(topic,delegate{ShowCreatePostDialog(topic);},false);chip.Padding=new Thickness(10,6,10,6);hints.Children.Add(chip);
            }
            composeBody.Children.Add(hints);Grid.SetColumn(composeBody,1);compose.Children.Add(composeBody);
            center.Children.Add(CommunityCard(compose,new Thickness(0,0,0,18)));

            var filters=new Grid{Margin=new Thickness(0,0,0,12)};filters.ColumnDefinitions.Add(new ColumnDefinition());filters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});
            var tabs=new StackPanel{Orientation=Orientation.Horizontal};communityTabs.Clear();
            foreach(string mode in new[]{"动态","Skill","AI 应用","收藏"}){
                string selected=mode;var tab=CommunityButton(mode=="动态"?"最新分享":mode=="收藏"?"我的收藏":mode+"分享",delegate{communityMode=selected;RefreshCommunityFeed();},false);
                tab.Margin=new Thickness(0,0,4,0);communityTabs[mode]=tab;tabs.Children.Add(tab);
            }
            filters.Children.Add(tabs);
            var search=new Grid{VerticalAlignment=VerticalAlignment.Center};communitySearchBox=new TextBox{Height=36,Padding=new Thickness(10,6,10,6),VerticalContentAlignment=VerticalAlignment.Center,Background=Brushes.White,ToolTip="搜索分享、作者或话题"};
            var placeholder=HubText("搜索分享、话题…",11.5,CommunityMuted,FontWeights.Normal);placeholder.Margin=new Thickness(12,0,0,0);placeholder.VerticalAlignment=VerticalAlignment.Center;placeholder.IsHitTestVisible=false;
            search.Children.Add(communitySearchBox);search.Children.Add(placeholder);
            communitySearchBox.TextChanged+=delegate{placeholder.Visibility=String.IsNullOrEmpty(communitySearchBox.Text)?Visibility.Visible:Visibility.Collapsed;RefreshCommunityFeed();};
            Grid.SetColumn(search,1);filters.Children.Add(search);center.Children.Add(filters);
            communityViewTitle=HubText("",11,CommunityMuted,FontWeights.Normal);communityViewTitle.Margin=new Thickness(3,0,0,10);center.Children.Add(communityViewTitle);
            communityFeed=new StackPanel();center.Children.Add(communityFeed);columns.Children.Add(center);
            communityRightRail=new StackPanel();Grid.SetColumn(communityRightRail,1);columns.Children.Add(communityRightRail);page.Children.Add(columns);
            var scroll=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            Grid.SetRow(scroll,1);root.Children.Add(scroll);
            communityPanel.SizeChanged+=delegate{
                bool compact=communityPanel.ActualWidth<1000;railColumn.Width=new GridLength(compact?0:264);
                communityRightRail.Visibility=compact?Visibility.Collapsed:Visibility.Visible;center.Margin=new Thickness(0,0,compact?0:20,0);
            };
            // 窄窗口依旧保留交流入口。
            var access=new WrapPanel{Margin=new Thickness(0,16,0,2)};
            access.Children.Add(CommunityButton("桌宠账号",delegate{OpenMomoAccountPanel();},false));
            access.Children.Add(HubText("帖子、Skill 和 AI 应用均从云端同步",10.5,CommunityMuted,FontWeights.Normal));page.Children.Add(access);
            shell.Child=root;communityPanel.Content=shell;
            communityPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;communityPanel.Hide();}};
            RefreshCommunityFeed();
        }

        void RefreshCommunityFeed()
        {
            if(communityFeed==null||communityRightRail==null)return;
            communityFeed.Children.Clear();communityRightRail.Children.Clear();
            if(communityMode=="小组")communityMode="动态";
            foreach(var tab in communityTabs){tab.Value.Background=tab.Key==communityMode?CommunityPeach:Brushes.Transparent;tab.Value.Foreground=tab.Key==communityMode?CommunityRust:CommunityMuted;}
            string query=communitySearchBox==null?"":(communitySearchBox.Text??"").Trim();
            if(communityMode=="Skill"||communityMode=="AI 应用"){
                string kind=communityMode=="Skill"?"skill":"app";var packages=communityCloudPackages.Where(x=>x.Kind==kind&&(query.Length==0||(x.Name+" "+x.Description+" "+x.Author).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();communityViewTitle.Text=communityMode+" · 云端共享 "+packages.Count;var share=CommunityButton(communityMode=="Skill"?"＋ 共享 Skill 文件":"＋ 共享 EXE 应用",delegate{ShowShareCommunityPackage(kind);},true);share.HorizontalAlignment=HorizontalAlignment.Left;share.Margin=new Thickness(0,0,0,12);communityFeed.Children.Add(share);foreach(var item in packages)communityFeed.Children.Add(BuildCommunityPackageCard(item));if(communityCloudBusy)communityFeed.Children.Add(BuildEmptyState("正在连接云端","小猫正在取回大家共享的内容。"));else if(packages.Count==0)communityFeed.Children.Add(BuildEmptyState("这里还没有共享内容","上传第一个文件，让其他人可以直接使用。"));if(!String.IsNullOrWhiteSpace(communityCloudError))communityFeed.Children.Add(HubText(communityCloudError,11.5,CommunityRust,FontWeights.Normal));return;
            }
            IEnumerable<CommunityPost> items=communityPosts;
            if(communityMode=="收藏")items=items.Where(x=>x.Favorite);
            if(query.Length>0)items=items.Where(x=>(x.Title+" "+x.Body+" "+x.Author+" "+x.Tags).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);
            var visible=items.ToList();
            communityViewTitle.Text=(communityMode=="收藏"?"收藏的分享":"云端分享")+" · "+visible.Count;
            foreach(var post in visible)communityFeed.Children.Add(BuildCommunityPostCard(post));
            if(visible.Count==0){
                var empty=new StackPanel{Margin=new Thickness(12,14,12,14)};
                var title=HubText(query.Length>0?"暂时没有找到这条分享":communityMode=="收藏"?"把喜欢的想法，留在这里":"第一张椅子，已经为你留好。",20,CommunityInk,FontWeights.SemiBold);
                title.TextAlignment=TextAlignment.Center;empty.Children.Add(title);
                var subtitle=HubText(query.Length>0?"换个关键词试试，或清空搜索看看全部内容。":communityMode=="收藏"?"看到想再读的分享，点一下「收藏」就能找到。":"从一个小发现开始，让这里慢慢热闹起来。",12,CommunityMuted,FontWeights.Normal);
                subtitle.TextAlignment=TextAlignment.Center;subtitle.Margin=new Thickness(0,10,0,18);empty.Children.Add(subtitle);
                var action=CommunityButton(query.Length>0?"清空搜索":communityMode=="收藏"?"去看分享":"写下第一条分享",delegate{
                    if(query.Length>0)communitySearchBox.Clear();else if(communityMode=="收藏"){communityMode="动态";RefreshCommunityFeed();}else ShowCreatePostDialog();
                },true);action.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(action);
                communityFeed.Children.Add(CommunityCard(empty,new Thickness(0,0,0,12)));
            }
            if(communityCloudBusy)communityFeed.Children.Insert(0,HubText("正在同步云端内容…",11.5,CommunityRust,FontWeights.Normal));else if(!String.IsNullOrWhiteSpace(communityCloudError))communityFeed.Children.Insert(0,HubText(communityCloudError,11.5,CommunityRust,FontWeights.Normal));var identity=new StackPanel();
            identity.Children.Add(HubText(IsMomoSignedIn()?momoAccount.Nickname:"登录后查看云端社区",17,CommunityInk,FontWeights.SemiBold));
            var accountHint=HubText(IsMomoSignedIn()?"@"+momoAccount.Username:"使用桌宠账号，在这里记录好想法。",12,CommunityMuted,FontWeights.Normal);
            accountHint.Margin=new Thickness(0,10,0,16);identity.Children.Add(accountHint);
            identity.Children.Add(CommunityButton(IsMomoSignedIn()?"同步云端":"登录 / 注册",delegate{if(IsMomoSignedIn())SyncCommunityCloud();else OpenMomoAccountPanel();},true));
            var identityCard=CommunityCard(identity,new Thickness(0,0,0,16));identityCard.Background=new SolidColorBrush(Color.FromRgb(237,242,232));communityRightRail.Children.Add(identityCard);
            var topics=new StackPanel();topics.Children.Add(HubText("从这些话题开始",14,CommunityInk,FontWeights.SemiBold));
            foreach(string idea in new[]{"最近发现的 AI 小技巧","一个想请教的问题","今天试过的新方法"}){
                string prompt=idea;var button=CommunityButton("＋ "+idea,delegate{ShowCreatePostDialog(prompt);},false);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new Thickness(0,12,0,8);button.FontSize=11.5;topics.Children.Add(button);
            }
            communityRightRail.Children.Add(CommunityCard(topics,new Thickness(0,0,0,16)));
            var note=HubText("每一点真实的经验，都值得被认真听见。",11.5,CommunityMuted,FontWeights.Normal);note.Margin=new Thickness(12,4,12,10);communityRightRail.Children.Add(note);
        }

        Border BuildCommunityPostCard(CommunityPost post)
        {
            var root=new StackPanel();var author=new Grid();author.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(46)});author.ColumnDefinitions.Add(new ColumnDefinition());author.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var initial=HubText(String.IsNullOrWhiteSpace(post.Author)?"我":post.Author.Substring(0,1),15,CommunityRust,FontWeights.Bold);
            initial.HorizontalAlignment=HorizontalAlignment.Center;initial.VerticalAlignment=VerticalAlignment.Center;
            author.Children.Add(new Border{Width=36,Height=36,CornerRadius=new CornerRadius(18),Background=CommunityPeach,Child=initial,HorizontalAlignment=HorizontalAlignment.Left});
            var identity=new StackPanel();identity.Children.Add(HubText(post.Author,13,CommunityInk,FontWeights.SemiBold));identity.Children.Add(HubText(post.Created+" · 云端分享",10.5,CommunityMuted,FontWeights.Normal));
            Grid.SetColumn(identity,1);author.Children.Add(identity);
            var category=Ui.PillBadge(post.Category,CommunityRust,CommunityCream,56);Grid.SetColumn(category,2);author.Children.Add(category);root.Children.Add(author);
            var title=HubText(post.Title,19,CommunityInk,FontWeights.SemiBold);title.Margin=new Thickness(0,16,0,8);root.Children.Add(title);
            var excerpt=Ui.ReadOnlyText(post.Body,13);excerpt.Foreground=CommunityMuted;excerpt.MaxHeight=132;root.Children.Add(excerpt);
            if(!String.IsNullOrWhiteSpace(post.Tags)){var tags=HubText(post.Tags,11.5,CommunityRust,FontWeights.Normal);tags.Margin=new Thickness(0,12,0,0);root.Children.Add(tags);}
            var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};
            actions.Children.Add(CommunityButton("阅读全文 ↗",delegate{ShowPostDetail(post);},false));
            actions.Children.Add(CommunityButton((post.Liked?"♥ ":"♡ ")+post.Likes,delegate{ToggleCommunityLike(post);},false));
            actions.Children.Add(CommunityButton(post.Favorite?"★ 已收藏":"☆ 收藏",delegate{post.Favorite=!post.Favorite;SaveCommunityData();RefreshCommunityFeed();},false));
            root.Children.Add(actions);return CommunityCard(root,new Thickness(0,0,0,14));
        }

        Border BuildEmptyState(string title,string subtitle)
        {
            var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(20,70,20,70)};stack.Children.Add(HubText("🐾",28,Ui.SubInk,FontWeights.Normal));stack.Children.Add(HubText(title,16,Ui.Ink,FontWeights.SemiBold));stack.Children.Add(HubText(subtitle,12,Ui.SubInk,FontWeights.Normal));return HubCard(stack,new Thickness(0),new Thickness(20));
        }

        void ShowCreatePostDialog(){ShowCreatePostDialog("");}

        void ShowCreatePostDialog(string prompt)
        {
            if(!IsMomoSignedIn()){OpenMomoAccountPanel();return;}
            var dialog=new Window{Title="写分享",Width=660,Height=610,MinWidth=520,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,
                WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,
                Background=Brushes.Transparent,Topmost=communityPanel.Topmost,Owner=communityPanel};
            Ui.StyleWindow(dialog);
            var shell=new Border{CornerRadius=new CornerRadius(20),Padding=new Thickness(26),Background=CommunityCream,BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid{Margin=new Thickness(0,0,0,22)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var heading=new StackPanel();heading.Children.Add(HubText("把好想法，写下来",22,CommunityInk,FontWeights.Bold));var sub=HubText("不必完整，一点经验或一个问题就很好。",12,CommunityMuted,FontWeights.Normal);sub.Margin=new Thickness(0,6,0,0);heading.Children.Add(sub);header.Children.Add(heading);
            var close=Ui.MakeCloseButton();close.Click+=delegate{dialog.Close();};Grid.SetColumn(close,1);header.Children.Add(close);EnableWindowInteraction(dialog,header);root.Children.Add(header);
            var titleArea=new StackPanel{Margin=new Thickness(0,0,0,16)};titleArea.Children.Add(HubText("分享标题",12,CommunityInk,FontWeights.SemiBold));
            var title=new TextBox{Text=prompt,Height=42,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,7,0,0),ToolTip="用一句话说说你的想法"};titleArea.Children.Add(title);Grid.SetRow(titleArea,1);root.Children.Add(titleArea);
            var bodyArea=new Grid();bodyArea.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bodyArea.RowDefinitions.Add(new RowDefinition());
            bodyArea.Children.Add(HubText("聊聊过程、收获，或卡住的地方",12,CommunityInk,FontWeights.SemiBold));
            var body=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(14),Margin=new Thickness(0,8,0,0),FontSize=14};
            Grid.SetRow(body,1);bodyArea.Children.Add(body);Grid.SetRow(bodyArea,2);root.Children.Add(bodyArea);
            var footer=new Grid{Margin=new Thickness(0,18,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var hint=HubText("发布到云端\n所有社区成员都能看到",11.5,CommunityMuted,FontWeights.Normal);hint.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(hint);
            Button publish=null;publish=CommunityButton("发布到云端",delegate{
                if(String.IsNullOrWhiteSpace(title.Text)||String.IsNullOrWhiteSpace(body.Text)){hint.Text="请补充标题和内容";return;}
                publish.IsEnabled=false;PublishCommunityPost(title.Text.Trim(),body.Text.Trim(),"实践分享",hint,delegate{communityMode="动态";if(communitySearchBox!=null)communitySearchBox.Clear();dialog.Close();});
            },true);
            Grid.SetColumn(publish,1);footer.Children.Add(publish);Grid.SetRow(footer,3);root.Children.Add(footer);shell.Child=root;dialog.Content=shell;dialog.ShowDialog();
        }

        void ShowPostDetail(CommunityPost post)
        {
            Ui.Alert(communityPanel,post.Title,post.Body+"\n\n"+post.Author+" · "+post.Tags);
        }

        void OpenAiAppsPanel()
        {
            EnsureCommunityData();if(aiAppsPanel==null)BuildAiAppsPanel();if(RestoreShelvedIfNeeded(aiAppsPanel))return;RefreshAppsGrid();PositionHubWindow(aiAppsPanel);aiAppsPanel.Show();aiAppsPanel.Activate();SyncCommunityCloud();
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
            var root=new StackPanel{MinHeight=190};var icon=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(15),Background=Ui.AccentSoft,HorizontalAlignment=HorizontalAlignment.Left,Child=HubText("AI",15,Ui.AccentDeep,FontWeights.Bold)};((TextBlock)icon.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)icon.Child).VerticalAlignment=VerticalAlignment.Center;root.Children.Add(icon);root.Children.Add(HubText(item.Name,16,Ui.Ink,FontWeights.SemiBold));root.Children.Add(HubText(item.Description,12,Ui.SubInk,FontWeights.Normal));root.Children.Add(HubText(item.Category+" · v"+item.Version+" · "+item.Author,10.5,Ui.SubInk,FontWeights.Normal));var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};var run=MakeButton(!String.IsNullOrWhiteSpace(item.Path)?"运行":item.Cloud?"保存到 AI 应用":"关联本地应用",!String.IsNullOrWhiteSpace(item.Path)?Ui.Accent:Ui.Neutral);if(!String.IsNullOrWhiteSpace(item.Path))run.Foreground=Brushes.White;run.Click+=delegate{if(String.IsNullOrWhiteSpace(item.Path)){if(item.Cloud){var package=communityCloudPackages.FirstOrDefault(x=>x.Id==item.Id);if(package!=null)DownloadCommunityPackage(package);}else AttachAiApp(item);return;}try{if(!File.Exists(item.Path)){Ui.Alert(aiAppsPanel,"应用文件不存在","请重新关联本地应用文件。 ");return;}Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true});}catch(Exception error){Ui.Alert(aiAppsPanel,"无法运行应用",error.Message);}};var fav=MakeButton(item.Favorite?"★":"☆",Brushes.Transparent);fav.Click+=delegate{item.Favorite=!item.Favorite;SaveCommunityData();RefreshAppsGrid();};actions.Children.Add(run);actions.Children.Add(fav);root.Children.Add(actions);return HubCard(root,new Thickness(0),new Thickness(17));
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
            EnsureCommunityData();if(resourceCenterPanel==null)BuildResourceCenterPanel();if(RestoreShelvedIfNeeded(resourceCenterPanel))return;RefreshResources();PositionHubWindow(resourceCenterPanel);resourceCenterPanel.Show();resourceCenterPanel.Activate();SyncCommunityCloud();
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
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(64)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var kind=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(15),Background=item.Kind=="Skill"?Ui.AccentSoft:Ui.PeachSoft,Child=HubText(item.Kind=="Skill"?"S":"图",15,item.Kind=="Skill"?Ui.AccentDeep:Ui.Ink,FontWeights.Bold)};((TextBlock)kind.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)kind.Child).VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(kind);var copy=new StackPanel();copy.Children.Add(HubText(item.Name,15,Ui.Ink,FontWeights.SemiBold));copy.Children.Add(HubText(item.Description,12,Ui.SubInk,FontWeights.Normal));copy.Children.Add(HubText(item.Kind+" · "+item.Tags,10.5,Ui.AccentDeep,FontWeights.SemiBold));Grid.SetColumn(copy,1);grid.Children.Add(copy);var actions=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};if(item.Cloud){var saveCloud=MakeButton("保存到资源中心",Ui.Accent);saveCloud.Foreground=Brushes.White;saveCloud.Click+=delegate{var package=communityCloudPackages.FirstOrDefault(x=>x.Id==item.Id);if(package!=null)DownloadCommunityPackage(package);};actions.Children.Add(saveCloud);}else if(!String.IsNullOrWhiteSpace(item.Path)){var open=MakeButton("打开",Ui.Neutral);open.Click+=delegate{try{if(File.Exists(item.Path))Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true});else Ui.Alert(resourceCenterPanel,"找不到资源","这个文件可能已移动，请重新导入。 ");}catch(Exception error){Ui.Alert(resourceCenterPanel,"无法打开",error.Message);}};actions.Children.Add(open);}var favorite=MakeButton(item.Favorite?"★":"☆",Brushes.Transparent);favorite.Click+=delegate{item.Favorite=!item.Favorite;SaveCommunityData();RefreshResources();};actions.Children.Add(favorite);Grid.SetColumn(actions,2);grid.Children.Add(actions);return HubCard(grid,new Thickness(0,0,0,10),new Thickness(16));
        }

        void ImportCommunityResource()
        {
            var picker=new OpenFileDialog{Title="导入 Skill 或素材",Filter="所有支持的资源|*.md;*.txt;*.json;*.zip;*.png;*.jpg;*.jpeg;*.svg;*.pptx;*.docx;*.xlsx|Skill 与文档|*.md;*.txt;*.json;*.zip|图片与办公素材|*.png;*.jpg;*.jpeg;*.svg;*.pptx;*.docx;*.xlsx|所有文件|*.*"};if(picker.ShowDialog(resourceCenterPanel)!=true)return;string ext=Path.GetExtension(picker.FileName).ToLowerInvariant();string kind=(ext==".md"||ext==".txt"||ext==".json"||ext==".zip")?"Skill":"素材";communityResources.Insert(0,new CommunityResourceItem{Id=Guid.NewGuid().ToString("N"),Kind=kind,Name=Path.GetFileNameWithoutExtension(picker.FileName),Description="从本机导入，可在资源中心快速检索和打开。",Tags="本地导入",Path=picker.FileName});SaveCommunityData();resourceKind=kind;RefreshResources();
        }

        void SetCommunityTopmost(bool value){if(communityPanel!=null)communityPanel.Topmost=value;if(aiAppsPanel!=null)aiAppsPanel.Topmost=value;if(resourceCenterPanel!=null)resourceCenterPanel.Topmost=value;}
        void CloseCommunityWindows(){if(communityPanel!=null)communityPanel.Close();if(aiAppsPanel!=null)aiAppsPanel.Close();if(resourceCenterPanel!=null)resourceCenterPanel.Close();}
    }
}
