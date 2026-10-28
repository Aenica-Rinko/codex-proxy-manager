// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Threading;

namespace CodexProxyManager
{
    internal enum LaunchReadinessKind { Checking, Launch, Resume, NeedsRestart, MissingFiles, Unknown }

    // UI hints are advisory. ProxySessionService rechecks all launch guards at execution time.
    // Only fixed messages cross this boundary; no process arguments or private paths.
    internal sealed class LaunchReadiness
    {
        internal readonly LaunchReadinessKind Kind;
        internal readonly string ActionText, Hint;
        internal LaunchReadiness(LaunchReadinessKind kind, string action, string hint)
        { Kind = kind; ActionText = action; Hint = hint; }
        internal static LaunchReadiness Checking()
        { return new LaunchReadiness(LaunchReadinessKind.Checking, "正在检查…", "应用：正在检查启动条件；不会自动启动或关闭应用。"); }
        internal static LaunchReadiness FromProcess(bool running, bool compatible)
        {
            if (!running) return new LaunchReadiness(LaunchReadinessKind.Launch, "启动应用并连接",
                "应用：未运行。点击后才会启动应用和专用代理。");
            if (compatible) return new LaunchReadiness(LaunchReadinessKind.Resume, "恢复代理",
                "应用：运行中，启动参数匹配；可恢复代理而不重启，实际参数效果仍待验证。");
            return new LaunchReadiness(LaunchReadinessKind.NeedsRestart, "查看启动说明",
                "应用：运行中，但无法确认匹配参数。请保存工作并退出应用，再从管理器启动；不会自动关闭应用。");
        }
        internal static LaunchReadiness Inspect(ManagedApplication app, string core, string home, string config, int port,
            Func<ManagedApplication, string> resolve = null)
        {
            if (!File.Exists(core) || !File.Exists(config) || !File.Exists(Path.Combine(home, "GeoSite.dat")) ||
                !File.Exists(Path.Combine(home, "geoip.metadb")))
                return new LaunchReadiness(LaunchReadinessKind.MissingFiles, "查看准备说明",
                    "启动条件：缺少核心、地理数据或档案。请查看准备说明并检查代理档案；尚未启动代理。");
            try
            {
                string executable = resolve == null ? ApplicationConfiguration.ResolveExecutable(app) : resolve(app);
                bool running = ApplicationConfiguration.IsRunning(app);
                return FromProcess(running, running && RunningApplication.CanResume(executable, port, app));
            }
            catch
            {
                return new LaunchReadiness(LaunchReadinessKind.Unknown, "查看检查说明",
                    "应用：暂时无法检查。请确认应用已安装，并在“管理应用”检查程序路径，再重试；尚未启动代理。");
            }
        }
    }

    // A five-second UI refresh must not launch an Appx discovery PowerShell every time.
    // Cache only advisory lookup; ProxySessionService still discovers the package afresh.
    internal sealed class AdvisoryExecutableResolver
    {
        private readonly Func<string> discover;
        private readonly Func<DateTime> now;
        private string cached;
        private DateTime refreshAfter;
        internal AdvisoryExecutableResolver() : this(ProcessProxyLauncher.FindCodexExecutable, delegate { return DateTime.UtcNow; }) { }
        internal AdvisoryExecutableResolver(Func<string> discover, Func<DateTime> now)
        { this.discover = discover; this.now = now; }
        internal string Resolve(ManagedApplication app)
        {
            if (app.Id != "codex") return ApplicationConfiguration.ResolveExecutable(app);
            if (now() < refreshAfter)
            {
                if (cached != null && File.Exists(cached)) return cached;
                if (cached == null) throw new InvalidOperationException("应用安装状态暂时无法确认。");
            }
            cached = null;
            refreshAfter = now().AddSeconds(60);
            cached = discover();
            return cached;
        }
    }

    internal sealed class LaunchReadinessPoller
    {
        private int pending;
        internal static bool CanApply(int checkedRevision, int currentRevision, bool active, bool busy, bool exiting)
        { return checkedRevision == currentRevision && !active && !busy && !exiting; }
        internal bool Begin(Func<LaunchReadiness> inspect, Action<LaunchReadiness> complete)
        {
            if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LaunchReadiness result;
                try { result = inspect(); }
                catch { result = new LaunchReadiness(LaunchReadinessKind.Unknown, "查看检查说明", "启动条件暂时无法检查。请打开连接诊断后重试。"); }
                try { complete(result); }
                catch { /* A closing UI must not crash a worker thread. */ }
                finally { Interlocked.Exchange(ref pending, 0); }
            });
            return true;
        }
    }
}
