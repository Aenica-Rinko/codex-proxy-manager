// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;

namespace CodexProxyManager
{
    // Owns core/target lifecycle and private session files; no WinForms controls or node policy.
    internal sealed class ProxySessionService : IDisposable
    {
        private readonly string corePath, coreHome, configPath, statePath, managerLog, coreOutLog, coreErrLog;
        private readonly int mixedPort, controllerPort;
        private readonly string controllerSecret, watchdogExecutable;
        private readonly object gate = new object();
        private readonly ControllerClient controller;
        private string sessionConfigPath;
        private ManagedApplication targetApplication;
        private Process coreProcess;
        private ProxyState ownedState;

        internal ProxySessionService(StartupProfile startup)
        {
            if (startup == null) throw new ArgumentNullException("startup");
            corePath = Path.Combine(startup.ProjectRoot, "runtime", "mihomo.exe");
            coreHome = startup.CoreHome; configPath = startup.ConfigPath; statePath = startup.StatePath;
            managerLog = startup.ManagerLog; coreOutLog = startup.CoreOutLog; coreErrLog = startup.CoreErrLog;
            mixedPort = startup.MixedPort; controllerPort = startup.ControllerPort; controllerSecret = startup.ControllerSecret;
            targetApplication = startup.Application ?? ApplicationConfiguration.DefaultApplication();
            controller = new ControllerClient("http://127.0.0.1:" + controllerPort, controllerSecret);
            watchdogExecutable = Assembly.GetEntryAssembly().Location;
        }
        internal bool CoreAlive { get { try { return coreProcess != null && !coreProcess.HasExited; } catch { return false; } } }
        internal int CorePid { get { return coreProcess.Id; } }
        internal long CoreStartUtcTicks { get { return coreProcess.StartTime.ToUniversalTime().Ticks; } }

        internal bool Start(ManagedApplication application)
        {
            if (application == null) throw new ArgumentNullException("application");
            lock (gate)
            {
                if (CoreAlive) throw new InvalidOperationException("专用核心已经运行。");
                targetApplication = application;
                try
                {
                    RecoverStaleSession();
                    string executable = ApplicationConfiguration.ResolveExecutable(application);
                    bool resumed = IsCodexRunning();
                    if (resumed && !RunningApplication.CanResume(executable, mixedPort, application))
                        throw new InvalidOperationException("当前应用正在运行，但未能确认它使用本管理器的代理入口和当前隐私启动参数。\n\n" +
                            "只有已带匹配参数启动的 Chromium/Electron 应用才能不重启恢复代理；普通启动、端口或隐私参数已变更、环境变量模式以及无法读取参数的应用仍需重启。\n\n" +
                            "本次未关闭应用、未启用系统代理。保存工作后首次从管理器启动；兼容的 Chromium/Electron 应用之后可停止并恢复代理，无需退出应用。");
                    PrepareCore(application);
                    StartOrResumeTarget(executable, resumed);
                    var wait = Stopwatch.StartNew();
                    while (wait.ElapsedMilliseconds < 12000 && !IsCodexRunning()) Thread.Sleep(250);
                    if (!IsCodexRunning()) throw new InvalidOperationException("未检测到应用进程，可能程序已退出或不支持所选启动方式。");
                    return resumed;
                }
                catch { Stop(); throw; }
            }
        }

        // Shared preparation stage also supports isolated lifecycle integration tests.
        internal void PrepareCore(ManagedApplication application)
        {
            if (application == null) throw new ArgumentNullException("application");
            lock (gate)
            {
                if (CoreAlive) throw new InvalidOperationException("专用核心已经运行。");
                targetApplication = application;
                try
                {
                    ValidateFiles();
                    if (application.Id != "codex")
                    {
                        string yaml = ApplicationConfiguration.BuildSessionConfig(File.ReadAllText(configPath, Encoding.UTF8), application);
                        sessionConfigPath = Path.Combine(Path.GetDirectoryName(statePath), "session-config.yaml");
                        File.WriteAllText(sessionConfigPath, yaml, new UTF8Encoding(false));
                    }
                    StartCore();
                    var state = new ProxyState { NetworkMode = "process", CorePid = coreProcess.Id,
                        SavedAt = DateTime.UtcNow.ToString("o"), CoreExecutablePath = Path.GetFullPath(corePath),
                        CoreStartUtcTicks = coreProcess.StartTime.ToUniversalTime().Ticks };
                    using (var manager = Process.GetCurrentProcess()) state.ManagerStartUtcTicks = manager.StartTime.ToUniversalTime().Ticks;
                    Program.WriteState(statePath, state);
                    ownedState = state;
                    StartWatchdog(state);
                }
                catch { Stop(); throw; }
            }
        }

