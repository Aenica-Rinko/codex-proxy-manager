// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Collections;

namespace CodexProxyManager
{
    internal sealed class ImportedLocalProfile : IDisposable
    {
        internal string Id, RelativeConfig, ConfigPath, ProviderPath, ProfileDirectory;
        internal int NodeCount;
        internal int GroupCount;
        internal string EntryGroup;
        internal bool ConfigCreated, ProviderCreated, ProfileDirectoryCreated, Committed;
        public void Dispose()
        {
            if (Committed) return;
            // Only the two exact files created by this import; never the selected source.
            try { if (ConfigCreated && PrivacyLog.SafePath(ConfigPath)) File.Delete(ConfigPath); } catch { }
            try { if (ProviderCreated && PrivacyLog.SafePath(ProviderPath)) File.Delete(ProviderPath); } catch { }
            try { if (ProfileDirectoryCreated && PrivacyLog.SafePath(ProfileDirectory)) Directory.Delete(ProfileDirectory, false); } catch { }
        }
    }

    internal static class LocalFileImport
    {
        internal const int MaximumBytes = 10 * 1024 * 1024;

        internal static string ReadSnapshot(string source)
        {
            try
            {
                string full = Path.GetFullPath(source);
                string extension = Path.GetExtension(full).ToLowerInvariant();
                if (!Path.IsPathRooted(source) || full.StartsWith(@"\\", StringComparison.Ordinal) ||
                    full.Substring(Path.GetPathRoot(full).Length).Contains(":") ||
                    !PrivacyLog.SafePath(full) || (extension != ".yaml" && extension != ".yml" && extension != ".txt"))
                    throw new InvalidDataException();
                byte[] bytes;
                using (var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length == 0 || input.Length > MaximumBytes) throw new InvalidDataException();
                    bytes = new byte[(int)input.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int count = input.Read(bytes, read, bytes.Length - read);
                        if (count == 0) throw new InvalidDataException();
                        read += count;
                    }
                }
                Encoding encoding = new UTF8Encoding(false, true);
                int offset = 0;
                if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) offset = 3;
                else if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
                else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
                string text = encoding.GetString(bytes, offset, bytes.Length - offset);
                if (String.IsNullOrWhiteSpace(text) || text.IndexOf('\0') >= 0) throw new InvalidDataException();
                return text;
            }
            catch { throw new InvalidDataException("无法读取本地文件：请选择不含链接的本机 YAML/YML/TXT 文本，大小不超过 10 MiB，编码为 UTF-8 或带 BOM 的 UTF-16。原文件未修改。"); }
        }

        internal static ImportedLocalProfile Prepare(string userRoot, string source, int mixedPort,
            Func<string, int> validate)
        {
            string snapshot = ReadSnapshot(source);
            if (mixedPort < 1024 || mixedPort > 65535) throw new ArgumentOutOfRangeException("mixedPort");
            if (validate == null) throw new ArgumentNullException("validate");
            var topology = ImportedTopology.Parse(snapshot);
            int count = validate(snapshot);
            if (count <= 0) throw new InvalidDataException("文件中没有可用节点；请选择含内联 proxies 的 Clash/Mihomo YAML、分享链接或 Base64 文本。仅有远程 provider 引用的档案暂不支持。");
            if (topology != null && count != topology.Nodes.Children.Count)
                throw new InvalidDataException("核心识别的节点数量与档案不一致，已停止导入，避免遗漏节点。");
            var result = new ImportedLocalProfile { Id = "profile-" + Guid.NewGuid().ToString("N"), NodeCount = count,
                GroupCount = topology == null ? 0 : topology.GroupCount, EntryGroup = topology == null ? "Codex Proxy" : topology.Entry };
            result.RelativeConfig = Path.Combine("profiles", result.Id, "config.yaml");
            result.ConfigPath = StartupConfiguration.ResolveUserPath(userRoot, result.RelativeConfig);
            result.ProfileDirectory = Path.GetDirectoryName(result.ConfigPath);
            result.ProviderPath = StartupConfiguration.ResolveUserPath(userRoot, Path.Combine("core-data", "providers", result.Id + ".txt"));
            try
            {
                if (Directory.Exists(result.ProfileDirectory) || File.Exists(result.ProviderPath)) throw new IOException();
                Directory.CreateDirectory(result.ProfileDirectory);
                result.ProfileDirectoryCreated = true;
                Directory.CreateDirectory(Path.GetDirectoryName(result.ProviderPath));
                if (!PrivacyLog.SafePath(result.ConfigPath) || !PrivacyLog.SafePath(result.ProviderPath)) throw new IOException();
                CreateFile(result.ProviderPath, snapshot, delegate { result.ProviderCreated = true; });
                CreateFile(result.ConfigPath, topology == null ? ProviderConfigFactory.BuildFileProvider(result.Id, mixedPort) :
                    topology.BuildConfig(mixedPort, false), delegate { result.ConfigCreated = true; });
                return result;
            }
            catch { result.Dispose(); throw new IOException("本地档案保存失败，请检查用户数据目录权限；原文件和已有档案未修改。"); }
        }

        internal static void CreateFile(string path, string text, Action created)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created();
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        // Isolated node-only core: no target launch, data listeners, geodata, health check,
        // remote providers or inherited CLASH_* hooks. Never run the source as a config.
        internal static int ValidateWithCore(string projectRoot, string userRoot, string snapshot)
        {
            var topology = ImportedTopology.Parse(snapshot);
            string executable = Path.Combine(projectRoot, "runtime", "mihomo.exe");
            if (!File.Exists(executable) || !PrivacyLog.SafePath(executable))
                throw new InvalidDataException("本地导入校验需要 runtime/mihomo.exe，请先准备可信的 Mihomo 核心。");
            string scratch = StartupConfiguration.ResolveUserPath(userRoot, "import-check-" + Guid.NewGuid().ToString("N"));
            Process core = null;
            bool started = false;
            try
            {
                Directory.CreateDirectory(scratch);
                File.WriteAllText(Path.Combine(scratch, "nodes.txt"), snapshot, new UTF8Encoding(false));
                int port;
                var reserve = new TcpListener(IPAddress.Loopback, 0);
                reserve.Start();
                try { port = ((IPEndPoint)reserve.LocalEndpoint).Port; } finally { reserve.Stop(); }
                string secret = Guid.NewGuid().ToString("N");
                string yaml = "mode: rule\nlog-level: silent\nallow-lan: false\nport: 0\nsocks-port: 0\nmixed-port: 0\n" +
                    "dns:\n  enable: false\ntun:\n  enable: false\nprofile:\n  store-selected: false\n" +
                    "external-controller: 127.0.0.1:" + port + "\nsecret: " + secret +
                    "\nproxy-providers:\n  import-check:\n    type: file\n    path: ./nodes.txt\n    health-check:\n      enable: false\n" +
                    "proxy-groups:\n  - name: import-selection\n    type: select\n    use: [import-check]\n    proxies: [DIRECT]\nrules: ['MATCH,DIRECT']\n";
                string config = Path.Combine(scratch, "check.yaml");
                if (topology != null) yaml = topology.BuildConfig(0, true);
                File.WriteAllText(config, yaml, new UTF8Encoding(false));
                var info = new ProcessStartInfo(executable, (topology == null ? "" : "-t ") + "-d \"" + scratch + "\" -f \"" + config + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = scratch, RedirectStandardOutput = true, RedirectStandardError = true
                };
                var environmentKeys = new List<string>();
                foreach (string key in info.EnvironmentVariables.Keys) environmentKeys.Add(key);
                foreach (string key in environmentKeys)
                    if (key.StartsWith("CLASH_", StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
                core = new Process { StartInfo = info };
                core.OutputDataReceived += delegate { }; core.ErrorDataReceived += delegate { };
                core.Start(); started = true; core.BeginOutputReadLine(); core.BeginErrorReadLine();
                var timer = Stopwatch.StartNew();
                var controller = new ControllerClient("http://127.0.0.1:" + port, secret);
                while (timer.ElapsedMilliseconds < 10000)
                {
                    if (core.HasExited)
                    {
                        if (topology != null && core.ExitCode == 0) return topology.Nodes.Children.Count;
                        break;
                    }
                    if (topology != null) { Thread.Sleep(100); continue; }
                    try
                    {
                        var data = controller.Read("/providers/proxies/import-check");
                        object value;
                        if (data != null && data.TryGetValue("proxies", out value))
                        {
                            var nodes = value as IList;
                            if (nodes != null && nodes.Count > 0) return nodes.Count;
                        }
                    }
                    catch { }
                    Thread.Sleep(100);
                }
                throw new InvalidDataException();
            }
            catch { throw new InvalidDataException("节点校验失败：核心不支持该内容、节点为空或校验超时。支持含内联 proxies 的 YAML、分享链接及 Base64；仅远程 provider、外部文件依赖或其他客户端 JSON 暂不支持。未启用代理，原文件未修改。"); }
            finally
            {
                bool exited = !started;
                if (core != null && started)
                {
                    try { if (!core.HasExited) core.Kill(); exited = core.WaitForExit(3000); } catch { }
                }
                if (core != null) core.Dispose();
                try
                {
                    if (exited && PrivacyLog.SafePath(scratch) &&
                        Path.GetDirectoryName(scratch).Equals(Path.GetFullPath(userRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(scratch).StartsWith("import-check-", StringComparison.Ordinal)) Directory.Delete(scratch, true);
                }
                catch { }
            }
        }
    }
}
