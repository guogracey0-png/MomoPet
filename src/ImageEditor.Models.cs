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
}

