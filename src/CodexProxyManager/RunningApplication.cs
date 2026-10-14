// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace CodexProxyManager
{
    internal static class ProcessIdentity
    {
        internal static bool Matches(Process process, string executable, long startUtcTicks)
        {
            if (process == null || String.IsNullOrWhiteSpace(executable) || startUtcTicks <= 0) return false;
            try
            {
                return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startUtcTicks &&
                    String.Equals(process.MainModule.FileName, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    // Read-only launch evidence, not a promise that an application honors its flags.
    // Never attach a debugger, inject code, persist command lines or terminate a target.
    internal static class RunningApplication
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        internal static bool MatchesLaunch(string commandLine, int port, ManagedApplication application)
        {
            if (application == null || application.LaunchMode != "chromium" ||
                String.IsNullOrWhiteSpace(commandLine) || port < 1024 || port > 65535) return false;
            string[] actual = Parse(commandLine);
            string[] expected = Parse("fixture.exe --proxy-server=http://127.0.0.1:" + port +
                " --proxy-bypass-list=\"localhost;127.0.0.1;[::1]\"" + PrivacyProtection.Arguments(application));
            var switches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < actual.Length; i++)
            {
                string arg = actual[i];
                if (arg == "--") return false; // Switches after the terminator are not flags.
                if (!arg.StartsWith("--", StringComparison.Ordinal) &&
                    (arg.StartsWith("-", StringComparison.Ordinal) || arg.StartsWith("/", StringComparison.Ordinal))) return false;
                if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
                int separator = arg.IndexOf('=');
                string key = separator < 0 ? arg : arg.Substring(0, separator);
                // Duplicate switches and alternative proxy sources are ambiguous; refuse.
                if (switches.ContainsKey(key)) return false;
                switches[key] = arg;
            }
            foreach (string key in new[] { "--type", "--no-proxy-server", "--proxy-pac-url", "--proxy-auto-detect" })
                if (switches.ContainsKey(key)) return false;
            for (int i = 1; i < expected.Length; i++)
            {
                string arg = expected[i];
                int separator = arg.IndexOf('=');
                string key = separator < 0 ? arg : arg.Substring(0, separator);
                string value;
                if (!switches.TryGetValue(key, out value) || value != arg) return false;
            }
            // Extra privacy flags from an earlier configuration also require a restart:
            // do not display the newly selected privacy settings as already requested.
            foreach (string key in new[] { "--webrtc-ip-handling-policy", "--force-webrtc-ip-handling-policy",
                "--host-resolver-rules", "--disable-quic" })
            {
                string value;
                if (switches.TryGetValue(key, out value) && Array.IndexOf(expected, value) < 0) return false;
            }
            return true;
        }

        internal static bool CanResume(string executable, int port, ManagedApplication application)
        {
            if (application == null || application.LaunchMode != "chromium") return false;
            try
            {
                string fullPath = Path.GetFullPath(executable);
                string name = Path.GetFileName(fullPath).Replace("\\", "\\\\").Replace("'", "\\'");
                var options = new EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(3) };
                using (var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"),
                    new ObjectQuery("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE Name='" + name + "'"), options))
                using (var results = searcher.Get())
                {
                    bool found = false;
                    foreach (ManagementObject process in results)
                    using (process)
                    {
                        string path = Convert.ToString(process["ExecutablePath"]);
                        if (String.IsNullOrEmpty(path)) return false; // Access denied is not proof.
                        if (!String.Equals(fullPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) continue;
                        string commandLine = Convert.ToString(process["CommandLine"]);
                        string[] arguments = Parse(commandLine);
                        bool child = false;
                        foreach (string arg in arguments)
                            if (arg == "--type" || arg.StartsWith("--type=", StringComparison.Ordinal)) child = true;
                        if (child) continue;
                        if (!MatchesLaunch(commandLine, port, application)) return false;
                        found = true;
                    }
                    return found;
                }
            }
            catch { return false; } // No raw private paths or command lines escape this probe.
        }

        internal static bool HasConfigArgument(string commandLine, string config, string sessionConfig)
        {
            string[] args = Parse(commandLine);
            int count = 0;
            bool matches = false;
            for (int i = 1; i + 1 < args.Length; i++)
                if (args[i] == "-f")
                {
                    count++;
                    matches = String.Equals(args[i + 1], config, StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(args[i + 1], sessionConfig, StringComparison.OrdinalIgnoreCase);
                }
            return count == 1 && matches;
        }

        private static string[] Parse(string commandLine)
        {
            if (String.IsNullOrWhiteSpace(commandLine)) return new string[0];
            int count;
            IntPtr pointer = CommandLineToArgvW(commandLine, out count);
            if (pointer == IntPtr.Zero) return new string[0];
            try
            {
                var result = new string[count];
                for (int i = 0; i < count; i++)
                    result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size));
                return result;
            }
            finally { LocalFree(pointer); }
        }
    }
}
