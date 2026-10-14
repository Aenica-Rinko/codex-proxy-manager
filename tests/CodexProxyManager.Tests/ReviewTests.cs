// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class ReviewTests
    {
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("Review: " + message); }
        private static bool Rejected(Action action) { try { action(); return false; } catch { return true; } }
        private static void Set(object obj, string name, object value)
        { obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value); }
        private static T Get<T>(object obj, string name)
        { return (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }

        internal static void Run(string scratch)
        {
            Check(StartupConfiguration.NormalizeControllerUrl("http://[::1]:9090/") == "http://[::1]:9090", "IPv6 loopback rejected");
            foreach (string value in new[] { "http://example.invalid:9090", "http://127.0.0.1:0", "http://user@localhost:9090",
                "http://localhost:9090/path", "http://localhost:9090?secret=fixture", "http://localhost:9090#private" })
                Check(Rejected(delegate { StartupConfiguration.NormalizeControllerUrl(value); }), "Unsafe/ambiguous controller URL accepted");
            string root = Path.Combine(scratch, "review-settings"); Directory.CreateDirectory(root);
            foreach (string value in new[] { "../escape.yaml", "profiles/test.yaml:secret", Path.Combine(root, "absolute.yaml") })
                Check(Rejected(delegate { StartupConfiguration.ResolveUserPath(root, value); }), "Non-relative config path accepted");
            Check(StartupConfiguration.ResolveUserPath(root, "profiles/test.yaml").StartsWith(root), "Valid relative path rejected");
            string settings = Path.Combine(root, "settings.json");
            File.WriteAllText(settings, "{\"Profiles\":[null]}");
            Check(Rejected(delegate { StartupConfiguration.ReadSettings(settings); }) && File.ReadAllText(settings).Contains("null"), "Malformed profile rewritten");
            Check(Rejected(delegate { ApplicationConfiguration.Normalize(new ManagerSettings { Profiles = new List<ProxyProfileDefinition>
                { new ProxyProfileDefinition { Id = "duplicate" }, new ProxyProfileDefinition { Id = "duplicate" } } }); }), "Duplicate profiles accepted");
            File.WriteAllText(settings, new string(' ', 2 * 1024 * 1024 + 1));
            Check(Rejected(delegate { StartupConfiguration.ReadSettings(settings); }), "Unbounded settings accepted");
            string statePath = Path.Combine(root, "state.json");
            var owned = new ProxyState { NetworkMode = "process", CorePid = 123 };
            Program.WriteState(statePath, owned);
            Check(!String.IsNullOrEmpty(owned.SessionId) && RecoveryWatchdog.OwnsSession(Program.ReadState(statePath), owned), "Atomic state identity lost");
            Program.WriteState(statePath, new ProxyState { CorePid = 123, NetworkMode = "process" });
            Check(!RecoveryWatchdog.OwnsSession(Program.ReadState(statePath), owned), "Same-PID newer session accepted by old watchdog");
            Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Atomic write left temporary state");
            using (var self = Process.GetCurrentProcess())
            {
                long ticks = self.StartTime.ToUniversalTime().Ticks;
                string executable = self.MainModule.FileName;
                Check(ProcessIdentity.Matches(self, executable, ticks), "Valid process identity rejected");
                Check(!ProcessIdentity.Matches(self, executable, ticks + 1) && !ProcessIdentity.Matches(self, "wrong.exe", ticks) &&
                    !ProcessIdentity.Matches(self, executable, 0), "Reused/unknown process identity accepted");
            }
            Check(RunningApplication.HasConfigArgument("core.exe -f \"C:\\fixture path\\config.yaml\"", @"C:\fixture path\config.yaml", ""), "Quoted config rejected");
            Check(!RunningApplication.HasConfigArgument("core.exe -f C:\\fixture\\config.yaml.old", @"C:\fixture\config.yaml", ""), "Substring config match accepted");
            Check(!RunningApplication.HasConfigArgument("core.exe -f C:\\fixture\\config.yaml -f another.yaml", @"C:\fixture\config.yaml", ""), "Duplicate config flags accepted");

            string user = Path.Combine(root, "profile-editor"); Directory.CreateDirectory(user);
            string editorSettings = Path.Combine(user, "settings.json");
            StartupConfiguration.WriteSettings(editorSettings, new ManagerSettings { SchemaVersion = 1, ActiveProfileId = "first",
                Profiles = new List<ProxyProfileDefinition> { new ProxyProfileDefinition { Id = "first", Name = "First" },
                    new ProxyProfileDefinition { Id = "second", Name = "Second" } } });
            using (var editor = (Form)Activator.CreateInstance(typeof(ProfileManagerForm), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { scratch, user, false }, null))
            using (var trigger = new Button())
            {
                IntPtr editorHandle = editor.Handle;
                Get<ListBox>(editor, "profileList").SelectedIndex = 1;
                string backup = editorSettings + ".fixture-backup";
                File.Move(editorSettings, backup); Directory.CreateDirectory(editorSettings);
                try
                {
                    typeof(ProfileManagerForm).GetMethod("ActivateSelectedProfile", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(editor, new object[] { null, EventArgs.Empty });
                    Check(Get<ManagerSettings>(editor, "settings").ActiveProfileId == "first", "Failed save changed active profile");
                    typeof(ProfileManagerForm).GetMethod("CommitChange", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(editor, new object[] { (Action)delegate { Get<ManagerSettings>(editor, "settings").Profiles.RemoveAt(1); } });
                    Check(Get<ManagerSettings>(editor, "settings").Profiles.Count == 2, "Failed save did not restore profile list");
                    editor.Close();
                    Check(!editor.IsDisposed && Get<Label>(editor, "statusLabel").Text.Contains("保存失败"), "Save failure closed editor");
                }
                finally { Directory.Delete(editorSettings); File.Move(backup, editorSettings); }
                using (var server = new ControllerProbeTests.Server("200 OK", "{}", null, 0))
                {
                    Get<TextBox>(editor, "localHostBox").Text = "127.0.0.1";
                    Get<NumericUpDown>(editor, "localPortBox").Value = new Uri(server.Url).Port;
                    var watch = Stopwatch.StartNew();
                    typeof(ProfileManagerForm).GetMethod("TestLocalPort", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(editor, new object[] { trigger, EventArgs.Empty });
                    Check(watch.ElapsedMilliseconds < 200, "Local port test blocked editor");
                    while (Get<bool>(editor, "localTestRunning") && watch.ElapsedMilliseconds < 4000)
                    { Application.DoEvents(); Thread.Sleep(5); }
                    Check(!Get<bool>(editor, "localTestRunning") && trigger.Enabled, "Local port test did not finish safely");
                }
            }

            using (var target = new ControllerProbeTests.Server("200 OK", "{}", null, 0))
            using (var redirect = new ControllerProbeTests.Server("302 Found", "", target.Url + "/elsewhere", 0))
            {
                foreach (string method in new[] { "GET", "PUT", "DELETE" })
                    Check(Rejected(delegate { LocalControllerTransport.Send(redirect.Url, "fixture-secret", "/version", method,
                        method == "GET" ? null : "{}", 1000); }), "Controller redirect accepted");
                Check(target.Hits == 0, "Control request followed redirect");
            }
            using (var server = new ControllerProbeTests.Server("200 OK", "{}", null, 700))
            {
                var watch = Stopwatch.StartNew();
                Check(Rejected(delegate { LocalControllerTransport.Send(server.Url, "", "/version", "PUT", "{}", 150); }) &&
                    watch.ElapsedMilliseconds < 2000, "Control write not bounded");
            }
            using (var server = new ControllerProbeTests.Server("200 OK", new string('x', 2 * 1024 * 1024 + 1), null, 0))
                Check(Rejected(delegate { Diagnostics.ReadApi(server.Url, "", "/version", 1000); }), "Oversized response accepted");

            var dripping = new TcpListener(IPAddress.Loopback, 0);
            dripping.Start();
            using (var stop = new CancellationTokenSource())
            {
                var writer = Task.Run(delegate
                {
                    try
                    {
                        using (var socket = dripping.AcceptTcpClient())
                        using (var stream = socket.GetStream())
                        {
                            socket.ReceiveTimeout = socket.SendTimeout = 1000;
                            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                            string header; do { header = reader.ReadLine(); } while (!String.IsNullOrEmpty(header));
                            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 10000\r\nConnection: close\r\n\r\n");
                            stream.Write(head, 0, head.Length);
                            while (!stop.IsCancellationRequested) { stream.WriteByte((byte)' '); stream.Flush(); Thread.Sleep(25); }
                        }
                    }
                    catch { }
                });
                try
                {
                    var watch = Stopwatch.StartNew();
                    string failure = "";
                    try { Diagnostics.ReadApi("http://127.0.0.1:" + ((IPEndPoint)dripping.LocalEndpoint).Port, "", "/version", 180); }
                    catch (Exception error) { failure = Diagnostics.Explain(error); }
                    Check(failure.Contains("超时") && watch.ElapsedMilliseconds < 2000, "Slow-drip body escaped total deadline");
                }
                finally { stop.Cancel(); dripping.Stop(); Check(writer.Wait(3000), "Slow-drip fixture did not stop"); }
            }

            using (var server = new ControllerProbeTests.Server("200 OK", "{\"connections\":[],\"downloadTotal\":42,\"uploadTotal\":1}", null, 350))
            using (var form = new DashboardForm())
            using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                IntPtr handle = form.Handle;
                object context = FormatterServices.GetUninitializedObject(typeof(ManagerContext));
                Set(context, "host", form); Set(context, "sessionActive", true);
                Set(context, "currentNodes", new List<NodeInfo> { new NodeInfo { Name = "fixture" } });
                Set(context, "mixedPort", new Uri(server.Url).Port); Set(context, "controllerPort", new Uri(server.Url).Port);
                var controller = new ControllerClient(server.Url, "");
                Set(context, "nodeService", new NodeControlService(controller, controller, ""));
                Set(context, "dashboardPoller", new DashboardPoller());
                var method = typeof(ManagerContext).GetMethod("UpdateDashboard", BindingFlags.Instance | BindingFlags.NonPublic);
                int ticks = 0; timer.Tick += delegate { ticks++; }; timer.Start();
                var watch = Stopwatch.StartNew(); method.Invoke(context, null);
                Check(watch.ElapsedMilliseconds < 200, "Dashboard blocked the UI thread");
                Set(context, "sessionRevision", 1); // Simulate stop/switch while the old request is pending.
                while (Get<DashboardPoller>(context, "dashboardPoller").IsPending && watch.ElapsedMilliseconds < 5000)
                { Application.DoEvents(); Thread.Sleep(5); }
                Application.DoEvents();
                Check(ticks > 0 && !Get<DashboardPoller>(context, "dashboardPoller").IsPending &&
                    Get<long>(context, "previousDownloadTotal") == 0, "Stale poll applied / UI did not stay responsive");
                form.SetScreenshotPrivacy(true);
                Check(Rejected(delegate { StartupConfiguration.ReadSettings(Path.Combine(root, "missing.json")); }), "Missing settings accepted");
                typeof(ManagerContext).GetMethod("OpenConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(context, new object[] { true }); // Privacy guard must return before any real settings/UI work.
                Application.DoEvents();
                form.SetScreenshotPrivacy(false);
                form.UpdateNodes(new[] { new NodeInfo { Name = "Prefix SF2-full-name" } }, "Prefix SF2-full-name");
                Check(Get<ListView>(form, "nodeList").Items[0].Text == "Prefix SF2-full-name", "Node label was truncated by a hardcoded prefix");
            }
            using (var form = new DashboardForm())
            using (var tray = new NotifyIcon())
            using (var timer = new System.Windows.Forms.Timer())
            {
                IntPtr handle = form.Handle;
                object context = FormatterServices.GetUninitializedObject(typeof(ManagerContext));
                Set(context, "host", form); Set(context, "tray", tray); Set(context, "timer", timer);
                Set(context, "statePath", statePath);
                Set(context, "managerLog", Path.Combine(root, "exit-test.log"));
                var watch = Stopwatch.StartNew();
                typeof(ManagerContext).GetMethod("ExitManager", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null);
                Check(watch.ElapsedMilliseconds < 200, "Exit cleanup blocked UI");
                while (!Get<bool>(context, "allowExit") && watch.ElapsedMilliseconds < 5000)
                { Application.DoEvents(); Thread.Sleep(5); }
                Application.DoEvents();
                Check(Get<bool>(context, "allowExit") && File.Exists(statePath) && form.IsDisposed, "Async exit changed unowned state or did not finish");
            }
            Console.WriteLine("PASS review hardening: controller boundaries, write timeout, size caps, settings/state integrity, process identity, responsive/stale-safe dashboard");
        }
    }
}
