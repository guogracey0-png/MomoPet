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
    public class ImageTextRegion { public string Text; public double X,Y,Width,Height; }
    public class PrecisionLayer
    {
        public string Name { get; set; } public string Path { get; set; } public BitmapSource Bitmap { get; set; }
        public double X { get; set; } public double Y { get; set; } public double Width { get; set; } public double Height { get; set; } public double NaturalWidth { get; set; } public double NaturalHeight { get; set; }
        public int Z { get; set; } public bool Visible { get; set; } public Image Visual { get; set; }
        public string SizeLabel { get { return Math.Round(Width)+" × "+Math.Round(Height)+" · "+Math.Round(NaturalWidth>0?Width/NaturalWidth*100:100)+"%"; } }
        public PrecisionLayer(){Visible=true;}
    }
    public class PsdExportLayer
    {
        public string Name { get; set; } public BitmapSource Bitmap { get; set; } public bool Visible { get; set; }
    }
    public class ImageTextCache { public string Text { get; set; } public List<ImageTextRegion> Regions { get; set; } }
    public class ImageAiConfig
    {
        public string TextProvider { get; set; }
        public string TextBaseUrl { get; set; }
        public string TextModel { get; set; }
        public List<string> TextModels { get; set; }
        public string ToApisTextBaseUrl { get; set; }
        public string ToApisTextModel { get; set; }
        public List<string> ToApisTextModels { get; set; }
        public string ImageProvider { get; set; }
        public string ImageBaseUrl { get; set; }
        public string ImageModel { get; set; }
        public List<string> ImageModels { get; set; }
        public string ToApisImageBaseUrl { get; set; }
        public string ToApisImageModel { get; set; }
        public List<string> ToApisImageModels { get; set; }
        public bool TransparentBackground { get; set; }
        public string ImageQuality { get; set; }
        public string ImageSize { get; set; }
        public string ToApisAspectRatio { get; set; }
        public string ToApisResolution { get; set; }
        public string ToApisQuality { get; set; }
        // 精确局部编辑 / 图层拆分拥有独立的模型通道，避免覆盖日常生图设置。
        public string PrecisionBaseUrl { get; set; }
        public string PrecisionModel { get; set; }
        public List<string> PrecisionModels { get; set; }
        public bool? RememberImagePrompt { get; set; }
        public string SavedImagePrompt { get; set; }
        public List<string> PromptLibrary { get; set; }
        // A provider is a saved connection, not a hard-coded vendor.  One saved
        // source can expose many models and is selected at generation time.
        public List<ImageProviderProfile> ImageProfiles { get; set; }
        public string ActiveImageProfileId { get; set; }
        public List<ImageProviderProfile> TextProfiles { get; set; }
        public string ActiveTextProfileId { get; set; }
    }
    public class ImageProviderProfile
    {
        public string Id { get; set; } public string Name { get; set; }
        public string BaseUrl { get; set; } public string Model { get; set; }
        public List<string> Models { get; set; }
        // auto tries the broadly supported OpenAI Images contract first, then
        // the chat-image fallback. ark is only needed for native layer split.
        public string Protocol { get; set; }
        public bool SupportsEditing { get; set; } public bool SupportsLayers { get; set; }
        public bool SupportsTransparency { get; set; }
        public override string ToString(){return String.IsNullOrWhiteSpace(Name)?"未命名来源":Name;}
    }

    // AI 图片的撤销 / 恢复历史：每完成一步会改变图片的操作就记录一个快照。
    public class ImageHistoryEntry { public BitmapSource Bitmap; public byte[] Encoded; }

    public partial class PetController
    {
        string imageAiConfigFile,imageTextKeyFile,imageModelKeyFile,toApisTextKeyFile,toApisImageKeyFile,precisionImageKeyFile,imageBackupDir,imageTempDir,imageOcrCacheDir;
        Window imageEditorPanel,imageAiSettingsPanel,precisionAiSettingsPanel;
        Canvas imageCanvas,imageTextLayer,precisionOverlay,precisionLayerCanvas; Image editorImage; Border cropBox,precisionBoxOverlay,layerSelectionBox; Border[] cropHandles,layerResizeHandles; bool imageCanvasLayoutQueued;
        TextBox imageStatus,widthBox,heightBox,targetKbBox,redrawPromptBox,recognizedTextBox,precisionPromptBox,precisionBaseBox;
        TextBlock compressionSizeText,compressionLevelText,imageProviderHint,toApisCostText,precisionStatus,imageFileInfo,imageAlphaInfo,imageTaskSummary;
        TextBlock imageSettingsStatus,precisionSettingsStatus;
        Slider compressionSlider;
        TextBox imageTextBaseBox,imageModelBaseBox,imageProfileNameBox,textProfileNameBox;
        PasswordBox imageTextKeyBox,imageModelKeyBox,precisionKeyBox;
        ComboBox outputFormatBox,imageTextModelCombo,imageModelCombo,imageQuickModelCombo,imageProfileCombo,imageProfileSettingsCombo,textProfileSettingsCombo,imageProtocolCombo,canvasBackgroundBox,cropPresetBox,imageQualityBox,imageSizeBox,toApisAspectBox,toApisResolutionBox,toApisQualityBox,precisionModelCombo,precisionModeCombo,precisionSizeBox,precisionOutputFormatBox,precisionOptimizeBox,precisionResultBox;
        ListBox precisionLayerList; Slider precisionLayerScale; TextBlock precisionLayerScaleText;
        CheckBox transparentBackgroundBox,toApisTransparentBackgroundBox,precisionTransparentBackgroundBox,rememberPromptBox,imageProfileEditBox,imageProfileLayerBox,imageProfileTransparentBox;
        StackPanel localImageToolsPanel,imageGenerateToolsPanel,imageTextToolsPanel,precisionImageToolsPanel,officialImageOptionsPanel,toApisImageOptionsPanel;
        FrameworkElement toApisQualityRow;
        Button aiPocketChatTab,aiPocketImageTab,localImageToolsTab,generateImageToolsTab,textImageToolsTab,precisionImageToolsTab;
        string settingsTextProvider="官方 API",settingsImageProvider="官方 API",imageToolMode="local";
        BitmapSource originalBitmap,workingBitmap,preLayerSplitBitmap;
        StashItem editingImageItem;
        Point cropStart,cropDragStart,cropResizeAnchor; Rect cropRect,cropDragInitial,precisionSelectionRect; string cropDragMode,precisionMode=""; bool cropSelecting,imageAiBusy,customCropSizeActive,cropModeActive,precisionSelecting,precisionMarkingActive,imageOriginalPreviewActive,imageProfileSettingsLoading,textProfileSettingsLoading; int customCropOutputWidth,customCropOutputHeight;
        Point precisionPoint,layerDragStart,layerResizeAnchor; readonly List<Point> precisionPath=new List<Point>(); readonly List<string> precisionResultPaths=new List<string>();readonly List<PrecisionLayer> precisionLayers=new List<PrecisionLayer>();PrecisionLayer selectedPrecisionLayer;bool layerDragging,layerResizing,layerCompositionActive;string layerResizeMode;double layerDragX,layerDragY;
        readonly List<ImageTextRegion> imageTextRegions=new List<ImageTextRegion>();
        readonly List<ImageTextRegion> orderedImageTextRegions=new List<ImageTextRegion>();
        readonly Queue<string> imageOcrPrecacheQueue=new Queue<string>();bool imageOcrPrecacheRunning;
        int imageTextSelectionStart=-1,imageTextSelectionEnd=-1;bool imageTextSelecting;
        byte[] workingEncodedBytes,compressionCandidateBytes,preLayerSplitEncodedBytes; BitmapSource compressionCandidateBitmap; Process imageAiProcess,localOcrProcess;
        DispatcherTimer compressionEstimateTimer;int compressionEstimateVersion;
        ImageAiConfig imageAiConfig=new ImageAiConfig { TextModels=new List<string>(),ImageModels=new List<string>() };
        readonly List<ImageHistoryEntry> imageHistory=new List<ImageHistoryEntry>();int imageHistoryIndex=-1;
        Button imageUndoButton,imageRedoButton,imageRestoreButton,imageRetryButton;
        readonly List<ComboBox> promptLibraryBoxes=new List<ComboBox>();
        readonly List<string> extraReferenceImages=new List<string>();
        ListBox extraReferenceList;TextBlock extraReferenceHint;
        Dictionary<string,object> lastImageTaskRequest;string lastImageTaskOutput;bool lastImageTaskLayers;readonly List<string> lastImageTaskInputs=new List<string>();

        void InitializeImageEditorPaths()
        {
            imageAiConfigFile=Path.Combine(dataDir,"image-ai-settings.json");
            imageTextKeyFile=Path.Combine(dataDir,"image-text-key.dat");
            imageModelKeyFile=Path.Combine(dataDir,"image-model-key.dat");
            toApisTextKeyFile=Path.Combine(dataDir,"toapis-text-key.dat");
            toApisImageKeyFile=Path.Combine(dataDir,"toapis-image-key.dat");
            precisionImageKeyFile=Path.Combine(dataDir,"precision-image-key.dat");
            imageBackupDir=Path.Combine(dataDir,"ImageBackups");
            imageTempDir=Path.Combine(dataDir,"ImageAiTemp");
            imageOcrCacheDir=Path.Combine(dataDir,"ImageOcrCache");
            try { Directory.CreateDirectory(imageBackupDir); Directory.CreateDirectory(imageTempDir); Directory.CreateDirectory(imageOcrCacheDir); } catch { }
            try { if(File.Exists(imageAiConfigFile)) imageAiConfig=json.Deserialize<ImageAiConfig>(File.ReadAllText(imageAiConfigFile,Encoding.UTF8)); } catch { imageAiConfig=null; }
            if(imageAiConfig==null) imageAiConfig=new ImageAiConfig();
            if(imageAiConfig.TextModels==null) imageAiConfig.TextModels=new List<string>();
            if(imageAiConfig.ImageModels==null) imageAiConfig.ImageModels=new List<string>();
            if(imageAiConfig.ToApisTextModels==null) imageAiConfig.ToApisTextModels=new List<string>();
            if(imageAiConfig.ToApisImageModels==null) imageAiConfig.ToApisImageModels=new List<string>();
            if(imageAiConfig.PrecisionModels==null) imageAiConfig.PrecisionModels=new List<string>();
            if(imageAiConfig.PromptLibrary==null) imageAiConfig.PromptLibrary=new List<string>();
            if(imageAiConfig.ImageProfiles==null) imageAiConfig.ImageProfiles=new List<ImageProviderProfile>();
            if(imageAiConfig.TextProfiles==null) imageAiConfig.TextProfiles=new List<ImageProviderProfile>();
            if(String.IsNullOrWhiteSpace(imageAiConfig.TextProvider)) imageAiConfig.TextProvider="官方 API";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ImageProvider)) imageAiConfig.ImageProvider="官方 API";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisTextBaseUrl)) imageAiConfig.ToApisTextBaseUrl="https://toapis.com/v1";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisImageBaseUrl)) imageAiConfig.ToApisImageBaseUrl="https://toapis.com/v1";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ImageQuality)) imageAiConfig.ImageQuality="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ImageSize)) imageAiConfig.ImageSize="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisAspectRatio)) imageAiConfig.ToApisAspectRatio="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisResolution)) imageAiConfig.ToApisResolution="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisQuality)) imageAiConfig.ToApisQuality="low";
            // 新建精确编辑配置默认直连火山方舟；已有 ToApis 配置不自动改写，避免覆盖用户现有通道。
            if(String.IsNullOrWhiteSpace(imageAiConfig.PrecisionBaseUrl)) imageAiConfig.PrecisionBaseUrl="https://ark.cn-beijing.volces.com/api/v3";
            if(String.IsNullOrWhiteSpace(imageAiConfig.PrecisionModel)) imageAiConfig.PrecisionModel="doubao-seedream-5-0-pro-260628";
            if(!imageAiConfig.PrecisionModels.Contains(imageAiConfig.PrecisionModel)) imageAiConfig.PrecisionModels.Add(imageAiConfig.PrecisionModel);
            if(!imageAiConfig.RememberImagePrompt.HasValue) imageAiConfig.RememberImagePrompt=true;
            // 兼容上一版错误拆分的“官方 / ToApis”配置：迁回一套文本、一套图像，之后只按 Base URL 自动识别。
            if(String.Equals(imageAiConfig.TextProvider,"ToApis",StringComparison.OrdinalIgnoreCase)){
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisTextBaseUrl))imageAiConfig.TextBaseUrl=imageAiConfig.ToApisTextBaseUrl;
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisTextModel))imageAiConfig.TextModel=imageAiConfig.ToApisTextModel;
                if(imageAiConfig.ToApisTextModels.Count>0)imageAiConfig.TextModels=imageAiConfig.ToApisTextModels.ToList();
                string migrated=LoadImageAiKey(true,true);if(!String.IsNullOrWhiteSpace(migrated))SaveImageAiKey(true,migrated,false);
            }
            if(String.Equals(imageAiConfig.ImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase)){
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisImageBaseUrl))imageAiConfig.ImageBaseUrl=imageAiConfig.ToApisImageBaseUrl;
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisImageModel))imageAiConfig.ImageModel=imageAiConfig.ToApisImageModel;
                if(imageAiConfig.ToApisImageModels.Count>0)imageAiConfig.ImageModels=imageAiConfig.ToApisImageModels.ToList();
                string migrated=LoadImageAiKey(false,true);if(!String.IsNullOrWhiteSpace(migrated))SaveImageAiKey(false,migrated,false);
            }
            imageAiConfig.TextProvider="自动";imageAiConfig.ImageProvider="自动";SaveImageAiConfig();
            MigrateImageProfiles();
            MigrateTextProfiles();
        }

        void MigrateImageProfiles()
        {
            if(imageAiConfig.ImageProfiles==null)imageAiConfig.ImageProfiles=new List<ImageProviderProfile>();
            if(imageAiConfig.ImageProfiles.Count==0){
                var legacy=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name=UrlUsesToApis(imageAiConfig.ImageBaseUrl)?"我的 ToApis":"默认图像来源",BaseUrl=imageAiConfig.ImageBaseUrl??"",Model=imageAiConfig.ImageModel??"",Models=(imageAiConfig.ImageModels??new List<string>()).ToList(),Protocol="auto",SupportsEditing=true,SupportsTransparency=true};
                imageAiConfig.ImageProfiles.Add(legacy);imageAiConfig.ActiveImageProfileId=legacy.Id;
                string legacyKey=LoadImageAiKey(false);if(!String.IsNullOrWhiteSpace(legacyKey))SaveProfileKey(legacy.Id,legacyKey);
            }
            foreach(var profile in imageAiConfig.ImageProfiles){if(String.IsNullOrWhiteSpace(profile.Id))profile.Id=Guid.NewGuid().ToString("N");if(profile.Models==null)profile.Models=new List<string>();if(String.IsNullOrWhiteSpace(profile.Protocol))profile.Protocol="auto";if(!profile.SupportsEditing)profile.SupportsEditing=true;}
            if(ActiveImageProfile()==null)imageAiConfig.ActiveImageProfileId=imageAiConfig.ImageProfiles[0].Id;
            SaveImageAiConfig();
        }
        ImageProviderProfile ActiveImageProfile(){return imageAiConfig==null||imageAiConfig.ImageProfiles==null?null:imageAiConfig.ImageProfiles.FirstOrDefault(x=>x.Id==imageAiConfig.ActiveImageProfileId);}
        string ProfileKeyFile(string id){return Path.Combine(dataDir,"image-profile-"+id+".key");}
        string LoadProfileKey(string id){try{string file=ProfileKeyFile(id);return File.Exists(file)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file),ImageKeyEntropy("Profile."+id),DataProtectionScope.CurrentUser)):null;}catch{return null;}}
        void SaveProfileKey(string id,string key){byte[] plain=Encoding.UTF8.GetBytes(key);try{File.WriteAllBytes(ProfileKeyFile(id),ProtectedData.Protect(plain,ImageKeyEntropy("Profile."+id),DataProtectionScope.CurrentUser));}finally{Array.Clear(plain,0,plain.Length);}}
        string LoadActiveImageKey(){var profile=ActiveImageProfile();return profile==null?LoadImageAiKey(false):(LoadProfileKey(profile.Id)??LoadImageAiKey(false));}
        void MigrateTextProfiles()
        {
            if(imageAiConfig.TextProfiles==null)imageAiConfig.TextProfiles=new List<ImageProviderProfile>();
            if(imageAiConfig.TextProfiles.Count==0){var legacy=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name=UrlUsesToApis(imageAiConfig.TextBaseUrl)?"我的 ToApis 文本":"默认文本来源",BaseUrl=imageAiConfig.TextBaseUrl??"",Model=imageAiConfig.TextModel??"",Models=(imageAiConfig.TextModels??new List<string>()).ToList(),Protocol="auto"};imageAiConfig.TextProfiles.Add(legacy);imageAiConfig.ActiveTextProfileId=legacy.Id;string key=LoadImageAiKey(true);if(!String.IsNullOrWhiteSpace(key))SaveProfileKey(legacy.Id,key);}
            foreach(var profile in imageAiConfig.TextProfiles){if(String.IsNullOrWhiteSpace(profile.Id))profile.Id=Guid.NewGuid().ToString("N");if(profile.Models==null)profile.Models=new List<string>();if(String.IsNullOrWhiteSpace(profile.Protocol))profile.Protocol="auto";}
            if(ActiveTextProfile()==null)imageAiConfig.ActiveTextProfileId=imageAiConfig.TextProfiles[0].Id;SaveImageAiConfig();
        }
        ImageProviderProfile ActiveTextProfile(){return imageAiConfig==null||imageAiConfig.TextProfiles==null?null:imageAiConfig.TextProfiles.FirstOrDefault(x=>x.Id==imageAiConfig.ActiveTextProfileId);}
        string LoadActiveTextKey(){var profile=ActiveTextProfile();return profile==null?LoadImageAiKey(true):(LoadProfileKey(profile.Id)??LoadImageAiKey(true));}
        void SelectTextProfile(ImageProviderProfile profile){if(profile==null)return;imageAiConfig.ActiveTextProfileId=profile.Id;imageAiConfig.TextBaseUrl=profile.BaseUrl;imageAiConfig.TextModel=profile.Model;imageAiConfig.TextModels=profile.Models??new List<string>();SaveImageAiConfig();SyncImageModelCombos();}

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

        BitmapSource LoadEditorBitmap(string path)
        {
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.UriSource=new Uri(path); image.EndInit(); image.Freeze(); return image;
        }

        BitmapSource LoadEditorBytes(byte[] bytes)
        {
            using(var stream=new MemoryStream(bytes)) { var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream; image.EndInit(); image.Freeze(); return image; }
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
        void TrackImageTask(Dictionary<string,object> request,bool layers)
        {
            lastImageTaskRequest=new Dictionary<string,object>(request);lastImageTaskLayers=layers;lastImageTaskOutput=request.ContainsKey("output_path")?Convert.ToString(request["output_path"]):null;lastImageTaskInputs.Clear();
            if(request.ContainsKey("image_path"))lastImageTaskInputs.Add(Convert.ToString(request["image_path"]));if(request.ContainsKey("reference_paths")){var paths=request["reference_paths"] as System.Collections.IEnumerable;if(paths!=null)foreach(object path in paths)lastImageTaskInputs.Add(Convert.ToString(path));}
            if(imageRetryButton!=null)imageRetryButton.IsEnabled=false;if(imageTaskSummary!=null)imageTaskSummary.Text="任务进行中：可继续查看画布；完成后结果会保留在这里。";
        }
        void FinishImageTask(bool success,string message)
        {
            if(imageTaskSummary!=null)imageTaskSummary.Text=(success?"任务完成：":"任务失败：")+message;if(imageRetryButton!=null)imageRetryButton.IsEnabled=!success&&lastImageTaskRequest!=null;
            if(success){foreach(string input in lastImageTaskInputs.Where(x=>x!=null&&x.StartsWith(imageTempDir,StringComparison.OrdinalIgnoreCase)).ToList())DeleteTemporaryImageFile(input);lastImageTaskInputs.Clear();}
        }
        void RetryLastImageTask()
        {
            if(lastImageTaskRequest==null){imageStatus.Text="没有可重试的图像任务";return;}if(imageAiBusy)return;
            foreach(string input in lastImageTaskInputs)if(!String.IsNullOrWhiteSpace(input)&&!File.Exists(input)){imageStatus.Text="原始输入已不在临时工作区，无法安全重试；请重新提交。";return;}
            imageAiBusy=true;if(imageRetryButton!=null)imageRetryButton.IsEnabled=false;imageStatus.Text="正在按原请求重试，不会改变图片尺寸或内容…";string mode=Convert.ToString(lastImageTaskRequest["mode"]);
            RunImageHelper(lastImageTaskRequest,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(mode=="precision")HandlePrecisionImageResponse(response,lastImageTaskLayers);else HandleGeneratedImageResponse(response,lastImageTaskOutput);});
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
        void RunImageGenerate(bool useReference)
        {
            if(imageAiBusy||!ValidateImageAi(false))return;string prompt=(redrawPromptBox.Text??"").Trim();if(String.IsNullOrEmpty(prompt)){imageStatus.Text="请先填写生成提示词";return;}
            if(useReference&&workingBitmap==null&&extraReferenceImages.Count==0){imageStatus.Text="参考图生图需要画布上有图片，或先上传附加参考图";return;}
            RememberCurrentImagePrompt();bool toApis=ImageUsesToApis();imageAiBusy=true;string output=Path.Combine(imageTempDir,"result-"+Guid.NewGuid().ToString("N")+".png"),input=null;string aspect=toApis?imageAiConfig.ToApisAspectRatio:NearestImageAiAspect(workingBitmap),resolution=toApis?imageAiConfig.ToApisResolution:ImageAiResolution(aspect),quality=toApis?imageAiConfig.ToApisQuality:imageAiConfig.ImageQuality;var request=new Dictionary<string,object>{{"provider","auto"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"output_path",output},{"prompt",prompt},{"aspect_ratio",aspect},{"resolution",resolution},{"size",toApis?"auto":imageAiConfig.ImageSize},{"transparent_background",imageAiConfig.TransparentBackground},{"quality",quality}};
            if(useReference){
                var referencePaths=new List<string>();
                if(workingBitmap!=null){input=PrepareWorkingImageFile();referencePaths.Add(input);}
                referencePaths.AddRange(extraReferenceImages);
                request["mode"]="edit";request["image_path"]=referencePaths[0];
                if(referencePaths.Count>1)request["reference_paths"]=referencePaths.Skip(1).ToArray();
                request["prompt"]="以输入图片作为主要视觉参考，在保留关键主体特征的基础上生成新图片。用户要求：\n"+prompt;
            }else request["mode"]="generate";
            imageStatus.Text=(useReference?(workingBitmap!=null?"正在参考画布图片"+(extraReferenceImages.Count>0?"和 "+extraReferenceImages.Count+" 张附加参考图":"")+"生成新图":"正在参考 "+extraReferenceImages.Count+" 张附加参考图生成新图"):"正在根据文字生成图片")+(toApis?(" · ToApis · "+aspect+" · "+resolution+" · "+ToApisCostShortText()):(" · "+imageAiConfig.ImageSize+" · 清晰度 "+quality+(imageAiConfig.TransparentBackground?" · 透明 PNG":"")))+"…";
            TrackImageTask(request,false);RunImageHelper(request,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(input!=null&&response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandleGeneratedImageResponse(response,output);});
        }

        DataTemplate BuildPrecisionLayerItemTemplate()
        {
            var template=new DataTemplate(typeof(PrecisionLayer));var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);row.SetValue(FrameworkElement.MarginProperty,new Thickness(5,3,5,3));var thumbnail=new FrameworkElementFactory(typeof(Border));thumbnail.SetValue(FrameworkElement.WidthProperty,46.0);thumbnail.SetValue(FrameworkElement.HeightProperty,40.0);thumbnail.SetValue(Border.BackgroundProperty,Ui.Inner);thumbnail.SetValue(Border.BorderBrushProperty,Ui.Line);thumbnail.SetValue(Border.BorderThicknessProperty,new Thickness(1));var image=new FrameworkElementFactory(typeof(Image));image.SetValue(Image.StretchProperty,Stretch.Uniform);image.SetBinding(Image.SourceProperty,new Binding("Bitmap"));thumbnail.AppendChild(image);row.AppendChild(thumbnail);var details=new FrameworkElementFactory(typeof(StackPanel));details.SetValue(FrameworkElement.WidthProperty,178.0);details.SetValue(StackPanel.VerticalAlignmentProperty,VerticalAlignment.Center);details.SetValue(FrameworkElement.MarginProperty,new Thickness(7,0,0,0));var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Name"));name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);details.AppendChild(name);var size=new FrameworkElementFactory(typeof(TextBlock));size.SetBinding(TextBlock.TextProperty,new Binding("SizeLabel"));size.SetValue(TextBlock.FontSizeProperty,10.0);size.SetValue(TextBlock.ForegroundProperty,Ui.SubInk);details.AppendChild(size);row.AppendChild(details);template.VisualTree=row;return template;
        }
        void ShowImageToolMode(string mode)
        {
            imageToolMode=mode;if(localImageToolsPanel==null)return;if(mode!="local")ExitCropMode();
            if(mode!="precision"){precisionMarkingActive=false;ClearPrecisionMarks();}
            localImageToolsPanel.Visibility=mode=="local"?Visibility.Visible:Visibility.Collapsed;imageGenerateToolsPanel.Visibility=(mode=="generate"||mode=="precision")?Visibility.Visible:Visibility.Collapsed;imageTextToolsPanel.Visibility=mode=="text"?Visibility.Visible:Visibility.Collapsed;precisionImageToolsPanel.Visibility=(mode=="generate"||mode=="precision")?Visibility.Visible:Visibility.Collapsed;
            Brush active=Ui.AccentSoft,inactive=Ui.Neutral;localImageToolsTab.Background=mode=="local"?active:inactive;generateImageToolsTab.Background=(mode=="generate"||mode=="precision")?active:inactive;textImageToolsTab.Background=mode=="text"?active:inactive;
            if(mode=="precision"){precisionMarkingActive=false;ClearImageTextLayer();SetPrecisionModeFromUi();if(imageStatus!=null)imageStatus.Text="请选择标记方式，点击“开始局部编辑”后才会进入图片标记状态。";}
        }
        void RememberCurrentImagePrompt(){if(imageAiConfig==null||redrawPromptBox==null||imageAiConfig.RememberImagePrompt!=true)return;imageAiConfig.SavedImagePrompt=redrawPromptBox.Text??"";SaveImageAiConfig();}
        // ToApis 大陆官方域名是 toapis.cn，海外是 toapis.com；必须一并识别，
        // 否则大陆用户会被当作普通 OpenAI 兼容接口，图生图发错端点必然失败。
        bool UrlUsesToApis(string value){try{string host=new Uri((value??"").Trim()).Host;return System.Text.RegularExpressions.Regex.IsMatch(host,@"(^|\.)toapis\.(com|cn|xyz)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);}catch{return false;}}
        bool TextUsesToApis(){return UrlUsesToApis(imageAiConfig.TextBaseUrl);}
        bool ImageUsesToApis(){var profile=ActiveImageProfile();return UrlUsesToApis(profile==null?imageAiConfig.ImageBaseUrl:profile.BaseUrl);}
        string ActiveTextBaseUrl(){var profile=ActiveTextProfile();return profile==null?imageAiConfig.TextBaseUrl:profile.BaseUrl;}
        string ActiveTextModel(){var profile=ActiveTextProfile();return profile==null?imageAiConfig.TextModel:profile.Model;}
        List<string> ActiveTextModels(){var profile=ActiveTextProfile();return profile==null?imageAiConfig.TextModels:profile.Models;}
        string ActiveImageBaseUrl(){var profile=ActiveImageProfile();return profile==null?imageAiConfig.ImageBaseUrl:profile.BaseUrl;}
        string ActiveImageModel(){var profile=ActiveImageProfile();return profile==null?imageAiConfig.ImageModel:profile.Model;}
        List<string> ActiveImageModels(){var profile=ActiveImageProfile();return profile==null?imageAiConfig.ImageModels:profile.Models;}
        string ActiveImageProtocol(){var profile=ActiveImageProfile();return profile==null?"auto":profile.Protocol??"auto";}
        bool ActiveImageSupportsLayers(){var profile=ActiveImageProfile();return profile!=null&&profile.SupportsLayers;}
        bool ActiveImageSupportsTransparency(){var profile=ActiveImageProfile();return profile==null||profile.SupportsTransparency;}
        void SelectImageProfile(ImageProviderProfile profile)
        {
            if(profile==null)return;imageAiConfig.ActiveImageProfileId=profile.Id;imageAiConfig.ImageBaseUrl=profile.BaseUrl;imageAiConfig.ImageModel=profile.Model;imageAiConfig.ImageModels=profile.Models??new List<string>();SaveImageAiConfig();
            if(imageProfileCombo!=null&&imageProfileCombo.SelectedItem!=profile)imageProfileCombo.SelectedItem=profile;
            if(imageQuickModelCombo!=null){imageQuickModelCombo.ItemsSource=null;imageQuickModelCombo.ItemsSource=profile.Models;imageQuickModelCombo.Text=profile.Model??"";}UpdateImageProviderOptions();
        }
        void UpdateImageProviderOptions()
        {
            if(officialImageOptionsPanel==null)return;bool toApis=ImageUsesToApis();officialImageOptionsPanel.Visibility=toApis?Visibility.Collapsed:Visibility.Visible;toApisImageOptionsPanel.Visibility=toApis?Visibility.Visible:Visibility.Collapsed;
            var active=ActiveImageProfile();string source=active==null?"默认来源":active.Name;
            imageProviderHint.Text="当前来源："+source+" · "+(ActiveImageProtocol()=="ark"?"火山方舟图像协议":toApis?"OpenAI 兼容图像接口":"自动兼容模式（Images → Chat 图片回退）")+(ActiveImageSupportsTransparency()?" · 可请求透明 PNG":" · 未声明透明底能力");
            if(toApis){toApisResolutionBox.SelectedItem=imageAiConfig.ToApisResolution=="auto"?"自动（模型默认）":imageAiConfig.ToApisResolution;toApisQualityBox.SelectedItem=imageAiConfig.ToApisQuality;UpdateToApisAspectOptions();UpdateToApisCostPreview();}
        }
        void UpdateToApisAspectOptions()
        {
            // 比例是用户的构图意图，不应由前端按 1K/2K/4K 擅自裁掉。
            // 兼容来源若不接受某个组合，会由请求层回退到模型默认尺寸。
            if(toApisAspectBox==null)return;string automatic="自动（模型默认）";string[] values=new[]{automatic,"1:1","3:2","2:3","4:3","3:4","5:4","4:5","16:9","9:16","2:1","1:2","21:9","9:21"};
            string selected=imageAiConfig.ToApisAspectRatio??"auto";string display=selected=="auto"?automatic:selected;if(!values.Contains(display)){selected="auto";display=automatic;}imageAiConfig.ToApisAspectRatio=selected;toApisAspectBox.ItemsSource=values;toApisAspectBox.SelectedItem=display;string model=ActiveImageModel()??"";toApisQualityRow.Visibility=(model.IndexOf("official",StringComparison.OrdinalIgnoreCase)>=0||model.IndexOf("vip",StringComparison.OrdinalIgnoreCase)>=0)?Visibility.Visible:Visibility.Collapsed;
        }
        void UpdateToApisCostPreview()
        {
            if(toApisCostText==null)return;string model=(ActiveImageModel()??"").Trim();string resolution=imageAiConfig.ToApisResolution??"auto";if(resolution=="auto"){toApisCostText.Text="自动尺寸：不指定比例和分辨率，由当前模型决定 · 积分以 ToApis 实际计费为准";}else if(String.Equals(model,"gpt-image-2",StringComparison.OrdinalIgnoreCase)){int credits=resolution=="4K"?5:resolution=="2K"?4:3;toApisCostText.Text="预计消耗："+credits+" 积分 / 张 · 提交前提醒，最终以 ToApis 账户计费为准";}else toApisCostText.Text="预计积分：当前模型请以 ToApis 账户价格为准";
        }
        void SetTransparentBackground(bool enabled){imageAiConfig.TransparentBackground=enabled;if(transparentBackgroundBox!=null&&transparentBackgroundBox.IsChecked!=enabled)transparentBackgroundBox.IsChecked=enabled;if(toApisTransparentBackgroundBox!=null&&toApisTransparentBackgroundBox.IsChecked!=enabled)toApisTransparentBackgroundBox.IsChecked=enabled;SaveImageAiConfig();if(enabled&&outputFormatBox!=null)outputFormatBox.SelectedItem="PNG";if(imageStatus!=null)imageStatus.Text=enabled?"透明底图已开启：将请求 background=transparent 与 PNG Alpha 通道":"透明底图已关闭";}
        void SetImageQuality(string quality){string normalized=(quality??"auto").Trim().ToLowerInvariant();if(normalized!="low"&&normalized!="medium"&&normalized!="high")normalized="auto";imageAiConfig.ImageQuality=normalized;SaveImageAiConfig();if(imageStatus!=null)imageStatus.Text="图像生成质量已设为 "+normalized;}
        void SetImageSize(string size){string normalized=(size??"auto").Trim().ToLowerInvariant().Replace('×','x').Replace(" ","");if(normalized=="auto"){imageAiConfig.ImageSize="auto";SaveImageAiConfig();if(imageStatus!=null)imageStatus.Text="图像分辨率已设为自动";return;}string[] parts=normalized.Split('x');int width,height;if(parts.Length!=2||!Int32.TryParse(parts[0],out width)||!Int32.TryParse(parts[1],out height)||width%16!=0||height%16!=0||Math.Max(width,height)>3840||Math.Max(width,height)>Math.Min(width,height)*3||(long)width*height<655360||(long)width*height>8294400){if(imageStatus!=null)imageStatus.Text="分辨率无效：宽高须为 16 的倍数，最长边不超过 3840，比例不超过 3:1，总像素需在官方范围内";if(imageSizeBox!=null)imageSizeBox.Text=imageAiConfig.ImageSize??"auto";return;}imageAiConfig.ImageSize=width+"x"+height;SaveImageAiConfig();if(imageStatus!=null)imageStatus.Text="图像分辨率已设为 "+imageAiConfig.ImageSize;}
        void PositionImageEditor(){if(manuallyPlacedWindows.Contains(imageEditorPanel)||shelvedWindows.ContainsKey(imageEditorPanel)||imageEditorPanel.WindowState!=WindowState.Normal)return;var work=SystemParameters.WorkArea;imageEditorPanel.Left=Math.Max(work.Left+8,work.Left+(work.Width-imageEditorPanel.Width)/2);imageEditorPanel.Top=Math.Max(work.Top+8,work.Top+(work.Height-imageEditorPanel.Height)/2);}
        void EnterCropMode(){if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}ClearImageTextLayer();cropModeActive=true;imageCanvas.Cursor=Cursors.Cross;InitializeCropBox();imageStatus.Text="裁切模式：拖动框体移动，拖四角缩放；完成后点击“确认裁剪”";}
        void ExitCropMode(){cropModeActive=false;cropSelecting=false;if(imageCanvas!=null){imageCanvas.ReleaseMouseCapture();imageCanvas.Cursor=Cursors.Arrow;}ResetCrop();}
        void ShowCrop()
        {
            if(cropBox==null||cropRect.IsEmpty)return;cropBox.Visibility=Visibility.Visible;Canvas.SetLeft(cropBox,cropRect.X);Canvas.SetTop(cropBox,cropRect.Y);cropBox.Width=cropRect.Width;cropBox.Height=cropRect.Height;
            if(cropHandles==null)return;double half=5.5;Point[] corners={new Point(cropRect.Left,cropRect.Top),new Point(cropRect.Right,cropRect.Top),new Point(cropRect.Right,cropRect.Bottom),new Point(cropRect.Left,cropRect.Bottom)};
            for(int i=0;i<cropHandles.Length;i++){cropHandles[i].Visibility=Visibility.Visible;Canvas.SetLeft(cropHandles[i],corners[i].X-half);Canvas.SetTop(cropHandles[i],corners[i].Y-half);}
        }

        void ResetCrop()
        {
            cropRect=Rect.Empty;if(cropBox!=null)cropBox.Visibility=Visibility.Collapsed;if(cropHandles!=null)foreach(var handle in cropHandles)handle.Visibility=Visibility.Collapsed;
        }

        void InitializeCropBox()
        {
            if(!cropModeActive){ResetCrop();return;}
            Rect bounds=RenderedImageRect();if(bounds.IsEmpty){ResetCrop();return;}double ratio=SelectedCropRatio(),width=bounds.Width*.72,height=bounds.Height*.72;
            if(ratio>0){if(width/height>ratio)width=height*ratio;else height=width/ratio;}
            width=Math.Max(24,Math.Min(width,bounds.Width));height=Math.Max(24,Math.Min(height,bounds.Height));
            cropRect=new Rect(bounds.Left+(bounds.Width-width)/2,bounds.Top+(bounds.Height-height)/2,width,height);ShowCrop();
        }

        string CropHitMode(Point point)
        {
            if(cropRect.IsEmpty)return "create";double hit=13;
            if(Math.Abs(point.X-cropRect.Left)<=hit&&Math.Abs(point.Y-cropRect.Top)<=hit)return "nw";
            if(Math.Abs(point.X-cropRect.Right)<=hit&&Math.Abs(point.Y-cropRect.Top)<=hit)return "ne";
            if(Math.Abs(point.X-cropRect.Right)<=hit&&Math.Abs(point.Y-cropRect.Bottom)<=hit)return "se";
            if(Math.Abs(point.X-cropRect.Left)<=hit&&Math.Abs(point.Y-cropRect.Bottom)<=hit)return "sw";
            return cropRect.Contains(point)?"move":"create";
        }

        void CropMouseDown(object sender,MouseButtonEventArgs e)
        {
            if(layerCompositionActive&&TryStartLayerDrag(e))return;
            if(imageToolMode=="precision"){PrecisionMouseDown(e);return;}
            if(!cropModeActive)return;
            Rect bounds=RenderedImageRect();if(bounds.IsEmpty)return;Point point=ClampCropPoint(e.GetPosition(imageCanvas),bounds);cropDragMode=CropHitMode(point);cropSelecting=true;cropDragStart=point;cropDragInitial=cropRect;
            if(cropDragMode=="nw")cropResizeAnchor=new Point(cropRect.Right,cropRect.Bottom);else if(cropDragMode=="ne")cropResizeAnchor=new Point(cropRect.Left,cropRect.Bottom);else if(cropDragMode=="se")cropResizeAnchor=new Point(cropRect.Left,cropRect.Top);else if(cropDragMode=="sw")cropResizeAnchor=new Point(cropRect.Right,cropRect.Top);else if(cropDragMode=="create"){cropStart=point;cropRect=new Rect(point,point);}
            imageCanvas.CaptureMouse();e.Handled=true;
        }

        void CropMouseMove(object sender,MouseEventArgs e)
        {
            if(layerResizing){ResizePrecisionLayer(e);return;}
            if(layerDragging){MovePrecisionLayer(e);return;}
            if(imageToolMode=="precision"){PrecisionMouseMove(e);return;}
            if(!cropModeActive){imageCanvas.Cursor=Cursors.Arrow;return;}
            Point point=e.GetPosition(imageCanvas);if(!cropSelecting){string hover=CropHitMode(point);imageCanvas.Cursor=hover=="move"?Cursors.SizeAll:(hover=="nw"||hover=="se"?Cursors.SizeNWSE:(hover=="ne"||hover=="sw"?Cursors.SizeNESW:Cursors.Cross));return;}
            Rect bounds=RenderedImageRect();point=ClampCropPoint(point,bounds);
            if(cropDragMode=="move"){
                double x=cropDragInitial.X+point.X-cropDragStart.X,y=cropDragInitial.Y+point.Y-cropDragStart.Y;x=Math.Max(bounds.Left,Math.Min(x,bounds.Right-cropDragInitial.Width));y=Math.Max(bounds.Top,Math.Min(y,bounds.Bottom-cropDragInitial.Height));cropRect=new Rect(x,y,cropDragInitial.Width,cropDragInitial.Height);
            }else if(cropDragMode=="create"){cropRect=CreateCropRect(point);}else{cropStart=cropResizeAnchor;cropRect=CreateCropRect(point);}
            if(cropRect.Width>=2&&cropRect.Height>=2)ShowCrop();
        }

        void CropMouseUp(object sender,MouseButtonEventArgs e)
        {
            if(layerResizing){FinishLayerResize(e);return;}
            if(layerDragging){FinishLayerDrag(e);return;}
            if(imageToolMode=="precision"){PrecisionMouseUp(e);return;}
            if(!cropModeActive)return;
            if(!cropSelecting)return;cropSelecting=false;imageCanvas.ReleaseMouseCapture();if(cropRect.Width<12||cropRect.Height<12)InitializeCropBox();e.Handled=true;
        }

        void SetPrecisionModeFromUi()
        {
            if(precisionModeCombo==null)return;string[] modes={"point","bbox","lasso","doodle","arrow"};int index=Math.Max(0,Math.Min(modes.Length-1,precisionModeCombo.SelectedIndex));precisionMode=modes[index];ClearPrecisionMarks();
            if(imageCanvas!=null)imageCanvas.Cursor=precisionMarkingActive?(precisionMode=="point"?Cursors.Cross:Cursors.Pen):Cursors.Arrow;
            if(precisionStatus!=null)precisionStatus.Text=precisionMarkingActive?(precisionMode=="point"?"请在图片上单击需要修改的位置。":precisionMode=="bbox"?"请在图片上拖出需要修改的矩形区域。":"请在图片上绘制标记；完成后点击“提交精确编辑”。"):"已选择标记方式；点击“开始精确编辑”后再在图片上标记。";
        }

        void BeginPrecisionMarking()
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}if(imageToolMode!="precision")ShowImageToolMode("precision");precisionMarkingActive=true;ClearPrecisionMarks();SetPrecisionModeFromUi();imageStatus.Text="已进入局部编辑标记状态；完成标记后点击“提交局部编辑”。";
        }

        void ClearPrecisionMarks()
        {
            precisionSelecting=false;precisionSelectionRect=Rect.Empty;precisionPoint=new Point(Double.NaN,Double.NaN);precisionPath.Clear();
            if(imageCanvas!=null)imageCanvas.ReleaseMouseCapture();LayoutPrecisionOverlay();
        }

        Point ClampPrecisionPoint(Point point)
        {
            Rect bounds=RenderedImageRect();return bounds.IsEmpty?point:new Point(Math.Max(bounds.Left,Math.Min(point.X,bounds.Right)),Math.Max(bounds.Top,Math.Min(point.Y,bounds.Bottom)));
        }

        void PrecisionMouseDown(MouseButtonEventArgs e)
        {
            if(!precisionMarkingActive)return;
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}Rect bounds=RenderedImageRect();Point point=ClampPrecisionPoint(e.GetPosition(imageCanvas));if(bounds.IsEmpty||!bounds.Contains(point))return;
            ClearImageTextLayer();precisionSelecting=true;
            if(precisionMode=="point"){precisionPoint=point;precisionPath.Clear();precisionSelecting=false;precisionMarkingActive=false;imageCanvas.Cursor=Cursors.Arrow;imageStatus.Text="已标记点选坐标 "+PrecisionPointTag()+"；点击“提交精确编辑”发送。";}
            else if(precisionMode=="bbox"){precisionPoint=point;precisionSelectionRect=new Rect(point,point);}
            else {precisionPath.Clear();precisionPath.Add(point);}
            if(precisionSelecting)imageCanvas.CaptureMouse();LayoutPrecisionOverlay();e.Handled=true;
        }

        void PrecisionMouseMove(MouseEventArgs e)
        {
            if(!precisionSelecting)return;Point point=ClampPrecisionPoint(e.GetPosition(imageCanvas));if(precisionMode=="bbox")precisionSelectionRect=new Rect(precisionPoint,point);else if(precisionMode!="point"&&(precisionPath.Count==0||DistanceSquared(precisionPath[precisionPath.Count-1],point)>3))precisionPath.Add(point);LayoutPrecisionOverlay();e.Handled=true;
        }

        double DistanceSquared(Point a,Point b){double x=a.X-b.X,y=a.Y-b.Y;return x*x+y*y;}

        void PrecisionMouseUp(MouseButtonEventArgs e)
        {
            if(!precisionSelecting)return;precisionSelecting=false;imageCanvas.ReleaseMouseCapture();if(precisionMode=="bbox"&&(precisionSelectionRect.Width<5||precisionSelectionRect.Height<5))precisionSelectionRect=Rect.Empty;LayoutPrecisionOverlay();
            precisionMarkingActive=false;imageCanvas.Cursor=Cursors.Arrow;if(precisionMode=="bbox"&&!precisionSelectionRect.IsEmpty)imageStatus.Text="已标记框选坐标 "+PrecisionBoxTag()+"；点击“提交精确编辑”发送。";else if((precisionMode=="lasso"||precisionMode=="doodle"||precisionMode=="arrow")&&precisionPath.Count>1)imageStatus.Text="已标记区域；点击“提交精确编辑”发送。";e.Handled=true;
        }

        void LayoutPrecisionOverlay()
        {
            if(precisionOverlay==null)return;precisionOverlay.Width=imageCanvas.ActualWidth;precisionOverlay.Height=imageCanvas.ActualHeight;precisionOverlay.Children.Clear();
            if(!precisionSelectionRect.IsEmpty){precisionBoxOverlay.Visibility=Visibility.Visible;precisionBoxOverlay.Width=precisionSelectionRect.Width;precisionBoxOverlay.Height=precisionSelectionRect.Height;Canvas.SetLeft(precisionBoxOverlay,precisionSelectionRect.Left);Canvas.SetTop(precisionBoxOverlay,precisionSelectionRect.Top);precisionOverlay.Children.Add(precisionBoxOverlay);}else precisionBoxOverlay.Visibility=Visibility.Collapsed;
            var red=new SolidColorBrush(Color.FromRgb(255,75,58));if(!Double.IsNaN(precisionPoint.X)&&precisionMode=="point"){var dot=new System.Windows.Shapes.Ellipse{Width=18,Height=18,Fill=new SolidColorBrush(Color.FromArgb(70,255,75,58)),Stroke=red,StrokeThickness=3};Canvas.SetLeft(dot,precisionPoint.X-9);Canvas.SetTop(dot,precisionPoint.Y-9);precisionOverlay.Children.Add(dot);}
            if(precisionPath.Count>1){var line=new System.Windows.Shapes.Polyline{Stroke=red,StrokeThickness=4,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};foreach(Point p in precisionPath)line.Points.Add(p);precisionOverlay.Children.Add(line);if(precisionMode=="arrow")AddPrecisionArrowHead(precisionOverlay,precisionPath[precisionPath.Count-2],precisionPath[precisionPath.Count-1],red);}
        }

        void LayoutPrecisionLayers()
        {
            if(precisionLayerCanvas==null)return;precisionLayerCanvas.Width=imageCanvas.ActualWidth;precisionLayerCanvas.Height=imageCanvas.ActualHeight;precisionLayerCanvas.Children.Clear();Rect display=RenderedImageRect();if(display.IsEmpty||workingBitmap==null||!layerCompositionActive){if(layerSelectionBox!=null)layerSelectionBox.Visibility=Visibility.Collapsed;if(layerResizeHandles!=null)foreach(var handle in layerResizeHandles)handle.Visibility=Visibility.Collapsed;return;}
            foreach(PrecisionLayer layer in precisionLayers.OrderBy(x=>x.Z)){if(!layer.Visible||layer.Bitmap==null)continue;var visual=new Image{Source=layer.Bitmap,Stretch=Stretch.Fill,IsHitTestVisible=false};layer.Visual=visual;double x=display.Left+layer.X/workingBitmap.PixelWidth*display.Width,y=display.Top+layer.Y/workingBitmap.PixelHeight*display.Height,w=Math.Max(2,layer.Width/workingBitmap.PixelWidth*display.Width),h=Math.Max(2,layer.Height/workingBitmap.PixelHeight*display.Height);visual.Width=w;visual.Height=h;Canvas.SetLeft(visual,x);Canvas.SetTop(visual,y);Panel.SetZIndex(visual,layer.Z);precisionLayerCanvas.Children.Add(visual);}
            if(selectedPrecisionLayer!=null&&selectedPrecisionLayer.Visible){double x=display.Left+selectedPrecisionLayer.X/workingBitmap.PixelWidth*display.Width,y=display.Top+selectedPrecisionLayer.Y/workingBitmap.PixelHeight*display.Height,w=Math.Max(2,selectedPrecisionLayer.Width/workingBitmap.PixelWidth*display.Width),h=Math.Max(2,selectedPrecisionLayer.Height/workingBitmap.PixelHeight*display.Height);layerSelectionBox.Visibility=Visibility.Visible;layerSelectionBox.Width=w;layerSelectionBox.Height=h;Canvas.SetLeft(layerSelectionBox,x);Canvas.SetTop(layerSelectionBox,y);if(!precisionOverlay.Children.Contains(layerSelectionBox))precisionOverlay.Children.Add(layerSelectionBox);PlaceLayerResizeHandle(0,x,y);PlaceLayerResizeHandle(1,x+w,y);PlaceLayerResizeHandle(2,x+w,y+h);PlaceLayerResizeHandle(3,x,y+h);}else{if(layerSelectionBox!=null)layerSelectionBox.Visibility=Visibility.Collapsed;if(layerResizeHandles!=null)foreach(var handle in layerResizeHandles)handle.Visibility=Visibility.Collapsed;}
        }

        void PlaceLayerResizeHandle(int index,double x,double y){if(layerResizeHandles==null||index<0||index>=layerResizeHandles.Length)return;var handle=layerResizeHandles[index];handle.Visibility=Visibility.Visible;Canvas.SetLeft(handle,x-handle.Width/2);Canvas.SetTop(handle,y-handle.Height/2);if(!precisionOverlay.Children.Contains(handle))precisionOverlay.Children.Add(handle);}

        PrecisionLayer PrecisionLayerAt(Point point)
        {
            if(!layerCompositionActive||workingBitmap==null)return null;Rect display=RenderedImageRect();if(display.IsEmpty)return null;double x=(point.X-display.Left)/display.Width*workingBitmap.PixelWidth,y=(point.Y-display.Top)/display.Height*workingBitmap.PixelHeight;return precisionLayers.Where(l=>l.Visible&&x>=l.X&&x<=l.X+l.Width&&y>=l.Y&&y<=l.Y+l.Height).OrderByDescending(l=>l.Z).FirstOrDefault();
        }
        bool TryStartLayerDrag(MouseButtonEventArgs e)
        {
            Point point=e.GetPosition(imageCanvas);string resize=LayerResizeHitMode(point);if(resize!=null&&selectedPrecisionLayer!=null){StartLayerResize(resize,point);e.Handled=true;return true;}PrecisionLayer layer=PrecisionLayerAt(point);if(layer==null)return false;selectedPrecisionLayer=layer;layerDragging=true;layerDragStart=point;layerDragX=layer.X;layerDragY=layer.Y;imageCanvas.CaptureMouse();RefreshPrecisionLayerList();LayoutPrecisionLayers();e.Handled=true;return true;
        }
        void MovePrecisionLayer(MouseEventArgs e)
        {
            if(selectedPrecisionLayer==null||workingBitmap==null)return;Rect display=RenderedImageRect();Point now=e.GetPosition(imageCanvas);selectedPrecisionLayer.X=Math.Max(-selectedPrecisionLayer.Width/2,Math.Min(workingBitmap.PixelWidth-selectedPrecisionLayer.Width/2,layerDragX+(now.X-layerDragStart.X)/Math.Max(1,display.Width)*workingBitmap.PixelWidth));selectedPrecisionLayer.Y=Math.Max(-selectedPrecisionLayer.Height/2,Math.Min(workingBitmap.PixelHeight-selectedPrecisionLayer.Height/2,layerDragY+(now.Y-layerDragStart.Y)/Math.Max(1,display.Height)*workingBitmap.PixelHeight));LayoutPrecisionLayers();e.Handled=true;
        }
        void FinishLayerDrag(MouseButtonEventArgs e){layerDragging=false;imageCanvas.ReleaseMouseCapture();LayoutPrecisionLayers();if(precisionStatus!=null)precisionStatus.Text="图层位置已调整；保存时会按当前画布合成。";e.Handled=true;}
        string LayerResizeHitMode(Point point){if(selectedPrecisionLayer==null||workingBitmap==null||!selectedPrecisionLayer.Visible)return null;Rect display=RenderedImageRect();double x=display.Left+selectedPrecisionLayer.X/workingBitmap.PixelWidth*display.Width,y=display.Top+selectedPrecisionLayer.Y/workingBitmap.PixelHeight*display.Height,w=selectedPrecisionLayer.Width/workingBitmap.PixelWidth*display.Width,h=selectedPrecisionLayer.Height/workingBitmap.PixelHeight*display.Height,hit=13;if(Math.Abs(point.X-x)<=hit&&Math.Abs(point.Y-y)<=hit)return "nw";if(Math.Abs(point.X-(x+w))<=hit&&Math.Abs(point.Y-y)<=hit)return "ne";if(Math.Abs(point.X-(x+w))<=hit&&Math.Abs(point.Y-(y+h))<=hit)return "se";if(Math.Abs(point.X-x)<=hit&&Math.Abs(point.Y-(y+h))<=hit)return "sw";return null;}
        void StartLayerResize(string mode,Point point){Rect display=RenderedImageRect();layerResizeMode=mode;layerResizing=true;if(mode=="nw")layerResizeAnchor=new Point(selectedPrecisionLayer.X+selectedPrecisionLayer.Width,selectedPrecisionLayer.Y+selectedPrecisionLayer.Height);else if(mode=="ne")layerResizeAnchor=new Point(selectedPrecisionLayer.X,selectedPrecisionLayer.Y+selectedPrecisionLayer.Height);else if(mode=="se")layerResizeAnchor=new Point(selectedPrecisionLayer.X,selectedPrecisionLayer.Y);else layerResizeAnchor=new Point(selectedPrecisionLayer.X+selectedPrecisionLayer.Width,selectedPrecisionLayer.Y);imageCanvas.CaptureMouse();imageCanvas.Cursor=(mode=="nw"||mode=="se")?Cursors.SizeNWSE:Cursors.SizeNESW;}
        void ResizePrecisionLayer(MouseEventArgs e){if(selectedPrecisionLayer==null||workingBitmap==null)return;Rect display=RenderedImageRect();Point p=e.GetPosition(imageCanvas);double px=(p.X-display.Left)/Math.Max(1,display.Width)*workingBitmap.PixelWidth,py=(p.Y-display.Top)/Math.Max(1,display.Height)*workingBitmap.PixelHeight;double left=Math.Min(px,layerResizeAnchor.X),top=Math.Min(py,layerResizeAnchor.Y),width=Math.Abs(px-layerResizeAnchor.X),height=Math.Abs(py-layerResizeAnchor.Y);if(width<12)width=12;if(height<12)height=12;if(layerResizeMode=="nw"||layerResizeMode=="sw")left=layerResizeAnchor.X-width;if(layerResizeMode=="nw"||layerResizeMode=="ne")top=layerResizeAnchor.Y-height;selectedPrecisionLayer.X=left;selectedPrecisionLayer.Y=top;selectedPrecisionLayer.Width=width;selectedPrecisionLayer.Height=height;LayoutPrecisionLayers();}
        void FinishLayerResize(MouseButtonEventArgs e){layerResizing=false;layerResizeMode=null;imageCanvas.Cursor=Cursors.Arrow;imageCanvas.ReleaseMouseCapture();RefreshPrecisionLayerList();LayoutPrecisionLayers();if(precisionStatus!=null)precisionStatus.Text="图层大小已调整；可继续拖动移动或调整前后层级。";e.Handled=true;}
        void RefreshPrecisionLayerList(){if(precisionLayerList==null)return;precisionLayerList.ItemsSource=null;precisionLayerList.ItemsSource=precisionLayers.OrderByDescending(x=>x.Z).ToList();precisionLayerList.Visibility=precisionLayers.Count>0?Visibility.Visible:Visibility.Collapsed;if(selectedPrecisionLayer!=null){precisionLayerList.SelectedItem=selectedPrecisionLayer;SyncSelectedLayerScale();}}
        void SyncSelectedLayerScale(){if(precisionLayerScale==null)return;double scale=100;if(selectedPrecisionLayer!=null&&selectedPrecisionLayer.NaturalWidth>0)scale=selectedPrecisionLayer.Width/selectedPrecisionLayer.NaturalWidth*100;precisionLayerScale.Value=Math.Max(precisionLayerScale.Minimum,Math.Min(precisionLayerScale.Maximum,scale));if(precisionLayerScaleText!=null)precisionLayerScaleText.Text=Math.Round(scale)+"%";}
        void ApplySelectedLayerScale(){if(precisionLayerScaleText!=null)precisionLayerScaleText.Text=Math.Round(precisionLayerScale.Value)+"%";if(selectedPrecisionLayer==null||selectedPrecisionLayer.NaturalWidth<=0)return;double scale=precisionLayerScale.Value/100;double oldWidth=selectedPrecisionLayer.Width,oldHeight=selectedPrecisionLayer.Height;selectedPrecisionLayer.Width=selectedPrecisionLayer.NaturalWidth*scale;selectedPrecisionLayer.Height=selectedPrecisionLayer.NaturalHeight*scale;selectedPrecisionLayer.X-=(selectedPrecisionLayer.Width-oldWidth)/2;selectedPrecisionLayer.Y-=(selectedPrecisionLayer.Height-oldHeight)/2;LayoutPrecisionLayers();}
        void NudgeSelectedLayerScale(double amount){if(selectedPrecisionLayer==null||precisionLayerScale==null)return;precisionLayerScale.Value=Math.Max(precisionLayerScale.Minimum,Math.Min(precisionLayerScale.Maximum,precisionLayerScale.Value+amount));RefreshPrecisionLayerList();}
        void MoveSelectedPrecisionLayer(int direction){if(selectedPrecisionLayer==null)return;int z=selectedPrecisionLayer.Z+direction;PrecisionLayer swap=precisionLayers.FirstOrDefault(x=>x.Z==z);if(swap==null)return;swap.Z=selectedPrecisionLayer.Z;selectedPrecisionLayer.Z=z;RefreshPrecisionLayerList();LayoutPrecisionLayers();}
        void ToggleSelectedPrecisionLayer(){if(selectedPrecisionLayer==null)return;selectedPrecisionLayer.Visible=!selectedPrecisionLayer.Visible;selectedPrecisionLayer.Name=(selectedPrecisionLayer.Visible?"◉ ":"○ ")+selectedPrecisionLayer.Name.TrimStart('◉','○',' ');RefreshPrecisionLayerList();LayoutPrecisionLayers();}
        BitmapSource RenderPrecisionLayerComposition()
        {
            if(!layerCompositionActive||workingBitmap==null)return workingBitmap;var visual=new DrawingVisual();using(DrawingContext context=visual.RenderOpen()){context.DrawImage(workingBitmap,new Rect(0,0,workingBitmap.PixelWidth,workingBitmap.PixelHeight));foreach(PrecisionLayer layer in precisionLayers.Where(x=>x.Visible&&x.Bitmap!=null).OrderBy(x=>x.Z))context.DrawImage(layer.Bitmap,new Rect(layer.X,layer.Y,layer.Width,layer.Height));}var bitmap=new RenderTargetBitmap(workingBitmap.PixelWidth,workingBitmap.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }

        void RestorePreLayerSplitImage()
        {
            if(preLayerSplitBitmap==null){imageStatus.Text="还没有可恢复的拆分前图片";return;}
            try
            {
                workingEncodedBytes=preLayerSplitEncodedBytes;
                workingBitmap=preLayerSplitEncodedBytes!=null?LoadEditorBytes(preLayerSplitEncodedBytes):preLayerSplitBitmap;
                layerCompositionActive=false;layerDragging=false;layerResizing=false;selectedPrecisionLayer=null;precisionLayers.Clear();precisionResultPaths.Clear();
                if(precisionResultBox!=null){precisionResultBox.ItemsSource=null;precisionResultBox.Visibility=Visibility.Collapsed;}
                if(precisionLayerList!=null){precisionLayerList.ItemsSource=null;precisionLayerList.Visibility=Visibility.Collapsed;}
                ClearPrecisionMarks();ClearImageTextLayer();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();ScheduleCompressionEstimate();LayoutPrecisionLayers();ResetImageHistory();
                imageStatus.Text="已恢复拆分前原图；已生成的图层文件不会被删除。";
                if(precisionStatus!=null)precisionStatus.Text="当前是拆分前原图；如需再次拆分可重新发起任务。";
            }
            catch(Exception ex){imageStatus.Text="恢复原图失败："+ex.Message;}
        }

        void ExportPrecisionLayersZip()
        {
            if(!EnsurePrecisionLayersForExport())return;var dialog=new Microsoft.Win32.SaveFileDialog{Filter="ZIP 图层包 (*.zip)|*.zip",FileName="博道咪-图层-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(dialog.ShowDialog()!=true)return;
            try
            {
                using(var stream=File.Create(dialog.FileName))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create))
                {
                    WriteZipBitmap(archive,"预览-当前合成.png",RenderPrecisionLayerComposition());
                    int index=1;foreach(PrecisionLayer layer in precisionLayers.OrderBy(x=>x.Z)){WriteZipLayerPng(archive,"图层/"+index.ToString("D2")+"-"+SafeExportName(layer.Name)+".png",layer);index++;}
                    var info=archive.CreateEntry("图层说明.txt",CompressionLevel.Optimal);using(var writer=new StreamWriter(info.Open(),new UTF8Encoding(true))){writer.WriteLine("博道咪图层包");writer.WriteLine("预览-当前合成.png 为按当前图层位置与顺序合成的预览图。");index=1;foreach(PrecisionLayer layer in precisionLayers.OrderBy(x=>x.Z)){writer.WriteLine(String.Format("{0:D2}. {1} | x={2:0.##}, y={3:0.##}, 宽={4:0.##}, 高={5:0.##}, {6}",index,layer.Name,layer.X,layer.Y,layer.Width,layer.Height,layer.Visible?"显示":"隐藏"));index++;}}
                }
                imageStatus.Text="已导出 ZIP 图层包："+Path.GetFileName(dialog.FileName);
            }
            catch(Exception ex){imageStatus.Text="导出 ZIP 图层包失败："+ex.Message;}
        }

        void WriteZipBitmap(ZipArchive archive,string name,BitmapSource bitmap)
        {
            if(bitmap==null)return;var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var encoded=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(encoded);encoded.Position=0;using(var output=entry.Open()){encoded.CopyTo(output);}}
        }

        void WriteZipLayerPng(ZipArchive archive,string name,PrecisionLayer layer)
        {
            if(layer==null)return;var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var output=entry.Open()){
                // 分层结果的原始 PNG 已带 Alpha，直接复制可避免任何重编码损失。
                if(!String.IsNullOrWhiteSpace(layer.Path)&&File.Exists(layer.Path)&&String.Equals(Path.GetExtension(layer.Path),".png",StringComparison.OrdinalIgnoreCase)){using(var source=File.OpenRead(layer.Path))source.CopyTo(output);}
                else {using(var encoded=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(layer.Bitmap));encoder.Save(encoded);encoded.Position=0;encoded.CopyTo(output);}}
            }
        }

        void ExportSelectedPrecisionLayerPng()
        {
            if(selectedPrecisionLayer==null){imageStatus.Text="请先在图层列表中选中要下载的图层。";return;}var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG 图片 (*.png)|*.png",FileName=SafeExportName(selectedPrecisionLayer.Name)+".png"};if(dialog.ShowDialog()!=true)return;
            try{
                if(!String.IsNullOrWhiteSpace(selectedPrecisionLayer.Path)&&File.Exists(selectedPrecisionLayer.Path)&&String.Equals(Path.GetExtension(selectedPrecisionLayer.Path),".png",StringComparison.OrdinalIgnoreCase))File.Copy(selectedPrecisionLayer.Path,dialog.FileName,true);
                else {using(var output=File.Create(dialog.FileName)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(selectedPrecisionLayer.Bitmap));encoder.Save(output);}}
                imageStatus.Text="已下载透明 PNG 图层："+Path.GetFileName(dialog.FileName);
            }catch(Exception ex){imageStatus.Text="下载图层失败："+ex.Message;}
        }

        bool EnsurePrecisionLayersForExport()
        {
            if(!layerCompositionActive||precisionLayers.Count==0){imageStatus.Text="请先完成图层拆分，再导出图层。";return false;}if(workingBitmap==null){imageStatus.Text="当前没有可导出的底图。";return false;}return true;
        }

        string SafeExportName(string value)
        {
            string name=String.IsNullOrWhiteSpace(value)?"图层":value.Trim();foreach(char bad in Path.GetInvalidFileNameChars())name=name.Replace(bad,'_');name=name.Replace('◉',' ').Replace('○',' ').Trim();return String.IsNullOrWhiteSpace(name)?"图层":name;
        }

        void ExportPrecisionLayersPsd()
        {
            if(!EnsurePrecisionLayersForExport())return;var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Photoshop 图层文件 (*.psd)|*.psd",FileName="博道咪-图层-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".psd"};if(dialog.ShowDialog()!=true)return;
            try{WritePrecisionPsd(dialog.FileName);imageStatus.Text="已导出可编辑 PSD："+Path.GetFileName(dialog.FileName)+"（可在 Photoshop 中继续移动、缩放和调整图层顺序）。";}
            catch(Exception ex){imageStatus.Text="导出 PSD 失败："+ex.Message;}
        }

        List<PsdExportLayer> BuildPsdExportLayers()
        {
            var layers=new List<PsdExportLayer>();foreach(PrecisionLayer layer in precisionLayers.OrderByDescending(x=>x.Z))layers.Add(new PsdExportLayer{Name=SafeExportName(layer.Name),Bitmap=RasterizePrecisionLayer(layer),Visible=layer.Visible});layers.Add(new PsdExportLayer{Name="底图",Bitmap=workingBitmap,Visible=true});return layers;
        }

        BitmapSource RasterizePrecisionLayer(PrecisionLayer layer)
        {
            var visual=new DrawingVisual();using(DrawingContext context=visual.RenderOpen()){context.DrawImage(layer.Bitmap,new Rect(layer.X,layer.Y,layer.Width,layer.Height));}var bitmap=new RenderTargetBitmap(workingBitmap.PixelWidth,workingBitmap.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }

        void WritePrecisionPsd(string path)
        {
            int width=workingBitmap.PixelWidth,height=workingBitmap.PixelHeight;if(width<1||height<1||width>30000||height>30000)throw new InvalidOperationException("当前图片尺寸不支持 PSD 导出。");var layers=BuildPsdExportLayers();BitmapSource composite=RenderPrecisionLayerComposition();
            using(var file=File.Create(path))using(var writer=new BinaryWriter(file,Encoding.ASCII))
            {
                WriteAscii(writer,"8BPS");WritePsdU16(writer,1);writer.Write(new byte[6]);WritePsdU16(writer,4);WritePsdI32(writer,height);WritePsdI32(writer,width);WritePsdU16(writer,8);WritePsdU16(writer,3);WritePsdI32(writer,0);WritePsdI32(writer,0);
                byte[] layerInfo=BuildPsdLayerInfo(layers,width,height);using(var section=new MemoryStream())using(var sectionWriter=new BinaryWriter(section,Encoding.ASCII)){WritePsdI32(sectionWriter,layerInfo.Length);sectionWriter.Write(layerInfo);if(section.Length%2!=0)sectionWriter.Write((byte)0);WritePsdI32(sectionWriter,0);sectionWriter.Flush();WritePsdI32(writer,(int)section.Length);writer.Write(section.ToArray());}
                WritePsdComposite(writer,composite,width,height);
            }
        }

        byte[] BuildPsdLayerInfo(List<PsdExportLayer> layers,int width,int height)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.ASCII))
            {
                WritePsdU16(writer,layers.Count);foreach(PsdExportLayer layer in layers){WritePsdI32(writer,0);WritePsdI32(writer,0);WritePsdI32(writer,height);WritePsdI32(writer,width);WritePsdU16(writer,4);for(int channel=0;channel<3;channel++){WritePsdI16(writer,(short)channel);WritePsdI32(writer,2+width*height);}WritePsdI16(writer,-1);WritePsdI32(writer,2+width*height);WriteAscii(writer,"8BIM");WriteAscii(writer,"norm");writer.Write((byte)255);writer.Write((byte)0);writer.Write((byte)(layer.Visible?0:2));writer.Write((byte)0);byte[] extra=BuildPsdLayerExtra(layer.Name);WritePsdI32(writer,extra.Length);writer.Write(extra);}
                foreach(PsdExportLayer layer in layers){byte[][] planes=PsdPlanes(layer.Bitmap,width,height);for(int channel=0;channel<4;channel++){WritePsdU16(writer,0);writer.Write(planes[channel]);}}
                writer.Flush();return stream.ToArray();
            }
        }

        byte[] BuildPsdLayerExtra(string name)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.ASCII)){WritePsdI32(writer,0);WritePsdI32(writer,0);byte[] bytes=Encoding.ASCII.GetBytes("Layer");writer.Write((byte)bytes.Length);writer.Write(bytes);while(stream.Length%4!=0)writer.Write((byte)0);writer.Flush();return stream.ToArray();}
        }

        byte[][] PsdPlanes(BitmapSource source,int width,int height)
        {
            var converted=new FormatConvertedBitmap();converted.BeginInit();converted.Source=source;converted.DestinationFormat=PixelFormats.Bgra32;converted.EndInit();int stride=width*4;byte[] pixels=new byte[stride*height];converted.CopyPixels(new Int32Rect(0,0,width,height),pixels,stride,0);byte[][] result={new byte[width*height],new byte[width*height],new byte[width*height],new byte[width*height]};for(int i=0,p=0;i<pixels.Length;i+=4,p++){result[0][p]=pixels[i+2];result[1][p]=pixels[i+1];result[2][p]=pixels[i];result[3][p]=pixels[i+3];}return result;
        }

        void WritePsdComposite(BinaryWriter writer,BitmapSource source,int width,int height){byte[][] planes=PsdPlanes(source,width,height);WritePsdU16(writer,0);for(int channel=0;channel<4;channel++)writer.Write(planes[channel]);}
        void WriteAscii(BinaryWriter writer,string text){writer.Write(Encoding.ASCII.GetBytes(text));}
        void WritePsdU16(BinaryWriter writer,int value){writer.Write((byte)((value>>8)&255));writer.Write((byte)(value&255));}
        void WritePsdI16(BinaryWriter writer,short value){WritePsdU16(writer,(ushort)value);}
        void WritePsdI32(BinaryWriter writer,int value){writer.Write((byte)((value>>24)&255));writer.Write((byte)((value>>16)&255));writer.Write((byte)((value>>8)&255));writer.Write((byte)(value&255));}

        void AddPrecisionArrowHead(Canvas canvas,Point before,Point end,Brush brush)
        {
            Vector direction=before-end;if(direction.Length<1)return;direction.Normalize();Vector side=new Vector(-direction.Y,direction.X);Point a=end+direction*16+side*8,b=end+direction*16-side*8;var left=new System.Windows.Shapes.Line{X1=end.X,Y1=end.Y,X2=a.X,Y2=a.Y,Stroke=brush,StrokeThickness=4,StrokeEndLineCap=PenLineCap.Round};var right=new System.Windows.Shapes.Line{X1=end.X,Y1=end.Y,X2=b.X,Y2=b.Y,Stroke=brush,StrokeThickness=4,StrokeEndLineCap=PenLineCap.Round};canvas.Children.Add(left);canvas.Children.Add(right);
        }

        int PrecisionCoordinate(double value,double origin,double span){return Math.Max(0,Math.Min(1000,(int)Math.Round((value-origin)/Math.Max(1,span)*1000)));}
        string PrecisionPointTag(){Rect r=RenderedImageRect();return r.IsEmpty||Double.IsNaN(precisionPoint.X)?"":String.Format("<point>{0} {1}</point>",PrecisionCoordinate(precisionPoint.X,r.Left,r.Width),PrecisionCoordinate(precisionPoint.Y,r.Top,r.Height));}
        string PrecisionBoxTag(){Rect r=RenderedImageRect();if(r.IsEmpty||precisionSelectionRect.IsEmpty)return "";return String.Format("<bbox>{0} {1} {2} {3}</bbox>",PrecisionCoordinate(precisionSelectionRect.Left,r.Left,r.Width),PrecisionCoordinate(precisionSelectionRect.Top,r.Top,r.Height),PrecisionCoordinate(precisionSelectionRect.Right,r.Left,r.Width),PrecisionCoordinate(precisionSelectionRect.Bottom,r.Top,r.Height));}

        string PreparePrecisionMarkedReference()
        {
            if(workingBitmap==null||precisionPath.Count<2)return PrepareWorkingImageFile();int w=workingBitmap.PixelWidth,h=workingBitmap.PixelHeight;Rect display=RenderedImageRect();var visual=new DrawingVisual();using(DrawingContext context=visual.RenderOpen()){
                context.DrawImage(workingBitmap,new Rect(0,0,w,h));var pen=new Pen(new SolidColorBrush(Color.FromRgb(255,50,45)),Math.Max(5,Math.Min(w,h)/170.0));pen.StartLineCap=PenLineCap.Round;pen.EndLineCap=PenLineCap.Round;pen.LineJoin=PenLineJoin.Round;var geometry=new StreamGeometry();using(StreamGeometryContext g=geometry.Open()){Point first=MapPrecisionPoint(precisionPath[0],display,w,h);g.BeginFigure(first,false,false);for(int i=1;i<precisionPath.Count;i++)g.LineTo(MapPrecisionPoint(precisionPath[i],display,w,h),true,false);}geometry.Freeze();context.DrawGeometry(null,pen,geometry);if(precisionMode=="arrow")DrawPrecisionArrow(context,MapPrecisionPoint(precisionPath[precisionPath.Count-2],display,w,h),MapPrecisionPoint(precisionPath[precisionPath.Count-1],display,w,h),pen);}
            var result=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);result.Render(visual);string file=Path.Combine(imageTempDir,"precision-mark-"+Guid.NewGuid().ToString("N")+".png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result));using(var stream=File.Create(file))encoder.Save(stream);return file;
        }

        Point MapPrecisionPoint(Point point,Rect display,int width,int height){return new Point((point.X-display.Left)/Math.Max(1,display.Width)*width,(point.Y-display.Top)/Math.Max(1,display.Height)*height);}
        void DrawPrecisionArrow(DrawingContext context,Point before,Point end,Pen pen){Vector direction=before-end;if(direction.Length<1)return;direction.Normalize();Vector side=new Vector(-direction.Y,direction.X);context.DrawLine(pen,end,end+direction*30+side*15);context.DrawLine(pen,end,end+direction*30-side*15);}

        Point ClampCropPoint(Point point,Rect bounds)
        {
            return new Point(Math.Max(bounds.Left,Math.Min(point.X,bounds.Right)),Math.Max(bounds.Top,Math.Min(point.Y,bounds.Bottom)));
        }

        Rect CreateCropRect(Point end)
        {
            Rect bounds=RenderedImageRect();if(bounds.IsEmpty)return Rect.Empty;end=ClampCropPoint(end,bounds);
            double dx=end.X-cropStart.X,dy=end.Y-cropStart.Y,w=Math.Abs(dx),h=Math.Abs(dy),ratio=SelectedCropRatio();
            if(ratio>0&&w>0&&h>0){if(w/h>ratio)w=h*ratio;else h=w/ratio;}
            double x=dx<0?cropStart.X-w:cropStart.X,y=dy<0?cropStart.Y-h:cropStart.Y;
            return new Rect(x,y,w,h);
        }

        double SelectedCropRatio()
        {
            if(customCropSizeActive&&customCropOutputWidth>0&&customCropOutputHeight>0)return (double)customCropOutputWidth/customCropOutputHeight;
            int selected=cropPresetBox==null?0:cropPresetBox.SelectedIndex;
            switch(selected){case 1:return 1;case 2:return 4.0/3;case 3:return 3.0/4;case 4:return 16.0/9;case 5:return 9.0/16;case 6:return 3.0/2;case 7:return 2.0/3;case 8:return 1;case 9:return 3840.0/2160;case 10:return 2560.0/1440;case 11:return 1920.0/1080;case 12:return 1280.0/720;case 13:return 1080.0/1920;case 14:return 1242.0/1660;case 15:return 750.0/460;case 16:return 1;default:return 0;}
        }

        bool TryGetCropOutputSize(out int width,out int height)
        {
            if(customCropSizeActive){width=customCropOutputWidth;height=customCropOutputHeight;return width>0&&height>0;}
            width=0;height=0;int selected=cropPresetBox==null?0:cropPresetBox.SelectedIndex;
            switch(selected){case 8:width=1080;height=1080;return true;case 9:width=3840;height=2160;return true;case 10:width=2560;height=1440;return true;case 11:width=1920;height=1080;return true;case 12:width=1280;height=720;return true;case 13:width=1080;height=1920;return true;case 14:width=1242;height=1660;return true;case 15:width=750;height=460;return true;case 16:width=800;height=800;return true;default:return false;}
        }

        void ApplyCustomCropSize()
        {
            int width,height;if(!Int32.TryParse(widthBox.Text,out width)||!Int32.TryParse(heightBox.Text,out height)||width<1||height<1||width>20000||height>20000){imageStatus.Text="请输入 1–20000 的裁切输出宽高";return;}
            if(cropPresetBox!=null)cropPresetBox.SelectedIndex=0;customCropOutputWidth=width;customCropOutputHeight=height;customCropSizeActive=true;cropModeActive=true;ClearImageTextLayer();imageCanvas.Cursor=Cursors.Cross;InitializeCropBox();imageStatus.Text="已生成 "+width+" × "+height+" 的裁切框；拖动调整后点击“确认裁剪”";
        }

        Rect RenderedImageRect()
        {
            if(workingBitmap==null||imageCanvas.ActualWidth<=0||imageCanvas.ActualHeight<=0)return Rect.Empty;
            double scale=Math.Min(imageCanvas.ActualWidth/workingBitmap.PixelWidth,imageCanvas.ActualHeight/workingBitmap.PixelHeight);double w=workingBitmap.PixelWidth*scale,h=workingBitmap.PixelHeight*scale;
            return new Rect((imageCanvas.ActualWidth-w)/2,(imageCanvas.ActualHeight-h)/2,w,h);
        }

        void ApplyCrop()
        {
            if(!cropModeActive){imageStatus.Text="请先点击“裁切”进入裁切模式";return;}
            if(workingBitmap==null||cropRect.IsEmpty||cropRect.Width<4||cropRect.Height<4){imageStatus.Text="请先在图片上拖出裁切区域";return;}
            Rect visible=Rect.Intersect(cropRect,RenderedImageRect());Rect rendered=RenderedImageRect();if(visible.IsEmpty)return;
            int x=(int)Math.Round((visible.X-rendered.X)/rendered.Width*workingBitmap.PixelWidth);int y=(int)Math.Round((visible.Y-rendered.Y)/rendered.Height*workingBitmap.PixelHeight);
            int w=(int)Math.Round(visible.Width/rendered.Width*workingBitmap.PixelWidth);int h=(int)Math.Round(visible.Height/rendered.Height*workingBitmap.PixelHeight);
            x=Math.Max(0,Math.Min(x,workingBitmap.PixelWidth-1));y=Math.Max(0,Math.Min(y,workingBitmap.PixelHeight-1));w=Math.Max(1,Math.Min(w,workingBitmap.PixelWidth-x));h=Math.Max(1,Math.Min(h,workingBitmap.PixelHeight-y));
            var cropped=new CroppedBitmap(workingBitmap,new Int32Rect(x,y,w,h));cropped.Freeze();BitmapSource result=cropped;int outputWidth,outputHeight;
            if(TryGetCropOutputSize(out outputWidth,out outputHeight))result=ResizeBitmap(result,outputWidth,outputHeight);
            workingBitmap=result;workingEncodedBytes=null;ClearCompressionCandidate();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();ExitCropMode();ScheduleCompressionEstimate();PushImageState();
            imageStatus.Text=String.Format("已确认裁剪：{0} × {1} px，确认覆盖前不会写入原图",workingBitmap.PixelWidth,workingBitmap.PixelHeight);
        }

        BitmapSource ResizeBitmap(BitmapSource source,int width,int height)
        {
            var scaled=new TransformedBitmap(source,new ScaleTransform((double)width/source.PixelWidth,(double)height/source.PixelHeight));scaled.Freeze();return scaled;
        }

        void ResetWorkingImage(){if(imageHistory.Count>0){RestoreOriginalImage();return;}workingBitmap=originalBitmap;workingEncodedBytes=null;ClearCompressionCandidate();ClearImageTextLayer();editorImage.Source=workingBitmap;if(workingBitmap!=null){widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();}if(cropModeActive)InitializeCropBox();else ResetCrop();ScheduleCompressionEstimate();imageStatus.Text="已恢复到打开时的原图";}

        // ===== 撤销 / 恢复 / 恢复原图 =====
        // 历史按“状态快照”记录：打开或生成图片时清空重来，之后每完成一步会改图的操作（裁剪、压缩、
        // AI 重绘、改字、精确编辑结果）就追加一个快照；撤销/恢复只是移动指针，恢复原图即回到第 0 个快照。
        void ResetImageHistory()
        {
            imageHistory.Clear();
            if(workingBitmap==null){imageHistoryIndex=-1;UpdateImageHistoryButtons();return;}
            imageHistory.Add(new ImageHistoryEntry{Bitmap=workingBitmap,Encoded=workingEncodedBytes});
            imageHistoryIndex=0;UpdateImageHistoryButtons();
        }

        void PushImageState()
        {
            if(workingBitmap==null)return;
            if(imageHistoryIndex<imageHistory.Count-1)imageHistory.RemoveRange(imageHistoryIndex+1,imageHistory.Count-imageHistoryIndex-1);
            imageHistory.Add(new ImageHistoryEntry{Bitmap=workingBitmap,Encoded=workingEncodedBytes});
            if(imageHistory.Count>30)imageHistory.RemoveAt(0);
            imageHistoryIndex=imageHistory.Count-1;UpdateImageHistoryButtons();
        }

        void ApplyImageHistoryEntry(string message)
        {
            var entry=imageHistory[imageHistoryIndex];
            workingBitmap=entry.Bitmap;workingEncodedBytes=entry.Encoded;
            ClearCompressionCandidate();ClearImageTextLayer();ExitCropMode();
            editorImage.Source=workingBitmap;
            widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();
            ScheduleCompressionEstimate();UpdateImageHistoryButtons();
            imageStatus.Text=message;
        }

        void UndoImageChange()
        {
            if(layerCompositionActive){imageStatus.Text="图层合成模式下暂不支持撤销；可先点“恢复拆分前原图”";return;}
            if(imageHistoryIndex<=0){imageStatus.Text="没有可撤销的操作";return;}
            imageHistoryIndex--;ApplyImageHistoryEntry("已撤销一步；不满意可点“恢复”返回");
        }

        void RedoImageChange()
        {
            if(layerCompositionActive){imageStatus.Text="图层合成模式下暂不支持恢复；可先点“恢复拆分前原图”";return;}
            if(imageHistoryIndex<0||imageHistoryIndex>=imageHistory.Count-1){imageStatus.Text="没有可恢复的操作";return;}
            imageHistoryIndex++;ApplyImageHistoryEntry("已恢复到下一步");
        }

        void RestoreOriginalImage()
        {
            if(layerCompositionActive){imageStatus.Text="图层合成模式下请使用“恢复拆分前原图”";return;}
            if(imageHistoryIndex>0){imageHistoryIndex=0;ApplyImageHistoryEntry("已恢复原图；若想找回修改后的版本，点“恢复”即可返回");return;}
            if(originalBitmap!=null){workingBitmap=originalBitmap;workingEncodedBytes=null;ClearCompressionCandidate();ClearImageTextLayer();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();if(cropModeActive)InitializeCropBox();else ResetCrop();ScheduleCompressionEstimate();imageStatus.Text="已恢复到打开时的原图";return;}
            imageStatus.Text="当前就是最初的版本";
        }
        void ShowOriginalComparison(bool show)
        {
            if(editorImage==null||originalBitmap==null||layerCompositionActive)return;
            imageOriginalPreviewActive=show;editorImage.Source=show?originalBitmap:workingBitmap;
            if(show)imageStatus.Text="正在查看原图（松开后回到当前结果）";else imageStatus.Text="已回到当前结果";
        }

        void UpdateImageHistoryButtons()
        {
            if(imageUndoButton==null)return;
            bool enabled=!layerCompositionActive;
            imageUndoButton.IsEnabled=enabled&&imageHistoryIndex>0;
            imageRedoButton.IsEnabled=enabled&&imageHistoryIndex>=0&&imageHistoryIndex<imageHistory.Count-1;
            imageRestoreButton.IsEnabled=enabled&&imageHistoryIndex>0;
        }

        // ===== 提示词库 =====
        ComboBox BuildPromptLibraryControls(StackPanel parent,TextBox promptBox)
        {
            parent.Children.Add(new TextBlock{Text="提示词库（选中即填入，可存常用提示词）",FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,3)});
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var box=new ComboBox{Height=32,Padding=new Thickness(7,4,7,4),ToolTip="保存常用提示词，下次生成时直接调用"};
            box.SelectionChanged+=delegate{string selected=Convert.ToString(box.SelectedItem);if(!String.IsNullOrWhiteSpace(selected))promptBox.Text=selected;};
            var save=MakeButton("存入词库",new SolidColorBrush(Color.FromRgb(235,229,222)));save.Click+=delegate{SavePromptToLibrary(promptBox.Text);};
            var remove=MakeButton("删除",Brushes.Transparent);remove.Click+=delegate{RemovePromptFromLibrary(Convert.ToString(box.SelectedItem));};
            row.Children.Add(box);Grid.SetColumn(save,1);row.Children.Add(save);Grid.SetColumn(remove,2);row.Children.Add(remove);
            parent.Children.Add(row);promptLibraryBoxes.Add(box);return box;
        }

        void RefreshPromptLibraryBoxes()
        {
            var items=(imageAiConfig.PromptLibrary??new List<string>()).Where(x=>!String.IsNullOrWhiteSpace(x)).ToList();
            foreach(var box in promptLibraryBoxes){box.ItemsSource=null;box.ItemsSource=items.ToList();}
        }

        void SavePromptToLibrary(string prompt)
        {
            prompt=(prompt??"").Trim();
            if(String.IsNullOrEmpty(prompt)){imageStatus.Text="提示词是空的，先写点内容再存入词库";return;}
            var library=imageAiConfig.PromptLibrary;
            library.RemoveAll(x=>String.Equals(x,prompt,StringComparison.Ordinal));
            library.Insert(0,prompt);
            if(library.Count>50)library.RemoveRange(50,library.Count-50);
            SaveImageAiConfig();RefreshPromptLibraryBoxes();
            foreach(var box in promptLibraryBoxes)box.SelectedItem=prompt;
            imageStatus.Text="提示词已存入词库；下次生成时在词库里选中即可直接调用";
        }

        void RemovePromptFromLibrary(string prompt)
        {
            if(String.IsNullOrWhiteSpace(prompt)){imageStatus.Text="请先在词库中选中要删除的提示词";return;}
            imageAiConfig.PromptLibrary.RemoveAll(x=>String.Equals(x,prompt,StringComparison.Ordinal));
            SaveImageAiConfig();RefreshPromptLibraryBoxes();
            imageStatus.Text="已从词库删除该提示词";
        }

        byte[] EncodeBitmap(BitmapSource source,string format,int quality)
        {
            BitmapEncoder encoder;if(format=="JPEG")encoder=new JpegBitmapEncoder{QualityLevel=Math.Max(1,Math.Min(100,quality))};else encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}
        }

        int JpegQualityForCompression(double intensity){return Math.Max(18,Math.Min(100,(int)Math.Round(100-intensity*0.86)));}

        BitmapSource FlattenTransparencyForJpeg(BitmapSource source)
        {
            var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int width=converted.PixelWidth,height=converted.PixelHeight,stride=width*4;byte[] pixels=new byte[stride*height];converted.CopyPixels(pixels,stride,0);
            for(int i=0;i<pixels.Length;i+=4){int alpha=pixels[i+3];if(alpha<255){pixels[i]=(byte)((pixels[i]*alpha+255*(255-alpha))/255);pixels[i+1]=(byte)((pixels[i+1]*alpha+255*(255-alpha))/255);pixels[i+2]=(byte)((pixels[i+2]*alpha+255*(255-alpha))/255);pixels[i+3]=255;}}
            var result=new WriteableBitmap(width,height,source.DpiX,source.DpiY,PixelFormats.Bgra32,null);result.WritePixels(new Int32Rect(0,0,width,height),pixels,stride,0);result.Freeze();return result;
        }

        BitmapSource QuantizePngForCompression(BitmapSource source,double intensity)
        {
            if(intensity<8)return source;int step=intensity<24?2:intensity<42?4:intensity<60?8:intensity<76?16:intensity<89?24:32;
            var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int width=converted.PixelWidth,height=converted.PixelHeight,stride=width*4;byte[] pixels=new byte[stride*height];converted.CopyPixels(pixels,stride,0);
            for(int i=0;i<pixels.Length;i+=4){for(int channel=0;channel<3;channel++){int value=pixels[i+channel],rounded=((value+step/2)/step)*step;pixels[i+channel]=(byte)Math.Min(255,rounded);}}
            var result=new WriteableBitmap(width,height,source.DpiX,source.DpiY,PixelFormats.Bgra32,null);result.WritePixels(new Int32Rect(0,0,width,height),pixels,stride,0);result.Freeze();return result;
        }

        byte[] EncodeForCompression(BitmapSource source,string format,double intensity,out BitmapSource encodedBitmap)
        {
            if(format=="JPEG"){encodedBitmap=FlattenTransparencyForJpeg(source);return EncodeBitmap(encodedBitmap,"JPEG",JpegQualityForCompression(intensity));}
            encodedBitmap=QuantizePngForCompression(source,intensity);return EncodeBitmap(encodedBitmap,"PNG",100);
        }

        long CurrentImageByteLength()
        {
            if(workingEncodedBytes!=null)return workingEncodedBytes.LongLength;if(editingImageItem!=null&&!String.IsNullOrWhiteSpace(editingImageItem.Value)&&File.Exists(editingImageItem.Value)&&workingBitmap==originalBitmap)return new FileInfo(editingImageItem.Value).Length;return 0;
        }

        void ClearCompressionCandidate(){compressionCandidateBytes=null;compressionCandidateBitmap=null;Interlocked.Increment(ref compressionEstimateVersion);}
        void ScheduleCompressionEstimate(){if(compressionEstimateTimer==null||workingBitmap==null)return;compressionCandidateBytes=null;compressionCandidateBitmap=null;Interlocked.Increment(ref compressionEstimateVersion);compressionEstimateTimer.Stop();compressionEstimateTimer.Start();}

        string CompressionEstimateText(string format,double intensity,BitmapSource candidate,byte[] bytes,long originalBytes)
        {
            string comparison=originalBytes>0?String.Format("原文件 {0:0.0} KB  →  预计 {1:0.0} KB · {2}",originalBytes/1024.0,bytes.Length/1024.0,bytes.Length<originalBytes?("节省 "+((1.0-bytes.Length/(double)originalBytes)*100).ToString("0.0")+"%"):"未缩小"):String.Format("预计输出 {0:0.0} KB",bytes.Length/1024.0);
            string detail=format=="JPEG"?("JPEG 质量 "+JpegQualityForCompression(intensity)+" · 透明区域铺白"):(intensity<8?"PNG 像素无损":"PNG 透明通道保留 · 颜色智能精简");return comparison+String.Format("\n分辨率锁定 {0} × {1} px · {2}",candidate.PixelWidth,candidate.PixelHeight,detail);
        }

        void BeginCompressionEstimate()
        {
            if(workingBitmap==null||compressionSlider==null)return;BitmapSource source=workingBitmap;if(!source.IsFrozen){source=source.Clone();source.Freeze();}string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";double intensity=compressionSlider.Value;long originalBytes=CurrentImageByteLength();int version=compressionEstimateVersion;
            ThreadPool.QueueUserWorkItem(delegate{try{BitmapSource candidate;byte[] bytes=EncodeForCompression(source,format,intensity,out candidate);string message=CompressionEstimateText(format,intensity,candidate,bytes,originalBytes);UiPost(new Action(delegate{if(version!=compressionEstimateVersion)return;compressionCandidateBitmap=candidate;compressionCandidateBytes=bytes;if(compressionSizeText!=null)compressionSizeText.Text=message;}));}catch(Exception ex){UiPost(new Action(delegate{if(version==compressionEstimateVersion&&compressionSizeText!=null)compressionSizeText.Text="大小估算失败："+ex.Message;}));}});
        }

        void UpdateCompressionEstimate()
        {
            if(workingBitmap==null||compressionSlider==null)return;try{string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";double intensity=compressionSlider.Value;BitmapSource candidate;byte[] bytes=EncodeForCompression(workingBitmap,format,intensity,out candidate);compressionCandidateBitmap=candidate;compressionCandidateBytes=bytes;
                long originalBytes=CurrentImageByteLength();compressionSizeText.Text=CompressionEstimateText(format,intensity,candidate,bytes,originalBytes);
            }catch(Exception ex){compressionSizeText.Text="大小估算失败："+ex.Message;}
        }

        void ConfirmCompressionAdjustment()
        {
            if(compressionCandidateBytes==null||compressionCandidateBitmap==null)UpdateCompressionEstimate();if(compressionCandidateBytes==null)return;
            workingEncodedBytes=compressionCandidateBytes;workingBitmap=LoadEditorBytes(workingEncodedBytes);editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();ResetCrop();PushImageState();
            imageStatus.Text=String.Format("已确认调整：{0} × {1} px · {2:0.0} KB；尚未覆盖原图",workingBitmap.PixelWidth,workingBitmap.PixelHeight,workingEncodedBytes.Length/1024.0);ClearCompressionCandidate();ScheduleCompressionEstimate();
        }

        void PrepareLosslessCompression()
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}try{
                outputFormatBox.SelectedItem="PNG";compressionSlider.Value=0;if(compressionEstimateTimer!=null)compressionEstimateTimer.Stop();Interlocked.Increment(ref compressionEstimateVersion);
                byte[] candidate=EncodeBitmap(workingBitmap,"PNG",100),baseline=null;
                if(workingEncodedBytes!=null)baseline=workingEncodedBytes;else if(editingImageItem!=null&&workingBitmap==originalBitmap&&File.Exists(editingImageItem.Value))baseline=File.ReadAllBytes(editingImageItem.Value);else baseline=EncodeBitmap(workingBitmap,Convert.ToString(outputFormatBox.SelectedItem)??"PNG",92);
                if(candidate.Length>=baseline.Length){compressionSizeText.Text=String.Format("当前 {0:0.0} KB · 无损 PNG {1:0.0} KB",baseline.Length/1024.0,candidate.Length/1024.0);imageStatus.Text="像素无损版本没有更小，已保留当前较小数据；JPEG 无法在保持每个像素不变时靠调质量压缩";return;}
                compressionCandidateBytes=candidate;compressionCandidateBitmap=workingBitmap;compressionSizeText.Text=String.Format("无损候选：{0:0.0} KB → {1:0.0} KB · 像素尺寸不变",baseline.Length/1024.0,candidate.Length/1024.0);imageStatus.Text="已生成像素无损 PNG 候选，点击“应用到当前图片”或“另存压缩副本”";
            }catch(Exception ex){imageStatus.Text="无损压缩失败："+ex.Message;}
        }

        void CompressPreview()
        {
            int target;if(workingBitmap==null)return;if(!Int32.TryParse(targetKbBox.Text,out target)||target<1){imageStatus.Text="请输入目标大小 KB";return;}string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";int limit=target*1024;byte[] best=null;BitmapSource bestBitmap=null;double bestIntensity=95;
            if(format=="JPEG"){int low=18,high=100,bestQuality=18;while(low<=high){int quality=(low+high)/2;BitmapSource bitmap=FlattenTransparencyForJpeg(workingBitmap);byte[] data=EncodeBitmap(bitmap,"JPEG",quality);if(data.Length<=limit){best=data;bestBitmap=bitmap;bestQuality=quality;low=quality+1;}else high=quality-1;}if(best==null){bestBitmap=FlattenTransparencyForJpeg(workingBitmap);best=EncodeBitmap(bestBitmap,"JPEG",18);bestQuality=18;}bestIntensity=Math.Max(0,Math.Min(95,(100-bestQuality)/0.86));}
            else {foreach(double intensity in new double[]{0,10,20,30,40,50,60,70,80,90,95}){BitmapSource bitmap;byte[] data=EncodeForCompression(workingBitmap,"PNG",intensity,out bitmap);best=data;bestBitmap=bitmap;bestIntensity=intensity;if(data.Length<=limit)break;}}
            compressionCandidateBytes=best;compressionCandidateBitmap=bestBitmap;compressionSlider.Value=bestIntensity;bool reached=best.Length<=limit;
            compressionSizeText.Text=String.Format("目标 {0} KB  →  {1:0.0} KB\n分辨率锁定 {2} × {3} px · 压缩强度 {4:0}%",target,best.Length/1024.0,workingBitmap.PixelWidth,workingBitmap.PixelHeight,bestIntensity);
            imageStatus.Text=reached?"已在保持分辨率不变的情况下匹配目标，点击“应用到当前图片”或“另存压缩副本”":"保持当前分辨率时无法达到目标大小；没有偷偷缩小图片，已提供当前最小候选";
        }

        void SaveCompressionCopy()
        {
            if(workingBitmap==null){imageStatus.Text="当前没有可以压缩的图片";return;}if(compressionCandidateBytes==null)UpdateCompressionEstimate();if(compressionCandidateBytes==null)return;string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG",extension=format=="JPEG"?".jpg":".png",baseName=editingImageItem==null?"博道咪图片":Path.GetFileNameWithoutExtension(editingImageItem.Name);
            var dialog=new Microsoft.Win32.SaveFileDialog{Title="另存压缩图片",Filter=format=="JPEG"?"JPEG 图片 (*.jpg)|*.jpg":"PNG 图片 (*.png)|*.png",DefaultExt=extension,FileName=baseName+"-compressed"+extension};if(dialog.ShowDialog()!=true)return;try{File.WriteAllBytes(dialog.FileName,compressionCandidateBytes);imageStatus.Text=String.Format("压缩副本已保存：{0:0.0} KB · 原图未改动",compressionCandidateBytes.Length/1024.0);}catch(Exception ex){imageStatus.Text="压缩副本保存失败："+ex.Message;}
        }

        void BatchCompressToZip()
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Title="选择要批量压缩的图片",Filter="图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有文件|*.*",Multiselect=true,CheckFileExists=true};if(picker.ShowDialog()!=true||picker.FileNames.Length==0)return;
            var save=new Microsoft.Win32.SaveFileDialog{Title="保存批量压缩包",Filter="ZIP 压缩包 (*.zip)|*.zip",DefaultExt=".zip",FileName="博道咪-压缩图片-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(save.ShowDialog()!=true)return;string[] files=picker.FileNames;string zipPath=save.FileName,format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";double intensity=compressionSlider.Value;imageStatus.Text="正在本地批量压缩 "+files.Length+" 张图片；不会上传网络…";
            ThreadPool.QueueUserWorkItem(delegate{int completed=0;var errors=new List<string>();try{using(var stream=new FileStream(zipPath,FileMode.Create,FileAccess.Write,FileShare.None))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create)){var usedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(string file in files){try{BitmapSource bitmap=LoadEditorBitmap(file),encodedBitmap;byte[] data=EncodeForCompression(bitmap,format,intensity,out encodedBitmap);string name=Path.GetFileNameWithoutExtension(file)+(format=="JPEG"?".jpg":".png"),candidate=name;int suffix=2;while(!usedNames.Add(candidate)){candidate=Path.GetFileNameWithoutExtension(name)+"-"+suffix+(format=="JPEG"?".jpg":".png");suffix++;}var entry=archive.CreateEntry(candidate,CompressionLevel.Optimal);using(var output=entry.Open())output.Write(data,0,data.Length);completed++;}catch(Exception ex){errors.Add(Path.GetFileName(file)+"："+ex.Message);}}}}catch(Exception ex){errors.Add(ex.Message);}UiPost(new Action(delegate{imageStatus.Text=errors.Count==0?("批量压缩完成："+completed+" 张 · 原图未改动 · "+zipPath):("批量压缩完成 "+completed+" 张，失败 "+errors.Count+" 张；"+String.Join("；",errors.Take(2).ToArray()));}));});
        }

        void ShowImageAiSettings()
        {
            if(imageAiSettingsPanel==null)BuildImageAiSettings();
            RestoreShelvedIfNeeded(imageAiSettingsPanel);
            LoadTextProfileSettings(ActiveTextProfile());
            LoadProfileSettings(ActiveImageProfile());
            imageSettingsStatus.Text="文本 Key "+(File.Exists(imageTextKeyFile)?"✓":"○")+" · 当前图像来源 Key "+(!String.IsNullOrWhiteSpace(LoadActiveImageKey())?"✓":"○")+"。可保存多个来源，切换时无需重复填写。";
            // 合规页可独立打开，不能假设 AI 图片窗口已经创建。收纳中的胶囊不能做锚点，否则设置窗会贴着标签定位。
            Window anchor=compliancePanel!=null&&compliancePanel.IsVisible&&!shelvedWindows.ContainsKey(compliancePanel)?compliancePanel:(imageEditorPanel!=null&&imageEditorPanel.IsVisible&&!shelvedWindows.ContainsKey(imageEditorPanel)?imageEditorPanel:null);
            // 该设置页也可从桌宠右键独立打开；隐藏窗口不能做 Owner，否则设置窗会随它被隐藏。
            if(!imageAiSettingsPanel.IsVisible)imageAiSettingsPanel.Owner=anchor;
            if(anchor!=null){imageAiSettingsPanel.Left=anchor.Left+Math.Max(0,(anchor.Width-imageAiSettingsPanel.Width)/2);imageAiSettingsPanel.Top=anchor.Top+45;}
            else {var work=SystemParameters.WorkArea;imageAiSettingsPanel.Left=work.Left+Math.Max(8,(work.Width-imageAiSettingsPanel.Width)/2);imageAiSettingsPanel.Top=work.Top+Math.Max(8,(work.Height-imageAiSettingsPanel.Height)/2);}
            imageAiSettingsPanel.Show();imageAiSettingsPanel.Activate();
        }

        void LoadProfileSettings(ImageProviderProfile profile)
        {
            if(profile==null||imageModelBaseBox==null||imageProfileSettingsLoading)return;imageProfileSettingsLoading=true;
            try{if(imageProfileSettingsCombo!=null){imageProfileSettingsCombo.ItemsSource=null;imageProfileSettingsCombo.ItemsSource=imageAiConfig.ImageProfiles;imageProfileSettingsCombo.SelectedItem=profile;}imageProfileNameBox.Text=profile.Name??"";imageModelBaseBox.Text=profile.BaseUrl??"";imageModelCombo.ItemsSource=null;imageModelCombo.ItemsSource=profile.Models;imageModelCombo.Text=profile.Model??"";imageModelKeyBox.Clear();if(imageProtocolCombo!=null)imageProtocolCombo.SelectedItem=profile.Protocol??"auto";if(imageProfileEditBox!=null)imageProfileEditBox.IsChecked=profile.SupportsEditing;if(imageProfileLayerBox!=null)imageProfileLayerBox.IsChecked=profile.SupportsLayers;if(imageProfileTransparentBox!=null)imageProfileTransparentBox.IsChecked=profile.SupportsTransparency;}finally{imageProfileSettingsLoading=false;}
        }
        void LoadTextProfileSettings(ImageProviderProfile profile)
        {
            if(profile==null||imageTextBaseBox==null||textProfileSettingsLoading)return;textProfileSettingsLoading=true;
            try{if(textProfileSettingsCombo!=null){textProfileSettingsCombo.ItemsSource=null;textProfileSettingsCombo.ItemsSource=imageAiConfig.TextProfiles;textProfileSettingsCombo.SelectedItem=profile;}textProfileNameBox.Text=profile.Name??"";imageTextBaseBox.Text=profile.BaseUrl??"";imageTextModelCombo.ItemsSource=null;imageTextModelCombo.ItemsSource=profile.Models;imageTextModelCombo.Text=profile.Model??"";imageTextKeyBox.Clear();}finally{textProfileSettingsLoading=false;}
        }
        ImageProviderProfile SaveTextProfileSettings(bool switchActive)
        {
            var profile=textProfileSettingsCombo==null?ActiveTextProfile():textProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(profile==null)return null;profile.Name=String.IsNullOrWhiteSpace(textProfileNameBox.Text)?"未命名文本来源":textProfileNameBox.Text.Trim();profile.BaseUrl=(imageTextBaseBox.Text??"").Trim();profile.Model=(imageTextModelCombo.Text??"").Trim();if(profile.Models==null)profile.Models=new List<string>();if(!String.IsNullOrEmpty(profile.Model)&&!profile.Models.Contains(profile.Model))profile.Models.Add(profile.Model);string key=(imageTextKeyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SaveProfileKey(profile.Id,key);imageTextKeyBox.Clear();if(switchActive)SelectTextProfile(profile);SaveImageAiConfig();return profile;
        }
        ImageProviderProfile SaveProfileSettings(bool switchActive)
        {
            var profile=imageProfileSettingsCombo==null?ActiveImageProfile():imageProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(profile==null)return null;
            profile.Name=String.IsNullOrWhiteSpace(imageProfileNameBox.Text)?"未命名来源":imageProfileNameBox.Text.Trim();profile.BaseUrl=(imageModelBaseBox.Text??"").Trim();profile.Model=(imageModelCombo.Text??"").Trim();if(profile.Models==null)profile.Models=new List<string>();if(!String.IsNullOrEmpty(profile.Model)&&!profile.Models.Contains(profile.Model))profile.Models.Add(profile.Model);profile.Protocol=Convert.ToString(imageProtocolCombo.SelectedItem)??"auto";profile.SupportsEditing=imageProfileEditBox.IsChecked==true;profile.SupportsLayers=imageProfileLayerBox.IsChecked==true;profile.SupportsTransparency=imageProfileTransparentBox.IsChecked==true;string key=(imageModelKeyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SaveProfileKey(profile.Id,key);imageModelKeyBox.Clear();if(switchActive)SelectImageProfile(profile);SaveImageAiConfig();return profile;
        }

        void BuildImageAiSettings()
        {
            imageAiSettingsPanel=new Window{Title="图片 AI 设置",Width=620,Height=760,MinWidth=500,MinHeight=600,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true};
            var outer=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(22)};
            Ui.StyleCard(outer); Ui.StyleWindow(imageAiSettingsPanel);
            var stack=new StackPanel();var settingsScroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=stack};
            var header=new Grid{Margin=new Thickness(0,0,0,12)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var heading=new StackPanel();heading.Children.Add(Ui.Title("文本与图像模型",21));heading.Children.Add(Ui.Subtitle("接口地址、Key 与模型独立配置，保存方式保持不变"));header.Children.Add(heading);var close=Ui.MakeCloseButton();close.Click+=delegate{imageAiSettingsPanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);stack.Children.Add(header);AddShelfControl(imageAiSettingsPanel,header,close);EnableWindowInteraction(imageAiSettingsPanel,header);
            stack.Children.Add(new TextBlock{Text="文本来源（识字 / 对话 / 合规）",FontSize=15,FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,5)});var textSourcePicker=new Grid();textSourcePicker.ColumnDefinitions.Add(new ColumnDefinition());textSourcePicker.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});textProfileSettingsCombo=new ComboBox{Height=40,DisplayMemberPath="Name",Padding=new Thickness(10,7,10,7)};textProfileSettingsCombo.SelectionChanged+=delegate{if(textProfileSettingsLoading)return;var selected=textProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(selected!=null)LoadTextProfileSettings(selected);};textSourcePicker.Children.Add(textProfileSettingsCombo);var newTextSource=MakeButton("＋ 新来源",Ui.Neutral);newTextSource.Height=40;newTextSource.Click+=delegate{var profile=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name="新文本来源",Models=new List<string>(),Protocol="auto"};imageAiConfig.TextProfiles.Add(profile);SaveImageAiConfig();LoadTextProfileSettings(profile);};Grid.SetColumn(newTextSource,1);textSourcePicker.Children.Add(newTextSource);stack.Children.Add(textSourcePicker);textProfileNameBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,6,0,0),ToolTip="文本来源名称"};stack.Children.Add(textProfileNameBox);imageTextBaseBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,5,0,0),ToolTip="文本模型 Base URL，例如 https://toapis.com/v1"};stack.Children.Add(imageTextBaseBox);stack.Children.Add(new TextBlock{Text="API Key（留空沿用此来源已保存值）",FontSize=11,Foreground=Ui.SubInk,Margin=new Thickness(0,4,0,2)});imageTextKeyBox=new PasswordBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,5)};stack.Children.Add(imageTextKeyBox);
            var textModels=new Grid();textModels.ColumnDefinitions.Add(new ColumnDefinition());textModels.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});imageTextModelCombo=new ComboBox{Height=40,IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,Padding=new Thickness(10,7,10,7)};EnableModelSearch(imageTextModelCombo,delegate{return imageAiConfig.TextModels;});textModels.Children.Add(imageTextModelCombo);var refreshText=MakeButton("刷新模型",new SolidColorBrush(Color.FromRgb(235,229,222)));refreshText.Height=40;refreshText.Click+=delegate{RefreshImageModels(true);};Grid.SetColumn(refreshText,1);textModels.Children.Add(refreshText);stack.Children.Add(textModels);
            stack.Children.Add(new TextBlock{Text="图像来源（生成 / 重绘 / 精确编辑）",FontSize=15,FontWeight=FontWeights.Bold,Margin=new Thickness(0,16,0,5)});
            var sourcePicker=new Grid();sourcePicker.ColumnDefinitions.Add(new ColumnDefinition());sourcePicker.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});imageProfileSettingsCombo=new ComboBox{Height=40,DisplayMemberPath="Name",Padding=new Thickness(10,7,10,7)};imageProfileSettingsCombo.SelectionChanged+=delegate{if(imageProfileSettingsLoading)return;var selected=imageProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(selected!=null)LoadProfileSettings(selected);};sourcePicker.Children.Add(imageProfileSettingsCombo);var newSource=MakeButton("＋ 新来源",Ui.Neutral);newSource.Height=40;newSource.Click+=delegate{var profile=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name="新图像来源",Models=new List<string>(),Protocol="auto",SupportsEditing=true,SupportsTransparency=true};imageAiConfig.ImageProfiles.Add(profile);SaveImageAiConfig();LoadProfileSettings(profile);};Grid.SetColumn(newSource,1);sourcePicker.Children.Add(newSource);stack.Children.Add(sourcePicker);
            imageProfileNameBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,6,0,0),ToolTip="给这个来源起一个自己看得懂的名字，例如 公司网关、ToApis、本地 ComfyUI"};stack.Children.Add(imageProfileNameBox);
            imageModelBaseBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,5,0,0),ToolTip="此来源的 Base URL；每个来源只需填写并保存一次"};stack.Children.Add(imageModelBaseBox);stack.Children.Add(new TextBlock{Text="API Key（留空沿用该来源已保存值）",FontSize=11,Foreground=Ui.SubInk,Margin=new Thickness(0,4,0,2)});imageModelKeyBox=new PasswordBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,5)};stack.Children.Add(imageModelKeyBox);
            var imageModels=new Grid();imageModels.ColumnDefinitions.Add(new ColumnDefinition());imageModels.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});imageModelCombo=new ComboBox{Height=40,IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,Padding=new Thickness(10,7,10,7)};EnableModelSearch(imageModelCombo,delegate{return imageAiConfig.ImageModels;});imageModels.Children.Add(imageModelCombo);var refreshImage=MakeButton("刷新模型",new SolidColorBrush(Color.FromRgb(235,229,222)));refreshImage.Height=40;refreshImage.Click+=delegate{RefreshImageModels(false);};Grid.SetColumn(refreshImage,1);imageModels.Children.Add(refreshImage);stack.Children.Add(imageModels);
            imageProtocolCombo=new ComboBox{Height=34,Padding=new Thickness(9,5,9,5),ItemsSource=new[]{"auto","openai-images","chat-image","ark"},ToolTip="auto：优先标准 Images API，失败后使用聊天图片回退；ark：火山方舟原生图像协议（支持已开启的专属能力）"};stack.Children.Add(imageProtocolCombo);var capabilityRow=new WrapPanel{Margin=new Thickness(1,5,1,2)};imageProfileEditBox=new CheckBox{Content="支持参考图编辑",Margin=new Thickness(0,0,12,0),IsChecked=true};imageProfileLayerBox=new CheckBox{Content="支持原生图层拆分",Margin=new Thickness(0,0,12,0)};imageProfileTransparentBox=new CheckBox{Content="支持透明 PNG",IsChecked=true};capabilityRow.Children.Add(imageProfileEditBox);capabilityRow.Children.Add(imageProfileLayerBox);capabilityRow.Children.Add(imageProfileTransparentBox);stack.Children.Add(capabilityRow);
            imageSettingsStatus=new TextBlock{Text="“自动”会优先尝试最通用的 OpenAI Images 契约，再回退聊天图片返回。只有来源明确支持时，才开启原生图层拆分。",FontSize=11,Foreground=Ui.SubInk,Margin=new Thickness(0,12,0,8),TextWrapping=TextWrapping.Wrap};stack.Children.Add(imageSettingsStatus);var actions=new WrapPanel();var save=MakeButton("保存并使用此来源",new SolidColorBrush(Color.FromRgb(255,126,115)));save.Foreground=Brushes.White;save.Click+=delegate{SaveImageAiSettings();};var remove=MakeButton("移除当前来源",Brushes.Transparent);remove.Click+=delegate{var profile=imageProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(profile!=null&&imageAiConfig.ImageProfiles.Count>1){imageAiConfig.ImageProfiles.Remove(profile);SelectImageProfile(imageAiConfig.ImageProfiles[0]);SaveImageAiConfig();LoadProfileSettings(ActiveImageProfile());}};var delete=MakeButton("删除所有图像 Key",Brushes.Transparent);delete.Click+=delegate{DeleteImageAiKeys();};actions.Children.Add(save);actions.Children.Add(remove);actions.Children.Add(delete);stack.Children.Add(actions);outer.Child=settingsScroll;imageAiSettingsPanel.Content=outer;imageAiSettingsPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;imageAiSettingsPanel.Hide();}};
        }

        byte[] ImageKeyEntropy(string kind){return Encoding.UTF8.GetBytes("MomoPet.ImageAi."+kind+".v1");}
        string LoadImageAiKey(bool text){return LoadImageAiKey(text,false);}
        string LoadImageAiKey(bool text,bool toApis){string file=toApis?(text?toApisTextKeyFile:toApisImageKeyFile):(text?imageTextKeyFile:imageModelKeyFile);string kind=toApis?"ToApis"+(text?"Text":"Image"):(text?"Text":"Image");try{if(!File.Exists(file))return null;return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file),ImageKeyEntropy(kind),DataProtectionScope.CurrentUser));}catch{return null;}}
        void SaveImageAiKey(bool text,string key,bool toApis){string file=toApis?(text?toApisTextKeyFile:toApisImageKeyFile):(text?imageTextKeyFile:imageModelKeyFile);string kind=toApis?"ToApis"+(text?"Text":"Image"):(text?"Text":"Image");byte[] plain=Encoding.UTF8.GetBytes(key);try{File.WriteAllBytes(file,ProtectedData.Protect(plain,ImageKeyEntropy(kind),DataProtectionScope.CurrentUser));}finally{Array.Clear(plain,0,plain.Length);}}
        void SaveImageAiConfig(){MomoStorage.WriteTextAtomic(imageAiConfigFile,json.Serialize(imageAiConfig),Encoding.UTF8);}
        List<string> SettingsModels(bool text){bool toApis=String.Equals(text?settingsTextProvider:settingsImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase);return text?(toApis?imageAiConfig.ToApisTextModels:imageAiConfig.TextModels):(toApis?imageAiConfig.ToApisImageModels:imageAiConfig.ImageModels);}
        void CaptureSettingsChannel(bool text)
        {
            bool toApis=String.Equals(text?settingsTextProvider:settingsImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase);TextBox baseBox=text?imageTextBaseBox:imageModelBaseBox;ComboBox combo=text?imageTextModelCombo:imageModelCombo;PasswordBox keyBox=text?imageTextKeyBox:imageModelKeyBox;string baseUrl=(baseBox.Text??"").Trim(),model=(combo.Text??"").Trim(),key=(keyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SaveImageAiKey(text,key,toApis);keyBox.Clear();
            if(text){if(toApis){imageAiConfig.ToApisTextBaseUrl=baseUrl;imageAiConfig.ToApisTextModel=model;}else{imageAiConfig.TextBaseUrl=baseUrl;imageAiConfig.TextModel=model;}}else{if(toApis){imageAiConfig.ToApisImageBaseUrl=baseUrl;imageAiConfig.ToApisImageModel=model;}else{imageAiConfig.ImageBaseUrl=baseUrl;imageAiConfig.ImageModel=model;}}
            List<string> list=SettingsModels(text);if(!String.IsNullOrEmpty(model)&&!list.Contains(model))list.Add(model);
        }
        void LoadSettingsChannel(bool text)
        {
            bool toApis=String.Equals(text?settingsTextProvider:settingsImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase);TextBox baseBox=text?imageTextBaseBox:imageModelBaseBox;ComboBox combo=text?imageTextModelCombo:imageModelCombo;PasswordBox keyBox=text?imageTextKeyBox:imageModelKeyBox;baseBox.Text=text?(toApis?imageAiConfig.ToApisTextBaseUrl:imageAiConfig.TextBaseUrl):(toApis?imageAiConfig.ToApisImageBaseUrl:imageAiConfig.ImageBaseUrl);combo.ItemsSource=null;combo.ItemsSource=SettingsModels(text);combo.Text=text?(toApis?imageAiConfig.ToApisTextModel:imageAiConfig.TextModel):(toApis?imageAiConfig.ToApisImageModel:imageAiConfig.ImageModel);keyBox.Clear();keyBox.ToolTip=(toApis?"ToApis":"官方")+(text?"文本":"图像")+" Key；留空沿用已保存值";
        }
        void SaveImageAiSettings(){SaveTextProfileSettings(true);SaveProfileSettings(true);imageAiConfig.TextProvider="自动";imageAiConfig.ImageProvider="自动";SaveImageAiConfig();SyncImageModelCombos();string message="文本与图像来源已保存；切换来源不会重复要求填写 Key";if(imageStatus!=null)imageStatus.Text=message;if(pocketChatStatus!=null)pocketChatStatus.Text=message;if(complianceStatus!=null)complianceStatus.Text=message;imageAiSettingsPanel.Hide();}
        void DeleteImageAiKeys(){try{foreach(string file in new[]{imageTextKeyFile,imageModelKeyFile,toApisTextKeyFile,toApisImageKeyFile})if(File.Exists(file))File.Delete(file);foreach(var profile in imageAiConfig.ImageProfiles){string file=ProfileKeyFile(profile.Id);if(File.Exists(file))File.Delete(file);}}catch{}imageStatus.Text="文本与图像来源 Key 已删除";if(imageSettingsStatus!=null)imageSettingsStatus.Text=imageStatus.Text;}
        void SyncImageModelCombos(){if(imageTextModelCombo!=null){imageTextModelCombo.ItemsSource=null;imageTextModelCombo.ItemsSource=ActiveTextModels();imageTextModelCombo.Text=ActiveTextModel()??"";}var profile=ActiveImageProfile();if(imageQuickModelCombo!=null&&profile!=null){imageProfileCombo.ItemsSource=null;imageProfileCombo.ItemsSource=imageAiConfig.ImageProfiles;imageProfileCombo.SelectedItem=profile;imageQuickModelCombo.ItemsSource=null;imageQuickModelCombo.ItemsSource=profile.Models;imageQuickModelCombo.Text=profile.Model??"";}if(pocketChatSourceCombo!=null){pocketChatSourceCombo.ItemsSource=null;pocketChatSourceCombo.ItemsSource=imageAiConfig.TextProfiles;pocketChatSourceCombo.SelectedItem=ActiveTextProfile();}if(pocketChatModelCombo!=null){pocketChatModelCombo.ItemsSource=null;pocketChatModelCombo.ItemsSource=ActiveTextModels();pocketChatModelCombo.Text=ActiveTextModel()??"";}UpdateImageProviderOptions();}

        void RefreshImageModels(bool text)
        {
            string baseUrl=((text?imageTextBaseBox.Text:imageModelBaseBox.Text)??"").Trim();string typed=text?imageTextKeyBox.Password:imageModelKeyBox.Password;string key=String.IsNullOrWhiteSpace(typed)?(text?LoadActiveTextKey():LoadActiveImageKey()):typed.Trim();if(String.IsNullOrEmpty(baseUrl)||String.IsNullOrEmpty(key)){imageStatus.Text="刷新前请填写 Base URL 和 API Key";imageSettingsStatus.Text=imageStatus.Text;return;}bool toApis=UrlUsesToApis(baseUrl);
            string loading="正在刷新"+(text?"文本":"图像")+"模型…";if(imageStatus!=null)imageStatus.Text=loading;imageSettingsStatus.Text="正在请求 "+baseUrl.TrimEnd('/')+"/models …";var request=new Dictionary<string,object>{{"mode","models"},{"base_url",baseUrl},{"key_type",text?"text":"image"},{"provider",toApis?"toapis":"official"}};
            RunImageHelper(request,text?key:null,text?null:key,delegate(Dictionary<string,object> response){if(!ImageResponseOk(response)){imageSettingsStatus.Text=response!=null&&response.ContainsKey("error")?Convert.ToString(response["error"]):"模型刷新失败";return;}var list=ModelNamesFromResponse(response);if(text){var profile=textProfileSettingsCombo.SelectedItem as ImageProviderProfile??ActiveTextProfile();profile.Models=list;profile.BaseUrl=baseUrl;if(list.Count>0&&String.IsNullOrWhiteSpace(profile.Model))profile.Model=list[0];}else{var profile=imageProfileSettingsCombo.SelectedItem as ImageProviderProfile??ActiveImageProfile();profile.Models=list;profile.BaseUrl=baseUrl;if(list.Count>0&&String.IsNullOrWhiteSpace(profile.Model))profile.Model=list[0];}ComboBox combo=text?imageTextModelCombo:imageModelCombo;combo.ItemsSource=null;combo.ItemsSource=list;if(list.Count>0&&String.IsNullOrWhiteSpace(combo.Text)){combo.SelectedIndex=0;combo.Text=list[0];}combo.IsDropDownOpen=list.Count>0;string message="已刷新 "+list.Count+" 个"+(text?"文本":"图像")+"模型，选择后请保存";if(imageStatus!=null)imageStatus.Text=message;imageSettingsStatus.Text=message;});
        }

        byte[] PrecisionKeyEntropy(){return Encoding.UTF8.GetBytes("MomoPet.PrecisionImage.v1");}
        string LoadPrecisionImageKey(){try{return File.Exists(precisionImageKeyFile)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(precisionImageKeyFile),PrecisionKeyEntropy(),DataProtectionScope.CurrentUser)):null;}catch{return null;}}
        void SavePrecisionImageKey(string key){byte[] plain=Encoding.UTF8.GetBytes(key);try{File.WriteAllBytes(precisionImageKeyFile,ProtectedData.Protect(plain,PrecisionKeyEntropy(),DataProtectionScope.CurrentUser));}finally{Array.Clear(plain,0,plain.Length);}}

        void ShowPrecisionAiSettings()
        {
            if(precisionAiSettingsPanel==null)BuildPrecisionAiSettings();RestoreShelvedIfNeeded(precisionAiSettingsPanel);precisionBaseBox.Text=imageAiConfig.PrecisionBaseUrl??"";precisionModelCombo.ItemsSource=null;precisionModelCombo.ItemsSource=imageAiConfig.PrecisionModels;precisionModelCombo.Text=imageAiConfig.PrecisionModel??"";precisionKeyBox.Clear();precisionSettingsStatus.Text=File.Exists(precisionImageKeyFile)?"Key 已加密保存；此处的设置只用于精确编辑与图层拆分。":"请填写可调用 doubao-seedream-5-0-pro 的 Base URL 和 API Key。";precisionAiSettingsPanel.Left=imageEditorPanel.Left+Math.Max(0,(imageEditorPanel.Width-precisionAiSettingsPanel.Width)/2);precisionAiSettingsPanel.Top=imageEditorPanel.Top+55;precisionAiSettingsPanel.Show();precisionAiSettingsPanel.Activate();
        }

        void BuildPrecisionAiSettings()
        {
            precisionAiSettingsPanel=new Window{Title="火山方舟精确编辑设置",Width=560,Height=500,MinWidth=460,MinHeight=420,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,Owner=imageEditorPanel};var outer=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(22)};Ui.StyleCard(outer);Ui.StyleWindow(precisionAiSettingsPanel);var stack=new StackPanel();var header=new Grid{Margin=new Thickness(0,0,0,10)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var heading=new StackPanel();heading.Children.Add(Ui.Title("火山方舟精确编辑",21));heading.Children.Add(Ui.Subtitle("独立配置 Seedream 精确编辑与图层拆分通道"));header.Children.Add(heading);var close=Ui.MakeCloseButton();close.Click+=delegate{precisionAiSettingsPanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);stack.Children.Add(header);AddShelfControl(precisionAiSettingsPanel,header,close);EnableWindowInteraction(precisionAiSettingsPanel,header);
            stack.Children.Add(new TextBlock{Text="直连火山方舟图片 API。Base URL 填 https://ark.cn-beijing.volces.com/api/v3；模型可填推理接入点 ID 或 doubao-seedream-5-0-pro-260628。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,8)});stack.Children.Add(new TextBlock{Text="Base URL",FontWeight=FontWeights.Bold});precisionBaseBox=new TextBox{Height=42,Padding=new Thickness(12,5,12,5),VerticalContentAlignment=VerticalAlignment.Center,ToolTip="https://ark.cn-beijing.volces.com/api/v3"};stack.Children.Add(precisionBaseBox);stack.Children.Add(new TextBlock{Text="API Key（留空沿用已保存值）",Foreground=Ui.SubInk,FontSize=11,Margin=new Thickness(0,5,0,2)});precisionKeyBox=new PasswordBox{Height=42,Padding=new Thickness(12,5,12,5),VerticalContentAlignment=VerticalAlignment.Center};stack.Children.Add(precisionKeyBox);stack.Children.Add(new TextBlock{Text="模型 / 推理接入点",FontWeight=FontWeights.Bold,Margin=new Thickness(0,10,0,3)});
            var models=new Grid();models.ColumnDefinitions.Add(new ColumnDefinition());models.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});precisionModelCombo=new ComboBox{Height=40,IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,Padding=new Thickness(10,7,10,7)};EnableModelSearch(precisionModelCombo,delegate{return imageAiConfig.PrecisionModels;});models.Children.Add(precisionModelCombo);var refresh=MakeButton("刷新模型",new SolidColorBrush(Color.FromRgb(235,229,222)));refresh.Height=40;refresh.Click+=delegate{RefreshPrecisionModels();};Grid.SetColumn(refresh,1);models.Children.Add(refresh);stack.Children.Add(models);precisionSettingsStatus=new TextBlock{Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)};stack.Children.Add(precisionSettingsStatus);var actions=new WrapPanel();var save=MakeButton("保存设置",new SolidColorBrush(Color.FromRgb(255,126,115)));save.Foreground=Brushes.White;save.Click+=delegate{SavePrecisionAiSettings();};var remove=MakeButton("删除此 Key",Brushes.Transparent);remove.Click+=delegate{try{if(File.Exists(precisionImageKeyFile))File.Delete(precisionImageKeyFile);}catch{}precisionSettingsStatus.Text="精确编辑 Key 已删除";};actions.Children.Add(save);actions.Children.Add(remove);stack.Children.Add(actions);outer.Child=stack;precisionAiSettingsPanel.Content=outer;precisionAiSettingsPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;precisionAiSettingsPanel.Hide();}};
        }

        void SavePrecisionAiSettings()
        {
            string baseUrl=(precisionBaseBox.Text??"").Trim(),model=(precisionModelCombo.Text??"").Trim(),key=(precisionKeyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SavePrecisionImageKey(key);imageAiConfig.PrecisionBaseUrl=baseUrl;imageAiConfig.PrecisionModel=model;if(!String.IsNullOrEmpty(model)&&!imageAiConfig.PrecisionModels.Contains(model))imageAiConfig.PrecisionModels.Add(model);SaveImageAiConfig();if(precisionStatus!=null)precisionStatus.Text="精确编辑设置已保存。";precisionAiSettingsPanel.Hide();
        }

        void RefreshPrecisionModels()
        {
            string baseUrl=(precisionBaseBox.Text??"").Trim(),typed=(precisionKeyBox.Password??"").Trim(),key=String.IsNullOrEmpty(typed)?LoadPrecisionImageKey():typed;if(String.IsNullOrEmpty(baseUrl)||String.IsNullOrEmpty(key)){precisionSettingsStatus.Text="刷新前请填写 Base URL 和 API Key";return;}precisionSettingsStatus.Text="正在刷新模型…";RunImageHelper(new Dictionary<string,object>{{"mode","models"},{"base_url",baseUrl},{"key_type","image"}},null,key,delegate(Dictionary<string,object> response){if(!ImageResponseOk(response)){precisionSettingsStatus.Text=imageStatus.Text;return;}imageAiConfig.PrecisionModels=ModelNamesFromResponse(response);precisionModelCombo.ItemsSource=null;precisionModelCombo.ItemsSource=imageAiConfig.PrecisionModels;if(imageAiConfig.PrecisionModels.Count>0){precisionModelCombo.Text=imageAiConfig.PrecisionModels[0];precisionModelCombo.IsDropDownOpen=true;}precisionSettingsStatus.Text="已刷新 "+imageAiConfig.PrecisionModels.Count+" 个模型，选择后点击保存";});
        }

        bool ValidatePrecisionAi()
        {
            if(String.IsNullOrWhiteSpace(ActiveImageBaseUrl())||String.IsNullOrWhiteSpace(ActiveImageModel())||String.IsNullOrWhiteSpace(LoadActiveImageKey())){imageStatus.Text="请先选择并配置图像来源、API Key 和模型";ShowImageAiSettings();return false;}return true;
        }

        // 方舟的 background: transparent 用于精确编辑时，要求参考 PNG 里本来就有至少一个真实的 Alpha 像素。
        // 提交前在本地拦截，避免用户等到接口返回 400 才知道条件不满足。
        bool HasTransparentPixels(BitmapSource source)
        {
            if(source==null||source.PixelWidth<1||source.PixelHeight<1)return false;
            try{
                var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int stride=converted.PixelWidth*4;var pixels=new byte[stride*converted.PixelHeight];converted.CopyPixels(pixels,stride,0);
                for(int index=3;index<pixels.Length;index+=4)if(pixels[index]<255)return true;
            }catch{}return false;
        }

        void RunPrecisionImageRequest(bool layers)
        {
            if(workingBitmap==null){imageStatus.Text="请先打开一张图片";return;}if(imageAiBusy||!ValidatePrecisionAi())return;string prompt=(precisionPromptBox.Text??"").Trim();if(layers)prompt=String.IsNullOrEmpty(prompt)?"将图片拆分为可独立使用的前景元素和背景图层，保持所有图层透明区域为真正的 Alpha PNG。":prompt+"\n同时将图片拆分为可独立使用的前景元素和背景图层，透明区域必须保留 Alpha。";if(String.IsNullOrEmpty(prompt)){imageStatus.Text="请填写对标记区域的修改要求";return;}
            if(layers&&!ActiveImageSupportsLayers()){imageStatus.Text="当前来源未声明支持原生图层拆分。请在“管理图像来源”中选择支持该能力的来源；普通图像模型仍可用于局部编辑。";return;}
            if(layers){preLayerSplitBitmap=workingBitmap;preLayerSplitEncodedBytes=workingEncodedBytes??EncodeBitmap(workingBitmap,"PNG",100);}
            string input=null;string tag="";if(precisionMode=="point"&&!Double.IsNaN(precisionPoint.X))tag=PrecisionPointTag();else if(precisionMode=="bbox"&&!precisionSelectionRect.IsEmpty)tag=PrecisionBoxTag();else if((precisionMode=="lasso"||precisionMode=="doodle"||precisionMode=="arrow")&&precisionPath.Count>1){input=PreparePrecisionMarkedReference();prompt="参考图中红色标记就是需要处理的区域。"+prompt;}if(String.IsNullOrEmpty(input))input=layers?PrepareLayerDecompositionInput():PrepareWorkingImageFile();if(!String.IsNullOrEmpty(tag))prompt="请仅处理 image 1 "+tag+" 指向的区域，其余内容不变。\n"+prompt;
            string size=precisionSizeBox==null?"auto":Convert.ToString(precisionSizeBox.SelectedItem);string outputFormat=precisionOutputFormatBox==null?"png":Convert.ToString(precisionOutputFormatBox.SelectedItem).ToLowerInvariant();string optimize=precisionOptimizeBox!=null&&Convert.ToString(precisionOptimizeBox.SelectedItem)=="快速"?"fast":"standard";bool transparent=precisionTransparentBackgroundBox!=null&&precisionTransparentBackgroundBox.IsChecked==true;
            bool ark=String.Equals(ActiveImageProtocol(),"ark",StringComparison.OrdinalIgnoreCase);
            if(transparent&&!ActiveImageSupportsTransparency()){imageStatus.Text="当前来源未声明支持透明 PNG；请在来源设置中开启并确认模型能力，或关闭透明通道。";return;}
            if(transparent&&!layers&&ark&&!HasTransparentPixels(workingBitmap)){imageStatus.Text="当前参考图没有 Alpha 通道。该原生局部编辑接口要求上传带真实透明像素的 PNG；请关闭“透明通道”，或改用带透明区域的 PNG。";return;}
            // 图层拆分本身返回透明 PNG 图层，不需要也不应附带 background: transparent。
            if(layers&&transparent){transparent=false;if(precisionTransparentBackgroundBox!=null)precisionTransparentBackgroundBox.IsChecked=false;}
            imageAiBusy=true;
            if(!layers&&!ark){string output=Path.Combine(imageTempDir,"result-"+Guid.NewGuid().ToString("N")+".png");imageStatus.Text="正在用当前来源提交局部编辑…";var request=new Dictionary<string,object>{{"mode","edit"},{"provider","auto"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"image_path",input},{"output_path",output},{"prompt",prompt},{"size",imageAiConfig.ImageSize},{"transparent_background",transparent},{"quality",imageAiConfig.ImageQuality}};TrackImageTask(request,false);RunImageHelper(request,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandleGeneratedImageResponse(response,output);});return;}
            string outputDir=Path.Combine(imageTempDir,"precision-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outputDir);imageStatus.Text=layers?"正在提交原生图层拆分任务（将返回底图与透明 PNG 图层）…":"正在提交当前来源的原生局部编辑任务…";var nativeRequest=new Dictionary<string,object>{{"mode","precision"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"image_path",input},{"output_dir",outputDir},{"prompt",prompt},{"size",size},{"output_format",outputFormat},{"transparent_background",transparent},{"optimize_mode",optimize},{"layer_decomposition",layers}};TrackImageTask(nativeRequest,layers);RunImageHelper(nativeRequest,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandlePrecisionImageResponse(response,layers);});
        }

        void HandlePrecisionImageResponse(Dictionary<string,object> response,bool layers)
        {
            imageAiBusy=false;if(!ImageResponseOk(response))return;var paths=response.ContainsKey("output_paths")?response["output_paths"] as System.Collections.IList:null;if(paths==null||paths.Count==0){imageStatus.Text="精确编辑未返回可预览图片";FinishImageTask(false,imageStatus.Text);return;}precisionResultPaths.Clear();foreach(object item in paths){string path=Convert.ToString(item);if(File.Exists(path))precisionResultPaths.Add(path);}if(precisionResultPaths.Count==0){imageStatus.Text="精确编辑图片下载失败";FinishImageTask(false,imageStatus.Text);return;}
            precisionLayers.Clear();layerCompositionActive=false;selectedPrecisionLayer=null;
            if(layers){ShowTransparentLayerCanvas();BuildPrecisionLayerCanvas(response);}else ShowPrecisionResultAt(0);
            var labels=new List<string>();for(int i=0;i<precisionResultPaths.Count;i++)labels.Add((layers?(i==0?"拆分参考图（不显示）":"透明图层 "+i):"编辑结果 "+(i+1))+" · "+Path.GetFileName(precisionResultPaths[i]));precisionResultBox.ItemsSource=null;precisionResultBox.ItemsSource=labels;precisionResultBox.Visibility=layers?Visibility.Collapsed:Visibility.Visible;if(!layers)precisionResultBox.SelectedIndex=0;
            imageStatus.Text=layers?"图层拆分完成：画布为透明底，仅显示拆出的图层。":"精确编辑完成；可预览后保存到中转袋。";FinishImageTask(true,layers?"已返回 "+Math.Max(0,precisionResultPaths.Count-1)+" 个透明图层，可继续移动、缩放和导出。":"局部编辑结果已载入画布；可按住“对比原图”检查。");if(precisionStatus!=null)precisionStatus.Text=layers?"拆分前原图未叠在画布上；选中图层后可拖动、缩放及调整层级，需要时点“恢复拆分前原图”。":"结果保存在临时工作区；选中预览后可用底部“保存到中转袋 / 覆盖原图”确认。";
        }

        void ShowTransparentLayerCanvas()
        {
            try
            {
                BitmapSource reference=LoadEditorBitmap(precisionResultPaths[0]);var blank=new WriteableBitmap(reference.PixelWidth,reference.PixelHeight,96,96,PixelFormats.Pbgra32,null);blank.Freeze();workingBitmap=blank;workingEncodedBytes=EncodeBitmap(blank,"PNG",100);ClearCompressionCandidate();ClearImageTextLayer();ClearPrecisionMarks();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();outputFormatBox.SelectedItem="PNG";ScheduleCompressionEstimate();ResetImageHistory();
            }
            catch(Exception ex){throw new InvalidOperationException("无法创建透明图层画布："+ex.Message);}
        }

        void RequestVisionLayerLayout()
        {
            if(precisionLayers.Count==0||precisionResultPaths.Count<2)return;if(!HasTextOcrConfiguration()){imageStatus.Text="图层已拆分，但未配置支持识图的文本模型；当前只能使用本地初始位置。";return;}imageAiBusy=true;var paths=new List<string>();paths.Add(precisionResultPaths[0]);paths.AddRange(precisionLayers.Select(x=>x.Path));var request=new Dictionary<string,object>{{"mode","layer_layout"},{"base_url",ActiveTextBaseUrl()},{"model",ActiveTextModel()},{"image_paths",paths}};RunImageHelper(request,LoadActiveTextKey(),null,delegate(Dictionary<string,object> response){imageAiBusy=false;if(!ImageResponseOk(response)){imageStatus.Text="视觉文本模型未能完成图层定位，保留可手动调整的图层画布。";return;}ApplyVisionLayerLayout(response);});
        }

        void ApplyVisionLayerLayout(Dictionary<string,object> response)
        {
            var values=response.ContainsKey("layers")?response["layers"] as System.Collections.IEnumerable:null;int placed=0;if(values!=null)foreach(object value in values){var map=value as Dictionary<string,object>;if(map==null)continue;int index;try{index=Convert.ToInt32(map["index"]);}catch{continue;}if(index<1||index>precisionLayers.Count)continue;double x=NumberFromMap(map,"x"),y=NumberFromMap(map,"y"),w=NumberFromMap(map,"width"),h=NumberFromMap(map,"height");if(w<=0||h<=0||w>1.5||h>1.5)continue;var layer=precisionLayers[index-1];layer.X=Math.Max(-workingBitmap.PixelWidth*.5,Math.Min(workingBitmap.PixelWidth,x*workingBitmap.PixelWidth));layer.Y=Math.Max(-workingBitmap.PixelHeight*.5,Math.Min(workingBitmap.PixelHeight,y*workingBitmap.PixelHeight));layer.Width=w*workingBitmap.PixelWidth;layer.Height=h*workingBitmap.PixelHeight;layer.NaturalWidth=layer.Width;layer.NaturalHeight=layer.Height;if(map.ContainsKey("z"))try{layer.Z=Convert.ToInt32(map["z"]);}catch{}placed++;}RefreshPrecisionLayerList();LayoutPrecisionLayers();imageStatus.Text=placed>0?"视觉文本模型已还原 "+placed+" 个图层的位置、大小和层级；仍可在画布上微调。":"视觉文本模型未返回可用坐标，保留可手动调整的图层画布。";if(precisionStatus!=null)precisionStatus.Text="拖动图层移动位置；使用“图层缩放”调整大小；上移/下移改变前后关系。";
        }

        void BuildPrecisionLayerCanvas(Dictionary<string,object> response)
        {
            if(precisionResultPaths.Count<2||workingBitmap==null)return;var raw=response.ContainsKey("layers")?response["layers"] as System.Collections.IList:null;
            for(int i=1;i<precisionResultPaths.Count;i++){try{BitmapSource bitmap=LoadEditorBitmap(precisionResultPaths[i]);var layer=new PrecisionLayer{Name="◉ 图层 "+i,Path=precisionResultPaths[i],Bitmap=bitmap,Z=i-1,Width=bitmap.PixelWidth,Height=bitmap.PixelHeight,NaturalWidth=bitmap.PixelWidth,NaturalHeight=bitmap.PixelHeight};Dictionary<string,object> meta=raw!=null&&i<raw.Count?raw[i] as Dictionary<string,object>:null;bool hasPosition=false;if(meta!=null){if(meta.ContainsKey("name")&&!String.IsNullOrWhiteSpace(Convert.ToString(meta["name"])))layer.Name="◉ "+Convert.ToString(meta["name"]);if(meta.ContainsKey("z_index"))try{layer.Z=Convert.ToInt32(meta["z_index"]);}catch{}hasPosition=ApplyLayerBoundingBox(layer,meta,workingBitmap.PixelWidth,workingBitmap.PixelHeight);}if(!hasPosition){layer.X=(workingBitmap.PixelWidth-layer.Width)/2.0;layer.Y=(workingBitmap.PixelHeight-layer.Height)/2.0;}precisionLayers.Add(layer);}catch(Exception ex){imageStatus.Text="第 "+i+" 个图层无法载入："+ex.Message;}}
            if(precisionLayers.Count==0)return;layerCompositionActive=true;selectedPrecisionLayer=precisionLayers.OrderByDescending(x=>x.Z).First();RefreshPrecisionLayerList();LayoutPrecisionLayers();
        }

        bool ApplyLayerBoundingBox(PrecisionLayer layer,Dictionary<string,object> meta,int baseWidth,int baseHeight)
        {
            object raw;if(!meta.TryGetValue("bounding_box",out raw)||raw==null)return false;double x=0,y=0,w=0,h=0;var list=raw as System.Collections.IList;if(list!=null&&list.Count>=4){try{x=Convert.ToDouble(list[0]);y=Convert.ToDouble(list[1]);w=Convert.ToDouble(list[2]);h=Convert.ToDouble(list[3]);}catch{return false;}}else{var box=raw as Dictionary<string,object>;if(box==null)return false;try{x=Convert.ToDouble(box.ContainsKey("x")?box["x"]:box["left"]);y=Convert.ToDouble(box.ContainsKey("y")?box["y"]:box["top"]);w=Convert.ToDouble(box.ContainsKey("width")?box["width"]:box["w"]);h=Convert.ToDouble(box.ContainsKey("height")?box["height"]:box["h"]);}catch{return false;}}
            double max=Math.Max(Math.Max(Math.Abs(x),Math.Abs(y)),Math.Max(Math.Abs(w),Math.Abs(h)));if(max<=1.01){x*=baseWidth;y*=baseHeight;w*=baseWidth;h*=baseHeight;}else if(max<=1000&&baseWidth>1000){x=x/1000*baseWidth;y=y/1000*baseHeight;w=w/1000*baseWidth;h=h/1000*baseHeight;}if(w>1&&h>1){layer.X=x;layer.Y=y;layer.Width=w;layer.Height=h;layer.NaturalWidth=w;layer.NaturalHeight=h;return true;}return false;
        }

        // 接口未提供 bbox 时，使用透明 PNG 的可见像素在原图里寻找最接近的位置。
        // 图层本身若保留了原始画布尺寸则无需匹配，直接从 (0,0) 绘制即可。
        void AutoPlacePrecisionLayer(PrecisionLayer layer,BitmapSource baseImage)
        {
            if(layer.Bitmap==null||baseImage==null)return;if(layer.Bitmap.PixelWidth==baseImage.PixelWidth&&layer.Bitmap.PixelHeight==baseImage.PixelHeight){layer.X=0;layer.Y=0;return;}if(layer.Width>baseImage.PixelWidth||layer.Height>baseImage.PixelHeight){double fit=Math.Min((double)baseImage.PixelWidth/layer.Width,(double)baseImage.PixelHeight/layer.Height);layer.Width*=fit;layer.Height*=fit;layer.NaturalWidth=layer.Width;layer.NaturalHeight=layer.Height;}
            try{var base32=new FormatConvertedBitmap(baseImage,PixelFormats.Bgra32,null,0);var layer32=new FormatConvertedBitmap(layer.Bitmap,PixelFormats.Bgra32,null,0);int bw=base32.PixelWidth,bh=base32.PixelHeight,lw=layer32.PixelWidth,lh=layer32.PixelHeight;byte[] bp=new byte[bw*bh*4],lp=new byte[lw*lh*4];base32.CopyPixels(bp,bw*4,0);layer32.CopyPixels(lp,lw*4,0);var samples=new List<Point>();int stride=Math.Max(3,Math.Min(lw,lh)/18);for(int sy=stride/2;sy<lh&&samples.Count<72;sy+=stride)for(int sx=stride/2;sx<lw&&samples.Count<72;sx+=stride){if(lp[(sy*lw+sx)*4+3]>110)samples.Add(new Point(sx,sy));}if(samples.Count<6){layer.X=(bw-layer.Width)/2;layer.Y=(bh-layer.Height)/2;return;}int step=Math.Max(8,Math.Min(Math.Max(1,bw-lw),Math.Max(1,bh-lh))/42);double best=Double.MaxValue;int bestX=0,bestY=0;for(int y=0;y<=Math.Max(0,bh-lh);y+=step)for(int x=0;x<=Math.Max(0,bw-lw);x+=step){double score=0;foreach(Point p in samples){int si=((int)p.Y*lw+(int)p.X)*4,bi=((y+(int)p.Y)*bw+x+(int)p.X)*4;int db=bp[bi]-lp[si],dg=bp[bi+1]-lp[si+1],dr=bp[bi+2]-lp[si+2];score+=db*db+dg*dg+dr*dr;if(score>=best)break;}if(score<best){best=score;bestX=x;bestY=y;}}layer.X=bestX;layer.Y=bestY;}
            catch{layer.X=(baseImage.PixelWidth-layer.Width)/2;layer.Y=(baseImage.PixelHeight-layer.Height)/2;}
        }

        void ShowPrecisionResult()
        {
            if(precisionResultBox==null||precisionResultBox.SelectedIndex<0||precisionResultBox.SelectedIndex>=precisionResultPaths.Count)return;ShowPrecisionResultAt(precisionResultBox.SelectedIndex);
        }
        void ShowPrecisionResultAt(int index){if(index<0||index>=precisionResultPaths.Count)return;try{string path=precisionResultPaths[index];workingEncodedBytes=File.ReadAllBytes(path);workingBitmap=LoadEditorBytes(workingEncodedBytes);ClearCompressionCandidate();ClearImageTextLayer();ClearPrecisionMarks();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();outputFormatBox.SelectedItem="PNG";ScheduleCompressionEstimate();PushImageState();}catch(Exception ex){imageStatus.Text="结果预览失败："+ex.Message;}}

        string PrepareWorkingImageFile(){string path=Path.Combine(imageTempDir,"input-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(path,EncodeBitmap(workingBitmap,"PNG",100));return path;}
        string PrepareLayerDecompositionInput()
        {
            // 未修改的图片必须原样交给模型：不缩放、不压缩、不转换格式、不铺白底。
            if(!layerCompositionActive&&workingEncodedBytes==null&&editingImageItem!=null&&!String.IsNullOrWhiteSpace(editingImageItem.Value)&&File.Exists(editingImageItem.Value))return editingImageItem.Value;
            // AI 返回的当前图片已有原始字节时，同样逐字节写出，保持来源文件编码与尺寸。
            if(!layerCompositionActive&&workingEncodedBytes!=null){string ext=workingEncodedBytes.Length>8&&workingEncodedBytes[0]==137&&workingEncodedBytes[1]==80?".png":".jpg";string direct=Path.Combine(imageTempDir,"layer-input-"+Guid.NewGuid().ToString("N")+ext);File.WriteAllBytes(direct,workingEncodedBytes);return direct;}
            // 只有用户已在画布上改变图层位置/尺寸、当前不存在源文件时，才将“当前画布状态”导出；不做任何缩放。
            return PrepareWorkingImageFile();
        }

        void DeleteTemporaryImageFile(string path)
        {
            try{if(String.IsNullOrWhiteSpace(path))return;string tempRoot=Path.GetFullPath(imageTempDir).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;string full=Path.GetFullPath(path);if(full.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)&&File.Exists(full))File.Delete(full);}catch{}
        }
        bool ValidateImageAi(bool text){string baseUrl=text?ActiveTextBaseUrl():ActiveImageBaseUrl(),model=text?ActiveTextModel():ActiveImageModel(),key=text?LoadActiveTextKey():LoadActiveImageKey();if(String.IsNullOrEmpty(baseUrl)||String.IsNullOrEmpty(model)||String.IsNullOrEmpty(key)){imageStatus.Text="请先配置"+(text?"文本":"图像")+"来源的 Base URL、API Key 和模型";ShowImageAiSettings();return false;}return true;}

        void RunImageOcr()
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}if(imageAiBusy||!ValidateImageAi(true))return;imageAiBusy=true;string input=PrepareWorkingImageFile();imageStatus.Text="文本模型正在识别图片文字…";var request=new Dictionary<string,object>{{"mode","ocr"},{"base_url",ActiveTextBaseUrl()},{"model",ActiveTextModel()},{"image_path",input},{"provider",TextUsesToApis()?"toapis":"official"}};
            RunImageHelper(request,LoadActiveTextKey(),null,delegate(Dictionary<string,object> response){imageAiBusy=false;try{File.Delete(input);}catch{}if(!ImageResponseOk(response))return;recognizedTextBox.Text=Convert.ToString(response["text"]);imageStatus.Text="识别完成，可修改文字后点“按文字改图”";});
        }

        void RunImageTextSelection()
        {
            StartImageTextSelection(true);
        }

        void StartImageTextSelection(bool userInitiated)
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}
            if(imageAiBusy)return;
            if(!userInitiated&&TryLoadImageTextCache(editingImageItem==null?null:editingImageItem.Value)){LayoutImageTextLayer();imageStatus.Text="文字已就绪：可跨行拖选，Ctrl+A 全选，Ctrl+C 复制";return;}
            ExitCropMode();ClearImageTextLayer();imageAiBusy=true;string input=PrepareWorkingImageFile();imageStatus.Text="本地识字中…";
            RunLocalOcr(input,delegate(LocalOcrOutput output,string localError)
            {
                if(output!=null&&output.Blocks.Count>0){ReadLocalOcrOutput(output);imageAiBusy=false;try{File.Delete(input);}catch{}if(editingImageItem!=null&&File.Exists(editingImageItem.Value))SaveImageTextCache(editingImageItem.Value);LayoutImageTextLayer();imageStatus.Text="文字已就绪：可跨行拖选，Ctrl+A 全选，Ctrl+C 复制";return;}
                if(HasTextOcrConfiguration()){StartRemoteImageTextSelection(input);return;}imageAiBusy=false;try{File.Delete(input);}catch{}imageStatus.Text="本地 OCR 没有识别到文字";
            });
        }

        void StartRemoteImageTextSelection(string input)
        {
            imageStatus.Text="本地 OCR 未识别到文字，正在使用文本模型兜底…";var request=new Dictionary<string,object>{{"mode","ocr_layout"},{"base_url",ActiveTextBaseUrl()},{"model",ActiveTextModel()},{"image_path",input},{"provider",TextUsesToApis()?"toapis":"official"}};
            RunImageHelper(request,LoadActiveTextKey(),null,delegate(Dictionary<string,object> response){imageAiBusy=false;try{File.Delete(input);}catch{}if(!ImageResponseOk(response))return;ReadImageTextResponse(response);if(editingImageItem!=null&&File.Exists(editingImageItem.Value))SaveImageTextCache(editingImageItem.Value);LayoutImageTextLayer();imageStatus.Text="文字已就绪：可跨行拖选，Ctrl+A 全选，Ctrl+C 复制";});
        }

        void ReadLocalOcrOutput(LocalOcrOutput output)
        {
            imageTextRegions.Clear();foreach(LocalOcrBlock block in output.Blocks)if(!String.IsNullOrWhiteSpace(block.Text))imageTextRegions.Add(new ImageTextRegion{Text=block.Text,X=block.X,Y=block.Y,Width=Math.Max(.002,block.Width),Height=Math.Max(.002,block.Height)});recognizedTextBox.Text=output.Text??BuildSelectedImageText(0,imageTextRegions.Count-1,imageTextRegions);
        }

        bool HasTextOcrConfiguration(){return imageAiConfig!=null&&!String.IsNullOrWhiteSpace(ActiveTextBaseUrl())&&!String.IsNullOrWhiteSpace(ActiveTextModel())&&!String.IsNullOrWhiteSpace(LoadActiveTextKey());}

        void ReadImageTextResponse(Dictionary<string,object> response)
        {
            imageTextRegions.Clear();object raw;
            if(response.TryGetValue("blocks",out raw)){var values=raw as System.Collections.IEnumerable;if(values!=null)foreach(object value in values){var map=value as Dictionary<string,object>;if(map==null)continue;string text=map.ContainsKey("text")?Convert.ToString(map["text"]):"";if(String.IsNullOrWhiteSpace(text))continue;imageTextRegions.Add(new ImageTextRegion{Text=text,X=NumberFromMap(map,"x"),Y=NumberFromMap(map,"y"),Width=Math.Max(.02,NumberFromMap(map,"width")),Height=Math.Max(.02,NumberFromMap(map,"height"))});}}
            recognizedTextBox.Text=response.ContainsKey("text")?Convert.ToString(response["text"]):String.Join(Environment.NewLine,imageTextRegions.Select(x=>x.Text));
        }

        string ImageTextCachePath(string imagePath)
        {
            if(String.IsNullOrWhiteSpace(imagePath)||!File.Exists(imagePath))return null;var file=new FileInfo(imagePath);string identity="local-geometry-v2|"+Path.GetFullPath(imagePath).ToLowerInvariant()+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
            using(var sha=SHA256.Create()){string name=String.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)).Select(x=>x.ToString("x2")));return Path.Combine(imageOcrCacheDir,name+".json");}
        }

        bool TryLoadImageTextCache(string imagePath)
        {
            try{string path=ImageTextCachePath(imagePath);if(String.IsNullOrEmpty(path)||!File.Exists(path))return false;var cache=json.Deserialize<ImageTextCache>(File.ReadAllText(path,Encoding.UTF8));if(cache==null||cache.Regions==null||cache.Regions.Count==0)return false;imageTextRegions.Clear();imageTextRegions.AddRange(cache.Regions);recognizedTextBox.Text=cache.Text??String.Join(Environment.NewLine,imageTextRegions.Select(x=>x.Text));return true;}catch{return false;}
        }

        void SaveImageTextCache(string imagePath)
        {
            SaveImageTextCache(imagePath,new ImageTextCache{Text=recognizedTextBox.Text,Regions=imageTextRegions.ToList()});
        }

        void SaveImageTextCache(string imagePath,ImageTextCache cache)
        {
            try{string path=ImageTextCachePath(imagePath);if(String.IsNullOrEmpty(path)||cache==null||cache.Regions==null||cache.Regions.Count==0)return;File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cache),new UTF8Encoding(false));}catch{}
        }

        void QueueImageTextPrecache(string imagePath)
        {
            try{if(String.IsNullOrWhiteSpace(imagePath)||!File.Exists(imagePath))return;string cachePath=ImageTextCachePath(imagePath);if(!String.IsNullOrEmpty(cachePath)&&File.Exists(cachePath))return;lock(imageOcrPrecacheQueue){if(!imageOcrPrecacheQueue.Contains(imagePath))imageOcrPrecacheQueue.Enqueue(imagePath);if(imageOcrPrecacheRunning)return;imageOcrPrecacheRunning=true;}ProcessImageTextPrecacheQueue();}catch{}
        }

        void ProcessImageTextPrecacheQueue()
        {
            string imagePath=null;lock(imageOcrPrecacheQueue){if(imageOcrPrecacheQueue.Count>0)imagePath=imageOcrPrecacheQueue.Dequeue();else{imageOcrPrecacheRunning=false;return;}}
            RunLocalOcr(imagePath,delegate(LocalOcrOutput output,string error){try{if(output!=null&&output.Blocks.Count>0){var regions=output.Blocks.Select(block=>new ImageTextRegion{Text=block.Text,X=block.X,Y=block.Y,Width=block.Width,Height=block.Height}).ToList();SaveImageTextCache(imagePath,new ImageTextCache{Text=output.Text,Regions=regions});}}catch{}finally{ProcessImageTextPrecacheQueue();}});
        }

        void RunLocalOcr(string imagePath,Action<LocalOcrOutput,string> completed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                LocalOcrOutput result=null;string error=null;
                try
                {
                    var psi=new ProcessStartInfo{FileName=EmbeddedRuntime.ResolveFile("MomoOcr.exe",root),Arguments="\""+imagePath+"\"",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};string output,errors;
                    using(var process=Process.Start(psi)){localOcrProcess=process;output=process.StandardOutput.ReadToEnd();errors=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)error=String.IsNullOrWhiteSpace(errors)?"本地 OCR 失败":errors.Trim();}localOcrProcess=null;
                    if(error==null){result=new LocalOcrOutput();foreach(string line in output.Replace("\r","").Split('\n')){if(String.IsNullOrWhiteSpace(line))continue;string[] parts=line.Split('\t');if(parts.Length>=2&&parts[0]=="TEXT")result.Text=Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));else if(parts.Length>=6&&parts[0]=="BLOCK"){double x,y,w,h;if(Double.TryParse(parts[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out x)&&Double.TryParse(parts[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out y)&&Double.TryParse(parts[4],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out w)&&Double.TryParse(parts[5],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out h))result.Blocks.Add(new LocalOcrBlock{Text=Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])),X=x,Y=y,Width=w,Height=h});}}}
                }
                catch(Exception ex){error=ex.Message;}
                UiPost(new Action(delegate{completed(result,error);}));
            });
        }

        double NumberFromMap(Dictionary<string,object> map,string key){object value;if(!map.TryGetValue(key,out value)||value==null)return 0;try{return Math.Max(0,Math.Min(1,Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture)));}catch{return 0;}}

        void ClearImageTextLayer(){imageTextRegions.Clear();orderedImageTextRegions.Clear();imageTextSelectionStart=imageTextSelectionEnd=-1;imageTextSelecting=false;if(imageTextLayer!=null){imageTextLayer.ReleaseMouseCapture();imageTextLayer.Children.Clear();imageTextLayer.Visibility=Visibility.Collapsed;}}

        void LayoutImageTextLayer()
        {
            if(imageTextLayer==null||imageTextRegions.Count==0||workingBitmap==null){if(imageTextLayer!=null)imageTextLayer.Visibility=Visibility.Collapsed;return;}imageTextLayer.Children.Clear();imageTextLayer.Width=imageCanvas.ActualWidth;imageTextLayer.Height=imageCanvas.ActualHeight;Rect rendered=RenderedImageRect();if(rendered.IsEmpty)return;
            orderedImageTextRegions.Clear();var lines=new List<List<ImageTextRegion>>();foreach(ImageTextRegion region in imageTextRegions.OrderBy(x=>x.Y+x.Height/2).ThenBy(x=>x.X)){List<ImageTextRegion> line=lines.LastOrDefault();if(line==null||Math.Abs(line.Average(x=>x.Y+x.Height/2)-(region.Y+region.Height/2))>Math.Max(region.Height,line.Max(x=>x.Height))*.65){line=new List<ImageTextRegion>();lines.Add(line);}line.Add(region);}foreach(var line in lines)orderedImageTextRegions.AddRange(line.OrderBy(x=>x.X));
            RenderImageTextSelection();imageTextLayer.Visibility=Visibility.Visible;Panel.SetZIndex(imageTextLayer,20);
        }

        void RenderImageTextSelection()
        {
            if(imageTextLayer==null)return;imageTextLayer.Children.Clear();if(imageTextSelectionStart<0||imageTextSelectionEnd<0)return;Rect rendered=RenderedImageRect();if(rendered.IsEmpty)return;int first=Math.Min(imageTextSelectionStart,imageTextSelectionEnd),last=Math.Max(imageTextSelectionStart,imageTextSelectionEnd);
            for(int index=first;index<=last&&index<orderedImageTextRegions.Count;index++){ImageTextRegion region=orderedImageTextRegions[index];var highlight=new System.Windows.Shapes.Rectangle{Fill=new SolidColorBrush(Color.FromArgb(112,54,139,255)),IsHitTestVisible=false,RadiusX=2,RadiusY=2,Width=Math.Max(2,region.Width*rendered.Width+3),Height=Math.Max(2,region.Height*rendered.Height+3)};Canvas.SetLeft(highlight,rendered.Left+region.X*rendered.Width-1.5);Canvas.SetTop(highlight,rendered.Top+region.Y*rendered.Height-1.5);imageTextLayer.Children.Add(highlight);}
        }

        int ImageTextHitIndex(Point point)
        {
            Rect rendered=RenderedImageRect();if(rendered.IsEmpty||!rendered.Contains(point)||orderedImageTextRegions.Count==0)return -1;double best=Double.MaxValue;int bestIndex=-1;
            for(int index=0;index<orderedImageTextRegions.Count;index++){ImageTextRegion region=orderedImageTextRegions[index];Rect box=new Rect(rendered.Left+region.X*rendered.Width,rendered.Top+region.Y*rendered.Height,Math.Max(2,region.Width*rendered.Width),Math.Max(2,region.Height*rendered.Height));Rect expanded=new Rect(box.X-4,box.Y-4,box.Width+8,box.Height+8);if(expanded.Contains(point))return index;double dx=point.X<box.Left?box.Left-point.X:(point.X>box.Right?point.X-box.Right:0),dy=point.Y<box.Top?box.Top-point.Y:(point.Y>box.Bottom?point.Y-box.Bottom:0),distance=dx*dx+dy*dy;if(distance<best){best=distance;bestIndex=index;}}
            return best<=28*28?bestIndex:-1;
        }

        void ImageTextMouseDown(object sender,MouseButtonEventArgs e)
        {
            int index=ImageTextHitIndex(e.GetPosition(imageTextLayer));if(index<0)return;imageTextSelectionStart=imageTextSelectionEnd=index;imageTextSelecting=true;imageTextLayer.Focus();imageTextLayer.CaptureMouse();RenderImageTextSelection();e.Handled=true;
        }

        void ImageTextMouseMove(object sender,MouseEventArgs e)
        {
            if(!imageTextSelecting)return;int index=ImageTextHitIndex(e.GetPosition(imageTextLayer));if(index>=0&&index!=imageTextSelectionEnd){imageTextSelectionEnd=index;RenderImageTextSelection();}e.Handled=true;
        }

        void ImageTextMouseUp(object sender,MouseButtonEventArgs e)
        {
            if(!imageTextSelecting)return;imageTextSelecting=false;imageTextLayer.ReleaseMouseCapture();string selected=BuildSelectedImageText(imageTextSelectionStart,imageTextSelectionEnd,orderedImageTextRegions);imageStatus.Text=String.IsNullOrEmpty(selected)?"未选中文字":"已选择 "+selected.Length+" 个字符；Ctrl+C 复制，Ctrl+A 全选";e.Handled=true;
        }

        void ImageTextKeyDown(object sender,KeyEventArgs e)
        {
            if((Keyboard.Modifiers&ModifierKeys.Control)==0)return;if(e.Key==Key.A){imageTextSelectionStart=0;imageTextSelectionEnd=orderedImageTextRegions.Count-1;RenderImageTextSelection();imageStatus.Text="已全选图片文字；按 Ctrl+C 复制";e.Handled=true;}else if(e.Key==Key.C){string selected=BuildSelectedImageText(imageTextSelectionStart,imageTextSelectionEnd,orderedImageTextRegions);if(!String.IsNullOrEmpty(selected)){Clipboard.SetText(selected);imageStatus.Text="已复制 "+selected.Length+" 个字符";}e.Handled=true;}
        }

        string BuildSelectedImageText(int start,int end,IList<ImageTextRegion> source)
        {
            if(source==null||source.Count==0||start<0||end<0)return "";int first=Math.Max(0,Math.Min(start,end)),last=Math.Min(source.Count-1,Math.Max(start,end));var builder=new StringBuilder();ImageTextRegion previous=null;
            for(int index=first;index<=last;index++){ImageTextRegion current=source[index];if(previous!=null){bool newLine=Math.Abs((previous.Y+previous.Height/2)-(current.Y+current.Height/2))>Math.Max(previous.Height,current.Height)*.7;if(newLine)builder.AppendLine();else if(NeedsImageTextSpace(previous.Text,current.Text))builder.Append(' ');}builder.Append(current.Text);previous=current;}return builder.ToString();
        }

        bool NeedsImageTextSpace(string left,string right)
        {
            if(String.IsNullOrEmpty(left)||String.IsNullOrEmpty(right))return false;char a=left[left.Length-1],b=right[0];return a<128&&b<128&&Char.IsLetterOrDigit(a)&&Char.IsLetterOrDigit(b);
        }

        void RunImageEdit(bool replaceText)
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}if(imageAiBusy||!ValidateImageAi(false))return;string prompt=replaceText?(recognizedTextBox.Text??"").Trim():(redrawPromptBox.Text??"").Trim();if(String.IsNullOrEmpty(prompt)){imageStatus.Text=replaceText?"请先识别或填写要替换的文字":"请填写重绘提示词";return;}if(replaceText)prompt="保留图片构图、人物、颜色和非文字元素不变，只重新绘制图片中的文字。请让所有可见文字严格变为以下内容，并保持清晰自然：\n"+prompt;
            if(!replaceText)RememberCurrentImagePrompt();bool toApis=ImageUsesToApis();imageAiBusy=true;string input=PrepareWorkingImageFile(),output=Path.Combine(imageTempDir,"result-"+Guid.NewGuid().ToString("N")+".png");string aspect=toApis?imageAiConfig.ToApisAspectRatio:NearestImageAiAspect(workingBitmap),resolution=toApis?imageAiConfig.ToApisResolution:ImageAiResolution(aspect),quality=toApis?imageAiConfig.ToApisQuality:imageAiConfig.ImageQuality;imageStatus.Text="正在通过当前来源创建图像编辑任务…";var request=new Dictionary<string,object>{{"mode","edit"},{"provider","auto"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"image_path",input},{"output_path",output},{"prompt",prompt},{"aspect_ratio",aspect},{"resolution",resolution},{"size",toApis?"auto":imageAiConfig.ImageSize},{"transparent_background",imageAiConfig.TransparentBackground},{"quality",quality}};if(!replaceText&&extraReferenceImages.Count>0)request["reference_paths"]=extraReferenceImages.ToArray();
            TrackImageTask(request,false);RunImageHelper(request,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandleGeneratedImageResponse(response,output);});
        }

        string ToApisCostShortText(){string model=(ActiveImageModel()??"").Trim(),resolution=imageAiConfig.ToApisResolution??"auto";if(resolution=="auto")return "自动尺寸 · 积分以实际结果为准";if(!String.Equals(model,"gpt-image-2",StringComparison.OrdinalIgnoreCase))return "积分以账户计费为准";return "预计 "+(resolution=="4K"?5:resolution=="2K"?4:3)+" 积分/张";}

        string NearestImageAiAspect(BitmapSource bitmap)
        {
            double ratio=1;if(bitmap!=null&&bitmap.PixelHeight>0)ratio=(double)bitmap.PixelWidth/bitmap.PixelHeight;else{int width,height;if(Int32.TryParse(widthBox==null?"":widthBox.Text,out width)&&Int32.TryParse(heightBox==null?"":heightBox.Text,out height)&&height>0)ratio=(double)width/height;}
            string[] labels={"1:1","3:2","2:3","4:3","3:4","5:4","4:5","16:9","9:16","2:1","1:2","21:9","9:21"};double[] values={1,1.5,2.0/3,4.0/3,3.0/4,1.25,.8,16.0/9,9.0/16,2,.5,21.0/9,9.0/21};int best=0;double distance=Double.MaxValue;
            for(int i=0;i<values.Length;i++){double current=Math.Abs(Math.Log(Math.Max(.01,ratio)/values[i]));if(current<distance){distance=current;best=i;}}return labels[best];
        }

        string ImageAiResolution(string aspect)
        {
            return aspect=="1:1"||aspect=="3:2"||aspect=="2:3"?"1K":"2K";
        }

        void HandleGeneratedImageResponse(Dictionary<string,object> response,string output)
        {
            imageAiBusy=false;if(!ImageResponseOk(response)){try{File.Delete(output);}catch{}return;}try{workingEncodedBytes=File.ReadAllBytes(output);workingBitmap=LoadEditorBytes(workingEncodedBytes);ClearCompressionCandidate();ClearImageTextLayer();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();outputFormatBox.SelectedItem="PNG";ScheduleCompressionEstimate();PushImageState();UpdateImageFacts();imageStatus.Text=editingImageItem==null?"AI 图片已生成；可继续改字、重绘、裁切或压缩，确认后保存到中转袋":"AI 图片已返回预览；可继续编辑，满意后确认保存";FinishImageTask(true,"结果已载入画布。按住“对比原图”可随时核对，再决定是否保存。");}catch(Exception ex){imageStatus.Text="AI 图片读取失败："+ex.Message;FinishImageTask(false,imageStatus.Text);}finally{try{File.Delete(output);}catch{}}
        }

        bool ImageResponseOk(Dictionary<string,object> response){bool ok=response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]);if(!ok){imageAiBusy=false;if(imageStatus!=null)imageStatus.Text=response!=null&&response.ContainsKey("error")?Convert.ToString(response["error"]):"图像 AI 调用失败";FinishImageTask(false,imageStatus==null?"图像 AI 调用失败":imageStatus.Text);}return ok;}
        string DetectLocalAiProxy()
        {
            foreach(int port in new[]{7890,7897,10809,10808})try{using(var client=new System.Net.Sockets.TcpClient()){var pending=client.BeginConnect("127.0.0.1",port,null,null);if(pending.AsyncWaitHandle.WaitOne(80)&&client.Connected){client.EndConnect(pending);return "http://127.0.0.1:"+port;}}}catch{}
            return null;
        }
        void RunImageHelper(Dictionary<string,object> request,string textKey,string imageKey,Action<Dictionary<string,object>> completed)
        {
            string helper=EmbeddedRuntime.ResolveFile(Path.Combine("wind_bridge","image_ai.mjs"),root),requestPath=Path.Combine(imageTempDir,"request-"+Guid.NewGuid().ToString("N")+".json"),localProxy=DetectLocalAiProxy();ThreadPool.QueueUserWorkItem(delegate{try{File.WriteAllText(requestPath,json.Serialize(request),new UTF8Encoding(false));var psi=new ProcessStartInfo{WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,Arguments="\""+helper+"\" \""+requestPath+"\""};EmbeddedRuntime.UseBundledNode(psi,root);psi.EnvironmentVariables["TEXT_LLM_API_KEY"]=textKey??"";psi.EnvironmentVariables["IMAGE_LLM_API_KEY"]=imageKey??"";if(!String.IsNullOrEmpty(localProxy)){psi.EnvironmentVariables["NODE_USE_ENV_PROXY"]="1";psi.EnvironmentVariables["HTTP_PROXY"]=localProxy;psi.EnvironmentVariables["HTTPS_PROXY"]=localProxy;}string output,errors;using(var process=Process.Start(psi)){imageAiProcess=process;output=process.StandardOutput.ReadToEnd();errors=process.StandardError.ReadToEnd();process.WaitForExit();}imageAiProcess=null;Dictionary<string,object> parsed=null;try{parsed=json.Deserialize<Dictionary<string,object>>(output);}catch{}if(parsed==null)parsed=new Dictionary<string,object>{{"ok",false},{"error",String.IsNullOrWhiteSpace(errors)?"图像 AI 返回无法解析":errors.Trim()}};app.Dispatcher.BeginInvoke(new Action(delegate{try{completed(parsed);}catch(Exception ex){imageAiBusy=false;if(imageStatus!=null)imageStatus.Text="图像处理结果无法显示："+ex.Message;}}));}catch(Exception ex){app.Dispatcher.BeginInvoke(new Action(delegate{try{completed(new Dictionary<string,object>{{"ok",false},{"error",ex.Message}});}catch{imageAiBusy=false;if(imageStatus!=null)imageStatus.Text="图像处理被安全终止："+ex.Message;}}));}finally{textKey=null;imageKey=null;try{File.Delete(requestPath);}catch{}}});
        }

        void ConfirmImageOverwrite()
        {
            if(workingBitmap==null){imageStatus.Text="当前没有可以保存的图片";return;}try{string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";BitmapSource saveBitmap=layerCompositionActive?RenderPrecisionLayerComposition():workingBitmap;byte[] data=layerCompositionActive?EncodeBitmap(saveBitmap,format,92):(workingEncodedBytes??EncodeBitmap(workingBitmap,format,92));string desiredExt=format=="JPEG"?".jpg":".png";
                if(editingImageItem==null){string generated=Path.Combine(stashDir,DateTime.Now.ToString("yyyyMMdd-HHmmss-")+Guid.NewGuid().ToString("N").Substring(0,6)+desiredExt);File.WriteAllBytes(generated,data);editingImageItem=new StashItem{Id=Guid.NewGuid().ToString("N"),Kind="image",Name=Path.GetFileName(generated),Value=generated,Owned=true,Created=DateTime.Now.ToString("o"),SourceApp="AI 图片"};if(!AddStashItemSmart(editingImageItem))editingImageItem=stashItems.FirstOrDefault(x=>x.ContentHash==editingImageItem.ContentHash)??editingImageItem;SaveStash();RefreshStash();originalBitmap=LoadEditorBitmap(editingImageItem.Value);workingBitmap=originalBitmap;workingEncodedBytes=null;ClearCompressionCandidate();editorImage.Source=workingBitmap;ScheduleCompressionEstimate();ResetImageHistory();imageStatus.Text="已保存到中转袋："+editingImageItem.Name;React("新图片放进中转袋啦～",true);return;}
                string original=editingImageItem.Value,originalExt=Path.GetExtension(original).ToLowerInvariant();Directory.CreateDirectory(imageBackupDir);string backup=Path.Combine(imageBackupDir,DateTime.Now.ToString("yyyyMMdd-HHmmss-")+Guid.NewGuid().ToString("N").Substring(0,6)+"-"+Path.GetFileName(original));File.Copy(original,backup,true);string target=original;
                if((desiredExt==".jpg"&&(originalExt!=".jpg"&&originalExt!=".jpeg"))||(desiredExt==".png"&&originalExt!=".png")){target=Path.Combine(stashDir,Path.GetFileNameWithoutExtension(original)+"-edited-"+DateTime.Now.ToString("HHmmss")+desiredExt);editingImageItem.Owned=true;}
                string temp=target+".momopet.tmp";File.WriteAllBytes(temp,data);File.Copy(temp,target,true);File.Delete(temp);editingImageItem.Value=target;editingImageItem.Name=Path.GetFileName(target);SaveStash();RefreshStash();originalBitmap=LoadEditorBitmap(target);workingBitmap=originalBitmap;workingEncodedBytes=null;ClearCompressionCandidate();editorImage.Source=workingBitmap;ScheduleCompressionEstimate();ResetImageHistory();imageStatus.Text="已保存。原图备份："+backup;React("图片改好并保存啦～",true);
            }catch(Exception ex){imageStatus.Text="保存失败："+ex.Message;}
        }

        void SetImageEditorTopmost(bool value){if(imageEditorPanel!=null)imageEditorPanel.Topmost=value;if(imageAiSettingsPanel!=null)imageAiSettingsPanel.Topmost=value;if(precisionAiSettingsPanel!=null)precisionAiSettingsPanel.Topmost=value;}
        void CloseImageEditorWindows(){try{if(imageAiProcess!=null&&!imageAiProcess.HasExited)imageAiProcess.Kill();}catch{}try{if(localOcrProcess!=null&&!localOcrProcess.HasExited)localOcrProcess.Kill();}catch{}if(precisionAiSettingsPanel!=null)precisionAiSettingsPanel.Close();if(imageAiSettingsPanel!=null)imageAiSettingsPanel.Close();if(imageEditorPanel!=null)imageEditorPanel.Close();}
    }
}
