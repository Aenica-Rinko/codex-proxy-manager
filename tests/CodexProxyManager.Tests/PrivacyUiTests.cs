// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class PrivacyUiTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Privacy UI: " + message);
        }
        private static T Field<T>(object form, string name)
        {
            return (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }
        private static IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            { yield return child; foreach (Control nested in AllControls(child)) yield return nested; }
        }
        internal static void Run(string scratch)
        {
            string logs = Path.Combine(scratch, "privacy-logs");
            Directory.CreateDirectory(logs);
            string log = Path.Combine(logs, "manager.log");
            string secret = "fixture-private-node-and-subscription";
            Program.AppendLog(log, "START ERROR: " + secret + " https://fixture.invalid/private?credential=demo");
            Program.AppendLog(log, "Switched node to " + secret + ".");
            Program.AppendLog(log, "Unknown detail " + secret);
            PrivacyLog.AppendCore(Path.Combine(logs, "mihomo.stdout.log"), "level=info msg=\"" + secret + "\"");
            PrivacyLog.AppendCore(Path.Combine(logs, "mihomo.stderr.log"), "level=error msg=\"" + secret + "\"");
            Check(!File.Exists(Path.Combine(logs, "mihomo.stdout.log")), "info output was persisted");
            foreach (string path in Directory.GetFiles(logs))
                Check(!File.ReadAllText(path).Contains(secret) && !File.ReadAllText(path).Contains("fixture.invalid"), "private log details persisted");
            File.WriteAllText(log, new string('x', PrivacyLog.MaxBytes));
            Program.AppendLog(log, "Manager started.");
            Check(new FileInfo(log).Length < PrivacyLog.MaxBytes && File.Exists(log + ".1"), "log rotation is unbounded");
            File.SetLastWriteTimeUtc(log + ".1", DateTime.UtcNow.AddDays(-8));
            Program.AppendLog(log, "Manager exited.");
            Check(!File.Exists(log + ".1"), "expired backup retained");
            File.WriteAllText(Path.Combine(logs, "keep.txt"), "keep");
            Directory.CreateDirectory(Path.Combine(logs, "keep-directory"));
            File.WriteAllText(Path.Combine(logs, "keep-directory", "manager.log"), "keep nested");
            Check(PrivacyLog.Clear(logs) == 2, "cleanup count differs");
            Check(File.Exists(Path.Combine(logs, "keep.txt")) &&
                File.Exists(Path.Combine(logs, "keep-directory", "manager.log")), "cleanup affected unrelated files");
            Check(!BrowserPrivacyProbe.Evaluate(0, 0, 0, true).Passed, "missing baseline falsely passed");
            Check(!BrowserPrivacyProbe.Evaluate(3, 1, 0, true).Passed && !BrowserPrivacyProbe.Evaluate(3, 0, 1, true).Passed,
                "observed UDP/host leakage falsely passed");
            Check(!BrowserPrivacyProbe.Evaluate(3, 0, 0, false).Passed && BrowserPrivacyProbe.Evaluate(3, 0, 0, true).Passed,
                "proxy reachability ignored");
            Check(!BrowserPrivacyProbe.Supports(ApplicationConfiguration.DefaultApplication()), "Codex falsely claimed test support");
            using (var dashboard = new DashboardForm())
            {
                dashboard.CreateControl();
                var list = Field<ListView>(dashboard, "nodeList");
                IntPtr handle = list.Handle;
                var nodes = new List<NodeInfo> { new NodeInfo { Name = secret, Type = "Vless", Delay = 23 },
                    new NodeInfo { Name = "fixture-other-private", Type = "Socks5" } };
                dashboard.SetProfileName(secret);
                dashboard.SetApplicationName(secret);
                dashboard.UpdateNodes(nodes, secret);
                dashboard.UpdateStatus(true, "运行中", secret, 1, 2, 3, 4, 1);
                list.Items[0].Selected = true;
                string switched = null;
                dashboard.NodeSwitchRequested += delegate(string value) { switched = value; };
                dashboard.SetScreenshotPrivacy(true);
                Check(list.Items[0].Text == "节点 01" && list.Items[0].ToolTipText == "" && !list.ShowItemToolTips,
                    "node name or tooltip exposed");
                Check(AllControls(dashboard).All(c => !c.Text.Contains(secret)) && !dashboard.Text.Contains(secret), "visible private labels exposed");
                Check(!Field<TextBox>(dashboard, "nodeSearch").Enabled && !Field<Button>(dashboard, "applicationsButton").Enabled,
                    "private management surface still enabled");
                var switchButton = Field<Button>(dashboard, "switchButton");
                Check(switchButton.Enabled, "masked selection became unswitchable");
                typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(switchButton, new object[] { EventArgs.Empty });
                Check(switched == secret, "screenshot mode changed actual node routing identity");
                dashboard.UpdateStatus(true, "运行中", secret, 1, 2, 3, 4, 1);
                dashboard.UpdateNodes(nodes, secret);
                Check(Field<Label>(dashboard, "nodeValue").Text == "节点 01", "refresh exposed selected node");
                using (var center = new PrivacyCenterForm(delegate { return ApplicationConfiguration.DefaultApplication(); },
                    delegate { return true; }, logs, dashboard, delegate { }))
                {
                    Check(Field<Label>(center, "rtcState").Text.Contains("未验证") && !Field<Button>(center, "probe").Enabled,
                        "Codex privacy state falsely verified");
                    Check(!Field<Button>(center, "logs").Enabled && !Field<Button>(center, "edit").Enabled &&
                        !Field<Button>(center, "clear").Enabled, "screenshot or active session guard missing");
                }
                dashboard.SetScreenshotPrivacy(false);
                Check(list.Items[0].Text == secret && Field<Label>(dashboard, "applicationValue").Text.Contains(secret), "privacy toggle could not restore view");
                Check(nodes[0].Name == secret && (string)list.Items[0].Tag == secret, "view masking changed source data");
            }
            Console.WriteLine("PASS privacy UI: fixed log events, bounded retention, scoped cleanup, honest probe decisions, screenshot masking/restore and node switch identity");
        }
    }
}
