// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexProxyManager
{
    internal sealed class BrowserProbeResult
    {
        internal bool Passed;
        internal string Message;
    }

    internal static class BrowserPrivacyProbe
    {
        internal static bool Supports(ManagedApplication app)
        {
            if (app == null || app.Id == "codex" || app.LaunchMode != "chromium" || !File.Exists(app.Executable)) return false;
            try
            {
                string name = Path.GetFileName(app.Executable);
                string product = FileVersionInfo.GetVersionInfo(app.Executable).ProductName ?? "";
                return (name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) && product.Contains("Google Chrome")) ||
                    (name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) && product.Contains("Microsoft Edge"));
            }
            catch { return false; }
        }

        internal static BrowserProbeResult Evaluate(int baselineUdp, int protectedUdp, int hostCandidates, bool proxyFetch)
        {
            if (baselineUdp <= 0) return new BrowserProbeResult { Message = "测试无法判定：基线没有收到 STUN 包，不能据此确认限制有效。" };
            if (protectedUdp != 0 || hostCandidates != 0)
                return new BrowserProbeResult { Message = "本地测试未通过：限制参数下仍观察到 UDP 或本机地址候选。" };
            if (!proxyFetch) return new BrowserProbeResult { Message = "测试未通过：限制下的代理 HTTP 请求失败。" };
            return new BrowserProbeResult { Passed = true, Message = "本地测试通过（隔离浏览器）：未观察到 STUN UDP 或本机地址候选，代理 HTTP 请求成功。仅覆盖本次本地场景，不证明当前会话或公网无泄漏。" };
        }

        internal static async Task<BrowserProbeResult> Run(ManagedApplication app, CancellationToken cancellation)
        {
            if (!Supports(app)) return new BrowserProbeResult { Message = "此应用暂不支持自动行为测试；Codex / Electron 的 WebRTC 效果仍未验证。" };
            if (!PrivacyProtection.WebRtcRequested(app)) return new BrowserProbeResult { Message = "WebRTC 限制未启用，请先保存应用隐私选项。" };
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var local = new LocalFixture())
            {
                timeout.CancelAfter(60000);
                var baseline = new ManagedApplication { Id = app.Id, Executable = app.Executable, LaunchMode = "chromium", LimitWebRtc = false };
                Sample first = await SampleBrowser(baseline, local, timeout.Token);
                Sample second = await SampleBrowser(new ManagedApplication { Id = app.Id, Executable = app.Executable,
                    LaunchMode = "chromium", LimitWebRtc = app.LimitWebRtc, RestrictBrowserDns = app.RestrictBrowserDns,
                    DisableQuic = app.DisableQuic }, local, timeout.Token);
                return Evaluate(first.Udp, second.Udp, second.Host, second.ProxyFetch);
            }
        }

        private sealed class Sample { internal int Udp, Host; internal bool ProxyFetch; }

        private static async Task<Sample> SampleBrowser(ManagedApplication app, LocalFixture local, CancellationToken cancellation)
        {
            string directory = Path.Combine(Path.GetTempPath(), "CodexPrivacyBrowser-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Process browser = null;
            ClientWebSocket socket = null;
            int before = local.Packets;
            try
            {
                var info = ProcessProxyLauncher.BuildStartInfo(app.Executable, local.Port, app);
                info.Arguments += " --headless=new --user-data-dir=\"" + directory + "\" --remote-debugging-port=0" +
                    " --remote-debugging-address=127.0.0.1 --no-first-run --no-default-browser-check" +
                    " --disable-background-networking --disable-component-update --disable-sync --disable-extensions" +
                    " --metrics-recording-only http://127.0.0.1:" + local.Port + "/blank";
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                browser = Process.Start(info);
                string activePath = Path.Combine(directory, "DevToolsActivePort");
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!File.Exists(activePath))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (browser.HasExited || DateTime.UtcNow > deadline) throw new IOException("隔离测试浏览器未能启动。");
                    await Task.Delay(100, cancellation);
                }
                int port = Int32.Parse(File.ReadAllLines(activePath)[0]);
                var json = new JavaScriptSerializer();
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
                request.Proxy = null; request.AllowAutoRedirect = false;
                request.Timeout = request.ReadWriteTimeout = 2500;
                string target = null;
                using (var abort = cancellation.Register(request.Abort))
                using (var response = await request.GetResponseAsync())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    object[] pages = json.Deserialize<object[]>(await reader.ReadToEndAsync());
                    foreach (Dictionary<string, object> page in pages)
                        if (Convert.ToString(page["type"]) == "page") { target = Convert.ToString(page["webSocketDebuggerUrl"]); break; }
                }
                Uri socketUrl;
                if (!Uri.TryCreate(target, UriKind.Absolute, out socketUrl) || socketUrl.Scheme != "ws" ||
                    socketUrl.Host != "127.0.0.1" || socketUrl.Port != port) throw new IOException("本地测试接口无效。");
                socket = new ClientWebSocket();
                socket.Options.Proxy = null;
                await socket.ConnectAsync(socketUrl, cancellation);
                await Task.Delay(1000, cancellation); // Initial local page navigation must complete.
                string script = "(async()=>{const pc=new RTCPeerConnection({iceServers:[{urls:'stun:127.0.0.1:" + local.UdpPort + "'}]});" +
                    "let host=0;pc.onicecandidate=e=>{if(e.candidate&&e.candidate.type==='host')host++;};" +
                    "pc.createDataChannel('local-probe');await pc.setLocalDescription(await pc.createOffer());" +
                    "await new Promise(r=>setTimeout(r,3500));pc.close();" +
                    "const r=await fetch('http://privacy-probe.invalid/check');return {host:host,proxyFetch:await r.text()==='proxy-ok'};})()";
                var command = new Dictionary<string, object> { { "id", 1 }, { "method", "Runtime.evaluate" },
                    { "params", new Dictionary<string, object> { { "expression", script }, { "awaitPromise", true }, { "returnByValue", true } } } };
                byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(command));
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation);
                while (true)
                {
                    var data = new MemoryStream();
                    byte[] buffer = new byte[8192];
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
                        if (received.MessageType != WebSocketMessageType.Text || data.Length + received.Count > 262144)
                            throw new IOException("本地测试响应无效。");
                        data.Write(buffer, 0, received.Count);
                    } while (!received.EndOfMessage);
                    var reply = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(data.ToArray()));
                    data.Dispose();
                    if (!reply.ContainsKey("id") || Convert.ToInt32(reply["id"]) != 1) continue;
                    if (reply.ContainsKey("error")) throw new IOException("浏览器未接受本地测试请求。");
                    var result = (Dictionary<string, object>)reply["result"];
                    if (result.ContainsKey("exceptionDetails")) throw new IOException("本地 WebRTC 或代理请求未完成。");
                    var remote = (Dictionary<string, object>)result["result"];
                    var value = (Dictionary<string, object>)remote["value"];
                    return new Sample { Udp = local.Packets - before, Host = Convert.ToInt32(value["host"]), ProxyFetch = Convert.ToBoolean(value["proxyFetch"]) };
                }
            }
            finally
            {
                if (socket != null)
                {
                    try
                    {
                        using (var stop = new CancellationTokenSource(2000))
                        {
                            byte[] close = Encoding.UTF8.GetBytes("{\"id\":2,\"method\":\"Browser.close\"}");
                            if (socket.State == WebSocketState.Open)
                                socket.SendAsync(new ArraySegment<byte>(close), WebSocketMessageType.Text, true, stop.Token).GetAwaiter().GetResult();
                        }
                    }
                    catch { }
                    socket.Dispose();
                }
                if (browser != null)
                {
                    try { if (!browser.WaitForExit(3000)) { browser.Kill(); browser.WaitForExit(2000); } } catch { }
                    browser.Dispose();
                }
                // Only our unique, validated temp directory. A locked directory is retained.
                try { if (PrivacyLog.SafePath(directory)) Directory.Delete(directory, true); } catch { }
            }
        }

        private sealed class LocalFixture : IDisposable
        {
            private readonly TcpListener http = new TcpListener(IPAddress.Loopback, 0);
            private readonly UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private readonly Task httpWorker, udpWorker;
            private int packets;
            internal int Port { get { return ((IPEndPoint)http.LocalEndpoint).Port; } }
            internal int UdpPort { get { return ((IPEndPoint)udp.Client.LocalEndPoint).Port; } }
            internal int Packets { get { return Interlocked.CompareExchange(ref packets, 0, 0); } }
            internal LocalFixture()
            {
                http.Start();
                udp.Client.ReceiveTimeout = 200;
                httpWorker = Task.Run(async delegate
                {
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            using (var client = await http.AcceptTcpClientAsync())
                            {
                                client.ReceiveTimeout = client.SendTimeout = 1000;
                                using (var stream = client.GetStream())
                                {
                                    var header = new StringBuilder();
                                    while (header.Length < 16384)
                                    {
                                        int next = stream.ReadByte();
                                        if (next < 0) break;
                                        header.Append((char)next);
                                        if (header.Length >= 4 && header.ToString(header.Length - 4, 4) == "\r\n\r\n") break;
                                    }
                                    string first = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                                    string body = first.StartsWith("GET http://privacy-probe.invalid/", StringComparison.Ordinal)
                                        ? "proxy-ok" : "<!doctype html><title>Local privacy test</title>";
                                    byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n" + body);
                                    stream.Write(response, 0, response.Length);
                                }
                            }
                        }
                        catch { if (stop.IsCancellationRequested) break; }
                    }
                });
                udpWorker = Task.Run(delegate
                {
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            IPEndPoint sender = null;
                            byte[] data = udp.Receive(ref sender);
                            if (data.Length >= 20 && data[4] == 0x21 && data[5] == 0x12 && data[6] == 0xa4 && data[7] == 0x42)
                                Interlocked.Increment(ref packets);
                        }
                        catch { if (stop.IsCancellationRequested) break; }
                    }
                });
            }
            public void Dispose()
            {
                stop.Cancel(); http.Stop(); udp.Close();
                try { Task.WaitAll(new[] { httpWorker, udpWorker }, 1500); } catch { }
                stop.Dispose();
            }
        }
    }
}
