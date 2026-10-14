// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace CodexProxyManager
{
    internal static class RunningApplicationTests
    {
        private static void Assert(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        internal static void Run(string scratch)
        {
            string fixture = Path.Combine(scratch, "RunningFixture.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, fixture);
            var app = new ManagedApplication { Id = "fixture", Executable = fixture, LaunchMode = "chromium" };
            string line = "\"" + fixture + "\" " + ProcessProxyLauncher.BuildStartInfo(fixture, 27899, app).Arguments;
            Assert(RunningApplication.MatchesLaunch(line, 27899, app), "Matching launch rejected");
            Assert(!RunningApplication.MatchesLaunch(line, 27900, app), "Changed proxy port accepted");
            Assert(!RunningApplication.MatchesLaunch("fixture.exe", 27899, app), "Ordinary launch accepted");
            foreach (string suffix in new[] { " --no-proxy-server", " --NO-PROXY-SERVER", " --proxy-pac-url=http://localhost/pac",
                " --proxy-auto-detect", " --type=renderer", " --proxy-server=http://127.0.0.1:27900",
                " --PROXY-SERVER=http://127.0.0.1:27900", " -no-proxy-server", " /no-proxy-server" })
                Assert(!RunningApplication.MatchesLaunch(line + suffix, 27899, app), "Ambiguous/child launch accepted");
            Assert(!RunningApplication.MatchesLaunch("fixture.exe -- " + line, 27899, app), "Terminated flags accepted");
            Assert(!RunningApplication.MatchesLaunch(null, 27899, app), "Unreadable launch accepted");
            app.DisableQuic = true;
            Assert(!RunningApplication.MatchesLaunch(line, 27899, app), "Missing new privacy restriction accepted");
            Assert(RunningApplication.MatchesLaunch(line + " --disable-quic", 27899, app), "Matching QUIC setting rejected");
            app.RestrictBrowserDns = true;
            string all = "\"" + fixture + "\" " + ProcessProxyLauncher.BuildStartInfo(fixture, 27899, app).Arguments;
            Assert(RunningApplication.MatchesLaunch(all, 27899, app), "Quoted DNS rule rejected");
            app.DisableQuic = false;
            Assert(!RunningApplication.MatchesLaunch(all, 27899, app), "Stale privacy restriction accepted");
            app.LaunchMode = "environment";
            Assert(!RunningApplication.MatchesLaunch(line, 27899, app), "Unverifiable environment mode accepted");
            Assert(!RunningApplication.CanResume(fixture, 27899, app), "Environment resume accepted");
            app.LaunchMode = "chromium";
            app.RestrictBrowserDns = false;
            Assert(!RunningApplication.CanResume(fixture, 27899, app), "Missing target accepted");

            ProcessStartInfo info = ProcessProxyLauncher.BuildStartInfo(fixture, 27899, app);
            info.Arguments += " --hold-proxy-fixture";
            info.CreateNoWindow = true;
            info.RedirectStandardInput = info.RedirectStandardOutput = true;
            using (Process child = Process.Start(info))
            {
                try
                {
                    var ready = child.StandardOutput.ReadLineAsync();
                    Assert(ready.Wait(5000) && ready.Result == "READY", "Fixture did not become ready");
                    int pid = child.Id;
                    Assert(RunningApplication.CanResume(fixture, 27899, app), "Read-only OS launch inspection failed");
                    Assert(!RunningApplication.CanResume(fixture, 27900, app), "Wrong endpoint accepted by OS inspection");
                    app.DisableQuic = true;
                    Assert(!RunningApplication.CanResume(fixture, 27899, app), "Changed privacy accepted by OS inspection");
                    app.DisableQuic = false;
                    Assert(RunningApplication.CanResume(fixture, 27899, app) && !child.HasExited && child.Id == pid,
                        "Inspection changed the running process");
                }
                finally
                {
                    child.StandardInput.WriteLine("exit");
                    if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(5000); }
                }
            }
            Console.WriteLine("PASS resume launch evidence: flags, privacy, ambiguous launches, live fixture PID unchanged");
        }
    }
}