        private void StartOrResumeTarget(string executable, bool resumed)
        {
            if (resumed)
            {
                // Recheck after core startup: the target may have exited or restarted.
                if (!RunningApplication.CanResume(executable, mixedPort, targetApplication))
                    throw new InvalidOperationException("应用在准备代理期间已退出或启动参数发生变化。请重试；未自动关闭或重新启动应用。");
                Program.AppendLog(managerLog, "Resumed proxy for an existing compatible application.");
                return;
            }
            if (IsCodexRunning()) throw new InvalidOperationException("应用在准备代理期间已被其他方式打开。请重试；未重复启动应用。");
            using (Process application = Process.Start(ProcessProxyLauncher.BuildStartInfo(executable, mixedPort, targetApplication)))
                Program.AppendLog(managerLog, "Launched application PID " + application.Id + " with process-only proxy; Windows proxy unchanged.");
        }

        private void ValidateFiles()
        {
            if (!File.Exists(corePath)) throw new FileNotFoundException("找不到内置 Mihomo 核心。", corePath);
            if (!File.Exists(configPath)) throw new FileNotFoundException("找不到代理配置。", configPath);
            if (!PrivacyLog.SafePath(corePath) || !PrivacyLog.SafePath(configPath) || !PrivacyLog.SafePath(coreHome) ||
                !PrivacyLog.SafePath(statePath) || !PrivacyLog.SafePath(Path.Combine(Path.GetDirectoryName(statePath), "session-config.yaml")))
                throw new InvalidDataException("核心、配置或会话路径包含链接，已取消启动。");
            if (new FileInfo(configPath).Length > LocalFileImport.MaximumBytes)
                throw new InvalidDataException("代理配置超过 10 MiB，已取消启动。");
            if (!File.Exists(Path.Combine(coreHome, "GeoSite.dat"))) throw new FileNotFoundException("找不到 GeoSite.dat。", coreHome);
            if (!File.Exists(Path.Combine(coreHome, "geoip.metadb"))) throw new FileNotFoundException("找不到 geoip.metadb。", coreHome);
            if (LoopbackProbe.IsListening(mixedPort, 150)) throw new InvalidOperationException("端口 " + mixedPort + " 已被其他程序占用。");
            if (LoopbackProbe.IsListening(controllerPort, 150)) throw new InvalidOperationException("管理接口端口 " + controllerPort + " 已被其他程序占用。");
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
            var environmentKeys = new System.Collections.Generic.List<string>();
            foreach (string key in info.EnvironmentVariables.Keys) environmentKeys.Add(key);
            foreach (string key in environmentKeys)
                if (key.StartsWith("CLASH_", StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
            coreProcess = new Process();
            coreProcess.StartInfo = info;
            coreProcess.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) PrivacyLog.AppendCore(coreOutLog, e.Data);
            };
            coreProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) PrivacyLog.AppendCore(coreErrLog, e.Data);
            };
            coreProcess.Start();
            coreProcess.BeginOutputReadLine();
            coreProcess.BeginErrorReadLine();
            Program.AppendLog(managerLog, "Started bundled Mihomo PID " + coreProcess.Id + ".");

