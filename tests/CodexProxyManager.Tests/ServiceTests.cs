// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexProxyManager
{
    internal static class ServiceTests
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception("Service: " + message); }
        private sealed class FixtureClient : IControllerClient
        {
            internal readonly List<string> Writes = new List<string>(), Deletes = new List<string>();
            internal string LastBody;
            internal Func<string, Dictionary<string, object>> Reader;
            public Dictionary<string, object> Read(string path) { return Reader(path); }
            public void Put(string path, string body, int timeout) { Writes.Add(path); LastBody = body; }
            public void Delete(string path) { Deletes.Add(path); }
            public void Patch(string path, string body) { Writes.Add(path); LastBody = body; }
        }
        private static Dictionary<string, object> Json(string text)
        { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text); }

        internal static void Run(string scratch)
        {
            var core = new FixtureClient { Reader = delegate { return Json("{\"downloadTotal\":42,\"uploadTotal\":7,\"connections\":[{},{}]}"); } };
            var external = new FixtureClient { Reader = delegate(string path)
            {
                if (path == "/proxies") return Json("{\"proxies\":{\"GLOBAL\":{\"type\":\"Selector\",\"all\":[\"fast\",\"slow\",\"REJECT\"]}," +
                    "\"Group /#\":{\"type\":\"Selector\",\"now\":\"fast\",\"all\":[\"fast\",\"slow\",\"DIRECT\"]}," +
                    "\"smaller\":{\"type\":\"Selector\",\"all\":[\"slow\"]},\"fast\":{\"type\":\"Vless\",\"history\":[{\"delay\":55},{\"delay\":25}]}," +
                    "\"slow\":{\"type\":\"Socks5\"}}}");
                if (path.StartsWith("/group/")) return Json("{\"fast\":20,\"slow\":200}");
                if (path == "/providers/proxies") return Json("{\"providers\":{\"remote /#\":{\"vehicleType\":\"HTTP\"},\"local\":{\"vehicleType\":\"File\"}}}");
                return Json("{\"delay\":31}");
            } };
            var service = new NodeControlService(core, external, "Group /#");
            var catalog = service.ReadCatalog();
            Check(catalog.Group == "Group /#" && catalog.Nodes.Count == 2 && catalog.Nodes[0].Delay == 25, "Preferred group/leaf/history parsing changed");
            Check(new NodeControlService(core, external, null).ReadCatalog().Group == "Group /#", "Automatic selector scoring changed");
            bool rejected = false;
            try { new NodeControlService(core, external, "missing").ReadCatalog(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Missing configured group silently controlled another group");
            var measured = service.MeasureDelays(catalog.Group, catalog.Nodes);
            Check(measured[0].Delay == 20 && catalog.Nodes[0].Delay == 25 && !Object.ReferenceEquals(measured[0], catalog.Nodes[0]), "Measurement mutated a shared snapshot");
            Check(NodeControlService.Fastest(measured).Name == "fast" &&
                NodeControlService.Fastest(new[] { new NodeInfo { Name = "unavailable", Delay = -1 } }) == null, "Invalid delay selected");
            service.Switch(catalog.Group, "quoted\"target");
            Check(external.Writes[0] == "/proxies/" + Uri.EscapeDataString(catalog.Group) &&
                new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(external.LastBody)["name"] == "quoted\"target", "Switch did not preserve escaped identities");
            Check(core.Deletes.SequenceEqual(new[] { "/connections" }) && external.Deletes.Count == 0, "External client's connections were reset");
            Check(service.RefreshProviders() == 1 && external.Writes.Last() == "/providers/proxies/" + Uri.EscapeDataString("remote /#"), "File provider refreshed / provider name changed");
            Check(service.ProbeNode("fast") == 31 && service.ReadTraffic().Connections == 2, "Health/traffic contract changed");
            Check(service.Health.Record("fast", false) == 1 && service.Health.Record("fast", false) == 2 &&
                service.Health.Record("fast", false) == NodeHealthPolicy.FailoverThreshold, "Failover threshold changed");
            Check(service.Health.Record("slow", false) == 1 && service.Health.Record("slow", true) == 0, "Health failures crossed node boundary");
            service.Health.Reset(); Check(service.Health.Record("fast", false) == 1, "Health reset failed");

            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var complete = new ManualResetEvent(false))
            {
                try
                {
                    core.Reader = delegate { entered.Set(); release.WaitOne(3000); return Json("{\"connections\":[],\"downloadTotal\":0}"); };
                    var poller = new DashboardPoller(); int callbacks = 0;
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    Check(poller.Begin(port, service, false, delegate(DashboardSnapshot snapshot) { Interlocked.Increment(ref callbacks); complete.Set(); }), "Poll not scheduled");
                    Check(entered.WaitOne(2000) && poller.IsPending, "Poll did not enter controller");
                    Check(!poller.Begin(port, service, false, delegate { Interlocked.Increment(ref callbacks); }), "Overlapping poll accepted");
                    release.Set(); Check(complete.WaitOne(3000), "Poll callback missing");
                    SpinWait.SpinUntil(delegate { return !poller.IsPending; }, 3000);
                    Check(callbacks == 1 && !poller.IsPending, "Poll gate did not reset");
                    complete.Reset(); core.Reader = delegate { throw new IOException("fixture-only-error"); };
                    bool failed = false;
                    poller.Begin(port, service, false, delegate(DashboardSnapshot snapshot) { failed = snapshot.ReadFailed && snapshot.Traffic == null; complete.Set(); });
                    Check(complete.WaitOne(3000) && failed, "Poll error escaped or was presented as traffic success");
                    SpinWait.SpinUntil(delegate { return !poller.IsPending; }, 3000);
                }
                finally { release.Set(); listener.Stop(); }
            }

            string state = Path.Combine(scratch, "unowned-service-state.json");
            var existing = new ProxyState { NetworkMode = "process", CorePid = Int32.MaxValue }; Program.WriteState(state, existing);
            using (var session = new ProxySessionService(new StartupProfile { ProjectRoot = scratch, CoreHome = scratch,
                ConfigPath = Path.Combine(scratch, "missing-profile.yaml"), StatePath = state, ManagerLog = Path.Combine(scratch, "service.log"),
                CoreOutLog = Path.Combine(scratch, "service.out.log"), CoreErrLog = Path.Combine(scratch, "service.err.log"),
                MixedPort = 27899, ControllerPort = 27900, ControllerSecret = "fixture-only-secret" }))
            {
                session.Stop(); session.Stop();
                Check(!session.CoreAlive && RecoveryWatchdog.OwnsSession(Program.ReadState(state), existing), "Idle service erased state it did not create");
            }
            Console.WriteLine("PASS services: selector policy, snapshot isolation, split-controller writes, failover boundary, single-flight polling and session ownership");
        }
    }
}
