using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexProxyManager
{
    public sealed class ProxyState
    {
        public string NetworkMode { get; set; }
        public int ProxyEnable { get; set; }
        public bool HasProxyServer { get; set; }
        public string ProxyServer { get; set; }
        public bool HasAutoConfigURL { get; set; }
        public string AutoConfigURL { get; set; }
        public int CorePid { get; set; }
        public string SavedAt { get; set; }
    }

    internal sealed class NodeInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Delay { get; set; }
    }

    internal sealed class TrafficSnapshot
    {
        public long DownloadTotal { get; set; }
        public long UploadTotal { get; set; }
        public int Connections { get; set; }
    }


    internal static class Program
    {
        internal static string ManagedProxy = "127.0.0.1:17890";
        internal const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 1 && String.Equals(args[0], "--guide", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var guide = new GettingStartedForm(AppDomain.CurrentDomain.BaseDirectory,
                    StartupConfiguration.GetUserDataRoot(), false)) guide.ShowDialog();
                return;
            }
            if (args.Length >= 5 && String.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase))
            {
                RecoveryWatchdog.Run(args);
                return;
            }
            if (args.Length >= 1 && String.Equals(args[0], "--diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                using (var form = new DiagnosticsForm(delegate { return Diagnostics.ReadSavedInput(root); })) form.ShowDialog();
                return;
            }
            if (args.Length >= 1 && String.Equals(args[0], "--preview", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                DashboardForm preview = new DashboardForm();
                preview.UpdateNodes(new List<NodeInfo>
                {
                    new NodeInfo { Name = "示例-Hysteria2", Type = "Hysteria2", Delay = 238 },
                    new NodeInfo { Name = "示例-VLESS", Type = "Vless", Delay = 426 },
                    new NodeInfo { Name = "本地-SOCKS5", Type = "Socks5", Delay = 5 }
                }, "示例-Hysteria2");
                preview.UpdateStatus(true, "已连接", "示例-Hysteria2", 582000, 42000, 18400000, 2300000, 12);
                Application.Run(preview);
                return;
            }

            if (args.Length >= 1 && String.Equals(args[0], "--applications-preview", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string previewRoot = Path.Combine(Path.GetTempPath(), "CodexApplicationsPreview-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(previewRoot);
                try
                {
                    StartupConfiguration.WriteSettings(Path.Combine(previewRoot, "settings.json"), new ManagerSettings
                    {
                        SchemaVersion = 1, ActiveProfileId = "example", Profiles = new List<ProxyProfileDefinition>
                        { new ProxyProfileDefinition { Id = "example", Name = "示例代理档案" } }
                    });
                    using (var form = new ApplicationManagerForm(previewRoot)) form.ShowDialog();
                }
                finally { try { Directory.Delete(previewRoot, true); } catch { } }
                return;
            }

            if (args.Length >= 1 && String.Equals(args[0], "--profiles-preview", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string previewRoot = Path.Combine(Path.GetTempPath(), "CodexProxyManager-Preview-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(previewRoot);
                    ProfileManagerForm.ShowManager(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                        previewRoot, true);
                }
                finally
                {
                    try { if (Directory.Exists(previewRoot)) Directory.Delete(previewRoot, true); } catch { }
                }
                return;
            }

            if (args.Length >= 1 && String.Equals(args[0], "--setup", StringComparison.OrdinalIgnoreCase))
            {
                bool setupCreated;
                using (Mutex setupMutex = new Mutex(true, @"Local\CodexPrivateProxyManager", out setupCreated))
                {
                    if (!setupCreated)
                    {
                        MessageBox.Show("请先退出正在运行的管理器，再修改代理配置。", "Codex Proxy Manager",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    StartupConfiguration.RunSetup(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                    return;
                }
            }

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\CodexPrivateProxyManager", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Codex 专用代理管理器已经在运行。", "Codex Proxy Manager",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    string projectRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                    StartupProfile startup = StartupConfiguration.LoadOrCreate(projectRoot);
                    if (startup == null) return;
                    ManagedProxy = startup.ManagedProxy;
                    Application.Run(new ManagerContext(startup));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Codex Proxy Manager 启动失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        internal static ProxyState ReadState(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return new JavaScriptSerializer().Deserialize<ProxyState>(json);
        }

        internal static void WriteState(string path, ProxyState state)
        {
            string json = new JavaScriptSerializer().Serialize(state);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        internal static bool IsManagedProxyActive()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettings, false))
            {
                if (key == null) return false;
                object enabled = key.GetValue("ProxyEnable", 0);
                string server = Convert.ToString(key.GetValue("ProxyServer", ""));
                return Convert.ToInt32(enabled) == 1 &&
                    String.Equals(server, ManagedProxy, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static void RestoreProxy(ProxyState state, string logPath)
        {
            if (String.Equals(state.NetworkMode, "process", StringComparison.Ordinal)) return;
            if (!IsManagedProxyActive())
            {
                AppendLog(logPath, "Proxy was changed externally; skipped registry restore.");
                return;
            }

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettings, true))
            {
                if (key == null) throw new InvalidOperationException("Cannot open Internet Settings registry key.");
                key.SetValue("ProxyEnable", state.ProxyEnable, RegistryValueKind.DWord);
                if (state.HasProxyServer)
                    key.SetValue("ProxyServer", state.ProxyServer ?? "", RegistryValueKind.String);
                else
                    key.DeleteValue("ProxyServer", false);

                if (state.HasAutoConfigURL)
                    key.SetValue("AutoConfigURL", state.AutoConfigURL ?? "", RegistryValueKind.String);
                else
                    key.DeleteValue("AutoConfigURL", false);
            }
            NativeMethods.RefreshInternetSettings();
            AppendLog(logPath, "Restored the previous Windows proxy state.");
        }

        internal static void AppendLog(string path, string message)
        {
            PrivacyLog.AppendManager(path, message);
        }
    }

    internal sealed class ManagerContext : ApplicationContext
    {
        private readonly string projectRoot;
        private readonly string corePath;
        private readonly string coreHome;
        private string configPath;
        private string sessionConfigPath;
        private ManagedApplication targetApplication;
        private readonly string statePath;
        private readonly string managerLog;
        private readonly string coreOutLog;
        private readonly string coreErrLog;
        private readonly int mixedPort;
        private readonly int controllerPort;
        private readonly string controllerSecret;
        private string nodeControllerUrl;
        private string nodeControllerSecret;
        private bool autoSelectFastest;
        private bool autoFailover;
        private string preferredProxyGroupName;
        private string activeProfileName;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem startItem;
        private readonly ToolStripMenuItem stopItem;
        private readonly DashboardForm host;
        private readonly System.Windows.Forms.Timer timer;
        private readonly object sessionLock = new object();

        private Process coreProcess;
        private volatile bool sessionActive;
        private volatile bool busy;
        private volatile bool sawCodex;
        private bool allowExit;
        private int noCodexTicks;
        private int dashboardTicks;
        private int healthCheckTicks;
        private int consecutiveHealthFailures;
        private int healthCheckRunning;
        private long previousDownloadTotal;
        private long previousUploadTotal;
        private DateTime previousTrafficSample;
        private string proxyGroupName;
        private string currentNodeName;
        private List<NodeInfo> currentNodes = new List<NodeInfo>();

        internal ManagerContext(StartupProfile startup)
        {
            projectRoot = startup.ProjectRoot;
            corePath = Path.Combine(projectRoot, "runtime", "mihomo.exe");
            coreHome = startup.CoreHome;
            configPath = startup.ConfigPath;
            targetApplication = startup.Application ?? ApplicationConfiguration.DefaultApplication();
            statePath = startup.StatePath;
            managerLog = startup.ManagerLog;
            coreOutLog = startup.CoreOutLog;
            coreErrLog = startup.CoreErrLog;
            mixedPort = startup.MixedPort;
            controllerPort = startup.ControllerPort;
            controllerSecret = startup.ControllerSecret;
            nodeControllerUrl = String.IsNullOrWhiteSpace(startup.NodeControllerUrl)
                ? "http://127.0.0.1:" + startup.ControllerPort : startup.NodeControllerUrl;
            nodeControllerSecret = String.IsNullOrWhiteSpace(startup.NodeControllerUrl)
                ? startup.ControllerSecret : startup.NodeControllerSecret;
            autoSelectFastest = startup.AutoSelectFastest;
            autoFailover = startup.AutoFailover;
            preferredProxyGroupName = startup.ProxyGroupName;
            activeProfileName = startup.ProfileName;
            RotateLog(managerLog);
            RotateLog(coreOutLog);
            RotateLog(coreErrLog);

            host = new DashboardForm();
            host.SetLocalProxyEndpoint(startup.ManagedProxy);
            host.SetProfileName(startup.ProfileName);
            host.SetNodeProfile(startup.UserDataRoot, startup.ProfileId);
            host.SetApplicationName(targetApplication.Name);
            host.PrivacyRequested += delegate
            {
                if (busy) return;
                using (var form = new PrivacyCenterForm(delegate { return targetApplication; },
                    delegate { return sessionActive; }, startup.LogsDirectory, host,
                    delegate { OpenConfiguration(true); })) form.ShowDialog(host);
            };
            host.FormClosing += HostFormClosing;
            host.StartRequested += delegate { BeginStart(); };
            host.StopRequested += delegate { BeginStop(true); };
            host.TestRequested += delegate { BeginLatencyTest(); };
            host.FastestRequested += delegate { BeginFastestSelection(false); };
            host.RefreshProvidersRequested += delegate { BeginProviderRefresh(); };
            host.NodeSwitchRequested += delegate(string node) { BeginNodeSwitch(node); };
            host.ProfilesRequested += delegate { OpenProfileManager(); };
            host.ApplicationsRequested += delegate { OpenConfiguration(true); };
            host.DiagnosticsRequested += delegate
            {
                if (busy) return;
                using (var form = new DiagnosticsForm(CaptureDiagnosticInput)) form.ShowDialog(host);
            };
            host.ExitRequested += delegate { ExitManager(); };
            MainForm = host;
            host.Show();

            ContextMenuStrip menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("状态：正在初始化") { Enabled = false };
            ToolStripMenuItem dashboardItem = new ToolStripMenuItem("打开管理面板");
            startItem = new ToolStripMenuItem("启动当前应用（专用代理）");
            stopItem = new ToolStripMenuItem("停止专用代理");
            ToolStripMenuItem profilesItem = new ToolStripMenuItem("管理代理档案");
            ToolStripMenuItem openItem = new ToolStripMenuItem("打开用户配置目录");
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出管理器");
            dashboardItem.Click += delegate { host.ShowDashboard(); };
            startItem.Click += delegate { BeginStart(); };
            stopItem.Click += delegate { BeginStop(true); };
            profilesItem.Click += delegate { OpenProfileManager(); };
            openItem.Click += delegate { Process.Start("explorer.exe", startup.UserDataRoot); };
            exitItem.Click += delegate { ExitManager(); };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(dashboardItem);
            menu.Items.Add(startItem);
            menu.Items.Add(stopItem);
            menu.Items.Add(profilesItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(openItem);
            menu.Items.Add(exitItem);

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Shield;
            tray.Text = "Codex 专用代理管理器";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { host.ShowDashboard(); };

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += TimerTick;
            timer.Start();
            Program.AppendLog(managerLog, "Manager started.");
        }

        private void TimerTick(object sender, EventArgs e)
        {
            // Opening the manager never starts an application. Only explicit
            // dashboard/tray start actions may call BeginStart.
            bool codexRunning = IsCodexRunning();
            if (sessionActive && sawCodex)
            {
                if (codexRunning)
                    noCodexTicks = 0;
                else if (++noCodexTicks >= 4)
                    BeginStop(false);
            }

            if (busy)
                SetStatus("状态：正在处理…");
            else if (sessionActive && TestPort(mixedPort, 150))
                SetStatus("状态：运行中 · " + mixedPort);
            else
                SetStatus("状态：专用代理已停止");

            startItem.Enabled = !busy && !sessionActive;
            stopItem.Enabled = !busy && (sessionActive || File.Exists(statePath));
            if (sessionActive && autoFailover && !busy && ++healthCheckTicks >= 30)
            {
                healthCheckTicks = 0;
                BeginAutoHealthCheck();
            }
            UpdateDashboard();
        }

        private void SetStatus(string text)
        {
            statusItem.Text = text;
            tray.Text = text.Length > 60 ? text.Substring(0, 60) : text;
        }

        private void UpdateDashboard()
        {
            if (!sessionActive || !TestPort(mixedPort, 100))
            {
                previousTrafficSample = DateTime.MinValue;
                previousDownloadTotal = 0;
                previousUploadTotal = 0;
                host.UpdateStatus(false, busy ? "正在处理" : "已停止", "—", 0, 0, 0, 0, 0);
                return;
            }
            if (busy)
            {
                host.UpdateStatus(true, "正在处理", currentNodeName, 0, 0,
                    previousDownloadTotal, previousUploadTotal, 0);
                return;
            }

            try
            {
                dashboardTicks++;
                if (currentNodes.Count == 0 || dashboardTicks % 5 == 0)
                {
                    RefreshProxyInfo();
                    host.UpdateNodes(currentNodes, currentNodeName);
                }

                TrafficSnapshot snapshot = GetTrafficSnapshot();
                DateTime now = DateTime.Now;
                double downloadRate = 0;
                double uploadRate = 0;
                if (previousTrafficSample != DateTime.MinValue)
                {
                    double seconds = Math.Max(0.2, (now - previousTrafficSample).TotalSeconds);
                    downloadRate = Math.Max(0, snapshot.DownloadTotal - previousDownloadTotal) / seconds;
                    uploadRate = Math.Max(0, snapshot.UploadTotal - previousUploadTotal) / seconds;
                }
                previousTrafficSample = now;
                previousDownloadTotal = snapshot.DownloadTotal;
                previousUploadTotal = snapshot.UploadTotal;
                host.UpdateStatus(true, busy ? "正在处理" : "已连接", currentNodeName,
                    downloadRate, uploadRate, snapshot.DownloadTotal, snapshot.UploadTotal, snapshot.Connections);
            }
            catch (Exception ex)
            {
                host.UpdateStatus(true, "代理运行中", currentNodeName, 0, 0,
                    previousDownloadTotal, previousUploadTotal, 0);
                if (dashboardTicks % 15 == 0) Program.AppendLog(managerLog, "Dashboard refresh error: " + ex.Message);
            }
        }

        private TrafficSnapshot GetTrafficSnapshot()
        {
            Dictionary<string, object> root = CoreApiGet("/connections");
            TrafficSnapshot snapshot = new TrafficSnapshot();
            snapshot.DownloadTotal = ToInt64(GetValue(root, "downloadTotal"));
            snapshot.UploadTotal = ToInt64(GetValue(root, "uploadTotal"));
            object connections = GetValue(root, "connections");
            IList list = connections as IList;
            snapshot.Connections = list == null ? 0 : list.Count;
            return snapshot;
        }

        private void RefreshProxyInfo()
        {
            Dictionary<string, object> root = NodeApiGet("/proxies");
            Dictionary<string, object> proxies = GetValue(root, "proxies") as Dictionary<string, object>;
            if (proxies == null) return;

            Dictionary<string, object> selector = null;
            string selectorName = null;
            int selectorScore = -1;
            foreach (KeyValuePair<string, object> pair in proxies)
            {
                Dictionary<string, object> candidate = pair.Value as Dictionary<string, object>;
                if (candidate == null || !String.Equals(Convert.ToString(GetValue(candidate, "type")), "Selector", StringComparison.OrdinalIgnoreCase))
                    continue;
                List<string> all = ToStringList(GetValue(candidate, "all"));
                bool isGlobal = all.Any(delegate(string name) { return String.Equals(name, "REJECT", StringComparison.OrdinalIgnoreCase); });
                int leafNodeCount = 0;
                foreach (string memberName in all)
                {
                    if (String.Equals(memberName, "DIRECT", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(memberName, "REJECT", StringComparison.OrdinalIgnoreCase)) continue;
                    object memberValue;
                    Dictionary<string, object> member = proxies.TryGetValue(memberName, out memberValue)
                        ? memberValue as Dictionary<string, object> : null;
                    string memberType = Convert.ToString(GetValue(member, "type"));
                    if (!String.Equals(memberType, "Selector", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(memberType, "URLTest", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(memberType, "Fallback", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(memberType, "LoadBalance", StringComparison.OrdinalIgnoreCase))
                        leafNodeCount++;
                }
                if (!String.IsNullOrWhiteSpace(preferredProxyGroupName) &&
                    String.Equals(pair.Key, preferredProxyGroupName, StringComparison.OrdinalIgnoreCase))
                {
                    selector = candidate;
                    selectorName = pair.Key;
                    break;
                }
                if (!isGlobal && leafNodeCount > selectorScore)
                {
                    selector = candidate;
                    selectorName = pair.Key;
                    selectorScore = leafNodeCount;
                }
            }
            if (selector == null) return;

            proxyGroupName = selectorName;
            currentNodeName = Convert.ToString(GetValue(selector, "now"));
            List<NodeInfo> nodes = new List<NodeInfo>();
            foreach (string name in ToStringList(GetValue(selector, "all")))
            {
                if (String.Equals(name, "DIRECT", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, "REJECT", StringComparison.OrdinalIgnoreCase)) continue;
                Dictionary<string, object> detail = null;
                object value;
                if (proxies.TryGetValue(name, out value)) detail = value as Dictionary<string, object>;
                if (detail == null) continue;
                nodes.Add(new NodeInfo
                {
                    Name = name,
                    Type = Convert.ToString(GetValue(detail, "type")),
                    Delay = GetLatestDelay(detail)
                });
            }
            currentNodes = nodes;
        }

        private void BeginLatencyTest()
        {
            if (busy || !sessionActive) return;
            busy = true;
            host.SetBusy(true, "正在并行测试所有节点延迟…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    MeasureGroupDelays();
                    Ui(delegate { host.UpdateNodes(currentNodes, currentNodeName); });
                    Notify("节点延迟测试完成。", ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    Program.AppendLog(managerLog, "Latency test error: " + ex.Message);
                    ShowError("节点测速失败：" + ex.Message);
                }
                finally
                {
                    busy = false;
                    Ui(delegate { host.SetBusy(false, "节点延迟测试完成。"); });
                }
            });
        }

        private void BeginFastestSelection(bool automatic)
        {
            if (busy || !sessionActive) return;
            busy = true;
            host.SetBusy(true, automatic ? "正在自动选择可用节点…" : "正在测速并选择最低延迟节点…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    MeasureGroupDelays();
                    NodeInfo fastest = currentNodes.Where(delegate(NodeInfo node) { return node.Delay > 0; })
                        .OrderBy(delegate(NodeInfo node) { return node.Delay; }).FirstOrDefault();
                    if (fastest == null) throw new InvalidOperationException("没有检测到可用节点。");
                    bool switched = SwitchNodeCore(fastest.Name);
                    Ui(delegate { host.UpdateNodes(currentNodes, currentNodeName); });
                    if (switched)
                    {
                        string reason = automatic ? "自动切换" : "最低延迟选择";
                        Program.AppendLog(managerLog, reason + " selected " + fastest.Name + " at " + fastest.Delay + " ms.");
                        Notify("已切换到 " + fastest.Name + "（" + fastest.Delay + " ms）。", ToolTipIcon.Info);
                    }
                    else if (!automatic)
                    {
                        Notify("当前节点已经是最低延迟节点（" + fastest.Delay + " ms）。", ToolTipIcon.Info);
                    }
                }
                catch (Exception ex)
                {
                    Program.AppendLog(managerLog, "Fastest selection error: " + ex.Message);
                    if (automatic)
                        Notify("自动选择节点失败，请稍后手动测速。", ToolTipIcon.Warning);
                    else
                        ShowError("选择最低延迟节点失败：" + ex.Message);
                }
                finally
                {
                    busy = false;
                    Ui(delegate { host.SetBusy(false, ""); });
                }
            });
        }

        private Dictionary<string, object> MeasureGroupDelays()
        {
            if (String.IsNullOrWhiteSpace(proxyGroupName)) RefreshProxyInfo();
            if (String.IsNullOrWhiteSpace(proxyGroupName)) throw new InvalidOperationException("找不到可测速的代理策略组。");
            string path = "/group/" + Uri.EscapeDataString(proxyGroupName) +
                "/delay?url=https%3A%2F%2Fwww.gstatic.com%2Fgenerate_204&timeout=10000";
            Dictionary<string, object> delays = NodeApiGet(path);
            foreach (NodeInfo node in currentNodes)
            {
                node.Delay = 0;
                object delay;
                if (delays.TryGetValue(node.Name, out delay)) node.Delay = Convert.ToInt32(delay);
            }
            return delays;
        }

        private void BeginNodeSwitch(string nodeName)
        {
            if (busy || !sessionActive || String.IsNullOrWhiteSpace(nodeName)) return;
            busy = true;
            host.SetBusy(true, "正在切换到 " + nodeName + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    SwitchNodeCore(nodeName);
                    Ui(delegate { host.UpdateNodes(currentNodes, currentNodeName); });
                    Program.AppendLog(managerLog, "Switched node to " + nodeName + ".");
                    Notify("已切换节点，Codex 正在重新连接。", ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    Program.AppendLog(managerLog, "Node switch error: " + ex.Message);
                    ShowError("节点切换失败：" + ex.Message);
                }
                finally
                {
                    busy = false;
                    Ui(delegate { host.SetBusy(false, ""); });
                }
            });
        }

        private bool SwitchNodeCore(string nodeName)
        {
            if (String.IsNullOrWhiteSpace(proxyGroupName)) RefreshProxyInfo();
            if (String.IsNullOrWhiteSpace(proxyGroupName)) throw new InvalidOperationException("找不到可切换的代理策略组。");
            if (String.Equals(currentNodeName, nodeName, StringComparison.Ordinal)) return false;
            string groupPath = "/proxies/" + Uri.EscapeDataString(proxyGroupName);
            NodeApiPut(groupPath, new JavaScriptSerializer().Serialize(new Dictionary<string, string> { { "name", nodeName } }));
            CoreApiDelete("/connections");
            currentNodeName = nodeName;
            consecutiveHealthFailures = 0;
            RefreshProxyInfo();
            return true;
        }

        private void BeginProviderRefresh()
        {
            if (busy || !sessionActive) return;
            busy = true;
            host.SetBusy(true, "正在刷新远程订阅…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Dictionary<string, object> root = NodeApiGet("/providers/proxies");
                    Dictionary<string, object> providers = GetValue(root, "providers") as Dictionary<string, object>;
                    int updated = 0;
                    if (providers != null)
                    {
                        foreach (KeyValuePair<string, object> pair in providers)
                        {
                            Dictionary<string, object> provider = pair.Value as Dictionary<string, object>;
                            string vehicle = Convert.ToString(GetValue(provider, "vehicleType"));
                            if (!String.Equals(vehicle, "HTTP", StringComparison.OrdinalIgnoreCase)) continue;
                            NodeApiPutEmpty("/providers/proxies/" + Uri.EscapeDataString(pair.Key));
                            updated++;
                        }
                    }
                    if (updated == 0)
                    {
                        Notify("当前档案没有远程订阅，无需刷新。", ToolTipIcon.Info);
                        return;
                    }
                    RefreshProxyInfo();
                    Ui(delegate { host.UpdateNodes(currentNodes, currentNodeName); });
                    Program.AppendLog(managerLog, "Refreshed " + updated + " remote proxy provider(s).");
                    Notify("远程订阅刷新完成，共更新 " + updated + " 个来源。", ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    Program.AppendLog(managerLog, "Provider refresh error: " + ex.Message);
                    ShowError("刷新远程订阅失败：" + ex.Message);
                }
                finally
                {
                    busy = false;
                    Ui(delegate { host.SetBusy(false, ""); });
                }
            });
        }

        private void BeginAutoHealthCheck()
        {
            if (!autoFailover || !sessionActive || busy || String.IsNullOrWhiteSpace(currentNodeName)) return;
            bool isManagedNode = currentNodes.Any(delegate(NodeInfo node)
            {
                return String.Equals(node.Name, currentNodeName, StringComparison.Ordinal);
            });
            if (!isManagedNode || Interlocked.CompareExchange(ref healthCheckRunning, 1, 0) != 0) return;
            string checkedNode = currentNodeName;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool triggerFailover = false;
                try
                {
                    string path = "/proxies/" + Uri.EscapeDataString(checkedNode) +
                        "/delay?url=https%3A%2F%2Fwww.gstatic.com%2Fgenerate_204&timeout=5000";
                    Dictionary<string, object> result = NodeApiGet(path);
                    int delay = Convert.ToInt32(GetValue(result, "delay") ?? 0);
                    if (delay <= 0) throw new InvalidOperationException("节点探测未返回有效延迟。");
                    consecutiveHealthFailures = 0;
                    NodeInfo node = currentNodes.FirstOrDefault(delegate(NodeInfo item)
                    {
                        return String.Equals(item.Name, checkedNode, StringComparison.Ordinal);
                    });
                    if (node != null) node.Delay = delay;
                }
                catch (Exception ex)
                {
                    int failures = Interlocked.Increment(ref consecutiveHealthFailures);
                    Program.AppendLog(managerLog, "Health check failed for " + checkedNode + " (" + failures + "/3): " + ex.Message);
                    if (failures >= 3)
                    {
                        consecutiveHealthFailures = 0;
                        triggerFailover = true;
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref healthCheckRunning, 0);
                }
                if (triggerFailover)
                {
                    Program.AppendLog(managerLog, "Auto failover triggered for " + checkedNode + ".");
                    Ui(delegate
                    {
                        if (!busy && sessionActive) BeginFastestSelection(true);
                    });
                }
            });
        }

        private Dictionary<string, object> CoreApiGet(string path)
        {
            return ApiGet("http://127.0.0.1:" + controllerPort, controllerSecret, path);
        }

        private Dictionary<string, object> NodeApiGet(string path)
        {
            return ApiGet(nodeControllerUrl, nodeControllerSecret, path);
        }

        private static Dictionary<string, object> ApiGet(string baseUrl, string secret, string path)
        {
            using (WebClient client = CreateApiClient(secret))
            {
                string json = client.DownloadString(baseUrl + path);
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
        }

        private void NodeApiPut(string path, string body)
        {
            using (WebClient client = CreateApiClient(nodeControllerSecret))
            {
                client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                client.UploadString(nodeControllerUrl + path, "PUT", body);
            }
        }

        private void NodeApiPutEmpty(string path)
        {
            using (WebClient client = CreateApiClient(nodeControllerSecret))
            {
                client.UploadString(nodeControllerUrl + path, "PUT", "");
            }
        }

        private void CoreApiDelete(string path)
        {
            using (WebClient client = CreateApiClient(controllerSecret))
            {
                client.UploadString("http://127.0.0.1:" + controllerPort + path, "DELETE", "");
            }
        }

        private static WebClient CreateApiClient(string secret)
        {
            WebClient client = new WebClient();
            client.Proxy = null;
            client.Encoding = Encoding.UTF8;
            if (!String.IsNullOrWhiteSpace(secret))
                client.Headers[HttpRequestHeader.Authorization] = "Bearer " + secret;
            return client;
        }

        private static object GetValue(Dictionary<string, object> values, string name)
        {
            object value;
            return values != null && values.TryGetValue(name, out value) ? value : null;
        }

        private static long ToInt64(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt64(value); }
            catch { return 0; }
        }

        private static List<string> ToStringList(object value)
        {
            List<string> result = new List<string>();
            IList list = value as IList;
            if (list == null) return result;
            foreach (object item in list) result.Add(Convert.ToString(item));
            return result;
        }

        private static int GetLatestDelay(Dictionary<string, object> detail)
        {
            IList history = GetValue(detail, "history") as IList;
            if (history == null || history.Count == 0) return 0;
            Dictionary<string, object> latest = history[history.Count - 1] as Dictionary<string, object>;
            return latest == null ? 0 : Convert.ToInt32(GetValue(latest, "delay") ?? 0);
        }

        private void OpenProfileManager()
        {
            OpenConfiguration(false);
        }

        private DiagnosticInput CaptureDiagnosticInput()
        {
            bool alive = false;
            try { alive = coreProcess != null && !coreProcess.HasExited; } catch { }
            return new DiagnosticInput
            {
                CorePath = corePath, ConfigPath = configPath, CoreHome = coreHome, StatePath = statePath,
                MixedPort = mixedPort, SessionActive = sessionActive, CoreAlive = alive,
                ApplicationRunning = ApplicationConfiguration.IsRunning(targetApplication),
                PrivacySummary = PrivacyProtection.Describe(targetApplication, sessionActive),
                ProcessNames = targetApplication.Id == "codex" ? new[] { "ChatGPT.exe", "codex.exe" } : new[] { Path.GetFileName(targetApplication.Executable) },
                CoreUrl = "http://127.0.0.1:" + controllerPort, CoreSecret = controllerSecret,
                NodeUrl = nodeControllerUrl, NodeSecret = nodeControllerSecret,
                GroupName = proxyGroupName ?? preferredProxyGroupName
            };
        }

        private void OpenConfiguration(bool applications)
        {
            if (busy) return;
            if (sessionActive)
            {
                MessageBox.Show("请先点击“停止专用代理”，再切换应用或代理档案。\n再次启动前，需要完全退出所选应用。",
                    "代理正在运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            busy = true;
            host.SetBusy(true, "正在管理代理档案…");
            try
            {
                if (applications)
                {
                    using (var form = new ApplicationManagerForm(StartupConfiguration.GetUserDataRoot()))
                        if (form.ShowDialog(host) != DialogResult.OK) return;
                }
                else if (!StartupConfiguration.RunSetup(projectRoot)) return;
                StartupProfile updated = StartupConfiguration.LoadOrCreate(projectRoot);
                if (updated.MixedPort != mixedPort || updated.ControllerPort != controllerPort ||
                    !String.Equals(updated.ControllerSecret, controllerSecret, StringComparison.Ordinal))
                    throw new InvalidOperationException("本地端口或管理密钥发生变化，请退出管理器后重新打开。");
                configPath = updated.ConfigPath;
                targetApplication = updated.Application;
                host.SetApplicationName(targetApplication.Name);
                preferredProxyGroupName = updated.ProxyGroupName;
                nodeControllerUrl = String.IsNullOrWhiteSpace(updated.NodeControllerUrl)
                    ? "http://127.0.0.1:" + controllerPort : updated.NodeControllerUrl;
                nodeControllerSecret = String.IsNullOrWhiteSpace(updated.NodeControllerUrl)
                    ? controllerSecret : updated.NodeControllerSecret;
                activeProfileName = updated.ProfileName;
                autoSelectFastest = updated.AutoSelectFastest;
                autoFailover = updated.AutoFailover;
                proxyGroupName = null;
                currentNodeName = null;
                currentNodes = new List<NodeInfo>();
                host.UpdateNodes(currentNodes, null);
                host.SetProfileName(activeProfileName);
                host.SetNodeProfile(updated.UserDataRoot, updated.ProfileId);
                Notify("已切换到代理档案：“" + activeProfileName + "”。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Program.AppendLog(managerLog, "Profile manager error: " + ex);
                ShowError("代理档案更新失败：" + ex.Message);
            }
            finally
            {
                busy = false;
                host.SetBusy(false, "");
            }
        }

        private void BeginStart()
        {
            if (busy) return;
            if (sessionActive)
            {
                Notify("代理已经在运行。", ToolTipIcon.Info);
                return;
            }
            busy = true;
            host.SetBusy(true, "正在启动专用代理并打开 " + targetApplication.Name + "…");
            ThreadPool.QueueUserWorkItem(delegate { StartSessionWorker(); });
        }

        private void StartSessionWorker()
        {
            bool startedSuccessfully = false;
            try
            {
                lock (sessionLock)
                {
                    RecoverStaleSession();
                    if (IsCodexRunning())
                        throw new InvalidOperationException("检测到 " + targetApplication.Name + " 已经运行。请完全退出该应用后再启动专用代理。");

                    ValidateFiles();
                    string executable = ApplicationConfiguration.ResolveExecutable(targetApplication);
                    if (targetApplication.Id != "codex")
                    {
                        string yaml = ApplicationConfiguration.BuildSessionConfig(File.ReadAllText(configPath, Encoding.UTF8), targetApplication);
                        sessionConfigPath = Path.Combine(Path.GetDirectoryName(statePath), "session-config.yaml");
                        File.WriteAllText(sessionConfigPath, yaml, new UTF8Encoding(false));
                    }
                    StartCore();
                    ProxyState state = new ProxyState
                    {
                        NetworkMode = "process",
                        CorePid = coreProcess.Id,
                        SavedAt = DateTime.UtcNow.ToString("o")
                    };
                    Program.WriteState(statePath, state);
                    StartWatchdog(coreProcess.Id);
                    using (Process application = Process.Start(ProcessProxyLauncher.BuildStartInfo(executable, mixedPort, targetApplication)))
                        Program.AppendLog(managerLog, "Launched application PID " + application.Id + " with process-only proxy; Windows proxy unchanged.");

                    DateTime deadline = DateTime.Now.AddSeconds(12);
                    while (DateTime.Now < deadline && !IsCodexRunning()) Thread.Sleep(250);
                    if (!IsCodexRunning()) throw new InvalidOperationException("未检测到应用进程，可能程序已退出或不支持所选启动方式。");

                    sawCodex = true;
                    noCodexTicks = 0;
                    sessionActive = true;
                }
                startedSuccessfully = true;
                Notify(targetApplication.Name + " 专用代理已启动。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Program.AppendLog(managerLog, "START ERROR: " + ex);
                try { EndSessionCore(); } catch { }
                ShowError(ex.Message);
            }
            finally
            {
                busy = false;
                Ui(delegate
                {
                    host.SetBusy(false, "");
                    if (startedSuccessfully && autoSelectFastest) BeginFastestSelection(true);
                });
            }
        }

        private void BeginStop(bool notify)
        {
            if (busy) return;
            busy = true;
            host.SetBusy(true, "正在停止 Codex 专用代理…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    lock (sessionLock) { EndSessionCore(); }
                    if (notify) Notify("专用代理已停止。若要继续使用，请完全退出当前应用后重新启动。", ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    Program.AppendLog(managerLog, "STOP ERROR: " + ex);
                    ShowError("停止专用代理时发生错误：" + ex.Message);
                }
                finally
                {
                    busy = false;
                    Ui(delegate { host.SetBusy(false, ""); });
                }
            });
        }

        private void ValidateFiles()
        {
            if (!File.Exists(corePath)) throw new FileNotFoundException("找不到内置 Mihomo 核心。", corePath);
            if (!File.Exists(configPath)) throw new FileNotFoundException("找不到代理配置。", configPath);
            if (!File.Exists(Path.Combine(coreHome, "GeoSite.dat"))) throw new FileNotFoundException("找不到 GeoSite.dat。", coreHome);
            if (!File.Exists(Path.Combine(coreHome, "geoip.metadb"))) throw new FileNotFoundException("找不到 geoip.metadb。", coreHome);
            if (TestPort(mixedPort, 150)) throw new InvalidOperationException("端口 " + mixedPort + " 已被其他程序占用。");
            if (TestPort(controllerPort, 150)) throw new InvalidOperationException("管理接口端口 " + controllerPort + " 已被其他程序占用。");
        }

        private void StartCore()
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = corePath;
            info.Arguments = "-d " + Quote(coreHome) + " -f " + Quote(sessionConfigPath ?? configPath) +
                " -ext-ctl 127.0.0.1:" + controllerPort + " -secret " + Quote(controllerSecret);
            info.WorkingDirectory = Path.GetDirectoryName(configPath);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            coreProcess = new Process();
            coreProcess.StartInfo = info;
            coreProcess.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) AppendCoreLog(coreOutLog, e.Data);
            };
            coreProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) AppendCoreLog(coreErrLog, e.Data);
            };
            coreProcess.Start();
            coreProcess.BeginOutputReadLine();
            coreProcess.BeginErrorReadLine();
            Program.AppendLog(managerLog, "Started bundled Mihomo PID " + coreProcess.Id + ".");

            DateTime deadline = DateTime.Now.AddSeconds(35);
            while (DateTime.Now < deadline)
            {
                if (coreProcess.HasExited) throw new InvalidOperationException("Mihomo 启动失败，退出代码：" + coreProcess.ExitCode);
                if (TestPort(mixedPort, 250)) { ReduceCoreLogging(); return; }
                Thread.Sleep(200);
            }
            throw new TimeoutException("代理核心未能在 35 秒内监听端口 " + mixedPort + "。");
        }

        private void AppendCoreLog(string path, string line)
        {
            PrivacyLog.AppendCore(path, line);
        }

        private void ReduceCoreLogging()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + controllerPort + "/configs");
                request.Method = "PATCH";
                request.Proxy = null;
                request.AllowAutoRedirect = false;
                request.Timeout = request.ReadWriteTimeout = 2000;
                request.ContentType = "application/json";
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + controllerSecret;
                byte[] body = Encoding.UTF8.GetBytes("{\"log-level\":\"warning\"}");
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (var response = request.GetResponse()) { }
            }
            catch { Program.AppendLog(managerLog, "Core log preference error."); }
        }

        private static void RotateLog(string path)
        {
            try
            {
                PrivacyLog.Rotate(path);
            }
            catch { }
        }

        private void EndSessionCore()
        {
            ProxyState state = null;
            if (File.Exists(statePath))
            {
                try { state = Program.ReadState(statePath); }
                catch (Exception ex) { Program.AppendLog(managerLog, "Could not read state: " + ex.Message); }
            }

            if (state != null) Program.RestoreProxy(state, managerLog);
            StopCore();
            if (!String.IsNullOrEmpty(sessionConfigPath))
            {
                try { File.Delete(sessionConfigPath); } catch { }
                sessionConfigPath = null;
            }
            try { if (File.Exists(statePath)) File.Delete(statePath); } catch { }
            sessionActive = false;
            sawCodex = false;
            noCodexTicks = 0;
            dashboardTicks = 0;
            healthCheckTicks = 0;
            consecutiveHealthFailures = 0;
            Interlocked.Exchange(ref healthCheckRunning, 0);
            previousTrafficSample = DateTime.MinValue;
            previousDownloadTotal = 0;
            previousUploadTotal = 0;
            proxyGroupName = null;
            currentNodeName = null;
            currentNodes = new List<NodeInfo>();
            Program.AppendLog(managerLog, "Session ended.");
        }

        private void StopCore()
        {
            try
            {
                if (coreProcess != null && !coreProcess.HasExited)
                {
                    int pid = coreProcess.Id;
                    coreProcess.Kill();
                    coreProcess.WaitForExit(5000);
                    Program.AppendLog(managerLog, "Stopped bundled Mihomo PID " + pid + ".");
                }
            }
            catch (Exception ex) { Program.AppendLog(managerLog, "Could not stop core: " + ex.Message); }
            finally
            {
                if (coreProcess != null) coreProcess.Dispose();
                coreProcess = null;
            }
        }

        private void RecoverStaleSession()
        {
            if (File.Exists(statePath))
            {
                Program.AppendLog(managerLog, "Found stale manager state.");
                try { Program.RestoreProxy(Program.ReadState(statePath), managerLog); }
                catch (Exception ex) { Program.AppendLog(managerLog, "Stale restore error: " + ex.Message); }
            }
            StopBundledOrphanCores();
            try { if (File.Exists(statePath)) File.Delete(statePath); } catch { }
        }

        private void StopBundledOrphanCores()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='mihomo.exe'"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string command = Convert.ToString(item["CommandLine"]);
                        if (command.IndexOf(corePath, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            (command.IndexOf(configPath, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             command.IndexOf(Path.Combine(Path.GetDirectoryName(statePath), "session-config.yaml"), StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            int pid = Convert.ToInt32((uint)item["ProcessId"]);
                            try { Process.GetProcessById(pid).Kill(); Program.AppendLog(managerLog, "Stopped stale bundled core PID " + pid + "."); }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Program.AppendLog(managerLog, "Orphan scan error: " + ex.Message); }
        }

        private void StartWatchdog(int corePid)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Application.ExecutablePath;
            info.Arguments = "--watchdog " + Process.GetCurrentProcess().Id + " " + corePid + " " +
                Quote(statePath) + " " + Quote(managerLog) + " " + Quote(Program.ManagedProxy);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(info);
            Program.AppendLog(managerLog, "Started recovery watchdog.");
        }

        private bool IsCodexRunning()
        {
            try
            {
                return ApplicationConfiguration.IsRunning(targetApplication);
            }
            catch { return false; }
        }

        private static bool TestPort(int port, int timeoutMs)
        {
            using (TcpClient client = new TcpClient())
            {
                try
                {
                    IAsyncResult result = client.BeginConnect("127.0.0.1", port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    client.EndConnect(result);
                    return client.Connected;
                }
                catch { return false; }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void Notify(string message, ToolTipIcon icon)
        {
            Ui(delegate
            {
                tray.BalloonTipTitle = "Codex Proxy Manager";
                tray.BalloonTipText = message;
                if (host.ScreenshotPrivacy) tray.BalloonTipText = "操作状态已更新；截图模式下隐藏详细信息。";
                tray.BalloonTipIcon = icon;
                tray.ShowBalloonTip(3000);
            });
        }

        private void ShowError(string message)
        {
            Ui(delegate
            {
                MessageBox.Show(host.ScreenshotPrivacy ? "操作失败。截图模式下已隐藏详情，请使用连接诊断。" : message,
                    "Codex Proxy Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }

        private void Ui(MethodInvoker action)
        {
            try
            {
                if (host.IsDisposed) return;
                if (host.InvokeRequired) host.BeginInvoke(action);
                else action();
            }
            catch { }
        }

        private void ExitManager()
        {
            if (busy)
            {
                MessageBox.Show("管理器正在处理网络状态，请稍后再退出。", "Codex Proxy Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { lock (sessionLock) { EndSessionCore(); } }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Codex Proxy Manager", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            allowExit = true;
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            host.Close();
            ExitThread();
        }

        private void HostFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!allowExit)
            {
                e.Cancel = true;
                host.Hide();
            }
        }

        protected override void ExitThreadCore()
        {
            try { if (File.Exists(statePath)) EndSessionCore(); } catch { }
            Program.AppendLog(managerLog, "Manager exited.");
            base.ExitThreadCore();
        }
    }

    internal static class RecoveryWatchdog
    {
        internal static void Run(string[] args)
        {
            int managerPid;
            int corePid;
            if (!Int32.TryParse(args[1], out managerPid) || !Int32.TryParse(args[2], out corePid)) return;
            string statePath = args[3];
            string logPath = args[4];
            if (args.Length >= 6 && !String.IsNullOrWhiteSpace(args[5])) Program.ManagedProxy = args[5];
            try
            {
                while (IsProcessRunning(managerPid))
                {
                    // A normal stop removes the session state before the manager exits.
                    // Exit immediately so old watchdog processes cannot accumulate.
                    if (!File.Exists(statePath) || Program.ReadState(statePath).CorePid != corePid) return;
                    Thread.Sleep(1000);
                }
                if (!File.Exists(statePath) || Program.ReadState(statePath).CorePid != corePid) return;
                Program.AppendLog(logPath, "Watchdog detected an unexpected manager exit.");
                try { Program.RestoreProxy(Program.ReadState(statePath), logPath); }
                catch (Exception ex) { Program.AppendLog(logPath, "Watchdog restore error: " + ex.Message); }
                try
                {
                    Process core = Process.GetProcessById(corePid);
                    if (String.Equals(core.ProcessName, "mihomo", StringComparison.OrdinalIgnoreCase)) core.Kill();
                }
                catch { }
                try { File.Delete(statePath); } catch { }
                Program.AppendLog(logPath, "Watchdog cleanup finished.");
            }
            catch (Exception ex) { Program.AppendLog(logPath, "Watchdog error: " + ex.Message); }
        }

        private static bool IsProcessRunning(int pid)
        {
            try { Process process = Process.GetProcessById(pid); return !process.HasExited; }
            catch { return false; }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

        internal static void RefreshInternetSettings()
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }

    }
}