            DateTime deadline = DateTime.Now.AddSeconds(35);
            while (DateTime.Now < deadline)
            {
                if (coreProcess.HasExited) throw new InvalidOperationException("Mihomo 启动失败，退出代码：" + coreProcess.ExitCode);
                if (LoopbackProbe.IsListening(mixedPort, 250)) { ReduceCoreLogging(); return; }
                Thread.Sleep(200);
            }
            throw new TimeoutException("代理核心未能在 35 秒内监听端口 " + mixedPort + "。");
        }


        private void ReduceCoreLogging()
        {
            try { controller.Patch("/configs", "{\"log-level\":\"warning\"}"); }
            catch { Program.AppendLog(managerLog, "Core log preference error."); }
        }

        internal void Stop()
        {
            lock (gate)
            {
                ProxyState state = null;
                bool ours = false;
                try
                {
                    if (File.Exists(statePath))
                    {
                        state = Program.ReadState(statePath);
                        ours = ownedState != null && RecoveryWatchdog.OwnsSession(state, ownedState);
                        if (ours) Program.RestoreProxy(state, managerLog);
                    }
                }
                catch { Program.AppendLog(managerLog, "Could not read state."); }
                StopCore();
                if (!String.IsNullOrEmpty(sessionConfigPath))
                {
                    try { File.Delete(sessionConfigPath); } catch { }
                    sessionConfigPath = null;
                }
                try
                {
                    if (ours && File.Exists(statePath) && RecoveryWatchdog.OwnsSession(Program.ReadState(statePath), ownedState))
                        File.Delete(statePath);
                }
                catch { Program.AppendLog(managerLog, "State cleanup error."); }
                ownedState = null;
                Program.AppendLog(managerLog, "Session ended.");
            }
        }

        private void StopCore()
        {
            try
            {
                if (coreProcess != null && !coreProcess.HasExited)
                {
                    coreProcess.Kill(); coreProcess.WaitForExit(5000);
                    Program.AppendLog(managerLog, "Stopped bundled proxy core.");
                }
            }
            catch { Program.AppendLog(managerLog, "Could not stop core."); }
            if (CoreAlive) throw new IOException("专用核心尚未退出，已保留会话状态以便重试停止。");
            if (coreProcess != null) coreProcess.Dispose(); coreProcess = null;
        }

        private void RecoverStaleSession()
        {
            ProxyState previous = null;
            if (File.Exists(statePath))
            {
                Program.AppendLog(managerLog, "Found stale manager state.");
                try { previous = Program.ReadState(statePath); Program.RestoreProxy(previous, managerLog); }
                catch { Program.AppendLog(managerLog, "Stale restore error."); }
            }
            StopBundledOrphanCores();
            try
            {
                if (previous != null && File.Exists(statePath) && RecoveryWatchdog.OwnsSession(Program.ReadState(statePath), previous))
                    File.Delete(statePath);
            }
            catch { Program.AppendLog(managerLog, "Stale state cleanup error."); }
        }

        private void StopBundledOrphanCores()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine, CreationDate FROM Win32_Process WHERE Name='mihomo.exe'"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                    searcher.Options.ReturnImmediately = false;
                    using (ManagementObjectCollection items = searcher.Get())
                    foreach (ManagementObject item in items)
                    using (item)
                    {
                        string command = Convert.ToString(item["CommandLine"]);
                        if (RunningApplication.HasConfigArgument(command, configPath,
                            Path.Combine(Path.GetDirectoryName(statePath), "session-config.yaml")))
                        {
                            int pid = Convert.ToInt32((uint)item["ProcessId"]);
                            try
                            {
                                DateTime created = ManagementDateTimeConverter.ToDateTime(Convert.ToString(item["CreationDate"]));
                                using (Process process = Process.GetProcessById(pid))
                                    if (process.StartTime.ToUniversalTime().Ticks / 10 == created.ToUniversalTime().Ticks / 10 &&
                                        ProcessIdentity.Matches(process, corePath, process.StartTime.ToUniversalTime().Ticks))
                                    {
                                        process.Kill();
                                        process.WaitForExit(5000);
                                        Program.AppendLog(managerLog, "Stopped stale bundled core PID " + pid + ".");
                                    }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Program.AppendLog(managerLog, "Orphan scan error: " + ex.Message); }
        }


        private void StartWatchdog(ProxyState state)
        {
            int pid;
            using (var manager = Process.GetCurrentProcess()) pid = manager.Id;
            var info = new ProcessStartInfo(watchdogExecutable);
            info.Arguments = "--watchdog " + pid + " " + state.CorePid + " " + Quote(statePath) + " " +
                Quote(managerLog) + " " + Quote("127.0.0.1:" + mixedPort) + " " + Quote(state.SessionId);
            info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
            using (var watchdog = Process.Start(info)) { }
            Program.AppendLog(managerLog, "Started recovery watchdog.");
        }

        private bool IsCodexRunning()
        { try { return ApplicationConfiguration.IsRunning(targetApplication); } catch { return false; } }
        private static string Quote(string value)
        { return "\"" + value.Replace("\"", "\\\"") + "\""; }
        public void Dispose() { Stop(); }
    }
}
