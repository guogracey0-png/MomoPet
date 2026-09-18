using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MomoPetApp
{
    public enum LogLevel { Info, Warn, Error }

    // P1-01 全局最小日志基础设施。轻量、无第三方依赖；日志自身失败绝不抛出，不影响主程序。
    // 安全约束：不记录 API Key / Token / AccessKey；不记录完整敏感请求体；不把聊天/文件正文当默认日志。
    // 对调用方显式注册为“敏感”的值，写入前做主动遮罩（[REDACTED:label]），见 RegisterSensitive。
    public static class AppLog
    {
        static readonly object gate = new object();
        static readonly List<KeyValuePair<string, string>> sensitive = new List<KeyValuePair<string, string>>();

        public static string LogsDirectory { get { return AppPaths.LogsDir(); } }
        public static string CurrentLogFilePath { get { return Path.Combine(AppPaths.LogsDir(), FileNameFor(DateTime.Now)); } }

        // 登记一个需要遮罩的敏感令牌；写日志时所有出现该值的地方都会替换为 [REDACTED:label]。
        public static void RegisterSensitive(string token, string label)
        {
            if (String.IsNullOrWhiteSpace(token)) return;
            lock (gate)
            {
                for (int i = 0; i < sensitive.Count; i++)
                    if (string.Equals(sensitive[i].Key, token, StringComparison.Ordinal))
                    {
                        sensitive[i] = new KeyValuePair<string, string>(token, label);
                        return;
                    }
                sensitive.Add(new KeyValuePair<string, string>(token, label));
            }
        }

        public static void Info(string component, string message) { Write(LogLevel.Info, component, message, null); }
        public static void Warn(string component, string message) { Write(LogLevel.Warn, component, message, null); }
        public static void Error(string component, string message) { Write(LogLevel.Error, component, message, null); }
        public static void Error(string component, Exception error) { Write(LogLevel.Error, component, null, error); }
        public static void Error(string component, string message, Exception error) { Write(LogLevel.Error, component, message, error); }
        public static void Log(LogLevel level, string component, string message, Exception error) { Write(level, component, message, error); }

        // 对外复用：返回去除敏感信息后的文本（供其它组件清洗待写内容）。
        public static string Scrub(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            List<KeyValuePair<string, string>> snapshot;
            lock (gate) { snapshot = new List<KeyValuePair<string, string>>(sensitive); }
            string result = text;
            foreach (KeyValuePair<string, string> kv in snapshot)
                if (kv.Key.Length > 0)
                    result = result.Replace(kv.Key, "[REDACTED:" + kv.Value + "]");
            return result;
        }

        static string FileNameFor(DateTime t)
        {
            return "momo-" + t.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
        }

        static void Write(LogLevel level, string component, string message, Exception error)
        {
            try
            {
                string path = Path.Combine(AppPaths.LogsDir(), FileNameFor(DateTime.Now));
                string line = BuildLine(level, component, message, error);
                lock (gate)
                {
                    Directory.CreateDirectory(AppPaths.LogsDir());
                    if (!File.Exists(path)) line = BuildHeader() + line;
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不能导致主程序崩溃（P1-01）：这里吞掉日志侧的所有异常。
            }
        }

        static string BuildHeader()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("# MomoPet log  (" + DateTime.Now.ToString("o") + ")");
            b.AppendLine("# version   " + AppDiagnostics.AppVersion());
            b.AppendLine("# commit    " + AppDiagnostics.Commit());
            b.AppendLine("# branch    " + AppDiagnostics.Branch());
            b.AppendLine("# buildTime " + AppDiagnostics.BuildTime());
            b.AppendLine("# os        " + AppDiagnostics.OsVersion());
            b.AppendLine("# runtime   " + AppDiagnostics.RuntimeVersion());
            return b.ToString();
        }

        static string BuildLine(LogLevel level, string component, string message, Exception error)
        {
            StringBuilder b = new StringBuilder();
            b.Append(DateTime.Now.ToString("o"));
            b.Append("\t[");
            b.Append(level.ToString());
            b.Append("]\t");
            b.Append(String.IsNullOrEmpty(component) ? "-" : Scrub(component));
            if (!String.IsNullOrEmpty(message))
            {
                b.Append("\t");
                b.Append(Scrub(message));
            }
            if (error != null)
            {
                b.Append("\tEX:");
                b.Append(error.GetType().FullName);
                if (!String.IsNullOrEmpty(error.Message))
                {
                    b.Append("\t");
                    b.Append(Scrub(error.Message));
                }
                if (!String.IsNullOrEmpty(error.StackTrace))
                {
                    b.Append(System.Environment.NewLine);
                    b.Append(Scrub(error.StackTrace));
                }
            }
            b.Append(System.Environment.NewLine);
            return b.ToString();
        }
    }
}