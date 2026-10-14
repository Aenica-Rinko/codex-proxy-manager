// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Text;

namespace CodexProxyManager
{
    internal static class PrivacyLog
    {
        internal const int MaxBytes = 1024 * 1024;
        internal const int RetentionDays = 7;
        private static readonly object Gate = new object();
        private static readonly string[] Names = { "manager.log", "mihomo.stdout.log", "mihomo.stderr.log" };

        internal static string ManagerEvent(string message)
        {
            string value = message ?? "";
            // Fixed output only. Never serialize arbitrary exception text or a node name.
            if (value.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.StartsWith("Could not", StringComparison.Ordinal)) return "Manager operation failed; use connection diagnostics.";
            if (value.StartsWith("Switched node", StringComparison.Ordinal)) return "Node selection changed.";
            if (value.IndexOf(" selected ", StringComparison.Ordinal) >= 0) return "Node selection completed.";
            if (value.StartsWith("Health check", StringComparison.Ordinal)) return "Node health check failed.";
            if (value.StartsWith("Auto failover", StringComparison.Ordinal)) return "Automatic node failover requested.";
            if (value.StartsWith("Refreshed ", StringComparison.Ordinal)) return "Proxy providers refreshed.";
            if (value.StartsWith("Started bundled", StringComparison.Ordinal)) return "Bundled proxy core started.";
            if (value.StartsWith("Stopped bundled", StringComparison.Ordinal) || value.StartsWith("Stopped stale", StringComparison.Ordinal)) return "Bundled proxy core stopped.";
            if (value.StartsWith("Launched application", StringComparison.Ordinal)) return "Target application launched with process-only proxy.";
            foreach (string safe in new[] { "Manager started.", "Manager exited.", "Session ended.",
                "Found stale manager state.", "Started recovery watchdog.",
                "Resumed proxy for an existing compatible application.",
                "Watchdog detected an unexpected manager exit.", "Watchdog cleanup finished.",
                "Proxy was changed externally; skipped registry restore.", "Restored the previous Windows proxy state." })
                if (value == safe) return safe;
            return "Manager event; private details omitted.";
        }

        internal static void AppendManager(string path, string message)
        {
            Append(path, ManagerEvent(message));
        }

        internal static void AppendCore(string path, string message)
        {
            string value = message ?? "";
            if (value.IndexOf("level=error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("panic", StringComparison.OrdinalIgnoreCase) >= 0)
                Append(path, "Proxy core reported an error; raw output omitted.");
            else if (value.IndexOf("level=warn", StringComparison.OrdinalIgnoreCase) >= 0)
                Append(path, "Proxy core reported a warning; raw output omitted.");
            // Info/debug and unclassified output are not persisted, including stderr.
        }

        private static void Append(string path, string safeMessage)
        {
            try
            {
                lock (Gate)
                {
                    Rotate(path);
                    string line = DateTime.UtcNow.ToString("o") + " " + safeMessage + Environment.NewLine;
                    if (!SafePath(path)) return;
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch { } // A logging failure must not affect the live proxy session.
        }

        internal static bool SafePath(string path)
        {
            if (String.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return false;
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                current = Path.GetDirectoryName(current);
            }
            return true;
        }

        internal static void Rotate(string path)
        {
            if (!SafePath(path) || !SafePath(path + ".1")) return;
            string previous = path + ".1";
            if (File.Exists(previous) && File.GetLastWriteTimeUtc(previous) < DateTime.UtcNow.AddDays(-RetentionDays))
                File.Delete(previous);
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-RetentionDays))
                File.Delete(path);
            if (!File.Exists(path) || new FileInfo(path).Length < MaxBytes - 512) return;
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(path, previous);
        }

        internal static int Clear(string logsDirectory)
        {
            string root = Path.GetFullPath(logsDirectory);
            if (!SafePath(root) || !Directory.Exists(root)) throw new IOException("日志目录不可用或包含目录链接。");
            int count = 0;
            lock (Gate)
            {
                // Resolve every exact target before deleting anything. No globs or recursion.
                foreach (string name in Names)
                    foreach (string suffix in new[] { "", ".1" })
                        if (!SafePath(Path.Combine(root, name + suffix))) throw new IOException("日志文件包含链接，已取消清理。");
                foreach (string name in Names)
                    foreach (string suffix in new[] { "", ".1" })
                    {
                        string path = Path.Combine(root, name + suffix);
                        if (File.Exists(path)) { File.Delete(path); count++; }
                    }
            }
            return count;
        }
    }
}
