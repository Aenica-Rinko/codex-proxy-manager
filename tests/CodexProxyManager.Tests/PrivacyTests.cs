// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class PrivacyTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Privacy: " + message);
        }

        private static T Field<T>(object form, string name)
        {
            return (T)form.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        }

        internal static void Run(string scratch)
        {
            string executable = Assembly.GetExecutingAssembly().Location;
            string parentHttp = Environment.GetEnvironmentVariable("HTTP_PROXY");
            string parentBypass = Environment.GetEnvironmentVariable("NO_PROXY");
            var app = new ManagedApplication { Id = "probe", Name = "Probe", Executable = executable,
                LaunchMode = "chromium", RestrictBrowserDns = true, DisableQuic = true };
            var info = ProcessProxyLauncher.BuildStartInfo(executable, 27899, app);
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            using (var child = Process.Start(info))
            {
                var output = child.StandardOutput.ReadToEndAsync();
                if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("Privacy child timed out"); }
                var report = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(output.Result);
                string args = report["Arguments"];
                Check(child.ExitCode == 0 && args.Contains("--proxy-server=http://127.0.0.1:27899"), "proxy lost in actual child");
                Check(args.Contains("--webrtc-ip-handling-policy=disable_non_proxied_udp") &&
                    args.Contains("--force-webrtc-ip-handling-policy=disable_non_proxied_udp"), "WebRTC requests lost");
                Check(args.Contains("MAP * ~NOTFOUND, EXCLUDE localhost, EXCLUDE 127.0.0.1, EXCLUDE [::1]"), "quoted DNS rules lost in actual child");
                Check(args.Contains("--disable-quic"), "QUIC request lost");
                Check(report["NO_PROXY"] == "localhost,127.0.0.1,::1", "bypass changed");
            }
            Check(parentHttp == Environment.GetEnvironmentVariable("HTTP_PROXY") &&
                parentBypass == Environment.GetEnvironmentVariable("NO_PROXY"), "parent environment changed");
            app.LimitWebRtc = false; app.RestrictBrowserDns = false; app.DisableQuic = false;
            Check(!ProcessProxyLauncher.BuildStartInfo(executable, 27899, app).Arguments.Contains("webrtc"), "opt-out ignored");
            app.LaunchMode = "environment"; app.LimitWebRtc = true; app.RestrictBrowserDns = true; app.DisableQuic = true;
            Check(ProcessProxyLauncher.BuildStartInfo(executable, 27899, app).Arguments == "", "unsupported mode received browser flags");
            Check(PrivacyProtection.Describe(app).Contains("不支持"), "unsupported mode falsely protected");

            string user = Path.Combine(scratch, "privacy-settings");
            Directory.CreateDirectory(user);
            string path = Path.Combine(user, "settings.json");
            // Old settings must be readable without rewriting a user's configuration on read.
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"ActiveApplicationId\":\"codex\",\"ActiveProfileId\":\"test\",\"Profiles\":[{\"Id\":\"test\",\"Name\":\"Fixture\",\"ConfigFile\":\"profiles/test.yaml\"}],\"Applications\":[{\"Id\":\"codex\",\"LaunchMode\":\"chromium\"}]}");
            string legacy = File.ReadAllText(path);
            var settings = StartupConfiguration.ReadSettings(path);
            Check(PrivacyProtection.WebRtcRequested(settings.Applications[0]) && !settings.Applications[0].RestrictBrowserDns &&
                !settings.Applications[0].DisableQuic, "legacy defaults incorrect");
            Check(File.ReadAllText(path) == legacy, "read rewrote user settings");
            using (var form = new ApplicationManagerForm(user))
            {
                var rtc = Field<CheckBox>(form, "webRtcBox");
                var dns = Field<CheckBox>(form, "dnsBox");
                var quic = Field<CheckBox>(form, "quicBox");
                Check(rtc.Checked && rtc.Enabled && !dns.Checked && !quic.Checked, "UI defaults incorrect");
                Check(Field<Label>(form, "privacyStatus").Text.Contains("待验证"), "UI claimed verified protection");
                rtc.Checked = false; dns.Checked = true; quic.Checked = true;
                form.GetType().GetMethod("SaveSelection", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { null, EventArgs.Empty });
            }
            settings = StartupConfiguration.ReadSettings(path);
            Check(settings.Applications[0].LimitWebRtc == false && settings.Applications[0].RestrictBrowserDns &&
                settings.Applications[0].DisableQuic && settings.Profiles[0].ConfigFile == "profiles/test.yaml", "privacy preferences or profile binding lost");
            settings.Applications.Add(app); settings.ActiveApplicationId = app.Id;
            StartupConfiguration.WriteSettings(path, settings);
            using (var form = new ApplicationManagerForm(user))
            {
                Check(!Field<CheckBox>(form, "webRtcBox").Enabled && !Field<CheckBox>(form, "dnsBox").Enabled &&
                    !Field<CheckBox>(form, "quicBox").Enabled, "environment mode UI not disabled");
                Field<ComboBox>(form, "modeBox").SelectedIndex = 0;
                Check(Field<CheckBox>(form, "dnsBox").Enabled && Field<CheckBox>(form, "dnsBox").Checked, "mode switch lost preferences");
            }
            var diagnostic = Diagnostics.Collect(new DiagnosticInput { CoreHome = user, MixedPort = 27899,
                PrivacySummary = PrivacyProtection.Describe(settings.Applications[0]) }, false, System.Threading.CancellationToken.None);
            Check(diagnostic.Rows.Exists(r => r.Code == "PRIVACY" && r.Status == "信息" && r.Message.Contains("待验证")), "diagnostics claimed verified protection");
            Check(!diagnostic.ToText().Contains(executable) && !diagnostic.ToText().Contains(user), "privacy report leaked paths");
            Console.WriteLine("PASS privacy: actual child flags/quoting, opt-out, unsupported mode, legacy settings, UI persistence and honest diagnostics");
        }
    }
}
