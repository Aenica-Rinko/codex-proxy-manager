using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class ControllerProbeTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Controller probe: " + message);
        }
        private sealed class ProxyTrap : IWebProxy
        {
            public ICredentials Credentials { get; set; }
            public Uri GetProxy(Uri destination) { throw new Exception("Inherited system proxy used"); }
            public bool IsBypassed(Uri host) { throw new Exception("Inherited system proxy used"); }
        }
        private sealed class Server : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Task worker;
            private volatile bool stopping;
            internal readonly string Url;
            internal int Hits;
            internal bool AuthSeen;
            internal Server(string status, string customBody, string location, int delay)
            {
                listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                worker = Task.Run(delegate
                {
                    while (!stopping)
                    {
                        try
                        {
                            using (var client = listener.AcceptTcpClient())
                            {
                                client.ReceiveTimeout = client.SendTimeout = 1500;
                                using (var stream = client.GetStream())
                                {
                                    var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                                    string request = reader.ReadLine() ?? "";
                                    string header;
                                    while (!String.IsNullOrEmpty(header = reader.ReadLine()))
                                        if (header == "Authorization: Bearer fixture-secret") AuthSeen = true;
                                    Interlocked.Increment(ref Hits);
                                    if (delay > 0) Thread.Sleep(delay);
                                    string body = customBody ?? (request.Contains("/version") ? "{\"version\":\"fixture\"}" :
                                        "{\"proxies\":{\"one\":{\"type\":\"Selector\"},\"two\":{\"type\":\"Direct\"}}}");
                                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                                    byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: " + bytes.Length +
                                        "\r\nConnection: close\r\n" + (location == null ? "" : "Location: " + location + "\r\n") + "\r\n");
                                    stream.Write(head, 0, head.Length);
                                    stream.Write(bytes, 0, bytes.Length);
                                }
                            }
                        }
                        catch (SocketException) { if (stopping) return; }
                        catch (IOException) { }
                        catch (ObjectDisposedException) { return; }
                    }
                });
            }
            public void Dispose() { stopping = true; listener.Stop(); Check(worker.Wait(4000), "fixture did not stop"); }
        }
        private static string Failure(string url, int timeout)
        {
            try { ControllerProbe.Check(url, "fixture-secret", timeout); return "unexpected-success"; }
            catch (Exception ex) { return Diagnostics.Explain(ex); }
        }
        private static T Field<T>(object obj, string name)
        {
            return (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);
        }
        internal static void Run(string scratch)
        {
            using (var server = new Server("200 OK", null, null, 0))
            {
                IWebProxy previous = WebRequest.DefaultWebProxy;
                try
                {
                    WebRequest.DefaultWebProxy = new ProxyTrap();
                    Check(ControllerProbe.Check(server.Url, "fixture-secret", 1000) == 1, "selector count");
                    Check(server.Hits == 2 && server.AuthSeen, "requests/authentication");
                }
                finally { WebRequest.DefaultWebProxy = previous; }
            }
            using (var server = new Server("401 Unauthorized", "private-error-text", null, 0))
            {
                string error = Failure(server.Url, 1000);
                Check(error.Contains("密钥") && !error.Contains("private-error"), "auth failure privacy");
            }
            using (var server = new Server("200 OK", "{\"unexpected\":1}", null, 0))
                Check(Failure(server.Url, 1000) != "unexpected-success", "invalid response accepted");
            using (var target = new Server("200 OK", null, null, 0))
            using (var redirect = new Server("302 Found", "", target.Url + "/version", 0))
            {
                Check(Failure(redirect.Url, 1000) != "unexpected-success", "redirect accepted");
                Check(target.Hits == 0, "redirect followed");
            }
            using (var server = new Server("200 OK", null, null, 800))
            {
                var watch = Stopwatch.StartNew();
                Check(Failure(server.Url, 150).Contains("超时") && watch.ElapsedMilliseconds < 2000, "timeout");
            }
            using (var server = new Server("200 OK", null, null, 400))
            {
                string user = Path.Combine(scratch, "probe-ui");
                var constructor = typeof(ProfileManagerForm).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
                using (var form = (Form)constructor.Invoke(new object[] { scratch, user, true }))
                using (var trigger = new Button())
                using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
                {
                    IntPtr handle = form.Handle;
                    Field<TextBox>(form, "externalControllerBox").Text = server.Url;
                    Field<TextBox>(form, "externalSecretBox").Text = "fixture-secret";
                    int ticks = 0;
                    timer.Tick += delegate { ticks++; };
                    timer.Start();
                    var method = typeof(ProfileManagerForm).GetMethod("TestExternalController", BindingFlags.NonPublic | BindingFlags.Instance);
                    var watch = Stopwatch.StartNew();
                    method.Invoke(form, new object[] { trigger, EventArgs.Empty });
                    Check(watch.ElapsedMilliseconds < 300 && !trigger.Enabled, "test blocks UI / repeat button not disabled");
                    while (Field<bool>(form, "controllerTestRunning") && watch.ElapsedMilliseconds < 6000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(5);
                    }
                    Check(ticks > 0 && trigger.Enabled && Field<Label>(form, "statusLabel").Text.Contains("检查成功"), "UI responsiveness/completion");
                    method.Invoke(form, new object[] { trigger, EventArgs.Empty });
                    form.Close();
                    watch.Restart();
                    while (Field<bool>(form, "controllerTestRunning") && watch.ElapsedMilliseconds < 6000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(5);
                    }
                    Check(!Field<bool>(form, "controllerTestRunning"), "closing during check did not finish safely");
                    Check(!Directory.Exists(user), "test wrote user settings");
                }
            }
            Console.WriteLine("PASS controller probe: auth, invalid response, redirect refusal, proxy isolation, timeout, responsive UI and safe close without settings writes");
        }
    }
}
