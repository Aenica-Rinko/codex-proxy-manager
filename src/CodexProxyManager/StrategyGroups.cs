// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodexProxyManager
{
    internal sealed class StrategyGroupInfo
    {
        internal string Name, Type, Current;
        internal List<string> Members;
        public override string ToString() { return Name + " · " + Type; }
    }

    internal sealed class StrategyGraph
    {
        private readonly Dictionary<string, object> proxies;
        internal StrategyGraph(Dictionary<string, object> response)
        {
            object value;
            proxies = response != null && response.TryGetValue("proxies", out value) ? value as Dictionary<string, object> : null;
            if (proxies == null) throw new InvalidDataException("策略接口返回内容无效。");
        }
        internal static bool IsGroup(string type)
        { return new[] { "Selector", "Fallback", "URLTest", "LoadBalance", "Relay" }.Contains(type, StringComparer.OrdinalIgnoreCase); }
        internal string TypeOf(string name) { return Convert.ToString(Field(name, "type")); }
        internal List<StrategyGroupInfo> Groups()
        {
            return proxies.Keys.Where(n => n != "GLOBAL" && IsGroup(TypeOf(n))).Select(Group).ToList();
        }
        internal StrategyGroupInfo Group(string name)
        {
            if (!IsGroup(TypeOf(name))) throw new InvalidDataException("策略组不存在或类型不受支持。");
            var members = Field(name, "all") as IList;
            if (members == null) throw new InvalidDataException("策略组成员不可读取。");
            return new StrategyGroupInfo { Name = name, Type = TypeOf(name), Current = Convert.ToString(Field(name, "now")),
                Members = members.Cast<object>().Select(Convert.ToString).ToList() };
        }
        private object Field(string name, string key)
        {
            object value, field;
            var item = name != null && proxies.TryGetValue(name, out value) ? value as Dictionary<string, object> : null;
            return item != null && item.TryGetValue(key, out field) ? field : null;
        }
        // Automatic choices never introduce DIRECT, PASS or a reject/unknown route,
        // including through nested groups. Manual choices remain available in the UI.
        internal bool ProxyOnly(string name) { return ProxyOnly(name, new HashSet<string>(StringComparer.Ordinal), new Dictionary<string,bool>(StringComparer.Ordinal), 0); }
        private bool ProxyOnly(string name, HashSet<string> visited, Dictionary<string,bool> memo, int depth)
        {
            bool cached;
            if (memo.TryGetValue(name, out cached)) return cached;
            if (depth > 48 || !visited.Add(name)) return false;
            try
            {
                string type = TypeOf(name);
                if (String.IsNullOrEmpty(type) || new[] { "Direct", "Reject", "RejectDrop", "Pass", "Compatible" }.Contains(type, StringComparer.OrdinalIgnoreCase)) return false;
                if (!IsGroup(type)) return true;
                var group = Group(name);
                bool result = group.Members.Count > 0 && group.Members.All(n => ProxyOnly(n, visited, memo, depth + 1));
                memo[name] = result;
                return result;
            }
            finally { visited.Remove(name); }
        }
        // Follow selector references only. Automatic groups own their own decisions.
        internal StrategyGroupInfo FailoverGroup(string entry, out string route)
        {
            var path = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            StrategyGroupInfo current = null;
            string name = entry;
            for (int i = 0; i < 48; i++)
            {
                if (!visited.Add(name)) break;
                path.Add(name);
                string type = TypeOf(name);
                if (!IsGroup(type))
                {
                    route = String.Join("\0", path);
                    return ProxyOnly(name) ? current : null;
                }
                if (!String.Equals(type, "Selector", StringComparison.OrdinalIgnoreCase)) break;
                current = Group(name);
                if (!current.Members.Contains(current.Current)) break;
                name = current.Current;
            }
            route = String.Join("\0", path);
            return null;
        }
    }
}
