using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace MomoPetApp
{
    public partial class PetController
    {
        static readonly string[,] SkinCatalog={
            {"default","原版博道咪"},{"detective","侦探暹罗"},{"green-scarf-calico","绿围巾三花"},
            {"tuxedo-bell","红铃铛黑白猫"},{"white-bowtie","蓝领结长毛白猫"},{"orange-scarf","蓝围巾橘猫"},
            {"red-collar-calico","红铃铛三花"},{"gentleman-monocle","礼帽单片眼镜白猫"}
        };
        static readonly string[] SkinMotionNames={"idle","walk","run","dragged","sleep","edge","happy","coffee"};
        static readonly string[] SkinMotionLabels={"待机","走路","奔跑","被拎","睡觉","扒边","开心","咖啡"};
        readonly Dictionary<string,BitmapImage[]> skinMotionFrames=new Dictionary<string,BitmapImage[]>();
        readonly Dictionary<string,Border> skinCardBorders=new Dictionary<string,Border>();
        readonly List<MenuItem> skinQuickMenuItems=new List<MenuItem>();
        Window skinWardrobePanel;
        WrapPanel skinCardPanel,skinMotionPreview;
        Image skinLargePreview;
        TextBlock skinPreviewName,skinPreviewStatus;
        string pendingSkinId="default";

        void LoadSkinMotion(string id)
        {
            var result=new BitmapImage[SkinMotionNames.Length];
            for(int i=0;i<SkinMotionNames.Length;i++){
                string file=SkinMotionNames[i]+".png";
                result[i]=EmbeddedRuntime.LoadBitmap(Path.Combine(root,"assets","normalized","skin-motion",id,file),"assets.normalized.skin-motion."+id+"."+file,320);
            }
            skinMotionFrames[id]=result;
        }

        int CurrentSkinMotionIndex()
        {
            if(petState=="walk")return 1;
            if(petState=="run")return 2;
            if(petState=="dragged"||petState=="climb"||petState=="drop")return 3;
            if(petState=="sleep")return 4;
            if(petState=="edge")return 5;
            if(petState=="happy"||petState=="face"||petState=="tail"||petState=="stretch"||petState=="hop")return 6;
            if(petState=="coffee")return 7;
            return 0;
        }

        ImageSource SelectedSkinMotionFrame()
        {
            if(petMovement==null||String.IsNullOrWhiteSpace(petMovement.SkinId)||petMovement.SkinId=="default")return null;
            BitmapImage[] values;if(!skinMotionFrames.TryGetValue(petMovement.SkinId,out values)||values==null)return SelectedSkinFrame();
            int index=CurrentSkinMotionIndex();return index>=0&&index<values.Length?values[index]:SelectedSkinFrame();
        }

        ImageSource SelectedSkinEdgeFrame()
        {
            if(petMovement==null||petMovement.SkinId=="default")return null;BitmapImage[] values;
            return skinMotionFrames.TryGetValue(petMovement.SkinId,out values)&&values!=null&&values.Length>5?values[5]:null;
        }

        void ApplySkinChoice(string id)
        {
            if(String.IsNullOrWhiteSpace(id))id="default";
            petMovement.SkinId=id;SavePetMovementSettings();ApplySelectedSkin();
            OnLocalSkinChanged(id);
            foreach(MenuItem item in skinQuickMenuItems)item.IsChecked=Convert.ToString(item.Tag)==id;
            React(id=="default"?"换回原来的我啦～":"新装扮换好啦～",false);
        }

        string SkinName(string id)
        {
            for(int i=0;i<SkinCatalog.GetLength(0);i++)if(SkinCatalog[i,0]==id)return SkinCatalog[i,1];return "博道咪";
        }

        Brush SkinCheckerBrush()
        {
            var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(249,250,252)),null,new RectangleGeometry(new Rect(0,0,20,20))));
            var pale=new SolidColorBrush(Color.FromRgb(232,235,240));group.Children.Add(new GeometryDrawing(pale,null,new RectangleGeometry(new Rect(0,0,10,10))));group.Children.Add(new GeometryDrawing(pale,null,new RectangleGeometry(new Rect(10,10,10,10))));
            return new DrawingBrush(group){TileMode=TileMode.Tile,Viewport=new Rect(0,0,20,20),ViewportUnits=BrushMappingMode.Absolute};
        }

        ImageSource SkinPreviewFrame(string id)
        {
            if(id=="default")return frames[0];BitmapImage value;return skinFrames.TryGetValue(id,out value)?value:frames[0];
        }

        ImageSource[] SkinPreviewMotions(string id)
        {
            if(id!="default"){BitmapImage[] values;if(skinMotionFrames.TryGetValue(id,out values))return values;}
            return new ImageSource[]{frames[0],walkFrames[2],runFrames[2],dragFrame,poses[7],edgePeekFrame,poses[6],coffeeFrame};
        }

        void BuildSkinWardrobe()
        {
            skinWardrobePanel=new Window{Title="皮肤衣柜",Width=860,Height=610,MinWidth=700,MinHeight=520,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=pet.Topmost};Ui.StyleWindow(skinWardrobePanel);
            var shell=Ui.CardBorder();shell.CornerRadius=new CornerRadius(22);shell.Margin=new Thickness(8);shell.Padding=new Thickness(22,18,22,20);shell.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=28,ShadowDepth=8,Opacity=.16};
            var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid{Margin=new Thickness(2,0,2,16),Cursor=Cursors.SizeAll};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var titleStack=new StackPanel();titleStack.Children.Add(Ui.Title("皮肤衣柜",23));titleStack.Children.Add(Ui.Subtitle("先预览，再换装 · 每套皮肤都保留自己的动作"));header.Children.Add(titleStack);var close=Ui.MakeCloseButton();close.Click+=delegate{skinWardrobePanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);header.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left)try{skinWardrobePanel.DragMove();}catch{}};layout.Children.Add(header);
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(260)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});body.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(body,1);layout.Children.Add(body);
            skinCardPanel=new WrapPanel{Margin=new Thickness(2)};var scroll=new ScrollViewer{Content=skinCardPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};body.Children.Add(scroll);
            var previewCard=Ui.CardBorder();previewCard.Padding=new Thickness(16);Grid.SetColumn(previewCard,2);body.Children.Add(previewCard);var previewLayout=new Grid();previewLayout.RowDefinitions.Add(new RowDefinition());previewLayout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});previewLayout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});previewCard.Child=previewLayout;
            var stage=new Border{CornerRadius=new CornerRadius(16),Background=SkinCheckerBrush(),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Padding=new Thickness(12),ClipToBounds=true};skinLargePreview=new Image{Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};stage.Child=skinLargePreview;previewLayout.Children.Add(stage);
            stage.SizeChanged+=delegate{skinLargePreview.MaxWidth=Math.Max(1,stage.ActualWidth-26);skinLargePreview.MaxHeight=Math.Max(1,stage.ActualHeight-26);};
            var copy=new StackPanel{Margin=new Thickness(2,14,2,12)};skinPreviewName=Ui.Title("",18);skinPreviewStatus=Ui.Subtitle("8 个动作 · 真实透明底");copy.Children.Add(skinPreviewName);copy.Children.Add(skinPreviewStatus);Grid.SetRow(copy,1);previewLayout.Children.Add(copy);
            skinMotionPreview=new WrapPanel();Grid.SetRow(skinMotionPreview,2);previewLayout.Children.Add(skinMotionPreview);
            var footer=new Grid{Margin=new Thickness(0,18,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var hint=Ui.Subtitle("右键小猫也能快速切换；衣柜用于完整预览");hint.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(hint);var actions=new StackPanel{Orientation=Orientation.Horizontal};var courier=Ui.MakeButton("预览送信",Ui.Neutral);courier.Click+=delegate{PreviewCourierSkin(pendingSkinId);};var cancel=Ui.MakeButton("取消",Ui.Neutral);cancel.Click+=delegate{skinWardrobePanel.Hide();};var apply=Ui.MakeButton("应用这套皮肤",Ui.Accent);apply.Foreground=Brushes.White;apply.Click+=delegate{ApplySkinChoice(pendingSkinId);skinWardrobePanel.Hide();};actions.Children.Add(courier);actions.Children.Add(cancel);actions.Children.Add(apply);Grid.SetColumn(actions,1);footer.Children.Add(actions);Grid.SetRow(footer,2);layout.Children.Add(footer);
            shell.Child=layout;skinWardrobePanel.Content=shell;skinWardrobePanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;skinWardrobePanel.Hide();}};
        }

        void OpenSkinWardrobe()
        {
            if(skinWardrobePanel==null)BuildSkinWardrobe();pendingSkinId=petMovement==null?"default":petMovement.SkinId;RefreshSkinWardrobe();var work=SystemParameters.WorkArea;skinWardrobePanel.Left=Math.Max(work.Left+8,work.Left+(work.Width-skinWardrobePanel.Width)/2);skinWardrobePanel.Top=Math.Max(work.Top+8,work.Top+(work.Height-skinWardrobePanel.Height)/2);skinWardrobePanel.Show();skinWardrobePanel.Activate();
        }

        void RefreshSkinWardrobe()
        {
            skinCardPanel.Children.Clear();skinCardBorders.Clear();
            for(int i=0;i<SkinCatalog.GetLength(0);i++){
                string id=SkinCatalog[i,0],label=SkinCatalog[i,1];var stack=new Grid();stack.RowDefinitions.Add(new RowDefinition());stack.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});var image=new Image{Source=SkinPreviewFrame(id),Stretch=Stretch.Uniform,Margin=new Thickness(9,7,9,2)};stack.Children.Add(image);var text=new TextBlock{Text=label,FontWeight=FontWeights.SemiBold,FontSize=12.5,Foreground=Ui.Ink,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(text,1);stack.Children.Add(text);var card=new Border{Width=160,Height=150,Margin=new Thickness(5),CornerRadius=new CornerRadius(15),Background=SkinCheckerBrush(),BorderThickness=new Thickness(2),Child=stack,Cursor=Cursors.Hand};string selectedId=id;card.MouseLeftButtonUp+=delegate{pendingSkinId=selectedId;UpdateSkinPreview();};skinCardBorders[id]=card;skinCardPanel.Children.Add(card);
            }
            UpdateSkinPreview();
        }

        void UpdateSkinPreview()
        {
            foreach(var pair in skinCardBorders){bool selected=pair.Key==pendingSkinId;pair.Value.BorderBrush=selected?Ui.Accent:Ui.Line;pair.Value.Effect=selected?new DropShadowEffect{Color=Color.FromRgb(70,112,190),BlurRadius=16,ShadowDepth=0,Opacity=.18}:null;}
            skinLargePreview.Source=SkinPreviewFrame(pendingSkinId);skinPreviewName.Text=SkinName(pendingSkinId);skinMotionPreview.Children.Clear();var motions=SkinPreviewMotions(pendingSkinId);
            for(int i=0;i<motions.Length;i++){var tile=new Border{Width=64,Height=70,Margin=new Thickness(2),CornerRadius=new CornerRadius(10),Background=SkinCheckerBrush(),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Cursor=Cursors.Hand};var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(20)});grid.Children.Add(new Image{Source=motions[i],Stretch=Stretch.Uniform,MaxWidth=54,MaxHeight=43,Margin=new Thickness(4,3,4,0)});var label=new TextBlock{Text=SkinMotionLabels[i],FontSize=9.5,Foreground=Ui.SubInk,HorizontalAlignment=HorizontalAlignment.Center};Grid.SetRow(label,1);grid.Children.Add(label);tile.Child=grid;int motionIndex=i;tile.MouseLeftButtonUp+=delegate{skinLargePreview.Source=motions[motionIndex];skinPreviewStatus.Text=SkinMotionLabels[motionIndex]+" · 点击其他动作切换预览";};skinMotionPreview.Children.Add(tile);}
            skinPreviewStatus.Text=pendingSkinId=="default"?"原版完整动画 · 点击动作放大":"8 个动作 · 点击动作放大检查";
        }
    }
}
