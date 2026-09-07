using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexProxyManager
{
    public sealed class ManagedApplication
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Executable { get; set; }
        public string LaunchMode { get; set; }
        public string ProfileId { get; set; }
        public override string ToString() { return Name; }
    }

    internal static class ApplicationConfiguration
    {
        internal static ManagedApplication DefaultApplication()
        {
            return new ManagedApplication { Id = "codex", Name = "Codex", LaunchMode = "chromium" };
        }

        internal static void Normalize(ManagerSettings settings)
        {
            if (settings == null) throw new InvalidDataException("设置文件无效。");
            if (settings.Applications == null) settings.Applications = new List<ManagedApplication>();
            if (!settings.Applications.Exists(delegate(ManagedApplication app) { return app != null && app.Id == "codex"; }))
                settings.Applications.Insert(0, DefaultApplication());
            settings.Applications.RemoveAll(delegate(ManagedApplication app) { return app == null; });
            if (!settings.Applications.Exists(delegate(ManagedApplication app) { return app.Id == settings.ActiveApplicationId; }))
                settings.ActiveApplicationId = "codex";
            foreach (ManagedApplication app in settings.Applications)
            {
                if (app.Id == "codex") { app.Name = "Codex"; app.LaunchMode = "chromium"; app.Executable = null; }
                if (!String.IsNullOrEmpty(app.ProfileId) && settings.Profiles != null &&
                    !settings.Profiles.Exists(delegate(ProxyProfileDefinition profile) { return profile.Id == app.ProfileId; }))
                    app.ProfileId = null;
            }
        }

        internal static void Validate(ManagedApplication app)
        {
            if (app == null || String.IsNullOrWhiteSpace(app.Name)) throw new InvalidDataException("请填写应用名称。");
            if (app.Id == "codex") return;
            if (app.LaunchMode != "chromium" && app.LaunchMode != "environment")
                throw new InvalidDataException("请选择代理启动方式。");
            if (String.IsNullOrWhiteSpace(app.Executable) || !Path.IsPathRooted(app.Executable) || !File.Exists(app.Executable))
                throw new InvalidDataException("请选择已安装应用的 EXE 文件。");
            if (!Regex.IsMatch(Path.GetFileName(app.Executable), @"^[\p{L}\p{N}_. -]+\.exe$", RegexOptions.IgnoreCase))
                throw new InvalidDataException("程序名须以 .exe 结尾，且不能包含逗号、引号或括号等规则字符。");
        }

        internal static string ResolveExecutable(ManagedApplication app)
        {
            Validate(app);
            return app.Id == "codex" ? ProcessProxyLauncher.FindCodexExecutable() : Path.GetFullPath(app.Executable);
        }

        internal static ProxyProfileDefinition ResolveProfile(ManagerSettings settings, ManagedApplication app)
        {
            string id = String.IsNullOrEmpty(app.ProfileId) ? settings.ActiveProfileId : app.ProfileId;
            ProxyProfileDefinition profile = settings.Profiles.Find(delegate(ProxyProfileDefinition item) { return item.Id == id; });
            if (profile == null) throw new InvalidDataException("所选应用没有可用的代理档案。");
            return profile;
        }

        internal static string BuildSessionConfig(string yaml, ManagedApplication app)
        {
            if (app == null || app.Id == "codex") return yaml;
            Validate(app);
            // Transform the existing managed process rules, retaining every node and policy name.
            Regex processRule = new Regex(@"PROCESS-NAME,(ChatGPT\.exe|codex\.exe)(?=,)", RegexOptions.IgnoreCase);
            string name = Path.GetFileName(app.Executable);
            bool inRules = false;
            int replacements = 0;
            var result = new StringBuilder();
            foreach (string original in Regex.Split(yaml, "(?<=\n)"))
            {
                string line = original;
                if (Regex.IsMatch(line, @"^rules:[ \t]*(?:#[^\r\n]*)?\r?\n?$")) inRules = true;
                else if (Regex.IsMatch(line, @"^[^\s#-]")) inRules = false;
                if (inRules && line.TrimStart().StartsWith("-"))
                    line = processRule.Replace(line, delegate(Match match) { replacements++; return "PROCESS-NAME," + name; });
                result.Append(line);
            }
            if (replacements == 0)
                throw new InvalidDataException("此档案没有标准应用分流规则，请通过订阅、分享链接或本地代理创建档案。");
            return result.ToString();
        }

        internal static bool IsRunning(ManagedApplication app)
        {
            if (app == null || app.Id == "codex")
                return HasProcess("ChatGPT") || HasProcess("codex");
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Executable)))
            {
                using (process)
                {
                    try
                    {
                        if (String.Equals(process.MainModule.FileName, app.Executable, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    // If access is denied, conservatively keep the session alive / avoid relaunching.
                    catch { return true; }
                }
            }
            return false;
        }

        private static bool HasProcess(string name)
        {
            Process[] processes = Process.GetProcessesByName(name);
            foreach (Process process in processes) process.Dispose();
            return processes.Length > 0;
        }
    }
}
