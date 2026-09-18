using System;
using System.IO;

namespace MomoPetApp
{
    // P1-03 统一路径入口。新基础设施需要的目录一律从这里取，
    // 严格禁止再往代码里散落 "%LOCALAPPDATA%\MomoPet" 之类字符串。
    // 数据根目录沿用 MomoPaths.DataDir()（支持 MOMOPET_DATA_DIR 覆盖），
    // 其余目录围绕它派生，不改变任何既有用户数据目录。
    static class AppPaths
    {
        public static string DataRoot()
        {
            return MomoPaths.DataDir();
        }

        // 统一应用日志目录（P1-01）：%LOCALAPPDATA%\MomoPet\logs
        public static string LogsDir()
        {
            return Path.Combine(DataRoot(), "logs");
        }

        // 首次运行释放的便携 Node / OCR / Skill 运行时目录（EmbeddedRuntime 使用）。
        public static string RuntimeDir()
        {
            return Path.Combine(DataRoot(), "app");
        }

        // 供诊断包等使用（P1-08）：%LOCALAPPDATA%\MomoPet\diagnostics
        public static string DiagnosticsDir()
        {
            return Path.Combine(DataRoot(), "diagnostics");
        }

        public static string EnsureDir(string path)
        {
            if (!String.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
            return path;
        }
    }
}