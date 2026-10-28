// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class LaunchUxTests
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception("Launch UX: " + message); }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
        }
        private static void Render(Form form, string name)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            form.Show();
            form.CreateControl();
            foreach (Control child in Descendants(form)) { IntPtr handle = child.Handle; }
            form.PerformLayout();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".png"));
            }
            form.Hide();
        }
        internal static void Run(string scratch)
        {
            int discoveries = 0;
            var history = new SessionHistory();
            for (int i = 0; i < 105; i++) history.Add(SessionEvent.Started);
            Check(history.Snapshot().Length == 100, "history unbounded");
            history.Add(SessionEvent.Failed);
            Check(history.Snapshot()[0].Contains("操作失败"), "history newest-first");
            using (var events = new SessionHistoryForm(history, true)) Render(events, "ux-session-history");
            history.Clear(); Check(history.Snapshot().Length == 0, "history clear failed");
            string prefsRoot = Path.Combine(scratch, "theme-preference"); Directory.CreateDirectory(prefsRoot);
            var preference = new ThemePreference(prefsRoot);
            Check(!preference.Read() && !File.Exists(Path.Combine(prefsRoot, "ui-theme.txt")), "reading theme wrote files");
            preference.Save(true); Check(new ThemePreference(prefsRoot).Read(), "theme not persisted");
            File.WriteAllText(Path.Combine(prefsRoot, "ui-theme.txt"), "corrupt-fixture");
            bool themeRejected = false; try { preference.Save(false); } catch { themeRejected = true; }
            Check(themeRejected && File.ReadAllText(Path.Combine(prefsRoot, "ui-theme.txt")) == "corrupt-fixture", "corrupt preference overwritten");
            DateTime now = DateTime.UtcNow;
            var resolver = new AdvisoryExecutableResolver(delegate { discoveries++; return Assembly.GetExecutingAssembly().Location; }, delegate { return now; });
            resolver.Resolve(ApplicationConfiguration.DefaultApplication());
            resolver.Resolve(ApplicationConfiguration.DefaultApplication());
            Check(discoveries == 1, "advisory lookup spawned repeated discovery");
            now = now.AddSeconds(61);
            resolver.Resolve(ApplicationConfiguration.DefaultApplication());
            Check(discoveries == 2, "advisory path never expires");
            int failedLookups = 0;
            var missingResolver = new AdvisoryExecutableResolver(delegate { failedLookups++; throw new IOException(); }, delegate { return now; });
            for (int i = 0; i < 2; i++) { try { missingResolver.Resolve(ApplicationConfiguration.DefaultApplication()); } catch { } }
            Check(failedLookups == 1, "failed discovery busy loop");
            Check(LaunchReadiness.FromProcess(false, false).Kind == LaunchReadinessKind.Launch, "stopped app action");
            Check(LaunchReadiness.FromProcess(true, true).Kind == LaunchReadinessKind.Resume, "compatible app action");
            Check(LaunchReadiness.FromProcess(true, false).Kind == LaunchReadinessKind.NeedsRestart, "ordinary launch not blocked");
            Check(LaunchReadiness.Inspect(ApplicationConfiguration.DefaultApplication(), Path.Combine(scratch, "absent.exe"), scratch,
                Path.Combine(scratch, "absent.yaml"), 27890).Kind == LaunchReadinessKind.MissingFiles, "missing components ignored");
            Check(LaunchReadinessPoller.CanApply(2, 2, false, false, false) &&
                !LaunchReadinessPoller.CanApply(1, 2, false, false, false) &&
                !LaunchReadinessPoller.CanApply(2, 2, true, false, false) &&
                !LaunchReadinessPoller.CanApply(2, 2, false, true, false) &&
                !LaunchReadinessPoller.CanApply(2, 2, false, false, true), "stale or busy probe accepted");
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var completed = new ManualResetEvent(false))
            {
                var poller = new LaunchReadinessPoller();
                try
                {
                    Check(poller.Begin(delegate { entered.Set(); release.WaitOne(5000); return LaunchReadiness.FromProcess(false, false); },
                        delegate { completed.Set(); }), "probe not scheduled");
                    Check(entered.WaitOne(3000), "probe worker not entered");
                    Check(!poller.Begin(LaunchReadiness.Checking, delegate { }), "overlapping probe scheduled");
                }
                finally { release.Set(); Check(completed.WaitOne(5000), "probe not completed"); }
            }
            using (var form = new DashboardForm())
            {
                var start = Field<Button>(form, "startButton");
                var stop = Field<Button>(form, "stopButton");
                int starts = 0; form.StartRequested += delegate { starts++; };
                form.SetReadiness(LaunchReadiness.Checking());
                Check(!start.Enabled, "checking enabled start");
                form.SetReadiness(LaunchReadiness.FromProcess(false, false));
                Check(start.Enabled && start.Text == "启动应用并连接", "launch button mismatch");
                form.UpdateNodes(new List<NodeInfo> { new NodeInfo { Name = "示例节点 A", Type = "Socks5", Delay = 28 } }, "");
                Render(form, "ux-overview-ready");
                form.ShowNodesPage(true);
                Check(Field<Label>(form, "pageTitle").Text == "节点与策略", "node navigation missing");
                var search = Field<TextBox>(form, "nodeSearch");
                search.Text = "示例";
                form.ShowNodesPage(false);
                form.ShowNodesPage(true);
                Check(search.Text == "示例" && Field<ListView>(form, "nodeList").Items.Count == 1, "navigation lost filter/catalog");
                Render(form, "ux-nodes-page");
                form.HandleShortcut(Keys.Control | Keys.T);
                Check(form.BackColor.ToArgb() == Color.FromArgb(244,246,249).ToArgb(), "light theme not applied");
                form.UpdateStatus(false, "已停止", "—", 0, 0, 0, 0, 0);
                Render(form, "ux-light-nodes");
                form.HandleShortcut(Keys.Control | Keys.D1);
                Check(Field<Label>(form, "pageTitle").Text == "连接概览", "overview keyboard shortcut");
                Render(form, "ux-light-overview");
                form.SetLightTheme(false);
                Check(form.BackColor.ToArgb() == Color.FromArgb(13,17,23).ToArgb(), "dark theme restore");
                form.HandleShortcut(Keys.Control | Keys.F);
                Check(Field<Label>(form, "pageTitle").Text == "节点与策略", "search shortcut navigation");
                Check(!Field<Button>(form, "groupsButton").Enabled, "stopped group action enabled");
                form.UpdateStatus(true, "入口可用", "示例节点 A", 0, 0, 0, 0, 0);
                Check(Field<Button>(form, "groupsButton").Enabled, "active group action disabled");
                form.SetScreenshotPrivacy(true);
                Check(!Field<Button>(form, "groupsButton").Enabled && Field<Label>(form, "profileSummary").Text.Contains("已隐藏"), "new surfaces leaked private state");
                form.SetScreenshotPrivacy(false);
                form.UpdateStatus(false, "已停止", "—", 0, 0, 0, 0, 0);
                form.ShowNodesPage(false);
                form.SetReadiness(LaunchReadiness.FromProcess(true, true));
                Check(start.Text == "恢复代理", "resume action missing");
                form.SetReadiness(LaunchReadiness.FromProcess(true, false));
                Check(start.Text == "查看启动说明", "blocked action misleading");
                form.ClientSize = new Size(1020, 720);
                Render(form, "ux-overview-blocked");
                form.SetBusy(true, "处理测试"); Check(!start.Enabled && !stop.Enabled, "busy action enabled");
                form.SetBusy(false, "");
                form.UpdateStatus(true, "入口可用", "示例节点 A", 0, 0, 0, 0, 0);
                Check(!start.Enabled && stop.Enabled && Field<Label>(form, "readinessValue").Text.Contains("未检测"), "active claims or controls wrong");
                Check(starts == 0, "state updates launched an application");
            }
            using (var scaled = new DashboardForm())
            {
                scaled.Scale(new SizeF(1.5F, 1.5F));
                scaled.ShowNodesPage(true); scaled.ShowNodesPage(false);
                Check(Math.Abs(Field<TableLayoutPanel>(scaled, "mainLayout").RowStyles[1].Height - 117) < 2, "navigation discarded scale");
                scaled.SetReadiness(LaunchReadiness.FromProcess(false, false));
                Render(scaled, "ux-scaled-overview");
            }
            string source = Path.Combine(scratch, "preview.yaml");
            string user = Path.Combine(scratch, "preview-user");
            string snapshot = StrategyTests.Nodes + StrategyTests.Groups + "secret: fixture-secret-value\ntun: {enable: true}\nrules: ['MATCH,DIRECT']\n";
            File.WriteAllText(source, snapshot);
            var preview = LocalFileImport.Analyze(source, delegate { return 2; });
            Check(!Directory.Exists(user) && preview.NodeCount == 2 && preview.GroupCount == 3, "preview wrote files or wrong counts");
            Check(preview.Topology.InitialRoute == "Main" && preview.Topology.DescribeGroups().First()[2].Contains("Backup"), "preview lost references/order");
            Check(preview.Topology.IgnoredFields.Contains("secret") && preview.Topology.IgnoredFields.Contains("tun"), "ignored settings not reported");
            using (var form = new ImportPreviewForm(preview))
            {
                Check(Descendants(form).All(c => !c.Text.Contains("fixture-secret-value")), "preview exposed secret");
                Check(((Button)form.CancelButton).DialogResult == DialogResult.Cancel, "cancel action missing");
                Render(form, "ux-import-preview");
                form.DialogResult = DialogResult.Cancel;
            }
            Check(!Directory.Exists(user), "canceled preview created a profile");
            File.WriteAllText(source, "proxies: []\n");
            using (var saved = LocalFileImport.SavePreview(user, preview, 27890))
            {
                Check(File.ReadAllText(saved.ProviderPath) == snapshot && saved.NodeCount == 2 && saved.GroupCount == 3,
                    "confirmation reread changed source");
                Check(File.ReadAllText(saved.ConfigPath).Contains("Backup"), "confirmation lost group graph");
            }
            Check(File.ReadAllText(source) == "proxies: []\n", "save rewrote source");
            Console.WriteLine("PASS launch UX: dynamic states, missing components, single-flight/stale checks, no auto-start, honest evidence labels, import preview/cancel/snapshot and fixture UI renders");
        }
    }
}
