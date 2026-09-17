using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Media.Imaging;

// 单文件发布支持：动画帧直接从嵌入资源读取；OCR 助手、AI 桥接脚本、合规词库、
// 便携版 Node 运行时与 .agents 技能库首次运行时释放到 %LOCALAPPDATA%\MomoPet\app。
// 因此目标电脑即使完全没有安装 Node 等任何环境，双击 exe 也能直接使用。
static class EmbeddedRuntime
{
    static readonly string[] extractFiles = {
        "MomoOcr.exe",
        "wind_bridge\\wind_ai_search.mjs",
        "wind_bridge\\image_ai.mjs",
        "compliance-rules.txt"
    };

    // 体积较大的运行时用 gzip 压缩后嵌入（资源名带 .gz 后缀），释放时解压还原。
    static readonly string[] extractGzipFiles = {
        "node.exe"
    };

    // 技能库整体打成一个 zip 嵌入（资源名 agents-skills.zip），解压后得到 app\.agents\...
    const string skillsResource = "agents-skills.zip";

    static string appDir;
    static readonly Assembly asm = Assembly.GetExecutingAssembly();
    static readonly string[] requiredFiles = {
        "MomoOcr.exe", "wind_bridge\\wind_ai_search.mjs", "wind_bridge\\image_ai.mjs",
        "compliance-rules.txt", "node.exe", ".agents\\skills\\wind-mcp-skill\\scripts\\cli.mjs"
    };
    public static bool IsReady { get; private set; }
    public static string LastInitializationError { get; private set; }

    public static void Initialize(string dataDir)
    {
        IsReady = false;
        LastInitializationError = null;
        try
        {
            appDir = Path.Combine(dataDir, "app");
            Directory.CreateDirectory(appDir);
            string stampPath = Path.Combine(appDir, "stamp.txt");
            string stamp = File.GetLastWriteTimeUtc(asm.Location).Ticks.ToString();
            string oldStamp = null;
            try { oldStamp = File.ReadAllText(stampPath).Trim(); } catch { }
            if (oldStamp == stamp && ValidateExtractedFiles()) { IsReady = true; return; }
            foreach (string relative in extractFiles) ExtractPlain(relative);
            foreach (string relative in extractGzipFiles) ExtractGzip(relative);
            ExtractSkills();
            if (!ValidateExtractedFiles())
            {
                LastInitializationError = "运行组件未完整释放，将在下次启动自动修复";
                try { if (File.Exists(stampPath)) File.Delete(stampPath); } catch { }
                return;
            }
            File.WriteAllText(stampPath, stamp);
            IsReady = true;
        }
        catch (Exception error)
        {
            LastInitializationError = error.Message;
            try { if (appDir != null) File.Delete(Path.Combine(appDir, "stamp.txt")); } catch { }
        }
    }

    static bool ValidateExtractedFiles()
    {
        if (String.IsNullOrWhiteSpace(appDir)) return false;
        foreach (string relative in requiredFiles)
            if (!File.Exists(Path.Combine(appDir, relative))) return false;
        return true;
    }

    static void ExtractPlain(string relative)
    {
        string target = Path.Combine(appDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        using (Stream source = asm.GetManifestResourceStream(ResourceName(relative)))
        {
            if (source == null) return;
            try
            {
                using (Stream targetFile = File.Create(target)) { source.CopyTo(targetFile); }
            }
            catch { /* 正在运行的 exe 无法覆盖，下次启动会重试 */ }
        }
    }

    static void ExtractGzip(string relative)
    {
        string target = Path.Combine(appDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        using (Stream source = asm.GetManifestResourceStream(ResourceName(relative) + ".gz"))
        {
            if (source == null) return;
            try
            {
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (Stream targetFile = File.Create(target)) { gzip.CopyTo(targetFile); }
            }
            catch { /* 正在运行的 node 无法覆盖，下次启动会重试 */ }
        }
    }

    static void ExtractSkills()
    {
        using (Stream source = asm.GetManifestResourceStream(skillsResource))
        {
            if (source == null) return;
            string temp = Path.Combine(appDir, "agents-skills.tmp.zip");
            try
            {
                using (Stream targetFile = File.Create(temp)) { source.CopyTo(targetFile); }
                string skillsRoot = Path.Combine(appDir, ".agents");
                try { if (Directory.Exists(skillsRoot)) Directory.Delete(skillsRoot, true); } catch { }
                ZipFile.ExtractToDirectory(temp, appDir);
            }
            catch { /* 正在运行无法覆盖，下次启动会重试 */ }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
    }

    static string ResourceName(string relativePath)
    {
        return relativePath.Replace('\\', '.');
    }

    // 取出便携版 Node：优先用 exe 同目录（开发机可放一份），其次用已释放的副本，
    // 都没有才回退到系统 PATH 里的 node。
    public static string ResolveNode(string root)
    {
        if (!String.IsNullOrEmpty(root))
        {
            string local = Path.Combine(root, "node.exe");
            if (File.Exists(local)) return local;
        }
        if (appDir != null)
        {
            string extracted = Path.Combine(appDir, "node.exe");
            if (File.Exists(extracted)) return extracted;
        }
        return "node";
    }

    // 让子进程使用便携版 Node，并把其所在目录前置到子进程 PATH，
    // 这样 Node 脚本内部再 spawn('node', …) 时也能命中同一份运行时。
    public static void UseBundledNode(ProcessStartInfo psi, string root)
    {
        string nodePath = ResolveNode(root);
        psi.FileName = nodePath;
        if (!Path.IsPathRooted(nodePath)) return;
        string directory = Path.GetDirectoryName(nodePath);
        if (String.IsNullOrEmpty(directory)) return;
        string current = null;
        try { current = psi.EnvironmentVariables["PATH"]; } catch { }
        if (String.IsNullOrEmpty(current)) current = Environment.GetEnvironmentVariable("PATH");
        psi.EnvironmentVariables["PATH"] = directory + ";" + (current ?? String.Empty);
    }

    // 返回内含 .agents 的根目录：开发机为 exe 目录，发行版为释放目录。
    public static string SkillRoot(string root)
    {
        if (!String.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, ".agents"))) return root;
        if (appDir != null && Directory.Exists(Path.Combine(appDir, ".agents"))) return appDir;
        return root;
    }

    public static string ResolveSkillDirectory(string relativePath, string root)
    {
        if (!String.IsNullOrEmpty(root))
        {
            string local = Path.Combine(root, relativePath);
            if (Directory.Exists(local)) return local;
        }
        if (appDir != null)
        {
            string extracted = Path.Combine(appDir, relativePath);
            if (Directory.Exists(extracted)) return extracted;
        }
        return String.IsNullOrEmpty(root) ? relativePath : Path.Combine(root, relativePath);
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
