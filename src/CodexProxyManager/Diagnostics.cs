using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexProxyManager
{
    // Input contains private configuration. Never serialize or export this object.
    internal sealed class DiagnosticInput
    {
        internal string CorePath, ConfigPath, CoreHome, StatePath;
        internal string CoreUrl, CoreSecret, NodeUrl, NodeSecret, GroupName;
        internal string[] ProcessNames;
        internal int MixedPort;
        internal bool SessionActive, CoreAlive, ApplicationRunning;
    }

    internal sealed class DiagnosticRow
    {
        internal string Code, Status, Message;
    }

    internal sealed class DiagnosticReport
    {
        internal readonly DateTime Created = DateTime.Now;
        internal readonly List<DiagnosticRow> Rows = new List<DiagnosticRow>();

        internal void Add(string code, string status, string message)
        {
            Rows.Add(new DiagnosticRow { Code = code, Status = status, Message = message });
        }

        // Only allowlisted observations and fixed messages reach the report. No raw API/log content.
        internal string ToText()
        {
            var text = new StringBuilder(AppInfo.Title + " · 连接诊断\r\n");
            text.AppendLine("检查时间：" + Created.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("这是一次状态快照；端口可用或节点探测成功不等于应用登录成功。");
            text.AppendLine();
            foreach (DiagnosticRow row in Rows)
                text.AppendLine("[" + row.Status + "] " + row.Code + "：" + row.Message);
            text.AppendLine();
            text.AppendLine("报告未包含原始日志、节点名称/地址、订阅、密钥、用户名、文件路径或访问网址。");
            return text.ToString();
        }
    }

    internal static class Diagnostics
    {
        internal static DiagnosticInput ReadSavedInput(string projectRoot)
        {
            string userRoot = StartupConfiguration.GetUserDataRoot();
            ManagerSettings settings = StartupConfiguration.ReadSettings(Path.Combine(userRoot, "settings.json"));
            ManagedApplication application = settings.Applications.Find(delegate(ManagedApplication app) { return app.Id == settings.ActiveApplicationId; });
            ProxyProfileDefinition profile = ApplicationConfiguration.ResolveProfile(settings, application);
            string corePath = Path.Combine(projectRoot, "runtime", "mihomo.exe");
            string statePath = Path.Combine(userRoot, "manager-state.json");
            bool alive = false;
            if (File.Exists(statePath))
            {
                try
                {
                    using (Process process = Process.GetProcessById(Program.ReadState(statePath).CorePid))
                        alive = !process.HasExited && String.Equals(process.MainModule.FileName, corePath, StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            }
            bool external = !String.IsNullOrWhiteSpace(profile.NodeControllerUrl);
            string coreUrl = "http://127.0.0.1:" + settings.ControllerPort;
            return new DiagnosticInput
            {
                CorePath = corePath, CoreHome = Path.Combine(userRoot, "core-data"), StatePath = statePath,
                ConfigPath = StartupConfiguration.ResolveUserPath(userRoot, profile.ConfigFile),
                MixedPort = settings.MixedPort, CoreUrl = coreUrl, CoreSecret = settings.ControllerSecret,
                NodeUrl = external ? profile.NodeControllerUrl : coreUrl,
                NodeSecret = external ? profile.NodeControllerSecret : settings.ControllerSecret,
                GroupName = profile.ProxyGroupName, SessionActive = File.Exists(statePath), CoreAlive = alive,
                ApplicationRunning = ApplicationConfiguration.IsRunning(application),
                ProcessNames = application.Id == "codex" ? new[] { "ChatGPT.exe", "codex.exe" } : new[] { Path.GetFileName(application.Executable) }
            };
        }

        internal static DiagnosticReport Collect(DiagnosticInput input, bool testNode, CancellationToken cancellation)
        {
            var report = new DiagnosticReport();
            AddFile(report, "CORE_FILE", input.CorePath);
            AddFile(report, "PROFILE_FILE", input.ConfigPath);
            AddFile(report, "GEOSITE_FILE", Path.Combine(input.CoreHome, "GeoSite.dat"));
            AddFile(report, "GEOIP_FILE", Path.Combine(input.CoreHome, "geoip.metadb"));
            report.Add("SESSION", "信息", input.SessionActive ? "专用代理会话运行中。" : "专用代理已停止；可以检查文件和端口。启动后可检查实际流量。");
            report.Add("APPLICATION", input.ApplicationRunning ? "通过" : "信息",
                input.ApplicationRunning ? "检测到目标应用进程；是否使用代理请结合 FLOW 检查。" : "未检测到目标应用进程。");
            CheckSystemProxy(input, report);
            try
            {
                if (!File.Exists(input.StatePath)) report.Add("STATE", input.SessionActive ? "注意" : "信息", "没有会话状态文件。");
                else report.Add("STATE", "信息", Program.ReadState(input.StatePath).NetworkMode == "process" ?
                    "状态文件为应用隔离模式，恢复时不会写入系统代理。" : "状态文件为旧版模式；仅报告，不自动执行恢复。");
            }
            catch (Exception ex) { report.Add("STATE", "注意", Explain(ex)); }
            cancellation.ThrowIfCancellationRequested();
            bool listening = PortOpen(input.MixedPort);
            report.Add("LOCAL_PORT", input.SessionActive ? (listening ? "通过" : "失败") : (listening ? "注意" : "通过"),
                input.SessionActive ? (listening ? "本地入口可连接；这不代表外网可用。" : "入口无法连接，请检查核心启动结果。") :
                (listening ? "会话未启动，但配置端口已有监听；可能有残留核心或其他程序占用。" : "配置端口目前空闲。"));
            if (!input.SessionActive || !input.CoreAlive)
            {
                report.Add("CORE_API", input.SessionActive ? "失败" : "跳过", input.SessionActive ?
                    "本项目核心已退出，请停止后重新启动。" : "会话未启动，未向可能属于其他程序的端口发送控制请求。");
                if (testNode) report.Add("NODE_PROBE", "跳过", "请先启动专用代理再测试当前节点。");
                return report;
            }
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var version = ReadApi(input.CoreUrl, input.CoreSecret, "/version", 3500);
                if (!version.ContainsKey("version")) throw new InvalidDataException();
                report.Add("CORE_API", "通过", "本项目核心控制接口响应有效，鉴权通过。");
            }
            catch (Exception ex) { report.Add("CORE_API", "失败", Explain(ex)); }
            cancellation.ThrowIfCancellationRequested();
            try { AnalyzeConnections(ReadApi(input.CoreUrl, input.CoreSecret, "/connections", 3500), input.ProcessNames, report); }
            catch (Exception ex) { report.Add("FLOW", "注意", "无法读取应用连接快照。" + Explain(ex)); }
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var proxies = Value(ReadApi(input.NodeUrl, input.NodeSecret, "/proxies", 3500), "proxies") as Dictionary<string, object>;
                if (proxies == null) throw new InvalidDataException();
                report.Add("NODE_API", "通过", "节点控制接口响应有效。");
                var group = Value(proxies, input.GroupName ?? "") as Dictionary<string, object>;
                string selected = Convert.ToString(Value(group, "now"));
                if (group == null || String.IsNullOrWhiteSpace(selected))
                {
                    report.Add("SELECTION", "注意", "尚未识别到当前策略组；请返回面板等待节点列表刷新，或检查档案中的策略组设置。");
                    return report;
                }
                bool direct = String.Equals(selected, "DIRECT", StringComparison.OrdinalIgnoreCase);
                bool reject = selected.StartsWith("REJECT", StringComparison.OrdinalIgnoreCase);
                report.Add("SELECTION", direct || reject ? "注意" : "通过", direct ? "当前策略组选择直连。" :
                    reject ? "当前策略组选择拒绝连接。" : "当前策略组已选择代理节点或子策略组。");
                if (!testNode) report.Add("NODE_PROBE", "跳过", "未发送外网测试请求；可勾选节点探测后重新检查。");
                else if (direct || reject) report.Add("NODE_PROBE", "跳过", "当前选择不是代理节点，未执行外网节点探测。");
                else
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var delay = ReadApi(input.NodeUrl, input.NodeSecret, "/proxies/" + Uri.EscapeDataString(selected) +
                            "/delay?url=https%3A%2F%2Fwww.gstatic.com%2Fgenerate_204&timeout=5000", 7000);
                        int ms = Convert.ToInt32(Value(delay, "delay") ?? 0);
                        report.Add("NODE_PROBE", ms > 0 ? "通过" : "失败", ms > 0 ?
                            "当前选择的外网探测延迟为 " + ms + " ms；不代表目标应用的服务可登录。" : "外网探测未返回有效延迟。");
                    }
                    catch (Exception ex) { report.Add("NODE_PROBE", "失败", Explain(ex)); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { report.Add("NODE_API", "失败", Explain(ex)); }
            return report;
        }

        internal static void AnalyzeConnections(Dictionary<string, object> root, string[] processNames, DiagnosticReport report)
        {
            var connections = Value(root, "connections") as IList;
            if (connections == null) throw new InvalidDataException();
            int matched = 0, proxied = 0, direct = 0, rejected = 0, unknown = 0;
            foreach (object entry in connections)
            {
                var connection = entry as Dictionary<string, object>;
                var metadata = Value(connection, "metadata") as Dictionary<string, object>;
                string name = Convert.ToString(Value(metadata, "process"));
                if (String.IsNullOrEmpty(name)) name = Path.GetFileName(Convert.ToString(Value(metadata, "processPath")));
                if (Array.FindIndex(processNames ?? new string[0], delegate(string expected)
                    { return String.Equals(expected, name, StringComparison.OrdinalIgnoreCase); }) < 0) continue;
                matched++;
                IList chains = Value(connection, "chains") as IList;
                if (chains == null || chains.Count == 0) { unknown++; continue; }
                bool isDirect = false, isRejected = false;
                foreach (object chain in chains)
                {
                    string value = Convert.ToString(chain);
                    if (String.Equals(value, "DIRECT", StringComparison.OrdinalIgnoreCase)) isDirect = true;
                    if (value.StartsWith("REJECT", StringComparison.OrdinalIgnoreCase)) isRejected = true;
                }
                if (isRejected) rejected++; else if (isDirect) direct++; else proxied++;
            }
            report.Add("FLOW", matched == 0 || rejected > 0 || unknown == matched ? "注意" : "通过", matched == 0 ?
                "未观察到目标应用的活动连接；可能暂时空闲、连接已结束、进程信息不可用或应用未使用代理。请在应用中发起请求后重查。" :
                "观察到目标应用活动连接 " + matched + " 条：代理 " + proxied + "，直连 " + direct + "，拒绝 " + rejected + "，路径未知 " + unknown + "。直连也可能是国内/本机分流规则的正常结果。");
        }

        private static void AddFile(DiagnosticReport report, string code, string path)
        {
            bool exists = !String.IsNullOrEmpty(path) && File.Exists(path);
            report.Add(code, exists ? "通过" : "失败", exists ? "所需文件存在。" : "所需文件缺失，请检查安装或代理档案。");
        }

        private static void CheckSystemProxy(DiagnosticInput input, DiagnosticReport report)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Program.InternetSettings, false))
                {
                    if (key == null) throw new InvalidDataException();
                    bool enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0;
                    string endpoint = Convert.ToString(key.GetValue("ProxyServer", ""));
                    bool ours = enabled && endpoint == "127.0.0.1:" + input.MixedPort;
                    bool pac = !String.IsNullOrEmpty(Convert.ToString(key.GetValue("AutoConfigURL", "")));
                    report.Add("SYSTEM_PROXY", enabled || pac ? "注意" : "通过", ours ?
                        "Windows 系统代理仍指向专用入口，可能是旧版残留或外部设置。诊断不会修改它。" : enabled || pac ?
                        "检测到其他系统代理或 PAC 设置；它可能独立影响商店、游戏等应用。诊断不会修改它。" :
                        "未配置启用的手动系统代理或 PAC 地址；此检查不检测 TUN/VPN 或自动发现代理。");
                }
            }
            catch (Exception ex) { report.Add("SYSTEM_PROXY", "注意", Explain(ex)); }
        }

        private static bool PortOpen(int port)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    IAsyncResult pending = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    using (pending.AsyncWaitHandle)
                    {
                        if (!pending.AsyncWaitHandle.WaitOne(600)) return false;
                        client.EndConnect(pending);
                        return true;
                    }
                }
                catch { return false; }
            }
        }

        internal static Dictionary<string, object> ReadApi(string url, string secret, string path, int timeout)
        {
            string endpoint = StartupConfiguration.NormalizeControllerUrl(url);
            if (String.IsNullOrEmpty(endpoint)) throw new InvalidDataException();
            var request = (HttpWebRequest)WebRequest.Create(endpoint + path);
            request.Proxy = null;
            request.AllowAutoRedirect = false;
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            if (!String.IsNullOrEmpty(secret)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + secret;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException();
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var buffer = new char[4096];
                    var text = new StringBuilder();
                    int read;
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        text.Append(buffer, 0, read);
                        if (text.Length > 2 * 1024 * 1024) throw new InvalidDataException();
                    }
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text.ToString());
                    if (root == null) throw new InvalidDataException();
                    return root;
                }
            }
        }

        internal static string Explain(Exception error)
        {
            var web = error as WebException;
            if (web != null)
            {
                using (var response = web.Response as HttpWebResponse)
                {
                    if (response != null && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden))
                        return "控制接口拒绝访问，请检查控制密钥。";
                    if (response != null) return "接口返回 HTTP " + (int)response.StatusCode + "，请检查核心兼容性或节点状态。";
                }
                if (web.Status == WebExceptionStatus.Timeout) return "请求超时，请检查核心或上游是否正常运行。";
                return "接口无法连接，请检查本机地址、端口及核心状态。";
            }
            return "检查未完成，配置或接口返回内容不符合预期。原始错误已从报告中省略。";
        }

        private static object Value(Dictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) ? value : null;
        }
    }
}
