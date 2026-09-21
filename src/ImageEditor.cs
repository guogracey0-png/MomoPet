using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MomoPetApp
{
    public partial class PetController
    {

        void OpenImageEditor(StashItem item)
        {
            if(item==null || item.Kind!="image" || !File.Exists(item.Value)) { React("图片已经不在了",false); return; }
            try {
                if(stashPanel!=null)stashPanel.Hide();
                editingImageItem=item; originalBitmap=LoadEditorBitmap(item.Value); workingBitmap=originalBitmap; workingEncodedBytes=null; compressionCandidateBytes=null; compressionCandidateBitmap=null;
                if(imageEditorPanel==null) BuildImageEditor();
                RestoreShelvedIfNeeded(imageEditorPanel);
                outputFormatBox.SelectedItem=Path.GetExtension(item.Value).ToLowerInvariant()==".png"?"PNG":"JPEG";
                compressionSlider.Value=30;
                customCropSizeActive=false;cropModeActive=false;layerCompositionActive=false;layerDragging=false;precisionLayers.Clear();selectedPrecisionLayer=null;preLayerSplitBitmap=null;preLayerSplitEncodedBytes=null;if(precisionLayerList!=null)precisionLayerList.Visibility=Visibility.Collapsed;if(cropPresetBox!=null)cropPresetBox.SelectedIndex=0;ClearImageTextLayer();
                editorImage.Source=workingBitmap; widthBox.Text=workingBitmap.PixelWidth.ToString(); heightBox.Text=workingBitmap.PixelHeight.ToString(); ResetImageHistory();
                imageStatus.Text=String.Format("{0} × {1} px · {2:0.0} KB · 浏览模式",workingBitmap.PixelWidth,workingBitmap.PixelHeight,new FileInfo(item.Value).Length/1024.0);UpdateImageFacts();
                if(imageAiConfig.RememberImagePrompt==true)redrawPromptBox.Text=imageAiConfig.SavedImagePrompt??"";else redrawPromptBox.Clear();recognizedTextBox.Clear();ShowImageToolMode("local");ShowAiPocketImage(); PositionImageEditor(); imageEditorPanel.Show(); imageEditorPanel.Activate();
                // 打开图片默认只进入浏览模式；识字、文字选取必须由用户主动点击，不能抢占打开图片的体验。
                imageEditorPanel.Dispatcher.BeginInvoke(new Action(delegate{ResetCrop();imageCanvas.Cursor=Cursors.Arrow;ClearImageTextLayer();}),DispatcherPriority.Loaded);
            } catch(Exception ex) { React("图片打不开："+ex.Message,false); }
        }

        Brush CheckerboardBrush()
        {
            var drawing=new DrawingGroup();drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(245,246,248)),null,new RectangleGeometry(new Rect(0,0,20,20))));var dark=new SolidColorBrush(Color.FromRgb(220,224,230));drawing.Children.Add(new GeometryDrawing(dark,null,new RectangleGeometry(new Rect(0,0,10,10))));drawing.Children.Add(new GeometryDrawing(dark,null,new RectangleGeometry(new Rect(10,10,10,10))));return new DrawingBrush(drawing){TileMode=TileMode.Tile,Viewport=new Rect(0,0,20,20),ViewportUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,20,20),ViewboxUnits=BrushMappingMode.Absolute};
        }

        void UpdateCanvasBackground(){if(imageCanvas==null)return;string mode=Convert.ToString(canvasBackgroundBox==null?"棋盘格":canvasBackgroundBox.SelectedItem);imageCanvas.Background=mode=="白底"?Brushes.White:mode=="黑底"?Brushes.Black:CheckerboardBrush();}

        void UpdateImageFacts()
        {
            if(workingBitmap==null)return;long bytes=workingEncodedBytes==null?(editingImageItem!=null&&File.Exists(editingImageItem.Value)?new FileInfo(editingImageItem.Value).Length:0):workingEncodedBytes.Length;
            if(imageFileInfo!=null)imageFileInfo.Text=String.Format("当前画布：{0} × {1} px · {2:0.0} KB"+(editingImageItem!=null?" · 原文件未编辑时会按原字节上传":""),workingBitmap.PixelWidth,workingBitmap.PixelHeight,bytes/1024.0);
            if(imageAlphaInfo!=null)imageAlphaInfo.Text=HasTransparentPixels(workingBitmap)?"透明检测：真实 Alpha 通道 ✓（切换棋盘格／白底／黑底查看）":"透明检测：没有透明像素（棋盘格仅是预览背景，不会导出）";
        }

        void BuildImageEditor()
        {
            imageEditorPanel=new Window { Title="博道咪 AI 口袋",Width=1120,Height=800,MinWidth=900,MinHeight=650,WindowStyle=WindowStyle.None,
                ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true };
            var outer=new Border { CornerRadius=new CornerRadius(18),Padding=new Thickness(20) };
            Ui.StyleCard(outer); Ui.StyleWindow(imageEditorPanel);
            var rootGrid=new Grid(); rootGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); rootGrid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) }); rootGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });rootGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            var header=new Grid { Margin=new Thickness(2,0,0,18) }; header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var heading=new StackPanel();heading.Children.Add(Ui.Title("AI 口袋",22));heading.Children.Add(Ui.Subtitle("对话、生成、编辑与图层处理集中在一个工作台"));header.Children.Add(heading);
            var pocketTabsGrid=new Grid();pocketTabsGrid.ColumnDefinitions.Add(new ColumnDefinition());pocketTabsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            aiPocketChatTab=ImageHeaderTab("AI 对话");aiPocketChatTab.Click+=delegate{ShowAiPocketChat();SetAiPocketNavigation(true);};pocketTabsGrid.Children.Add(aiPocketChatTab);
            aiPocketImageTab=ImageHeaderTab("AI 图片");aiPocketImageTab.Click+=delegate{ShowAiPocketImage();SetAiPocketNavigation(false);};Grid.SetColumn(aiPocketImageTab,1);pocketTabsGrid.Children.Add(aiPocketImageTab);
            var pocketTabs=new Border{Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(3),Margin=new Thickness(16,1,10,0),VerticalAlignment=VerticalAlignment.Top,Child=pocketTabsGrid};Grid.SetColumn(pocketTabs,1);header.Children.Add(pocketTabs);
            var close=Ui.MakeCloseButton(); close.Click+=delegate { RememberCurrentImagePrompt();imageEditorPanel.Hide(); }; Grid.SetColumn(close,2); header.Children.Add(close); rootGrid.Children.Add(header);AddShelfControl(imageEditorPanel,header,close);EnableWindowInteraction(imageEditorPanel,header);
            var body=new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(154) });body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(360) });
            var leftTools=new StackPanel{Margin=new Thickness(0,0,12,0)};leftTools.Children.Add(new TextBlock{Text="工具",FontWeight=FontWeights.Bold,FontSize=14,Margin=new Thickness(6,3,0,8)});
            var leftUpload=MakeButton("＋ 上传图片",Ui.Accent);leftUpload.Height=38;leftUpload.Click+=delegate{UploadImageToAiPocket();};leftTools.Children.Add(leftUpload);
            var leftLocal=MakeButton("本地编辑",Ui.Neutral);leftLocal.Margin=new Thickness(0,8,0,0);leftLocal.Click+=delegate{ShowImageToolMode("local");};leftTools.Children.Add(leftLocal);
            var leftAi=MakeButton("AI 创作",Ui.Neutral);leftAi.Margin=new Thickness(0,5,0,0);leftAi.Click+=delegate{ShowImageToolMode("generate");};leftTools.Children.Add(leftAi);
            var leftText=MakeButton("图片改字",Ui.Neutral);leftText.Margin=new Thickness(0,5,0,0);leftText.Click+=delegate{ShowImageToolMode("text");};leftTools.Children.Add(leftText);
            var leftDownload=MakeButton("下载画布",Ui.Neutral);leftDownload.Margin=new Thickness(0,14,0,0);leftDownload.Click+=delegate{DownloadCurrentCanvasImage();};leftTools.Children.Add(leftDownload);
            body.Children.Add(leftTools);
            imageCanvas=new Canvas { Background=CheckerboardBrush(),ClipToBounds=true,Cursor=Cursors.Arrow };
            editorImage=new Image { Stretch=Stretch.Uniform }; imageCanvas.Children.Add(editorImage);
            imageTextLayer=new Canvas{Visibility=Visibility.Collapsed,Background=Brushes.Transparent,Focusable=true,Cursor=Cursors.IBeam};imageTextLayer.MouseLeftButtonDown+=ImageTextMouseDown;imageTextLayer.MouseMove+=ImageTextMouseMove;imageTextLayer.MouseLeftButtonUp+=ImageTextMouseUp;imageTextLayer.KeyDown+=ImageTextKeyDown;imageCanvas.Children.Add(imageTextLayer);
            precisionLayerCanvas=new Canvas{IsHitTestVisible=false,Background=Brushes.Transparent};Panel.SetZIndex(precisionLayerCanvas,14);imageCanvas.Children.Add(precisionLayerCanvas);
            precisionOverlay=new Canvas{IsHitTestVisible=false,Background=Brushes.Transparent};Panel.SetZIndex(precisionOverlay,18);imageCanvas.Children.Add(precisionOverlay);
            precisionBoxOverlay=new Border { BorderBrush=new SolidColorBrush(Color.FromRgb(255,91,70)),BorderThickness=new Thickness(2),Background=new SolidColorBrush(Color.FromArgb(26,255,91,70)),Visibility=Visibility.Collapsed,IsHitTestVisible=false }; precisionOverlay.Children.Add(precisionBoxOverlay);
            layerSelectionBox=new Border {BorderBrush=new SolidColorBrush(Color.FromRgb(255,196,66)),BorderThickness=new Thickness(2),Background=Brushes.Transparent,Visibility=Visibility.Collapsed,IsHitTestVisible=false};precisionOverlay.Children.Add(layerSelectionBox);
            layerResizeHandles=new Border[4];for(int layerHandleIndex=0;layerHandleIndex<layerResizeHandles.Length;layerHandleIndex++){layerResizeHandles[layerHandleIndex]=new Border{Width=10,Height=10,CornerRadius=new CornerRadius(5),Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(255,196,66)),BorderThickness=new Thickness(2),Visibility=Visibility.Collapsed,IsHitTestVisible=false};precisionOverlay.Children.Add(layerResizeHandles[layerHandleIndex]);}
            cropBox=new Border { BorderBrush=new SolidColorBrush(Color.FromRgb(255,126,115)),BorderThickness=new Thickness(2),Background=new SolidColorBrush(Color.FromArgb(35,255,126,115)),Visibility=Visibility.Collapsed,IsHitTestVisible=false }; imageCanvas.Children.Add(cropBox);
            cropHandles=new Border[4];for(int handleIndex=0;handleIndex<cropHandles.Length;handleIndex++){cropHandles[handleIndex]=new Border{Width=11,Height=11,CornerRadius=new CornerRadius(2),Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(255,126,115)),BorderThickness=new Thickness(2),Visibility=Visibility.Collapsed,IsHitTestVisible=false};imageCanvas.Children.Add(cropHandles[handleIndex]);}
            imageCanvas.SizeChanged+=delegate {
                editorImage.Width=imageCanvas.ActualWidth;editorImage.Height=imageCanvas.ActualHeight;
                if(imageCanvasLayoutQueued)return;imageCanvasLayoutQueued=true;
                imageCanvas.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(delegate{
                    imageCanvasLayoutQueued=false;if(cropModeActive)InitializeCropBox();else ResetCrop();LayoutImageTextLayer();LayoutPrecisionOverlay();LayoutPrecisionLayers();
                }));
            };
            imageCanvas.MouseLeftButtonDown+=CropMouseDown;
            imageCanvas.MouseMove+=CropMouseMove;
            imageCanvas.MouseLeftButtonUp+=CropMouseUp;
            var canvasFrame=new Border{Background=new SolidColorBrush(Color.FromRgb(17,24,39)),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),ClipToBounds=true,Child=imageCanvas};Grid.SetColumn(canvasFrame,1);body.Children.Add(canvasFrame);
            var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(18,0,0,0),Padding=new Thickness(0,0,6,0) }; var controls=new StackPanel();
            var commandStack=new StackPanel();
            var topButtons=new Grid{Margin=new Thickness(0,0,0,7)};topButtons.ColumnDefinitions.Add(new ColumnDefinition());topButtons.ColumnDefinitions.Add(new ColumnDefinition());
            var uploadImage=MakeButton("上传图片",Ui.Accent);uploadImage.Height=40;uploadImage.Margin=new Thickness(0,0,4,0);uploadImage.ToolTip="选择本地图片，导入 AI 图片编辑区并保存到中转袋";uploadImage.Click+=delegate{UploadImageToAiPocket();};topButtons.Children.Add(uploadImage);
            var downloadCanvas=MakeButton("下载当前画布",Ui.Card);downloadCanvas.Height=40;downloadCanvas.Margin=new Thickness(4,0,0,0);downloadCanvas.ToolTip="把画布上的当前图片（含图层合成结果）另存到任意位置";downloadCanvas.Click+=delegate{DownloadCurrentCanvasImage();};Grid.SetColumn(downloadCanvas,1);topButtons.Children.Add(downloadCanvas);
            commandStack.Children.Add(topButtons);
            commandStack.Children.Add(new TextBlock{Text="上传后可重绘、改字、裁切或精确编辑，结果可随时下载",Foreground=Ui.SubInk,FontSize=11.5,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,2,11)});
            imageFileInfo=new TextBlock{Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,2,2)};commandStack.Children.Add(imageFileInfo);
            imageAlphaInfo=new TextBlock{Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,2,5)};commandStack.Children.Add(imageAlphaInfo);
            var backgroundRow=new WrapPanel{Margin=new Thickness(0,0,0,8)};backgroundRow.Children.Add(new TextBlock{Text="透明预览",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,8,0)});canvasBackgroundBox=new ComboBox{Width=130,Height=30,ItemsSource=new[]{"棋盘格","白底","黑底"},SelectedIndex=0,Padding=new Thickness(7,3,7,3)};canvasBackgroundBox.SelectionChanged+=delegate{UpdateCanvasBackground();};backgroundRow.Children.Add(canvasBackgroundBox);commandStack.Children.Add(backgroundRow);
            // 四个工具页改为真正的等宽分段导航，文字在窄窗口中也不会被硬裁。
            var toolTabsGrid=new Grid();for(int tabColumn=0;tabColumn<3;tabColumn++)toolTabsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            localImageToolsTab=ImageToolTab("本地编辑");localImageToolsTab.ToolTip="裁切、尺寸与压缩";localImageToolsTab.Click+=delegate{ShowImageToolMode("local");};toolTabsGrid.Children.Add(localImageToolsTab);
            generateImageToolsTab=ImageToolTab("AI 创作");generateImageToolsTab.ToolTip="生图、参考图、局部编辑与图层";generateImageToolsTab.Click+=delegate{ShowImageToolMode("generate");};Grid.SetColumn(generateImageToolsTab,1);toolTabsGrid.Children.Add(generateImageToolsTab);
            textImageToolsTab=ImageToolTab("图片改字");textImageToolsTab.ToolTip="识别并替换图片文字";textImageToolsTab.Click+=delegate{ShowImageToolMode("text");};Grid.SetColumn(textImageToolsTab,2);toolTabsGrid.Children.Add(textImageToolsTab);
            precisionImageToolsTab=ImageToolTab("局部编辑");precisionImageToolsTab.ToolTip="点选、框选、标记与图层拆分";
            var toolTabs=new Border{Visibility=Visibility.Collapsed,Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11),Padding=new Thickness(3),Margin=new Thickness(0,0,0,9),Child=toolTabsGrid};commandStack.Children.Add(toolTabs);
            // 撤销 / 恢复 / 恢复原图对全部工具页生效：不满意 AI 修改时可一路退回，原图随时可找回。
            var historyBar=new Grid();historyBar.ColumnDefinitions.Add(new ColumnDefinition());historyBar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});historyBar.Children.Add(new TextBlock{Text="编辑记录",Foreground=Ui.SubInk,FontSize=11.5,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
            var historyActions=new StackPanel{Orientation=Orientation.Horizontal};
            imageUndoButton=MakeButton("↶ 撤销",Brushes.Transparent);imageUndoButton.ToolTip="撤销最近一步修改（裁剪、压缩、AI 重绘等）";imageUndoButton.Click+=delegate{UndoImageChange();};StyleImageHistoryButton(imageUndoButton);
            imageRedoButton=MakeButton("↷ 重做",Brushes.Transparent);imageRedoButton.ToolTip="恢复被撤销的那一步";imageRedoButton.Click+=delegate{RedoImageChange();};StyleImageHistoryButton(imageRedoButton);
            imageRestoreButton=MakeButton("⟲ 原图",Brushes.Transparent);imageRestoreButton.ToolTip="放弃全部修改，回到最初打开 / 生成的版本；之后仍可点“重做”找回";imageRestoreButton.Click+=delegate{RestoreOriginalImage();};StyleImageHistoryButton(imageRestoreButton);
            var compareOriginal=MakeButton("按住对比原图",Brushes.Transparent);compareOriginal.ToolTip="按住显示原图，松开立即回到当前结果；不会修改画布";compareOriginal.PreviewMouseLeftButtonDown+=delegate{ShowOriginalComparison(true);};compareOriginal.PreviewMouseLeftButtonUp+=delegate{ShowOriginalComparison(false);};compareOriginal.MouseLeave+=delegate{ShowOriginalComparison(false);};StyleImageHistoryButton(compareOriginal);
            historyActions.Children.Add(imageUndoButton);historyActions.Children.Add(imageRedoButton);historyActions.Children.Add(imageRestoreButton);historyActions.Children.Add(compareOriginal);Grid.SetColumn(historyActions,1);historyBar.Children.Add(historyActions);commandStack.Children.Add(historyBar);
            controls.Children.Add(new Border{Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(12),Margin=new Thickness(0,0,0,10),Child=commandStack});

            localImageToolsPanel=new StackPanel();localImageToolsPanel.Children.Add(SectionTitle("本地编辑"));
            var editModes=new WrapPanel();var enterCrop=MakeButton("✂ 裁切",new SolidColorBrush(Color.FromRgb(255,232,226)));enterCrop.Click+=delegate{EnterCropMode();};var selectText=MakeButton("重新识别图片文字",new SolidColorBrush(Color.FromRgb(235,229,222)));selectText.Click+=delegate{RunImageTextSelection();};editModes.Children.Add(enterCrop);editModes.Children.Add(selectText);localImageToolsPanel.Children.Add(editModes);
            localImageToolsPanel.Children.Add(new TextBlock { Text="裁切预设",FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,3) });
            cropPresetBox=new ComboBox { Height=32,Padding=new Thickness(7,4,7,4),ItemsSource=new[]{
                "自由裁剪","正方形 1:1","横图 4:3","竖图 3:4","宽屏 16:9","竖屏 9:16","照片 3:2","竖照 2:3",
                "头像 1080×1080","4K 横屏 3840×2160","2K 横屏 2560×1440","高清横屏 1920×1080","横屏 1280×720",
                "手机竖屏 1080×1920","社媒竖图 1242×1660","商务插图 750×460","小图 800×800"},SelectedIndex=0 };
            cropPresetBox.SelectionChanged+=delegate{customCropSizeActive=false;int presetWidth,presetHeight;if(widthBox!=null&&TryGetCropOutputSize(out presetWidth,out presetHeight)){widthBox.Text=presetWidth.ToString();heightBox.Text=presetHeight.ToString();}if(cropModeActive)InitializeCropBox();if(imageStatus!=null&&cropModeActive)imageStatus.Text="裁切框已按预设生成：可拖动框体移动，拖四角调整大小";};localImageToolsPanel.Children.Add(cropPresetBox);
            var cropActions=new WrapPanel(); var crop=MakeButton("确认裁剪",new SolidColorBrush(Color.FromRgb(255,126,115)));crop.Foreground=Brushes.White;crop.Click+=delegate{ApplyCrop();}; var cancelCrop=MakeButton("退出裁切",new SolidColorBrush(Color.FromRgb(235,229,222)));cancelCrop.Click+=delegate{ExitCropMode();imageStatus.Text="已退出裁切，回到浏览模式";}; var reset=MakeButton("恢复原图",Brushes.Transparent);reset.Click+=delegate{ResetWorkingImage();};cropActions.Children.Add(crop);cropActions.Children.Add(cancelCrop);cropActions.Children.Add(reset);localImageToolsPanel.Children.Add(cropActions);
            localImageToolsPanel.Children.Add(new TextBlock { Text="自定义裁切输出尺寸",FontWeight=FontWeights.Bold,Margin=new Thickness(0,7,0,3) });
            var sizeRow=new Grid { Margin=new Thickness(0,7,0,0) }; sizeRow.ColumnDefinitions.Add(new ColumnDefinition());sizeRow.ColumnDefinitions.Add(new ColumnDefinition());sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            widthBox=new TextBox { Height=32,Padding=new Thickness(7,4,7,4),ToolTip="输出宽度 px" };heightBox=new TextBox { Height=32,Padding=new Thickness(7,4,7,4),Margin=new Thickness(5,0,0,0),ToolTip="输出高度 px" };var customCrop=MakeButton("生成裁切框",new SolidColorBrush(Color.FromRgb(235,229,222)));customCrop.Height=32;customCrop.Click+=delegate{ApplyCustomCropSize();};
            sizeRow.Children.Add(widthBox);Grid.SetColumn(heightBox,1);sizeRow.Children.Add(heightBox);Grid.SetColumn(customCrop,2);sizeRow.Children.Add(customCrop);localImageToolsPanel.Children.Add(sizeRow);
            var compressRow=new Grid { Margin=new Thickness(0,7,0,0) };compressRow.ColumnDefinitions.Add(new ColumnDefinition());compressRow.ColumnDefinitions.Add(new ColumnDefinition());compressRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            outputFormatBox=new ComboBox { Height=32,ItemsSource=new[]{"PNG","JPEG"},SelectedIndex=Path.GetExtension(editingImageItem==null?"":editingImageItem.Value).ToLowerInvariant()==".png"?0:1,Padding=new Thickness(7,4,7,4),ToolTip="PNG 保留透明底并优化颜色；JPEG 更适合照片，通常压得更小" };
            outputFormatBox.SelectionChanged+=delegate{ScheduleCompressionEstimate();};
            targetKbBox=new TextBox { Height=32,Padding=new Thickness(7,4,7,4),Margin=new Thickness(5,0,0,0),ToolTip="目标大小 KB" };var compress=MakeButton("匹配目标",new SolidColorBrush(Color.FromRgb(235,229,222)));compress.Height=32;compress.Click+=delegate{CompressPreview();};
            compressRow.Children.Add(outputFormatBox);Grid.SetColumn(targetKbBox,1);compressRow.Children.Add(targetKbBox);Grid.SetColumn(compress,2);compressRow.Children.Add(compress);localImageToolsPanel.Children.Add(compressRow);
            compressionLevelText=new TextBlock { Text="压缩强度 30%（越高文件越小）",FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,2) };localImageToolsPanel.Children.Add(compressionLevelText);
            compressionSlider=new Slider { Minimum=0,Maximum=95,Value=30,TickFrequency=5,IsSnapToTickEnabled=false,ToolTip="类似在线压缩器：强度越高，文件通常越小；分辨率始终不变" };
            compressionSlider.ValueChanged+=delegate{if(compressionLevelText!=null)compressionLevelText.Text="压缩强度 "+Math.Round(compressionSlider.Value)+"%（越高文件越小）";ScheduleCompressionEstimate();};localImageToolsPanel.Children.Add(compressionSlider);
            compressionSizeText=new TextBlock { Text="预计输出：等待计算",Foreground=Ui.SubInk,Margin=new Thickness(0,3,0,4),TextWrapping=TextWrapping.Wrap };localImageToolsPanel.Children.Add(compressionSizeText);
            localImageToolsPanel.Children.Add(new TextBlock { Text="PNG 会保留 Alpha 透明通道；JPEG 适合照片。压缩只改变编码或颜色精度，绝不自动缩小宽高。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3) });
            var compressionActions=new WrapPanel();var confirmCompression=MakeButton("应用到当前图片",new SolidColorBrush(Color.FromRgb(84,163,112)));confirmCompression.Foreground=Brushes.White;confirmCompression.Click+=delegate{ConfirmCompressionAdjustment();};var saveCompression=MakeButton("另存压缩副本",new SolidColorBrush(Color.FromRgb(255,232,226)));saveCompression.Click+=delegate{SaveCompressionCopy();};var batchCompression=MakeButton("批量压缩为 ZIP",new SolidColorBrush(Color.FromRgb(235,229,222)));batchCompression.ToolTip="一次选择多张图片，本地压缩后打包；图片不会上传网络";batchCompression.Click+=delegate{BatchCompressToZip();};var lossless=MakeButton("PNG 像素无损",new SolidColorBrush(Color.FromRgb(235,229,222)));lossless.ToolTip="保持宽高和每个像素不变，仅重新编码 PNG";lossless.Click+=delegate{PrepareLosslessCompression();};compressionActions.Children.Add(confirmCompression);compressionActions.Children.Add(saveCompression);compressionActions.Children.Add(batchCompression);compressionActions.Children.Add(lossless);localImageToolsPanel.Children.Add(compressionActions);controls.Children.Add(localImageToolsPanel);
            compressionEstimateTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(260) };compressionEstimateTimer.Tick+=delegate{compressionEstimateTimer.Stop();BeginCompressionEstimate();};

            imageGenerateToolsPanel=new StackPanel();imageGenerateToolsPanel.Children.Add(SectionTitle("AI 生成 / 重绘"));
            var sourceRow=new Grid{Margin=new Thickness(0,0,0,5)};sourceRow.ColumnDefinitions.Add(new ColumnDefinition());sourceRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            imageProfileCombo=new ComboBox{Height=34,DisplayMemberPath="Name",Padding=new Thickness(8,4,8,4),ToolTip="选择已保存的 AI 来源；切换不会要求再次填写 Key"};imageProfileCombo.SelectionChanged+=delegate{SelectImageProfile(imageProfileCombo.SelectedItem as ImageProviderProfile);};sourceRow.Children.Add(imageProfileCombo);
            var aiSettings=MakeButton("管理来源",new SolidColorBrush(Color.FromRgb(235,229,222)));aiSettings.Margin=new Thickness(5,0,0,0);aiSettings.Click+=delegate{ShowImageAiSettings();};Grid.SetColumn(aiSettings,1);sourceRow.Children.Add(aiSettings);imageGenerateToolsPanel.Children.Add(sourceRow);
            var quickModelRow=new Grid{Margin=new Thickness(0,0,0,4)};quickModelRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});quickModelRow.ColumnDefinitions.Add(new ColumnDefinition());quickModelRow.Children.Add(new TextBlock{Text="模型",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,8,0)});
            var quickModel=new ComboBox{Height=34,IsEditable=true,Padding=new Thickness(8,4,8,4),ToolTip="此来源下的图像模型；可手动输入并保存到来源"};quickModel.SelectionChanged+=delegate{var profile=ActiveImageProfile();if(profile!=null&&quickModel.SelectedItem!=null){profile.Model=Convert.ToString(quickModel.SelectedItem);if(!profile.Models.Contains(profile.Model))profile.Models.Add(profile.Model);SaveImageAiConfig();UpdateImageProviderOptions();}};quickModel.LostKeyboardFocus+=delegate{var profile=ActiveImageProfile();if(profile!=null&&!String.IsNullOrWhiteSpace(quickModel.Text)){profile.Model=quickModel.Text.Trim();if(!profile.Models.Contains(profile.Model))profile.Models.Add(profile.Model);SaveImageAiConfig();UpdateImageProviderOptions();}};imageQuickModelCombo=quickModel;Grid.SetColumn(quickModel,1);quickModelRow.Children.Add(quickModel);imageGenerateToolsPanel.Children.Add(quickModelRow);
            imageProviderHint=new TextBlock{Foreground=Ui.SubInk,FontSize=11,Margin=new Thickness(3,6,3,3),TextWrapping=TextWrapping.Wrap};imageGenerateToolsPanel.Children.Add(imageProviderHint);
            officialImageOptionsPanel=new StackPanel();
            transparentBackgroundBox=new CheckBox { Content="透明底图（PNG · Alpha 通道）",IsChecked=imageAiConfig.TransparentBackground,Margin=new Thickness(3,9,3,2),FontWeight=FontWeights.SemiBold,ToolTip="适用于支持透明背景的图像模型；开启后输出固定为 PNG" };
            transparentBackgroundBox.Checked+=delegate{SetTransparentBackground(true);};transparentBackgroundBox.Unchecked+=delegate{SetTransparentBackground(false);};officialImageOptionsPanel.Children.Add(transparentBackgroundBox);
            officialImageOptionsPanel.Children.Add(new TextBlock { Text="透明底图会请求真正的 PNG Alpha 通道；模型不支持时会明确报错。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,0,3,3) });
            var sizeRowAi=new Grid { Margin=new Thickness(3,5,3,3) };sizeRowAi.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});sizeRowAi.ColumnDefinitions.Add(new ColumnDefinition());sizeRowAi.Children.Add(new TextBlock{Text="分辨率",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,9,0)});imageSizeBox=new ComboBox{Height=34,Padding=new Thickness(8,4,8,4),IsEditable=true,IsTextSearchEnabled=true,ItemsSource=new[]{"auto","1024x1024","1536x1024","1024x1536","2048x2048","2048x1152","3840x2160","2160x3840"},ToolTip="可选常用尺寸，也可输入宽x高；GPT Image 2 要求边长为 16 的倍数且最长不超过 3840"};imageSizeBox.Text=imageAiConfig.ImageSize;imageSizeBox.SelectionChanged+=delegate{SetImageSize(Convert.ToString(imageSizeBox.SelectedItem));};imageSizeBox.LostKeyboardFocus+=delegate{SetImageSize(imageSizeBox.Text);};Grid.SetColumn(imageSizeBox,1);sizeRowAi.Children.Add(imageSizeBox);officialImageOptionsPanel.Children.Add(sizeRowAi);
            var qualityRow=new Grid { Margin=new Thickness(3,5,3,3) };qualityRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});qualityRow.ColumnDefinitions.Add(new ColumnDefinition());qualityRow.Children.Add(new TextBlock{Text="清晰度",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,9,0)});imageQualityBox=new ComboBox{Height=34,Padding=new Thickness(8,4,8,4),ItemsSource=new[]{"auto","low","medium","high"},SelectedItem=imageAiConfig.ImageQuality,ToolTip="low 适合快速草图；medium 平衡；high 更清晰但通常更慢、费用更高"};imageQualityBox.SelectionChanged+=delegate{SetImageQuality(Convert.ToString(imageQualityBox.SelectedItem));};Grid.SetColumn(imageQualityBox,1);qualityRow.Children.Add(imageQualityBox);officialImageOptionsPanel.Children.Add(qualityRow);
            officialImageOptionsPanel.Children.Add(new TextBlock { Text="官方接口：分辨率控制像素尺寸，清晰度控制渲染档位。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,0,3,3) });imageGenerateToolsPanel.Children.Add(officialImageOptionsPanel);

            toApisImageOptionsPanel=new StackPanel();
            toApisTransparentBackgroundBox=new CheckBox { Content="透明底图（PNG · Alpha 通道，GPT Image 2 预览）",IsChecked=imageAiConfig.TransparentBackground,Margin=new Thickness(3,5,3,2),FontWeight=FontWeights.SemiBold,ToolTip="按新版 OpenAI Images API 透传 background=transparent 与 output_format=png；若 ToApis 上游尚未同步，将明确报错" };
            toApisTransparentBackgroundBox.Checked+=delegate{SetTransparentBackground(true);};toApisTransparentBackgroundBox.Unchecked+=delegate{SetTransparentBackground(false);};toApisImageOptionsPanel.Children.Add(toApisTransparentBackgroundBox);
            toApisImageOptionsPanel.Children.Add(new TextBlock { Text="仅 GPT Image 2 官方通道尝试原生 Alpha；会按 background=transparent、output_format=png 原样发送。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,0,3,3) });
            var aspectRow=new Grid{Margin=new Thickness(3,5,3,3)};aspectRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});aspectRow.ColumnDefinitions.Add(new ColumnDefinition());aspectRow.Children.Add(new TextBlock{Text="画面比例",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,9,0)});toApisAspectBox=new ComboBox{Height=34,Padding=new Thickness(8,4,8,4),ToolTip="选择自动时不发送 size / 画面比例，由当前模型决定"};toApisAspectBox.SelectionChanged+=delegate{if(toApisAspectBox.SelectedItem!=null){string selected=Convert.ToString(toApisAspectBox.SelectedItem);imageAiConfig.ToApisAspectRatio=selected.StartsWith("自动")?"auto":selected;SaveImageAiConfig();UpdateToApisCostPreview();}};Grid.SetColumn(toApisAspectBox,1);aspectRow.Children.Add(toApisAspectBox);toApisImageOptionsPanel.Children.Add(aspectRow);
            var resolutionRow=new Grid{Margin=new Thickness(3,5,3,3)};resolutionRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});resolutionRow.ColumnDefinitions.Add(new ColumnDefinition());resolutionRow.Children.Add(new TextBlock{Text="分辨率",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,9,0)});toApisResolutionBox=new ComboBox{Height=34,Padding=new Thickness(8,4,8,4),ItemsSource=new[]{"自动（模型默认）","1K","2K","4K"},ToolTip="选择自动时不发送 resolution，由当前模型决定"};toApisResolutionBox.SelectionChanged+=delegate{if(toApisResolutionBox.SelectedItem!=null){string selected=Convert.ToString(toApisResolutionBox.SelectedItem);imageAiConfig.ToApisResolution=selected.StartsWith("自动")?"auto":selected;UpdateToApisAspectOptions();SaveImageAiConfig();UpdateToApisCostPreview();}};Grid.SetColumn(toApisResolutionBox,1);resolutionRow.Children.Add(toApisResolutionBox);toApisImageOptionsPanel.Children.Add(resolutionRow);
            var toApisQualityGrid=new Grid{Margin=new Thickness(3,5,3,3)};toApisQualityGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});toApisQualityGrid.ColumnDefinitions.Add(new ColumnDefinition());toApisQualityGrid.Children.Add(new TextBlock{Text="图片质量",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,9,0)});toApisQualityBox=new ComboBox{Height=34,Padding=new Thickness(8,4,8,4),ItemsSource=new[]{"low","medium","high","auto"},SelectedItem=imageAiConfig.ToApisQuality};toApisQualityBox.SelectionChanged+=delegate{if(toApisQualityBox.SelectedItem!=null){imageAiConfig.ToApisQuality=Convert.ToString(toApisQualityBox.SelectedItem);SaveImageAiConfig();}};Grid.SetColumn(toApisQualityBox,1);toApisQualityGrid.Children.Add(toApisQualityBox);toApisQualityRow=toApisQualityGrid;toApisImageOptionsPanel.Children.Add(toApisQualityGrid);
            toApisCostText=new TextBlock{Foreground=Ui.AccentDeep,FontWeight=FontWeights.SemiBold,Margin=new Thickness(3,5,3,4),TextWrapping=TextWrapping.Wrap};toApisImageOptionsPanel.Children.Add(toApisCostText);imageGenerateToolsPanel.Children.Add(toApisImageOptionsPanel);

            imageGenerateToolsPanel.Children.Add(new TextBlock { Text="附加参考图（可选）",FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,3) });
            var referenceRow=new WrapPanel();
            var addReference=MakeButton("＋ 上传参考图",new SolidColorBrush(Color.FromRgb(255,232,226)));addReference.ToolTip="可多选；生成 / 重绘时会连同画布图一起作为视觉参考";addReference.Click+=delegate{UploadExtraReferenceImage();};
            var removeReference=MakeButton("移除所选",new SolidColorBrush(Color.FromRgb(235,229,222)));removeReference.Click+=delegate{RemoveSelectedExtraReference();};
            var clearReferences=MakeButton("清空",Brushes.Transparent);clearReferences.Click+=delegate{extraReferenceImages.Clear();RefreshExtraReferenceList();imageStatus.Text="已清空附加参考图";};
            referenceRow.Children.Add(addReference);referenceRow.Children.Add(removeReference);referenceRow.Children.Add(clearReferences);imageGenerateToolsPanel.Children.Add(referenceRow);
            extraReferenceList=new ListBox{Height=72,Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),ToolTip="附加参考图列表；生成时按列表顺序跟随画布图一起传入"};
            imageGenerateToolsPanel.Children.Add(extraReferenceList);
            extraReferenceHint=new TextBlock{Text="未添加；图生图将仅以画布图片为参考",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,3,3,0)};imageGenerateToolsPanel.Children.Add(extraReferenceHint);

            imageGenerateToolsPanel.Children.Add(new TextBlock { Text="生成 / 重绘提示词",FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,4) });
            redrawPromptBox=new TextBox { Height=86,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(9),VerticalContentAlignment=VerticalAlignment.Top,Text=imageAiConfig.RememberImagePrompt==true?(imageAiConfig.SavedImagePrompt??""):"" };imageGenerateToolsPanel.Children.Add(redrawPromptBox);
            rememberPromptBox=new CheckBox{Content="保留提示词，下次直接复用",IsChecked=imageAiConfig.RememberImagePrompt==true,Margin=new Thickness(3,6,3,3)};rememberPromptBox.Checked+=delegate{imageAiConfig.RememberImagePrompt=true;RememberCurrentImagePrompt();};rememberPromptBox.Unchecked+=delegate{imageAiConfig.RememberImagePrompt=false;imageAiConfig.SavedImagePrompt="";SaveImageAiConfig();};imageGenerateToolsPanel.Children.Add(rememberPromptBox);
            BuildPromptLibraryControls(imageGenerateToolsPanel,redrawPromptBox);
            var generateActions=new WrapPanel();var textToImage=MakeButton("文生图",new SolidColorBrush(Color.FromRgb(255,126,115)));textToImage.Foreground=Brushes.White;textToImage.Click+=delegate{RunImageGenerate(false);};var referenceImage=MakeButton("参考图生图",new SolidColorBrush(Color.FromRgb(245,225,219)));referenceImage.Click+=delegate{RunImageGenerate(true);};var redraw=MakeButton("重绘当前图",new SolidColorBrush(Color.FromRgb(235,229,222)));redraw.Click+=delegate{RunImageEdit(false);};generateActions.Children.Add(textToImage);generateActions.Children.Add(referenceImage);generateActions.Children.Add(redraw);imageGenerateToolsPanel.Children.Add(generateActions);controls.Children.Add(imageGenerateToolsPanel);

            imageTextToolsPanel=new StackPanel();imageTextToolsPanel.Children.Add(SectionTitle("图片改字"));var textSettings=MakeButton("⚙ 文本 / 图像模型设置",new SolidColorBrush(Color.FromRgb(235,229,222)));textSettings.Click+=delegate{ShowImageAiSettings();};imageTextToolsPanel.Children.Add(textSettings);
            imageTextToolsPanel.Children.Add(new TextBlock { Text="先识别文字，再修改内容并交给图像模型重绘。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,6,3,4) });
            imageTextToolsPanel.Children.Add(new TextBlock { Text="图片文字（可修改）",FontWeight=FontWeights.Bold,Margin=new Thickness(0,9,0,4) });
            recognizedTextBox=new TextBox { Height=180,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(9),VerticalContentAlignment=VerticalAlignment.Top };imageTextToolsPanel.Children.Add(recognizedTextBox);
            var textActions=new WrapPanel();var ocr=MakeButton("识别文字",new SolidColorBrush(Color.FromRgb(235,229,222)));ocr.Click+=delegate{RunImageOcr();};var replace=MakeButton("按文字改图",new SolidColorBrush(Color.FromRgb(255,126,115)));replace.Foreground=Brushes.White;replace.Click+=delegate{RunImageEdit(true);};textActions.Children.Add(ocr);textActions.Children.Add(replace);imageTextToolsPanel.Children.Add(textActions);controls.Children.Add(imageTextToolsPanel);
            precisionImageToolsPanel=new StackPanel{Margin=new Thickness(0,14,0,0)};precisionImageToolsPanel.Children.Add(new Separator{Margin=new Thickness(0,4,0,12)});precisionImageToolsPanel.Children.Add(SectionTitle("局部编辑与图层"));
            var precisionSettings=MakeButton("⚙ 管理图像来源",new SolidColorBrush(Color.FromRgb(235,229,222)));precisionSettings.Click+=delegate{ShowImageAiSettings();};precisionImageToolsPanel.Children.Add(precisionSettings);
            precisionImageToolsPanel.Children.Add(new TextBlock{Text="点选和框选会写入通用坐标提示；套索、涂鸦、箭头会烘焙进参考图。局部编辑使用当前图像来源；图层拆分只会在来源明确声明支持时提交。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,6,3,4)});
            precisionModeCombo=new ComboBox{Height=34,Padding=new Thickness(8,4,8,4),ItemsSource=new[]{"点选（<point>）","框选（<bbox>）","套索标记","涂鸦标记","箭头标记"},SelectedIndex=0};precisionModeCombo.SelectionChanged+=delegate{SetPrecisionModeFromUi();};precisionImageToolsPanel.Children.Add(precisionModeCombo);
            var precisionOptions=new WrapPanel{Margin=new Thickness(0,5,0,2)};precisionSizeBox=new ComboBox{Width=112,Height=34,Padding=new Thickness(8,4,8,4),ItemsSource=new[]{"自动","1K","2K"},SelectedItem="自动",ToolTip="自动时不额外限制当前模型的尺寸参数"};precisionOutputFormatBox=new ComboBox{Width=98,Height=34,Padding=new Thickness(8,4,8,4),Margin=new Thickness(5,0,0,0),ItemsSource=new[]{"PNG","JPEG"},SelectedItem="PNG",ToolTip="输出格式"};precisionOptimizeBox=new ComboBox{Width=110,Height=34,Padding=new Thickness(8,4,8,4),Margin=new Thickness(5,0,0,0),ItemsSource=new[]{"标准","快速"},SelectedItem="标准",ToolTip="仅支持该选项的接口会接收它"};precisionOptions.Children.Add(precisionSizeBox);precisionOptions.Children.Add(precisionOutputFormatBox);precisionOptions.Children.Add(precisionOptimizeBox);precisionImageToolsPanel.Children.Add(precisionOptions);
            precisionTransparentBackgroundBox=new CheckBox{Content="透明通道（PNG）",Margin=new Thickness(3,2,0,3),ToolTip="官方 background: transparent；模型不支持时会明确返回接口错误，不会伪造白底图"};precisionImageToolsPanel.Children.Add(precisionTransparentBackgroundBox);
            precisionPromptBox=new TextBox{Height=92,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(9),VerticalContentAlignment=VerticalAlignment.Top,Text="请仅修改我标记的区域，其余内容、构图和风格保持不变。"};precisionImageToolsPanel.Children.Add(precisionPromptBox);
            BuildPromptLibraryControls(precisionImageToolsPanel,precisionPromptBox);
            var precisionActions=new WrapPanel();var precisionEdit=MakeButton("开始局部编辑",new SolidColorBrush(Color.FromRgb(255,126,115)));precisionEdit.Foreground=Brushes.White;precisionEdit.Click+=delegate{BeginPrecisionMarking();};var submitPrecisionEdit=MakeButton("提交局部编辑",new SolidColorBrush(Color.FromRgb(84,163,112)));submitPrecisionEdit.Foreground=Brushes.White;submitPrecisionEdit.Click+=delegate{RunPrecisionImageRequest(false);};var layerSplit=MakeButton("拆分图层",new SolidColorBrush(Color.FromRgb(84,163,112)));layerSplit.Foreground=Brushes.White;layerSplit.ToolTip="需要在来源设置中勾选“支持原生图层拆分”";layerSplit.Click+=delegate{RunPrecisionImageRequest(true);};var clearMarks=MakeButton("清除标记",new SolidColorBrush(Color.FromRgb(235,229,222)));clearMarks.Click+=delegate{ClearPrecisionMarks();};precisionActions.Children.Add(precisionEdit);precisionActions.Children.Add(submitPrecisionEdit);precisionActions.Children.Add(layerSplit);precisionActions.Children.Add(clearMarks);precisionImageToolsPanel.Children.Add(precisionActions);
            precisionResultBox=new ComboBox{Height=34,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,8,0,0),Visibility=Visibility.Collapsed};precisionResultBox.SelectionChanged+=delegate{ShowPrecisionResult();};precisionImageToolsPanel.Children.Add(precisionResultBox);
            precisionImageToolsPanel.Children.Add(new TextBlock{Text="图层（拖动画布元素移动位置）",FontWeight=FontWeights.Bold,Margin=new Thickness(0,9,0,3)});
            precisionLayerList=new ListBox{Height=172,Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Visibility=Visibility.Collapsed,ItemTemplate=BuildPrecisionLayerItemTemplate()};precisionLayerList.SelectionChanged+=delegate{if(precisionLayerList.SelectedItem is PrecisionLayer){selectedPrecisionLayer=(PrecisionLayer)precisionLayerList.SelectedItem;SyncSelectedLayerScale();LayoutPrecisionLayers();}};precisionImageToolsPanel.Children.Add(precisionLayerList);
            var layerActions=new WrapPanel();var layerUp=MakeButton("上移",Ui.Neutral);layerUp.Click+=delegate{MoveSelectedPrecisionLayer(1);};var layerDown=MakeButton("下移",Ui.Neutral);layerDown.Click+=delegate{MoveSelectedPrecisionLayer(-1);};var layerVisible=MakeButton("显示 / 隐藏",Ui.Neutral);layerVisible.Click+=delegate{ToggleSelectedPrecisionLayer();};layerActions.Children.Add(layerUp);layerActions.Children.Add(layerDown);layerActions.Children.Add(layerVisible);precisionImageToolsPanel.Children.Add(layerActions);
            var layerExportActions=new WrapPanel{Margin=new Thickness(0,4,0,0)};var restoreLayerImage=MakeButton("恢复拆分前原图",Ui.Neutral);restoreLayerImage.ToolTip="放弃当前图层合成，恢复开始拆分前正在编辑的图片";restoreLayerImage.Click+=delegate{RestorePreLayerSplitImage();};var downloadLayer=MakeButton("下载选中图层 PNG",Ui.Neutral);downloadLayer.ToolTip="按原始 PNG 导出选中的图层，保留透明通道";downloadLayer.Click+=delegate{ExportSelectedPrecisionLayerPng();};var exportLayersZip=MakeButton("导出 ZIP 图层包",new SolidColorBrush(Color.FromRgb(84,163,112)));exportLayersZip.Foreground=Brushes.White;exportLayersZip.Click+=delegate{ExportPrecisionLayersZip();};var exportLayersPsd=MakeButton("导出 PSD 图层",new SolidColorBrush(Color.FromRgb(84,163,112)));exportLayersPsd.Foreground=Brushes.White;exportLayersPsd.Click+=delegate{ExportPrecisionLayersPsd();};layerExportActions.Children.Add(restoreLayerImage);layerExportActions.Children.Add(downloadLayer);layerExportActions.Children.Add(exportLayersZip);layerExportActions.Children.Add(exportLayersPsd);precisionImageToolsPanel.Children.Add(layerExportActions);
            var layerScaleRow=new Grid{Margin=new Thickness(2,7,2,1)};layerScaleRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});layerScaleRow.ColumnDefinitions.Add(new ColumnDefinition());layerScaleRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});layerScaleRow.Children.Add(new TextBlock{Text="图层缩放",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});precisionLayerScale=new Slider{Minimum=25,Maximum=250,Value=100,SmallChange=1,LargeChange=10,IsSnapToTickEnabled=false,VerticalAlignment=VerticalAlignment.Center};precisionLayerScale.ValueChanged+=delegate{ApplySelectedLayerScale();};Grid.SetColumn(precisionLayerScale,1);layerScaleRow.Children.Add(precisionLayerScale);precisionLayerScaleText=new TextBlock{Text="100%",Width=46,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};Grid.SetColumn(precisionLayerScaleText,2);layerScaleRow.Children.Add(precisionLayerScaleText);precisionImageToolsPanel.Children.Add(layerScaleRow);
            var layerZoomActions=new WrapPanel();var zoomOut=MakeButton("− 缩小",Ui.Neutral);zoomOut.Click+=delegate{NudgeSelectedLayerScale(-10);};var zoomIn=MakeButton("＋ 放大",Ui.Neutral);zoomIn.Click+=delegate{NudgeSelectedLayerScale(10);};var resetLayer=MakeButton("重置大小",Ui.Neutral);resetLayer.Click+=delegate{if(selectedPrecisionLayer!=null){precisionLayerScale.Value=100;}};layerZoomActions.Children.Add(zoomOut);layerZoomActions.Children.Add(zoomIn);layerZoomActions.Children.Add(resetLayer);precisionImageToolsPanel.Children.Add(layerZoomActions);
            precisionStatus=new TextBlock{Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,6,3,3),Text="先在左侧图片上选择一个位置或绘制标记。"};precisionImageToolsPanel.Children.Add(precisionStatus);controls.Children.Add(precisionImageToolsPanel);
            var confirm=MakeButton("保存到中转袋 / 覆盖原图",new SolidColorBrush(Color.FromRgb(84,163,112)));confirm.Foreground=Brushes.White;confirm.Margin=new Thickness(3,12,3,3);confirm.Click+=delegate{ConfirmImageOverwrite();};controls.Children.Add(confirm);
            scroll.Content=controls;Grid.SetColumn(scroll,2);body.Children.Add(scroll);Grid.SetRow(body,1);rootGrid.Children.Add(body);
            var taskBorder=new Border{Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(12),Margin=new Thickness(0,10,0,0)};var taskRow=new Grid();taskRow.ColumnDefinitions.Add(new ColumnDefinition());taskRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var taskText=new StackPanel();taskText.Children.Add(new TextBlock{Text="任务结果",FontWeight=FontWeights.Bold,FontSize=12});imageTaskSummary=new TextBlock{Text="尚未提交图像任务",Foreground=Ui.SubInk,FontSize=11,Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.Wrap};taskText.Children.Add(imageTaskSummary);taskRow.Children.Add(taskText);imageRetryButton=MakeButton("重试原任务",Ui.Neutral);imageRetryButton.Height=32;imageRetryButton.IsEnabled=false;imageRetryButton.Click+=delegate{RetryLastImageTask();};Grid.SetColumn(imageRetryButton,1);taskRow.Children.Add(imageRetryButton);taskBorder.Child=taskRow;Grid.SetRow(taskBorder,2);rootGrid.Children.Add(taskBorder);
            imageStatus=new TextBox { IsReadOnly=true,IsReadOnlyCaretVisible=false,BorderThickness=new Thickness(0),Background=Brushes.Transparent,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,0),Foreground=Ui.SubInk };Grid.SetRow(imageStatus,3);rootGrid.Children.Add(imageStatus);
            imagePocketContent=body;aiPocketChatContent=BuildAiPocketChatContent();Grid.SetRow(aiPocketChatContent,1);aiPocketChatContent.Visibility=Visibility.Collapsed;aiPocketChatContent.IsVisibleChanged+=delegate{SetAiPocketNavigation(aiPocketChatContent.IsVisible);};rootGrid.Children.Add(aiPocketChatContent);
            outer.Child=rootGrid;imageEditorPanel.Content=outer;
            SetAiPocketNavigation(false);ShowImageToolMode("local");SyncImageModelCombos();RefreshPromptLibraryBoxes();UpdateImageHistoryButtons();
            imageEditorPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){RememberCurrentImagePrompt();if(!exiting){e.Cancel=true;imageEditorPanel.Hide();}};
        }

        Button ImageHeaderTab(string text)
        {
            var button=MakeButton(text,Brushes.Transparent);button.Height=34;button.MinWidth=92;button.Padding=new Thickness(15,5,15,5);button.Margin=new Thickness(0);button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;return button;
        }

        Button ImageToolTab(string text)
        {
            var button=MakeButton(text,Brushes.Transparent);button.Height=36;button.MinWidth=0;button.Padding=new Thickness(4,5,4,5);button.Margin=new Thickness(1);button.FontSize=12.5;button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;return button;
        }

        void StyleImageNavigationTab(Button button,bool active)
        {
            if(button==null)return;button.Background=active?Ui.Card:Brushes.Transparent;button.Foreground=active?Ui.AccentDeep:Ui.SubInk;button.BorderBrush=active?Ui.Line:Brushes.Transparent;button.BorderThickness=active?new Thickness(1):new Thickness(0);button.FontWeight=active?FontWeights.SemiBold:FontWeights.Medium;
        }

        void SetAiPocketNavigation(bool chatActive)
        {
            StyleImageNavigationTab(aiPocketChatTab,chatActive);StyleImageNavigationTab(aiPocketImageTab,!chatActive);
        }

        void StyleImageHistoryButton(Button button)
        {
            button.Height=30;button.MinWidth=0;button.Padding=new Thickness(8,4,8,4);button.Margin=new Thickness(1,0,0,0);button.FontSize=12;button.Foreground=Ui.SubInk;
        }

        TextBlock SectionTitle(string text){return new TextBlock{Text=text,FontSize=14,FontWeight=FontWeights.Bold,Foreground=Ui.AccentDeep,Margin=new Thickness(0,9,0,5)};}

        void UploadImageToAiPocket()
        {
            if(imageAiBusy){if(imageStatus!=null)imageStatus.Text="当前图像任务仍在进行，请稍候";return;}
            var picker=new Microsoft.Win32.OpenFileDialog{Title="上传图片到博道咪 AI 口袋",Filter="图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有文件|*.*",Multiselect=false,CheckFileExists=true};
            if(picker.ShowDialog()!=true)return;
            try
            {
                string source=picker.FileName;
                if(!IsImageFile(source))throw new InvalidOperationException("请选择图片文件");
                string saved=CopyFileIntoStash(source);
                var item=new StashItem{Id=Guid.NewGuid().ToString("N"),Kind="image",Name=Path.GetFileName(source),Value=saved,Owned=true,Created=DateTime.Now.ToString("o"),SourceApp="AI 口袋上传"};
                if(!AddStashItemSmart(item))item=stashItems.FirstOrDefault(x=>x.ContentHash==item.ContentHash)??item;SaveStash();RefreshStash();OpenImageEditor(item);
            }
            catch(Exception ex){if(imageStatus!=null)imageStatus.Text="图片上传失败："+ex.Message;}
        }

        // ===== 画布下载与附加参考图 =====

        void DownloadCurrentCanvasImage()
        {
            if(workingBitmap==null){imageStatus.Text="画布上还没有图片";return;}
            try
            {
                string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG",extension=format=="JPEG"?".jpg":".png";
                BitmapSource saveBitmap=layerCompositionActive?RenderPrecisionLayerComposition():workingBitmap;
                byte[] data=(!layerCompositionActive&&workingEncodedBytes!=null)?workingEncodedBytes:EncodeBitmap(saveBitmap,format,92);
                string baseName=editingImageItem==null||String.IsNullOrWhiteSpace(editingImageItem.Name)?"AI图片-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"):Path.GetFileNameWithoutExtension(editingImageItem.Name);
                var dialog=new Microsoft.Win32.SaveFileDialog{Title="下载画布图片",Filter=format=="JPEG"?"JPEG 图片 (*.jpg)|*.jpg":"PNG 图片 (*.png)|*.png",DefaultExt=extension,FileName=baseName+extension};
                if(dialog.ShowDialog()!=true)return;
                File.WriteAllBytes(dialog.FileName,data);
                imageStatus.Text=String.Format("画布图已下载：{0} × {1} px · {2:0.0} KB · {3}",saveBitmap.PixelWidth,saveBitmap.PixelHeight,data.Length/1024.0,dialog.FileName);
            }
            catch(Exception ex){imageStatus.Text="下载画布图失败："+ex.Message;}
        }

        void UploadExtraReferenceImage()
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Title="上传附加参考图（可多选）",Filter="图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",Multiselect=true,CheckFileExists=true};
            if(picker.ShowDialog()!=true)return;
            int added=0;
            foreach(string file in picker.FileNames)
            {
                try
                {
                    if(!IsImageFile(file))continue;
                    string target=Path.Combine(imageTempDir,"ref-"+Guid.NewGuid().ToString("N")+Path.GetExtension(file).ToLowerInvariant());
                    File.Copy(file,target,true);
                    extraReferenceImages.Add(target);added++;
                }
                catch{}
            }
            RefreshExtraReferenceList();
            imageStatus.Text=added>0?("已添加 "+added+" 张附加参考图；参考图生图 / 重绘时会连同画布图一起传入"):"没有可用的图片被添加";
        }

        void RefreshExtraReferenceList()
        {
            if(extraReferenceList==null)return;
            extraReferenceList.ItemsSource=extraReferenceImages.Select(Path.GetFileName).ToList();
            if(extraReferenceHint!=null)extraReferenceHint.Text=extraReferenceImages.Count>0
                ?("已附加 "+extraReferenceImages.Count+" 张参考图；画布图将作为第一参考传入")
                :"未添加；图生图将仅以画布图片为参考";
        }

        void RemoveSelectedExtraReference()
        {
            int index=extraReferenceList==null?-1:extraReferenceList.SelectedIndex;
            if(index<0||index>=extraReferenceImages.Count){imageStatus.Text="请先在列表中选中要移除的参考图";return;}
            extraReferenceImages.RemoveAt(index);RefreshExtraReferenceList();imageStatus.Text="已移除该参考图";
        }

        // 参考图生图允许没有画布图：只要上传了附加参考图即可发起。

        void ShowImageToolMode(string mode)
        {
            imageToolMode=mode;if(localImageToolsPanel==null)return;if(mode!="local")ExitCropMode();
            if(mode!="precision"){precisionMarkingActive=false;ClearPrecisionMarks();}
            localImageToolsPanel.Visibility=mode=="local"?Visibility.Visible:Visibility.Collapsed;imageGenerateToolsPanel.Visibility=(mode=="generate"||mode=="precision")?Visibility.Visible:Visibility.Collapsed;imageTextToolsPanel.Visibility=mode=="text"?Visibility.Visible:Visibility.Collapsed;precisionImageToolsPanel.Visibility=(mode=="generate"||mode=="precision")?Visibility.Visible:Visibility.Collapsed;
            Brush active=Ui.AccentSoft,inactive=Ui.Neutral;localImageToolsTab.Background=mode=="local"?active:inactive;generateImageToolsTab.Background=(mode=="generate"||mode=="precision")?active:inactive;textImageToolsTab.Background=mode=="text"?active:inactive;
            if(mode=="precision"){precisionMarkingActive=false;ClearImageTextLayer();SetPrecisionModeFromUi();if(imageStatus!=null)imageStatus.Text="请选择标记方式，点击“开始局部编辑”后才会进入图片标记状态。";}
        }

        void PositionImageEditor(){if(manuallyPlacedWindows.Contains(imageEditorPanel)||shelvedWindows.ContainsKey(imageEditorPanel)||imageEditorPanel.WindowState!=WindowState.Normal)return;var work=SystemParameters.WorkArea;imageEditorPanel.Left=Math.Max(work.Left+8,work.Left+(work.Width-imageEditorPanel.Width)/2);imageEditorPanel.Top=Math.Max(work.Top+8,work.Top+(work.Height-imageEditorPanel.Height)/2);}

        void SetImageEditorTopmost(bool value){if(imageEditorPanel!=null)imageEditorPanel.Topmost=value;if(imageAiSettingsPanel!=null)imageAiSettingsPanel.Topmost=value;if(precisionAiSettingsPanel!=null)precisionAiSettingsPanel.Topmost=value;}

        void CloseImageEditorWindows(){try{if(imageAiProcess!=null&&!imageAiProcess.HasExited)imageAiProcess.Kill();}catch{}try{if(localOcrProcess!=null&&!localOcrProcess.HasExited)localOcrProcess.Kill();}catch{}if(precisionAiSettingsPanel!=null)precisionAiSettingsPanel.Close();if(imageAiSettingsPanel!=null)imageAiSettingsPanel.Close();if(imageEditorPanel!=null)imageEditorPanel.Close();}
}
}
