using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CodexProxyManager
{
    // Only the child receives these settings. Never set user/machine environment variables.
    internal static class ProcessProxyLauncher
    {
        internal static ProcessStartInfo BuildStartInfo(string executable, int port)
        {
            return BuildStartInfo(executable, port, "chromium");
        }

        internal static ProcessStartInfo BuildStartInfo(string executable, int port, string mode)
        {
            if (!File.Exists(executable)) throw new FileNotFoundException("找不到应用程序。", executable);
            if (mode != "chromium" && mode != "environment") throw new ArgumentException("不支持的启动模式。", "mode");
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException("port");
            string endpoint = "http://127.0.0.1:" + port;
            ProcessStartInfo info = new ProcessStartInfo(executable);
            info.UseShellExecute = false;
            info.WorkingDirectory = Path.GetDirectoryName(executable);
            info.Arguments = mode == "chromium" ? "--proxy-server=" + endpoint +
                " --proxy-bypass-list=\"localhost;127.0.0.1;[::1]\"" : "";
            foreach (string name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
                info.EnvironmentVariables[name] = endpoint;
            // Do not inherit a wildcard or domain bypass from the manager's own launcher.
            info.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.1,::1";
            return info;
        }

        internal static string FindCodexExecutable()
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            // Discover the registered package each time so Store updates do not break the path.
            string script = "$ErrorActionPreference='Stop';" +
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                "$p=Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1;" +
                "if($null -eq $p){throw 'Codex package not found'};" +
                "[xml]$m=Get-Content -LiteralPath (Join-Path $p.InstallLocation 'AppxManifest.xml');" +
                "$a=@($m.Package.Applications.Application) | Where-Object {$_.Id -eq 'App'} | Select-Object -First 1;" +
                "if(-not $a.Executable){throw 'Codex executable not found'};" +
                "[Console]::Write((Join-Path $p.InstallLocation ([string]$a.Executable)))";
            info.Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            using (Process process = Process.Start(info))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("查找 Codex 安装目录超时。");
                }
                if (process.ExitCode != 0 || !File.Exists(output.Result.Trim()))
                    throw new InvalidOperationException("未找到已安装的 Codex，请先通过 Microsoft Store 安装或修复应用。");
                return output.Result.Trim();
            }
        }
    }
}
