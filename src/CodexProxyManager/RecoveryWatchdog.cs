// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CodexProxyManager
{
    internal static class RecoveryWatchdog
    {
        internal static void Run(string[] args)
        {
            if (args == null || args.Length < 5) return;
            int managerPid;
            int corePid;
            if (!Int32.TryParse(args[1], out managerPid) || !Int32.TryParse(args[2], out corePid)) return;
            string statePath = args[3];
            string logPath = args[4];
            if (args.Length >= 6 && !String.IsNullOrWhiteSpace(args[5])) Program.ManagedProxy = args[5];
            try
            {
                if (!File.Exists(statePath)) return;
                ProxyState owned = Program.ReadState(statePath);
                if (owned.CorePid != corePid || (args.Length >= 7 && owned.SessionId != args[6])) return;
                while (IsProcessRunning(managerPid, owned.ManagerStartUtcTicks))
                {
                    // A normal stop removes the session state before the manager exits.
                    // Exit immediately so old watchdog processes cannot accumulate.
                    if (!File.Exists(statePath) || !OwnsSession(Program.ReadState(statePath), owned)) return;
                    Thread.Sleep(1000);
                }
                if (!File.Exists(statePath) || !OwnsSession(Program.ReadState(statePath), owned)) return;
                Program.AppendLog(logPath, "Watchdog detected an unexpected manager exit.");
                try { Program.RestoreProxy(Program.ReadState(statePath), logPath); }
                catch (Exception ex) { Program.AppendLog(logPath, "Watchdog restore error: " + ex.Message); }
                try
                {
                    using (Process core = Process.GetProcessById(corePid))
                        if (ProcessIdentity.Matches(core, owned.CoreExecutablePath, owned.CoreStartUtcTicks)) core.Kill();
                }
                catch { }
                try { if (File.Exists(statePath) && OwnsSession(Program.ReadState(statePath), owned)) File.Delete(statePath); } catch { }
                Program.AppendLog(logPath, "Watchdog cleanup finished.");
            }
            catch (Exception ex) { Program.AppendLog(logPath, "Watchdog error: " + ex.Message); }
        }

        internal static bool OwnsSession(ProxyState current, ProxyState owned)
        {
            return current != null && owned != null && current.CorePid == owned.CorePid &&
                String.Equals(current.SessionId, owned.SessionId, StringComparison.Ordinal);
        }

        private static bool IsProcessRunning(int pid, long startTicks)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    return !process.HasExited && (startTicks <= 0 || process.StartTime.ToUniversalTime().Ticks == startTicks);
            }
            catch { return false; }
        }
    }

}
