using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]

namespace MomoPetApp
{
    // P1-07 诊断元数据：让“日志 / 诊断包”能定位它来自哪个构建。不读写用户文件正文，不搜集敏感信息。
    // 顶层异常钩子的实际接入点在 Program.Main（既有 Dispatcher/AppDomain/TaskScheduler 回调），
    // 本类只负责提供可查询的元数据，供 AppLog 头信息与 collect-diagnostics 脚本引用。
    static class AppDiagnostics
    {
        public static string ExePath()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                if (current != null && current.MainModule != null) return current.MainModule.FileName;
            }
            catch { }
            try { return Assembly.GetExecutingAssembly().Location; }
            catch { return null; }
        }

        public static string AppVersion()
        {
            try
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "(unknown)" : v.ToString();
            }
            catch { return "(unknown)"; }
        }

        public static string Commit() { return ReadBuildMeta("commit"); }
        public static string Branch() { return ReadBuildMeta("branch"); }
        public static string BuildTime() { return ReadBuildMeta("buildTime"); }
        public static string NodeVersion() { return ReadBuildMeta("node"); }

        public static string OsVersion()
        {
            try { return Environment.OSVersion == null ? "(unknown)" : Environment.OSVersion.VersionString; }
            catch { return "(unknown)"; }
        }

        public static string RuntimeVersion()
        {
            try { return Environment.Version == null ? "(unknown)" : Environment.Version.ToString(); }
            catch { return "(unknown)"; }
        }

        // 读取构建时嵌入的 build-meta.txt（commit / branch / buildTime / node 等），缺失时返回 "(unknown)"。
        static string ReadBuildMeta(string key)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream("build-meta.txt"))
                {
                    if (stream == null) return "(unknown)";
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string text = reader.ReadToEnd();
                        foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int idx = raw.IndexOf('=');
                            if (idx > 0 && string.Equals(raw.Substring(0, idx).Trim(), key, StringComparison.OrdinalIgnoreCase))
                                return raw.Substring(idx + 1).Trim();
                        }
                    }
                }
            }
            catch { }
            return "(unknown)";
        }
    }
}