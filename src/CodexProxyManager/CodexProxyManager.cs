using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
        public string SessionId { get; set; }
        public string CoreExecutablePath { get; set; }
        public long CoreStartUtcTicks { get; set; }
        public long ManagerStartUtcTicks { get; set; }
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

    internal sealed class ProxyCatalogSnapshot
    {
        internal string Group, Current;
        internal List<NodeInfo> Nodes;
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
            if (!PrivacyLog.SafePath(path) || new FileInfo(path).Length > 65536) throw new InvalidDataException();
            string json = File.ReadAllText(path, Encoding.UTF8);
            var state = new JavaScriptSerializer().Deserialize<ProxyState>(json);
            if (state == null) throw new InvalidDataException();
            return state;
        }

        internal static void WriteState(string path, ProxyState state)
        {
            if (state == null || !PrivacyLog.SafePath(path)) throw new InvalidDataException();
            if (String.IsNullOrEmpty(state.SessionId)) state.SessionId = Guid.NewGuid().ToString("N");
            string json = new JavaScriptSerializer().Serialize(state);
            if (Encoding.UTF8.GetByteCount(json) > 65536) throw new InvalidDataException();
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
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

        private volatile bool sessionActive;
        private volatile bool busy;
        private volatile bool sawCodex;
        private bool allowExit;
        private int noCodexTicks;
        private int dashboardTicks;
        private int healthCheckTicks;
        private int healthCheckRunning;
        private ProxySessionService sessionService;
        private NodeControlService nodeService;
        private readonly DashboardPoller dashboardPoller = new DashboardPoller();
        private int sessionRevision;
        private readonly LaunchReadinessPoller readinessPoller = new LaunchReadinessPoller();
        private readonly AdvisoryExecutableResolver readinessExecutable = new AdvisoryExecutableResolver();
        private LaunchReadiness readiness = LaunchReadiness.Checking();
        private int readinessRevision = -1;
        private DateTime nextReadinessCheck;
        private volatile bool coreReachable;
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
            BindServices(startup);
            RotateLog(managerLog);
            RotateLog(coreOutLog);
            RotateLog(coreErrLog);

            host = new DashboardForm();
            host.SetThemePreferenceRoot(startup.UserDataRoot);
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
            host.GroupsRequested += delegate
            {
                if (busy || host.ScreenshotPrivacy) return;
                if (!sessionActive) { Notify("请先启动代理，再查看策略组与引用。", ToolTipIcon.Info); return; }
                busy = true; Interlocked.Increment(ref sessionRevision);
                try { using (var form = new StrategyGroupsForm(nodeService)) form.ShowDialog(host); }
                finally { nodeService.Health.Reset(); busy = false; Interlocked.Increment(ref sessionRevision); dashboardTicks = 4; }
            };
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
            startItem = new ToolStripMenuItem("启动应用 / 恢复专用代理");
            stopItem = new ToolStripMenuItem("停止专用代理");
            ToolStripMenuItem profilesItem = new ToolStripMenuItem("管理代理档案");
            ToolStripMenuItem openItem = new ToolStripMenuItem("打开用户配置目录");
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出管理器");
            dashboardItem.Click += delegate { host.ShowDashboard(); };
            startItem.Click += delegate { BeginStart(); };
            stopItem.Click += delegate { BeginStop(true); };
            profilesItem.Click += delegate { OpenProfileManager(); };
            openItem.Click += delegate { if (!host.ScreenshotPrivacy) Process.Start("explorer.exe", startup.UserDataRoot); };
            menu.Opening += delegate
            {
                profilesItem.Enabled = !busy && !sessionActive && !host.ScreenshotPrivacy;
                openItem.Enabled = !host.ScreenshotPrivacy;
            };
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
            RefreshReadiness();
            Program.AppendLog(managerLog, "Manager started.");
        }

        private void TimerTick(object sender, EventArgs e)
        {
            // Opening the manager never starts an application. Only explicit
            // dashboard/tray start actions may call BeginStart.
            bool codexRunning = sessionActive && IsCodexRunning();
            if (sessionActive && sawCodex)
            {
                if (codexRunning)
                    noCodexTicks = 0;
                else if (++noCodexTicks >= 4)
                    BeginStop(false);
            }

            if (busy)
                SetStatus("状态：正在处理…");
            else if (sessionActive && coreReachable)
                SetStatus("状态：运行中 · " + mixedPort);
            else if (sessionActive)
                SetStatus("状态：入口待检查 / 暂不可达");
            else
                SetStatus("状态：专用代理已停止");

            RefreshReadiness();
            startItem.Text = sessionActive ? "代理运行中" : readiness.ActionText;
            startItem.Enabled = !busy && !sessionActive && readiness.Kind != LaunchReadinessKind.Checking;
            stopItem.Enabled = !busy && (sessionActive || File.Exists(statePath));
            if (sessionActive && autoFailover && !busy && ++healthCheckTicks >= 30)
            {
                healthCheckTicks = 0;
                BeginAutoHealthCheck();
            }
            UpdateDashboard();
        }

        private void RefreshReadiness()
        {
            if (busy || sessionActive || allowExit) return;
            int revision = sessionRevision;
            if (readinessRevision != revision)
            {
                readiness = LaunchReadiness.Checking();
                host.SetReadiness(readiness);
            }
            if (readinessRevision == revision && DateTime.UtcNow < nextReadinessCheck) return;
            // Capture the selected application and file paths before leaving the UI thread.
            var app = new ManagedApplication { Id = targetApplication.Id, Name = targetApplication.Name,
                Executable = targetApplication.Executable, LaunchMode = targetApplication.LaunchMode,
                LimitWebRtc = targetApplication.LimitWebRtc, RestrictBrowserDns = targetApplication.RestrictBrowserDns,
                DisableQuic = targetApplication.DisableQuic };
            string config = configPath;
            if (readinessPoller.Begin(delegate { return LaunchReadiness.Inspect(app, corePath, coreHome, config, mixedPort, readinessExecutable.Resolve); },
                delegate(LaunchReadiness result)
                {
                    Ui(delegate
                    {
                        if (host.IsDisposed || !LaunchReadinessPoller.CanApply(revision, sessionRevision, sessionActive, busy, allowExit)) return;
                        readiness = result;
                        readinessRevision = revision;
                        nextReadinessCheck = DateTime.UtcNow.AddSeconds(5);
                        host.SetReadiness(result);
                    });
                })) nextReadinessCheck = DateTime.UtcNow.AddSeconds(5);
        }

        private void SetStatus(string text)
        {
            statusItem.Text = text;
            tray.Text = text.Length > 60 ? text.Substring(0, 60) : text;
        }

        private void UpdateDashboard()
        {
            if (!sessionActive)
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

            if (dashboardPoller.IsPending) return;
            int revision = sessionRevision;
            bool refreshNodes = currentNodes.Count == 0 || ++dashboardTicks % 5 == 0;
            dashboardPoller.Begin(mixedPort, nodeService, refreshNodes, delegate(DashboardSnapshot result)
            {
                bool reachable = result.Reachable;
                TrafficSnapshot traffic = result.Traffic;
                ProxyCatalogSnapshot catalog = result.Catalog;
                if (result.ReadFailed) Program.AppendLog(managerLog, "Dashboard refresh error.");
                Ui(delegate
                {
                    // A slow old response must not repopulate a stopped/switched session.
                    if (!sessionActive || busy || sessionRevision != revision) return;
                    coreReachable = reachable;
                    if (catalog != null)
                    {
                        proxyGroupName = catalog.Group; currentNodeName = catalog.Current; currentNodes = catalog.Nodes;
                        host.UpdateNodes(currentNodes, currentNodeName);
                    }
                    if (traffic == null)
                    {
                        previousTrafficSample = DateTime.MinValue;
                        host.UpdateStatus(true, reachable ? "控制接口暂不可用" : "代理入口不可达", currentNodeName,
                            0, 0, previousDownloadTotal, previousUploadTotal, 0);
                        return;
                    }
                    DateTime now = DateTime.UtcNow;
                    double seconds = Math.Max(0.2, (now - previousTrafficSample).TotalSeconds);
                    double down = previousTrafficSample == DateTime.MinValue ? 0 : Math.Max(0, traffic.DownloadTotal - previousDownloadTotal) / seconds;
                    double up = previousTrafficSample == DateTime.MinValue ? 0 : Math.Max(0, traffic.UploadTotal - previousUploadTotal) / seconds;
                    previousTrafficSample = now;
                    previousDownloadTotal = traffic.DownloadTotal; previousUploadTotal = traffic.UploadTotal;
                    host.UpdateStatus(true, result.ReadFailed ? "节点信息待更新" : "入口可用", currentNodeName,
                        down, up, traffic.DownloadTotal, traffic.UploadTotal, traffic.Connections);
                });
            });
        }

        private void RefreshProxyInfo()
        {
            ProxyCatalogSnapshot catalog = nodeService.ReadCatalog();
            proxyGroupName = catalog.Group;
            currentNodeName = catalog.Current;
            currentNodes = catalog.Nodes;
        }

        private void BeginLatencyTest()
        {
            if (busy || !sessionActive) return;
            busy = true;
            Interlocked.Increment(ref sessionRevision);
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
            Interlocked.Increment(ref sessionRevision);
            host.SetBusy(true, automatic ? "正在自动选择可用节点…" : "正在测速并选择最低延迟节点…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    MeasureGroupDelays();
                    if (automatic && currentNodes.Any(n => n.Name == currentNodeName && StrategyGraph.IsGroup(n.Type))) return;
                    NodeInfo fastest = automatic ? nodeService.AutomaticFastest(currentNodes) : NodeControlService.Fastest(currentNodes);
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

        private void MeasureGroupDelays()
        {
            if (String.IsNullOrWhiteSpace(proxyGroupName)) RefreshProxyInfo();
            currentNodes = nodeService.MeasureDelays(proxyGroupName, currentNodes);
        }

        private void BeginNodeSwitch(string nodeName)
        {
            if (busy || !sessionActive || String.IsNullOrWhiteSpace(nodeName)) return;
            busy = true;
            Interlocked.Increment(ref sessionRevision);
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
            nodeService.Switch(proxyGroupName, nodeName);
            currentNodeName = nodeName;
            nodeService.Health.Reset();
            RefreshProxyInfo();
            return true;
        }

        private void BeginProviderRefresh()
        {
            if (busy || !sessionActive) return;
            busy = true;
            Interlocked.Increment(ref sessionRevision);
            host.SetBusy(true, "正在刷新远程订阅…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int updated = nodeService.RefreshProviders();
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
            if (Interlocked.CompareExchange(ref healthCheckRunning, 1, 0) != 0) return;
            int revision = sessionRevision;
            NodeControlService checkedService = nodeService;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (checkedService.TryAutoFailover(delegate { return sessionActive && !busy && sessionRevision == revision; }))
                    { Program.AppendLog(managerLog, "Auto failover completed."); Notify("连续三次探测失败，已在当前选择组内切换可用线路。", ToolTipIcon.Info); }
                }
                catch
                {
                    Program.AppendLog(managerLog, "Health check error.");
                }
                finally
                {
                    Interlocked.Exchange(ref healthCheckRunning, 0);
                }
            });
        }

        private void BindServices(StartupProfile startup)
        {
            if (sessionService != null) sessionService.Dispose();
            sessionService = new ProxySessionService(startup);
            var core = new ControllerClient("http://127.0.0.1:" + startup.ControllerPort, startup.ControllerSecret);
            var node = String.IsNullOrWhiteSpace(startup.NodeControllerUrl) ? core :
                new ControllerClient(startup.NodeControllerUrl, startup.NodeControllerSecret);
            nodeService = new NodeControlService(core, node, startup.ProxyGroupName);
        }

        private void OpenProfileManager()
        {
            OpenConfiguration(false);
        }

        private DiagnosticInput CaptureDiagnosticInput()
        {
            bool alive = false;
            try { alive = sessionService != null && sessionService.CoreAlive; } catch { }
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
            if (host.ScreenshotPrivacy) { Notify("截图模式下不能打开私人配置窗口。", ToolTipIcon.Info); return; }
            if (sessionActive)
            {
                MessageBox.Show("请先点击“停止代理”，再切换应用或代理档案。\n修改启动端口或隐私参数后需要重新启动目标应用。",
                    "代理正在运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            busy = true;
            Interlocked.Increment(ref sessionRevision);
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
                if (updated == null) return;
                if (updated.MixedPort != mixedPort || updated.ControllerPort != controllerPort ||
                    !String.Equals(updated.ControllerSecret, controllerSecret, StringComparison.Ordinal))
                    throw new InvalidOperationException("本地端口或管理密钥发生变化，请退出管理器后重新打开。");
                BindServices(updated);
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
            if (readinessRevision != sessionRevision || readiness.Kind == LaunchReadinessKind.Checking)
            { RefreshReadiness(); return; }
            if (readiness.Kind == LaunchReadinessKind.MissingFiles)
            {
                if (host.ScreenshotPrivacy) { Notify("请关闭截图隐私模式后查看准备说明。", ToolTipIcon.Info); return; }
                using (var guide = new GettingStartedForm(projectRoot, StartupConfiguration.GetUserDataRoot(), false)) guide.ShowDialog(host);
                nextReadinessCheck = DateTime.MinValue;
                RefreshReadiness();
                return;
            }
            if (readiness.Kind == LaunchReadinessKind.NeedsRestart || readiness.Kind == LaunchReadinessKind.Unknown)
            {
                MessageBox.Show(host, readiness.Hint + "\r\n\r\n处理后状态会自动重新检查。只有匹配原端口和隐私启动参数的 Chromium/Electron 应用可不重启恢复；环境变量模式需重新启动应用。",
                    "启动前检查", MessageBoxButtons.OK, MessageBoxIcon.Information);
                nextReadinessCheck = DateTime.MinValue;
                RefreshReadiness();
                return;
            }
            busy = true;
            Interlocked.Increment(ref sessionRevision);
            host.SetBusy(true, "正在检查应用并启动专用代理…");
            ThreadPool.QueueUserWorkItem(delegate { StartSessionWorker(); });
        }

        private void StartSessionWorker()
        {
            bool startedSuccessfully = false;
            try
            {
                bool resumed = sessionService.Start(targetApplication);
                Ui(delegate { host.RecordEvent(resumed ? SessionEvent.Resumed : SessionEvent.Started); });
                sawCodex = true; noCodexTicks = 0; sessionActive = true; coreReachable = true;
                startedSuccessfully = true;
                Notify(resumed ? "代理已恢复，未重启当前应用。应用可能需要重新连接；启动参数效果仍需验证。" :
                    targetApplication.Name + " 专用代理已启动。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Program.AppendLog(managerLog, "START ERROR: " + ex);
                try { EndSessionCore(); } catch { }
                ShowError(ex.Message + "\r\n\r\n下一步：打开“连接诊断”检查组件、端口和档案。若提示应用已运行，请保存工作并退出应用后重试；管理器不会替你关闭应用。");
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
            Interlocked.Increment(ref sessionRevision);
            host.SetBusy(true, "正在停止 Codex 专用代理…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    EndSessionCore();
                    if (notify) Notify("专用代理已停止，应用未关闭，也不会自动改为直连。匹配原启动参数的 Chromium/Electron 应用可点击“启动 / 恢复”重新开启代理。", ToolTipIcon.Info);
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
            bool wasActive = sessionActive;
            Interlocked.Increment(ref sessionRevision);
            coreReachable = false;
            if (sessionService != null) sessionService.Stop();
            sessionActive = false;
            if (wasActive) Ui(delegate { host.RecordEvent(SessionEvent.Stopped); });
            sawCodex = false;
            noCodexTicks = 0;
            dashboardTicks = 0;
            healthCheckTicks = 0;
            if (nodeService != null) nodeService.Health.Reset();
            previousTrafficSample = DateTime.MinValue;
            previousDownloadTotal = 0;
            previousUploadTotal = 0;
            proxyGroupName = null;
            currentNodeName = null;
            currentNodes = new List<NodeInfo>();
            Program.AppendLog(managerLog, "Session ended.");
        }

        private bool IsCodexRunning()
        {
            try
            {
                return ApplicationConfiguration.IsRunning(targetApplication);
            }
            catch { return false; }
        }

        private void Notify(string message, ToolTipIcon icon)
        {
            Ui(delegate
            {
                tray.BalloonTipTitle = "Codex Proxy Manager";
                if (message.StartsWith("已切换") || message.StartsWith("连续三次"))
                    host.RecordEvent(message.StartsWith("已切换到代理档案") ? SessionEvent.ProfileChanged : SessionEvent.NodeChanged);
                if (message.StartsWith("远程订阅刷新完成")) host.RecordEvent(SessionEvent.ProvidersUpdated);
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
                host.RecordEvent(SessionEvent.Failed);
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
            busy = true;
            Interlocked.Increment(ref sessionRevision);
            host.SetBusy(true, "正在停止专用核心并安全退出…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { EndSessionCore(); }
                catch
                {
                    busy = false;
                    ShowError("退出清理未完成，请检查连接诊断后重试。目标应用未被关闭。");
                    Ui(delegate { host.SetBusy(false, ""); });
                    return;
                }
                Ui(delegate
                {
                    allowExit = true;
                    timer.Stop();
                    tray.Visible = false;
                    tray.Dispose();
                    host.Close();
                    ExitThread();
                });
            });
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
