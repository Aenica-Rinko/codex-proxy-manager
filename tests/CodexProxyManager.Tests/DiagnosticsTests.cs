using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexProxyManager
{
    internal static class DiagnosticsTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void Run(DiagnosticInput input)
        {
            var report = Diagnostics.Collect(input, false, CancellationToken.None);
            Assert(report.Rows.Exists(r => r.Code == "CORE_API" && r.Status == "通过"), "Diagnostic core API check failed");
            Assert(report.Rows.Exists(r => r.Code == "NODE_API" && r.Status == "通过"), "Diagnostic node API check failed");
            Assert(report.Rows.Exists(r => r.Code == "NODE_PROBE" && r.Status == "跳过"), "Diagnostic must not probe internet by default");
            string exported = report.ToText();
            foreach (string privateValue in new[] { input.CorePath, input.ConfigPath, input.CoreSecret, input.NodeSecret, input.GroupName, "Local Upstream" })
                Assert(String.IsNullOrEmpty(privateValue) || !exported.Contains(privateValue), "Report exposed private input");

            string validSecret = input.CoreSecret;
            input.CoreSecret = "wrong-test-secret";
            var denied = Diagnostics.Collect(input, false, CancellationToken.None);
            Assert(denied.Rows.Exists(r => r.Code == "CORE_API" && r.Status == "失败" && r.Message.Contains("密钥")), "401 must identify authentication failure");
            input.CoreSecret = validSecret;

            input.SessionActive = false;
            var stopped = Diagnostics.Collect(input, true, CancellationToken.None);
            Assert(stopped.Rows.Exists(r => r.Code == "CORE_API" && r.Status == "跳过"), "Stopped session must skip API access");
            Assert(stopped.Rows.Exists(r => r.Code == "LOCAL_PORT" && r.Status == "注意"), "Occupied port in stopped session must be identified");
            input.SessionActive = true;

            var fake = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                "{\"connections\":[{\"metadata\":{\"process\":\"Probe.exe\",\"host\":\"private-host\",\"processPath\":\"C:/private-user/Probe.exe\"},\"chains\":[\"private-node-secret\",\"private-group\"]},{\"metadata\":{\"process\":\"Other.exe\"},\"chains\":[\"DIRECT\"]}]}");
            var flow = new DiagnosticReport();
            Diagnostics.AnalyzeConnections(fake, new[] { "Probe.exe" }, flow);
            Assert(flow.Rows[0].Message.Contains("代理 1") && flow.Rows[0].Message.Contains("直连 0"), "Flow check must filter target process");
            Assert(!flow.ToText().Contains("private-") && !flow.ToText().Contains("Probe.exe"), "Flow report leaked metadata");
            var empty = new DiagnosticReport();
            Diagnostics.AnalyzeConnections(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"connections\":[]}"), new[] { "Probe.exe" }, empty);
            Assert(empty.Rows[0].Status == "注意" && empty.Rows[0].Message.Contains("空闲"), "No active connections must not claim definite routing failure");
            Assert(!Diagnostics.Explain(new IOException("secret-user subscription-token")).Contains("secret-user"), "Raw exception reached report");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var accepted = listener.AcceptTcpClientAsync();
                string url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                var attempt = Task.Run(delegate
                {
                    try { Diagnostics.ReadApi(url, "", "/version", 250); return "unexpected-success"; }
                    catch (Exception ex) { return Diagnostics.Explain(ex); }
                });
                Assert(accepted.Wait(2000), "Timeout fixture did not accept connection");
                using (TcpClient socket = accepted.Result)
                {
                    Assert(attempt.Wait(2500) && attempt.Result.Contains("超时"), "Unresponsive controller must time out");
                }
            }
            finally { listener.Stop(); }
            Console.WriteLine("PASS diagnostics: live APIs, auth failure, occupied port, no automatic internet probe, process filtering, privacy and timeout");
        }
    }
}
