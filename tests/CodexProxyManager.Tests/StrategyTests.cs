// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexProxyManager
{
    internal static class StrategyTests
    {
        internal const string Nodes = "proxies:\n  - {name: A, type: socks5, server: 127.0.0.1, port: 9}\n  - {name: B, type: socks5, server: 127.0.0.1, port: 10}\n";
        internal const string Groups = "proxy-groups:\n  - {name: Main, type: select, proxies: [Backup, A, DIRECT]}\n  - {name: Backup, type: fallback, proxies: [A, B], interval: 60, url: 'https://www.gstatic.com/generate_204'}\n  - {name: Other, type: select, proxies: [Main]}\n";
        private static void Check(bool ok, string message) { if (!ok) throw new Exception("Strategies: " + message); }
        private static void Reject(string yaml)
        { bool failed = false; try { ImportedTopology.Parse(yaml); } catch (InvalidDataException) { failed = true; } Check(failed, "invalid graph accepted"); }
        internal static void Run(string scratch)
        {
            var imported = ImportedTopology.Parse(Nodes + Groups + "tun: {enable: true}\ndns: {enable: true}\nmixed-port: 11111\nrules: ['MATCH,DIRECT']\n");
            Check(imported.Nodes.Children.Count == 2 && imported.GroupCount == 3, "counts changed");
            string config = imported.BuildConfig(27890, false);
            var roundtrip = ImportedTopology.Parse(config);
            Check(roundtrip.GroupCount == 4 && roundtrip.Snapshot().Contains("Backup"), "references or entry missing");
            Check(config.Contains("PROCESS-NAME,codex.exe,Codex Proxy") && !config.Contains("11111") && !config.Contains("enable: true"), "global settings adopted");
            Check(ImportedTopology.Parse(Nodes + "proxy-groups: [{name: Codex Proxy, type: select, proxies: [A]}]\n").Entry != "Codex Proxy", "entry collision");
            var anchors = ImportedTopology.Parse("base: &base {type: socks5, server: 127.0.0.1, port: 9}\nproxies: [{<<: *base, name: A}]\n");
            Check(anchors.Snapshot().Contains("socks5") && !anchors.Snapshot().Contains("<<"), "merge anchor not expanded");
            Reject(Nodes + "proxy-groups: [{name: X, type: select, proxies: [Y]}, {name: Y, type: select, proxies: [X]}]");
            Reject(Nodes + "proxy-groups: [{name: X, type: select, proxies: [missing]}]");
            Reject(Nodes + "proxy-groups: [{name: A, type: select, proxies: [B]}]");
            Reject(Nodes + "proxy-groups: [{name: X, type: select, use: [remote]}]");
            Reject(Nodes + "proxy-groups: [{name: X, type: select, proxies: []}]");
            Reject(Nodes + "proxy-groups: [{name: X, type: relay, proxies: [A]}]");
            Reject(Nodes + "proxy-groups: [{name: X, type: select, proxies: [A], default-selected: B}]");
            Reject(Nodes + "proxy-groups: [{name: X, type: select, proxies: [GLOBAL]}]");
            Reject("proxies: &loop [*loop]");
            Reject("proxies: []\nproxies: []");
            Reject(Nodes + "---\nproxies: []\n");
            Reject("!!map {proxies: []}");
            Reject("huge: &huge '" + new string('x', 100000) + "'\nproxies: [" + String.Join(",", Enumerable.Repeat("*huge", 110)) + "]");
            Reject("proxies: [{name: A, type: socks5, server: 127.0.0.1, port: 9, dialer-proxy: A}]");
            Check(ImportedTopology.Parse("socks5://127.0.0.1:9#fixture") == null, "URI treated as full config");
            string source = Path.Combine(scratch, "strategies.yaml"); File.WriteAllText(source, Nodes + Groups);
            using (var profile = LocalFileImport.Prepare(Path.Combine(scratch, "strategy-user"), source, 27890, delegate { return 2; }))
            { Check(profile.GroupCount == 3 && profile.EntryGroup == "Codex Proxy", "import group metadata lost"); }
            Failover();
            Console.WriteLine("PASS strategies: nodes/groups/references/anchors, graph rejection, scoped config, nested failover, cooldown, direct exclusion and stale routes");
        }
        private sealed class Fake : IControllerClient
        {
            internal string Current = "A", RootCurrent = "Nested", NestedType = "Selector";
            internal int Delay = 0, Switches, Probes, Measurements;
            internal bool Available = true, Race, CleanupFails;
            public Dictionary<string, object> Read(string path)
            {
                if (path == "/proxies") return Json("{\"proxies\":{\"Entry\":{\"type\":\"Selector\",\"now\":\""+RootCurrent+"\",\"all\":[\"Nested\",\"A\"]},"+
                    "\"Nested\":{\"type\":\""+NestedType+"\",\"now\":\""+Current+"\",\"all\":[\"A\",\"B\",\"DIRECT\",\"DirectGroup\"]},"+
                    "\"DirectGroup\":{\"type\":\"Selector\",\"now\":\"DIRECT\",\"all\":[\"DIRECT\"]},\"A\":{\"type\":\"Socks5\"},\"B\":{\"type\":\"Socks5\"},\"DIRECT\":{\"type\":\"Direct\"}}}");
                if (path.StartsWith("/group/")) { Measurements++; if(Race) RootCurrent="A"; return Json(Available ? "{\"B\":20,\"DIRECT\":1,\"DirectGroup\":1}" : "{}"); }
                Probes++; return Json("{\"delay\":"+Delay+"}");
            }
            public void Put(string path, string body, int timeout) { Check(path == "/proxies/Nested", "changed entry instead of nested group"); Current=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(body)["name"];Switches++; }
            public void Delete(string path) { if(CleanupFails) throw new IOException(); }
            public void Patch(string path, string body) { }
        }
        private static Dictionary<string, object> Json(string value) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(value); }
        private static void Failover()
        {
            var fake = new Fake(); var service = new NodeControlService(fake, fake, "Entry");
            Check(service.AutomaticFastest(new[] { new NodeInfo { Name="DirectGroup", Delay=1 }, new NodeInfo { Name="B", Delay=20 } }).Name=="B", "startup selection chose indirect DIRECT");
            DateTime now = new DateTime(2026, 1, 1); service.Clock = delegate { return now; };
            Func<bool> valid = delegate { return true; };
            Check(!service.TryAutoFailover(valid) && !service.TryAutoFailover(valid) && fake.Switches==0, "threshold bypassed");
            fake.CleanupFails=true;
            Check(service.TryAutoFailover(valid) && fake.Current=="B" && fake.RootCurrent=="Nested" && fake.Switches==1, "nested failover failed");
            int probes=fake.Probes;
            for(int i=0;i<4;i++) Check(!service.TryAutoFailover(valid), "cooldown bypassed");
            Check(fake.Probes==probes,"cooldown did network work");
            now=now.AddSeconds(121); fake.Current="A"; fake.Available=false;
            for(int i=0;i<3;i++) Check(!service.TryAutoFailover(valid), "failed nodes selected");
            Check(fake.Current=="A" && fake.Switches==1,"all failed changed route");
            foreach(string type in new[]{"Fallback","URLTest","LoadBalance"}) {
                fake.NestedType=type; now=now.AddSeconds(121); probes=fake.Probes;
                for(int i=0;i<4;i++) Check(!service.TryAutoFailover(valid),"automatic group overridden");
                Check(fake.Probes==probes,"automatic group probed by manager");
            }
            fake.NestedType="Selector";fake.Available=true;fake.Race=true;now=now.AddSeconds(121);
            for(int i=0;i<3;i++) Check(!service.TryAutoFailover(valid),"stale route modified");
            Check(fake.Switches==1,"stale write");
            Check(!service.TryAutoFailover(delegate{return false;}),"cancel ignored");
            bool rejected=false;try{service.Switch("Nested","unknown");}catch(InvalidDataException){rejected=true;}
            Check(rejected,"unknown switch accepted");
            using(var form=new StrategyGroupsForm(service)) {
                Check(form.Text.Contains("引用"),"group UI missing");
            }
        }

        // Real Mihomo + two loopback HTTP proxy fixtures; no public network or user nodes.
        internal static void RunCore(string scratch, string appRoot)
        {
            string dir = Path.Combine(scratch, "strategy-core"); Directory.CreateDirectory(dir);
            using (var first = new HttpProxyFixture())
            using (var second = new HttpProxyFixture())
            {
                int controllerPort;
                var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
                try { controllerPort = ((IPEndPoint)reserve.LocalEndpoint).Port; } finally { reserve.Stop(); }
                string secret = Guid.NewGuid().ToString("N");
                string source = "proxies:\n  - {name: A, type: http, server: 127.0.0.1, port: " + first.Port + "}\n" +
                    "  - {name: B, type: http, server: 127.0.0.1, port: " + second.Port + "}\n" +
                    "proxy-groups:\n  - {name: Main, type: select, proxies: [Backup]}\n" +
                    "  - {name: Backup, type: fallback, proxies: [A, B], url: 'http://fixture.invalid/health', interval: 1, timeout: 500, lazy: false}\n";
                var topology = ImportedTopology.Parse(source);
                string config = topology.BuildConfig(0, false).Replace("external-controller: \"\"", "external-controller: 127.0.0.1:" + controllerPort)
                    .Split(new[] { "rules:\n" }, StringSplitOptions.None)[0] + "rules: ['MATCH,REJECT']\nsecret: " + secret + "\n";
                string path = Path.Combine(dir, "config.yaml"); File.WriteAllText(path, config);
                var info = new ProcessStartInfo(Path.Combine(appRoot, "runtime", "mihomo.exe"), "-d \""+dir+"\" -f \""+path+"\"")
                { UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden, RedirectStandardOutput=true, RedirectStandardError=true };
                foreach (string key in info.EnvironmentVariables.Keys.Cast<string>().ToList()) if(key.StartsWith("CLASH_",StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
                using(var core=new Process { StartInfo=info })
                {
                    var output = new StringBuilder();
                    core.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(output){if(output.Length<4096)output.AppendLine(e.Data);}};
                    core.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(output){if(output.Length<4096)output.AppendLine(e.Data);}};
                    core.Start();core.BeginOutputReadLine();core.BeginErrorReadLine();
                    try
                    {
                        var api=new ControllerClient("http://127.0.0.1:"+controllerPort,secret);
                        Func<string,bool> chosen=delegate(string expected) { try { return new StrategyGraph(api.Read("/proxies")).Group("Backup").Current==expected; }catch{return false;} };
                        Check(SpinWait.SpinUntil(delegate { return first.Requests>0 && second.Requests>0 && chosen("A"); },10000),
                            "real fallback did not start on primary (exited="+core.HasExited+", first="+first.Requests+", second="+second.Requests+"). Fixture output: "+output.ToString());
                        first.Healthy=false;
                        Check(SpinWait.SpinUntil(delegate { return chosen("B"); },10000),"real fallback did not move to secondary");
                        var graph=new StrategyGraph(api.Read("/proxies"));
                        Check(graph.Group("Main").Current=="Backup" && graph.Group(topology.Entry).Current=="Main", "nested references changed during failover");
                        first.Healthy=true;
                        Check(SpinWait.SpinUntil(delegate { return chosen("A"); },10000),"ordered fallback did not recover primary");
                        Console.WriteLine("PASS real core fallback: primary failure -> backup -> recovery, nested group references preserved, loopback-only fixtures");
                    }
                    finally { if(!core.HasExited)core.Kill();core.WaitForExit(3000); }
                }
            }
        }
        private sealed class HttpProxyFixture : IDisposable
        {
            private readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);
            private readonly Task worker;
            private volatile bool stopped;
            internal volatile bool Healthy=true;
            internal int Requests;
            internal int Port { get {return ((IPEndPoint)listener.LocalEndpoint).Port;} }
            internal HttpProxyFixture()
            {
                listener.Start();worker=Task.Run(delegate {
                    while(!stopped)try { using(var client=listener.AcceptTcpClient()) {
                        client.ReceiveTimeout=client.SendTimeout=1000;
                        using(var stream=client.GetStream()) {
                            string header=ReadHeader(stream);
                            if(Healthy && header.StartsWith("CONNECT ",StringComparison.Ordinal)) {
                                byte[] connected=Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                                stream.Write(connected,0,connected.Length);ReadHeader(stream);
                            }
                            Interlocked.Increment(ref Requests);
                            if(Healthy) {byte[] bytes=Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");stream.Write(bytes,0,bytes.Length);}
                        }
                    }}catch {if(stopped)break;}
                });
            }
            private static string ReadHeader(Stream stream)
            {
                var header=new StringBuilder();int tail=0;
                while(header.Length<16384) {int b=stream.ReadByte();if(b<0)break;header.Append((char)b);tail=(tail<<8)|b;if(tail==0x0d0a0d0a)break;}
                return header.ToString();
            }
            public void Dispose(){stopped=true;listener.Stop();worker.Wait(2000);}
        }
    }
}
