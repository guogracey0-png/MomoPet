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
}
}
