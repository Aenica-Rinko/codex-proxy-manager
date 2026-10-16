// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CodexProxyManager
{
    internal interface IControllerClient
    {
        Dictionary<string, object> Read(string path);
        void Put(string path, string body, int timeout);
        void Delete(string path);
        void Patch(string path, string body);
    }

    // One immutable endpoint/secret pair per profile. Never borrow the system proxy.
    internal sealed class ControllerClient : IControllerClient
    {
        private readonly string url, secret;
        internal ControllerClient(string url, string secret)
        {
            this.url = StartupConfiguration.NormalizeControllerUrl(url);
            if (String.IsNullOrEmpty(this.url)) throw new InvalidDataException("控制接口地址不能为空。");
            this.secret = secret ?? "";
        }
        public Dictionary<string, object> Read(string path)
        { return Diagnostics.ReadApi(url, secret, path, path.Contains("/delay?") ? 15000 : 3500); }
        public void Put(string path, string body, int timeout)
        { LocalControllerTransport.Send(url, secret, path, "PUT", body, timeout); }
        public void Delete(string path)
        { LocalControllerTransport.Send(url, secret, path, "DELETE", "", 5000); }
        public void Patch(string path, string body)
        { LocalControllerTransport.Send(url, secret, path, "PATCH", body, 2000); }
    }

    // Protocol/API behavior and node policy, with no controls, process lifecycle or UI state.
    internal sealed class NodeHealthPolicy
    {
        internal const int FailoverThreshold = 3;
        private readonly object gate = new object();
        private string node;
        private int failures;
        internal int Record(string checkedNode, bool healthy)
        {
            lock (gate)
            {
                if (node != checkedNode) { node = checkedNode; failures = 0; }
                failures = healthy ? 0 : failures + 1;
                return failures;
            }
        }
        internal void Reset() { lock (gate) { node = null; failures = 0; } }
    }

    internal sealed class NodeControlService
    {
        private readonly IControllerClient core, node;
        private readonly string preferredProxyGroupName;
        internal readonly NodeHealthPolicy Health = new NodeHealthPolicy();
        private DateTime nextFailoverAttempt = DateTime.MinValue;
        internal Func<DateTime> Clock = delegate { return DateTime.UtcNow; };
        internal NodeControlService(IControllerClient core, IControllerClient node, string preferredGroup)
        {
            if (core == null || node == null) throw new ArgumentNullException();
            this.core = core; this.node = node; preferredProxyGroupName = preferredGroup;
        }

        internal TrafficSnapshot ReadTraffic()
        {
            Dictionary<string, object> root = core.Read("/connections");
            TrafficSnapshot snapshot = new TrafficSnapshot();
            snapshot.DownloadTotal = ToInt64(GetValue(root, "downloadTotal"));
            snapshot.UploadTotal = ToInt64(GetValue(root, "uploadTotal"));
            object connections = GetValue(root, "connections");
            IList list = connections as IList;
            snapshot.Connections = list == null ? 0 : list.Count;
            return snapshot;
        }

        internal ProxyCatalogSnapshot ReadCatalog()
        {
            Dictionary<string, object> root = node.Read("/proxies");
            Dictionary<string, object> proxies = GetValue(root, "proxies") as Dictionary<string, object>;
            if (proxies == null) throw new InvalidDataException("节点接口返回内容无效。");

            Dictionary<string, object> selector = null;
            string selectorName = null;
            int selectorScore = -1;
            foreach (KeyValuePair<string, object> pair in proxies)
            {
                Dictionary<string, object> candidate = pair.Value as Dictionary<string, object>;
                if (candidate == null || !String.Equals(Convert.ToString(GetValue(candidate, "type")), "Selector", StringComparison.OrdinalIgnoreCase))
                    continue;
                List<string> all = ToStringList(GetValue(candidate, "all"));
                bool isGlobal = all.Any(delegate(string name) { return String.Equals(name, "REJECT", StringComparison.OrdinalIgnoreCase); });
                int leafNodeCount = 0;
                foreach (string memberName in all)
                {
                    if (String.Equals(memberName, "DIRECT", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(memberName, "REJECT", StringComparison.OrdinalIgnoreCase)) continue;
                    object memberValue;
                    Dictionary<string, object> member = proxies.TryGetValue(memberName, out memberValue)
                        ? memberValue as Dictionary<string, object> : null;
                    string memberType = Convert.ToString(GetValue(member, "type"));
                    if (!String.Equals(memberType, "Selector", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(memberType, "URLTest", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(memberType, "Fallback", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(memberType, "LoadBalance", StringComparison.OrdinalIgnoreCase))
                        leafNodeCount++;
                }
                if (!String.IsNullOrWhiteSpace(preferredProxyGroupName) &&
                    String.Equals(pair.Key, preferredProxyGroupName, StringComparison.Ordinal))
                {
                    selector = candidate;
                    selectorName = pair.Key;
                    break;
                }
                if (!isGlobal && leafNodeCount > selectorScore)
                {
                    selector = candidate;
                    selectorName = pair.Key;
                    selectorScore = leafNodeCount;
                }
            }
            if (selector == null) throw new InvalidDataException("找不到可切换的策略组。");
            if (!String.IsNullOrWhiteSpace(preferredProxyGroupName) &&
                !String.Equals(selectorName, preferredProxyGroupName, StringComparison.Ordinal))
                throw new InvalidDataException("配置的策略组不存在；请检查档案，不会自动控制其他策略组。");
            List<NodeInfo> nodes = new List<NodeInfo>();
            foreach (string name in ToStringList(GetValue(selector, "all")))
            {
                if (String.Equals(name, "DIRECT", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, "REJECT", StringComparison.OrdinalIgnoreCase)) continue;
                Dictionary<string, object> detail = null;
                object value;
                if (proxies.TryGetValue(name, out value)) detail = value as Dictionary<string, object>;
                if (detail == null) continue;
                nodes.Add(new NodeInfo
                {
                    Name = name,
                    Type = Convert.ToString(GetValue(detail, "type")),
                    Delay = GetLatestDelay(detail)
                });
            }
            return new ProxyCatalogSnapshot { Group = selectorName, Current = Convert.ToString(GetValue(selector, "now")), Nodes = nodes };
        }


        internal List<NodeInfo> MeasureDelays(string group, IEnumerable<NodeInfo> nodes)
        {
            if (String.IsNullOrWhiteSpace(group)) throw new InvalidOperationException("找不到可测速的代理策略组。");
            var delays = node.Read("/group/" + Uri.EscapeDataString(group) +
                "/delay?url=https%3A%2F%2Fwww.gstatic.com%2Fgenerate_204&timeout=10000");
            return nodes.Select(delegate(NodeInfo entry)
            {
                object delay;
                return new NodeInfo { Name = entry.Name, Type = entry.Type,
                    Delay = delays.TryGetValue(entry.Name, out delay) ? Convert.ToInt32(delay) : 0 };
            }).ToList();
        }

        internal static NodeInfo Fastest(IEnumerable<NodeInfo> nodes)
        { return nodes.Where(n => n.Delay > 0).OrderBy(n => n.Delay).FirstOrDefault(); }

        internal NodeInfo AutomaticFastest(IEnumerable<NodeInfo> nodes)
        {
            var graph = new StrategyGraph(node.Read("/proxies"));
            return Fastest(nodes.Where(n => graph.ProxyOnly(n.Name)));
        }

        internal void Switch(string group, string target)
        {
            if (String.IsNullOrWhiteSpace(group) || String.IsNullOrWhiteSpace(target)) throw new InvalidDataException();
            var selected = new StrategyGraph(node.Read("/proxies")).Group(group);
            if (selected.Type != "Selector" || !selected.Members.Contains(target))
                throw new InvalidDataException("只能切换选择组中的现有成员。自动策略组由核心管理。");
            node.Put("/proxies/" + Uri.EscapeDataString(group),
                new JavaScriptSerializer().Serialize(new Dictionary<string, string> { { "name", target } }), 5000);
            // Reset only this manager's core connections, never the external client's connections.
            // Selection already succeeded. A connection cleanup failure must not report
            // the old selection as active or cause repeated selection writes.
            try { core.Delete("/connections"); } catch { }
        }

        internal List<StrategyGroupInfo> ReadGroups() { return new StrategyGraph(node.Read("/proxies")).Groups(); }

        internal bool TryAutoFailover(Func<bool> stillCurrent)
        {
            if (!stillCurrent() || Clock() < nextFailoverAttempt) return false;
            var graph = new StrategyGraph(node.Read("/proxies"));
            string entry = String.IsNullOrWhiteSpace(preferredProxyGroupName) ? ReadCatalog().Group : preferredProxyGroupName;
            string route;
            var group = graph.FailoverGroup(entry, out route);
            if (group == null) { Health.Reset(); return false; }
            bool healthy;
            try { healthy = ProbeNode(group.Current) > 0; } catch { healthy = false; }
            if (!stillCurrent()) return false;
            if (Health.Record(route, healthy) < NodeHealthPolicy.FailoverThreshold || healthy) return false;
            // Debounce both successful switches and all-nodes-failed retries.
            nextFailoverAttempt = Clock().AddSeconds(120);
            Health.Reset();
            var candidates = group.Members.Where(n => n != group.Current && graph.ProxyOnly(n))
                .Select(n => new NodeInfo { Name = n, Type = graph.TypeOf(n) }).ToList();
            if (candidates.Count == 0) return false;
            var best = Fastest(MeasureDelays(group.Name, candidates));
            if (best == null || !stillCurrent()) return false;
            string latestRoute;
            var latestGraph = new StrategyGraph(node.Read("/proxies"));
            var latest = latestGraph.FailoverGroup(entry, out latestRoute);
            if (latest == null || latestRoute != route || !latest.Members.Contains(best.Name) || !latestGraph.ProxyOnly(best.Name) || !stillCurrent()) return false;
            Switch(group.Name, best.Name);
            return true;
        }

        internal int RefreshProviders()
        {
            var providers = GetValue(node.Read("/providers/proxies"), "providers") as Dictionary<string, object>;
            int updated = 0;
            if (providers != null)
                foreach (var entry in providers)
                    if (String.Equals(Convert.ToString(GetValue(entry.Value as Dictionary<string, object>, "vehicleType")),
                        "HTTP", StringComparison.OrdinalIgnoreCase))
                    { node.Put("/providers/proxies/" + Uri.EscapeDataString(entry.Key), "", 15000); updated++; }
            return updated;
        }

        internal int ProbeNode(string name)
        {
            var result = node.Read("/proxies/" + Uri.EscapeDataString(name) +
                "/delay?url=https%3A%2F%2Fwww.gstatic.com%2Fgenerate_204&timeout=5000");
            return Convert.ToInt32(GetValue(result, "delay") ?? 0);
        }

        private static object GetValue(Dictionary<string, object> values, string name)
        {
            object value;
            return values != null && values.TryGetValue(name, out value) ? value : null;
        }

        private static long ToInt64(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt64(value); }
            catch { return 0; }
        }

        private static List<string> ToStringList(object value)
        {
            List<string> result = new List<string>();
            IList list = value as IList;
            if (list == null) return result;
            foreach (object item in list) result.Add(Convert.ToString(item));
            return result;
        }

        private static int GetLatestDelay(Dictionary<string, object> detail)
        {
            IList history = GetValue(detail, "history") as IList;
            if (history == null || history.Count == 0) return 0;
            Dictionary<string, object> latest = history[history.Count - 1] as Dictionary<string, object>;
            return latest == null ? 0 : Convert.ToInt32(GetValue(latest, "delay") ?? 0);
        }

    }
}
