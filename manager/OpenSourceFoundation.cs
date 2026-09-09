using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexProxyManager
{
    public sealed class ManagerSettings
    {
        public int SchemaVersion { get; set; }
        public string ActiveProfileId { get; set; }
        public bool AutoSelectFastest { get; set; }
        public bool AutoFailover { get; set; }
        public int MixedPort { get; set; }
        public int ControllerPort { get; set; }
        public string ControllerSecret { get; set; }
        public List<ProxyProfileDefinition> Profiles { get; set; }
        public string ActiveApplicationId { get; set; }
        public List<ManagedApplication> Applications { get; set; }
    }

    public sealed class ProxyProfileDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string ConfigFile { get; set; }
        public string ProxyGroupName { get; set; }
        public string NodeControllerUrl { get; set; }
        public string NodeControllerSecret { get; set; }
    }

    internal sealed class StartupProfile
    {
        internal ManagedApplication Application { get; set; }
        internal string ProjectRoot { get; set; }
        internal string UserDataRoot { get; set; }
        internal string LogsDirectory { get; set; }
        internal string CoreHome { get; set; }
        internal string ConfigPath { get; set; }
        internal string StatePath { get; set; }
        internal string ManagerLog { get; set; }
        internal string CoreOutLog { get; set; }
        internal string CoreErrLog { get; set; }
        internal string ProfileName { get; set; }
        internal string ProfileId { get; set; }
        internal string ProxyGroupName { get; set; }
        internal string NodeControllerUrl { get; set; }
        internal string NodeControllerSecret { get; set; }
        internal int MixedPort { get; set; }
        internal int ControllerPort { get; set; }
        internal string ControllerSecret { get; set; }
        internal bool AutoSelectFastest { get; set; }
        internal bool AutoFailover { get; set; }

        internal string ManagedProxy
        {
            get { return "127.0.0.1:" + MixedPort; }
        }
    }

    internal static class StartupConfiguration
    {
        private const int CurrentSchemaVersion = 1;

        internal static string GetUserDataRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexProxyManager");
        }

        internal static StartupProfile LoadOrCreate(string projectRoot)
        {
            string userRoot = GetUserDataRoot();
            if (!File.Exists(Path.Combine(userRoot, "settings.json")) || InstallationCheck.Missing(projectRoot, userRoot).Count > 0)
                using (var guide = new GettingStartedForm(projectRoot, userRoot, true))
                    if (guide.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
            EnsureDirectories(userRoot);
            EnsureCoreData(projectRoot, userRoot);
            string settingsPath = Path.Combine(userRoot, "settings.json");

            if (!File.Exists(settingsPath))
            {
                if (!ProfileManagerForm.ShowManager(projectRoot, userRoot, true)) return null;
            }

            ManagerSettings settings = ReadSettings(settingsPath);
            ValidateSettings(settings);
            ManagedApplication application = settings.Applications.Find(delegate(ManagedApplication item)
            { return item.Id == settings.ActiveApplicationId; });
            ProxyProfileDefinition profile = ApplicationConfiguration.ResolveProfile(settings, application);
            if (profile == null) throw new InvalidDataException("找不到当前启用的代理档案。");

            string configPath = ResolveUserPath(userRoot, profile.ConfigFile);
            if (!File.Exists(configPath))
                throw new FileNotFoundException("找不到当前代理档案的配置文件。", configPath);

            return new StartupProfile
            {
                Application = application,
                ProjectRoot = projectRoot,
                UserDataRoot = userRoot,
                LogsDirectory = Path.Combine(userRoot, "logs"),
                CoreHome = Path.Combine(userRoot, "core-data"),
                ConfigPath = configPath,
                ProfileId = profile.Id,
                StatePath = Path.Combine(userRoot, "manager-state.json"),
                ManagerLog = Path.Combine(userRoot, "logs", "manager.log"),
                CoreOutLog = Path.Combine(userRoot, "logs", "mihomo.stdout.log"),
                CoreErrLog = Path.Combine(userRoot, "logs", "mihomo.stderr.log"),
                ProfileName = profile.Name,
                ProxyGroupName = profile.ProxyGroupName,
                NodeControllerUrl = NormalizeControllerUrl(profile.NodeControllerUrl),
                NodeControllerSecret = profile.NodeControllerSecret ?? "",
                MixedPort = settings.MixedPort,
                ControllerPort = settings.ControllerPort,
                ControllerSecret = settings.ControllerSecret,
                AutoSelectFastest = settings.AutoSelectFastest,
                AutoFailover = settings.AutoFailover
            };
        }

        internal static bool RunSetup(string projectRoot)
        {
            string userRoot = GetUserDataRoot();
            if (!File.Exists(Path.Combine(userRoot, "settings.json")) || InstallationCheck.Missing(projectRoot, userRoot).Count > 0)
                using (var guide = new GettingStartedForm(projectRoot, userRoot, true))
                    if (guide.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
            EnsureDirectories(userRoot);
            EnsureCoreData(projectRoot, userRoot);
            return ProfileManagerForm.ShowManager(projectRoot, userRoot, false);
        }

        internal static ManagerSettings ReadSettings(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            // Legacy AutoStart is intentionally no longer represented or applied.
            // The serializer ignores it on read and omits it on the next save.
            ManagerSettings settings = new JavaScriptSerializer().Deserialize<ManagerSettings>(json);
            ApplicationConfiguration.Normalize(settings);
            return settings;
        }

        internal static void WriteSettings(string path, ManagerSettings settings)
        {
            ApplicationConfiguration.Normalize(settings);
            string json = new JavaScriptSerializer().Serialize(settings);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static string ResolveUserPath(string userRoot, string relativePath)
        {
            if (String.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("代理档案缺少配置文件路径。");
            string root = Path.GetFullPath(userRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(userRoot, relativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("代理档案的配置路径超出了用户数据目录。");
            return full;
        }

        internal static string NormalizeControllerUrl(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            Uri uri;
            if (!Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out uri) ||
                !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                throw new InvalidDataException("外部控制接口必须是有效的 HTTP 或 HTTPS 地址。");
            if (!(String.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("为避免控制密钥泄露，外部控制接口目前只允许使用本机地址。");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        private static void EnsureDirectories(string userRoot)
        {
            Directory.CreateDirectory(userRoot);
            Directory.CreateDirectory(Path.Combine(userRoot, "profiles"));
            Directory.CreateDirectory(Path.Combine(userRoot, "logs"));
            Directory.CreateDirectory(Path.Combine(userRoot, "core-data"));
        }

        private static void EnsureCoreData(string projectRoot, string userRoot)
        {
            string sourceRoot = Path.Combine(projectRoot, "runtime", "data");
            string targetRoot = Path.Combine(userRoot, "core-data");
            foreach (string name in new[] { "GeoSite.dat", "geoip.metadb" })
            {
                string source = Path.Combine(sourceRoot, name);
                string target = Path.Combine(targetRoot, name);
                if (!File.Exists(source)) continue;
                if (!File.Exists(target) || File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(target))
                    File.Copy(source, target, true);
            }
        }

        private static void ValidateSettings(ManagerSettings settings)
        {
            if (settings == null) throw new InvalidDataException("用户设置文件无效。");
            if (settings.SchemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException("不支持的设置文件版本：" + settings.SchemaVersion);
            if (settings.Profiles == null || settings.Profiles.Count == 0)
                throw new InvalidDataException("尚未配置任何代理档案。");
            if (settings.MixedPort < 1024 || settings.MixedPort > 65535)
                throw new InvalidDataException("本地代理端口无效。");
            if (settings.ControllerPort < 1024 || settings.ControllerPort > 65535)
                throw new InvalidDataException("管理接口端口无效。");
            if (settings.MixedPort == settings.ControllerPort)
                throw new InvalidDataException("本地代理端口不能与管理接口端口相同。");
            if (String.IsNullOrWhiteSpace(settings.ControllerSecret))
                throw new InvalidDataException("管理接口密钥不能为空。");
        }
    }

    internal static class MihomoConfigFactory
    {
        internal static string BuildLocalUpstream(string type, string host, int port,
            string username, string password, int mixedPort)
        {
            StringBuilder yaml = new StringBuilder();
            yaml.AppendLine("mode: rule");
            yaml.AppendLine("log-level: info");
            yaml.AppendLine("find-process-mode: strict");
            yaml.AppendLine("allow-lan: false");
            yaml.AppendLine("ipv6: true");
            yaml.AppendLine("mixed-port: " + mixedPort);
            yaml.AppendLine("external-controller: \"\"");
            yaml.AppendLine("proxies:");
            yaml.AppendLine("  - name: \"Local Upstream\"");
            yaml.AppendLine("    type: " + type);
            yaml.AppendLine("    server: \"" + EscapeYaml(host) + "\"");
            yaml.AppendLine("    port: " + port);
            if (!String.IsNullOrWhiteSpace(username))
                yaml.AppendLine("    username: \"" + EscapeYaml(username.Trim()) + "\"");
            if (!String.IsNullOrEmpty(password))
                yaml.AppendLine("    password: \"" + EscapeYaml(password) + "\"");
            yaml.AppendLine("proxy-groups:");
            yaml.AppendLine("  - name: \"Codex Proxy\"");
            yaml.AppendLine("    type: select");
            yaml.AppendLine("    proxies:");
            yaml.AppendLine("      - \"Local Upstream\"");
            yaml.AppendLine("      - DIRECT");
            yaml.AppendLine("rules:");
            yaml.AppendLine("  - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve");
            yaml.AppendLine("  - IP-CIDR,10.0.0.0/8,DIRECT,no-resolve");
            yaml.AppendLine("  - IP-CIDR,172.16.0.0/12,DIRECT,no-resolve");
            yaml.AppendLine("  - IP-CIDR,192.168.0.0/16,DIRECT,no-resolve");
            yaml.AppendLine("  - AND,((PROCESS-NAME,ChatGPT.exe),(GEOSITE,cn)),DIRECT");
            yaml.AppendLine("  - AND,((PROCESS-NAME,codex.exe),(GEOSITE,cn)),DIRECT");
            yaml.AppendLine("  - AND,((PROCESS-NAME,ChatGPT.exe),(GEOIP,CN)),DIRECT");
            yaml.AppendLine("  - AND,((PROCESS-NAME,codex.exe),(GEOIP,CN)),DIRECT");
            yaml.AppendLine("  - PROCESS-NAME,ChatGPT.exe,Codex Proxy");
            yaml.AppendLine("  - PROCESS-NAME,codex.exe,Codex Proxy");
            yaml.AppendLine("  - MATCH,DIRECT");
            return yaml.ToString();
        }

        private static string EscapeYaml(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
