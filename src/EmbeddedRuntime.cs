using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

// 单文件发布支持：动画帧直接从嵌入资源读取；OCR 助手、AI 桥接脚本、合规词库
// 首次运行时释放到 %LOCALAPPDATA%\MomoPet\app，无需随 exe 分发任何外部文件。
static class EmbeddedRuntime
{
    static readonly string[] extractFiles = {
        "MomoOcr.exe",
        "wind_bridge\\wind_ai_search.mjs",
        "wind_bridge\\image_ai.mjs",
        "compliance-rules.txt"
    };

    static string appDir;
    static readonly Assembly asm = Assembly.GetExecutingAssembly();

    public static void Initialize(string dataDir)
    {
        try
        {
            appDir = Path.Combine(dataDir, "app");
            Directory.CreateDirectory(appDir);
            string stampPath = Path.Combine(appDir, "stamp.txt");
            string stamp = File.GetLastWriteTimeUtc(asm.Location).Ticks.ToString();
            string oldStamp = null;
            try { oldStamp = File.ReadAllText(stampPath).Trim(); } catch { }
            if (oldStamp == stamp) return;
            foreach (string relative in extractFiles)
            {
                string target = Path.Combine(appDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (Stream source = asm.GetManifestResourceStream(ResourceName(relative)))
                {
                    if (source == null) continue;
                    try
                    {
                        using (Stream targetFile = File.Create(target)) { source.CopyTo(targetFile); }
                    }
                    catch { /* 正在运行的 exe 无法覆盖，下次启动会重试 */ }
                }
            }
            try { File.WriteAllText(stampPath, stamp); } catch { }
        }
        catch { }
    }

    static string ResourceName(string relativePath)
    {
        return relativePath.Replace('\\', '.');
    }

    // 优先使用 exe 同目录的文件（开发环境），不存在时回退到已释放的副本。
    public static string ResolveFile(string relativePath, string root)
    {
        string local = Path.Combine(root, relativePath);
        if (File.Exists(local)) return local;
        if (appDir != null)
        {
            string extracted = Path.Combine(appDir, relativePath);
            if (File.Exists(extracted)) return extracted;
        }
        return local;
    }

    public static BitmapImage LoadBitmap(string filePath, string resourceName, int decodePixelWidth)
    {
        if (File.Exists(filePath)) return LoadFromUri(filePath, decodePixelWidth);
        return LoadFromStream(resourceName, decodePixelWidth);
    }

    static BitmapImage LoadFromUri(string path, int decodePixelWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    static BitmapImage LoadFromStream(string resourceName, int decodePixelWidth)
    {
        var bitmap = new BitmapImage();
        using (Stream stream = asm.GetManifestResourceStream(resourceName))
        {
            if (stream == null) throw new FileNotFoundException("缺少嵌入资源：" + resourceName);
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        }
        return bitmap;
    }
}
