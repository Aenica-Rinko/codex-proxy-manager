// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace CodexProxyManager
{
    // Parse data, not executable configuration. Only nodes and a closed policy graph
    // cross the import boundary. Never deserialize CLR types from YAML tags.
    internal sealed class ImportedTopology
    {
        internal YamlSequenceNode Nodes, Groups;
        internal string Entry;
        internal string IgnoredFields;
        internal int GroupCount { get { return Groups.Children.Count; } }
        private static readonly HashSet<string> Builtins = new HashSet<string>(
            new[] { "DIRECT", "REJECT", "REJECT-DROP", "PASS", "COMPATIBLE", "GLOBAL" }, StringComparer.Ordinal);
        private static readonly HashSet<string> GroupFields = new HashSet<string>(new[] {
            "name", "type", "proxies", "url", "interval", "lazy", "timeout", "max-failed-times",
            "tolerance", "strategy", "disable-udp", "include-all-proxies", "filter", "exclude-filter",
            "exclude-type", "expected-status", "hidden", "icon", "default-selected", "empty-fallback"
        }, StringComparer.Ordinal);

        internal static ImportedTopology Parse(string text)
        {
            try
            {
                // Bound parsing before constructing the representation graph.
                var parser = new Parser(new StringReader(text));
                int depth = 0, events = 0;
                while (parser.MoveNext())
                {
                    if (++events > 100000) throw new InvalidDataException();
                    if (parser.Current is SequenceStart || parser.Current is MappingStart)
                        if (++depth > 48) throw new InvalidDataException();
                    if (parser.Current is SequenceEnd || parser.Current is MappingEnd) depth--;
                    var node = parser.Current as NodeEvent;
                    if (node != null && !node.Tag.IsEmpty) throw new InvalidDataException("不支持显式 YAML 标签。");
                }
                var stream = new YamlStream();
                stream.Load(new StringReader(text));
                if (stream.Documents.Count != 1) throw new InvalidDataException("请选择只有一个 YAML 文档的档案。");
                if (stream.Documents[0].RootNode is YamlScalarNode) return null; // URI/Base64: core provider parser.
                int budget = LocalFileImport.MaximumBytes;
                var root = Expand(stream.Documents[0].RootNode, new HashSet<YamlNode>(ReferenceComparer.Instance), ref budget, 0) as YamlMappingNode;
                if (root == null) throw new InvalidDataException("YAML 顶层必须是映射。");
                var result = new ImportedTopology { Nodes = Get(root, "proxies") as YamlSequenceNode,
                    Groups = Get(root, "proxy-groups") as YamlSequenceNode ?? new YamlSequenceNode() };
                var knownGlobals = new HashSet<string>(new[] { "rules", "rule-providers", "proxy-providers", "dns", "tun",
                    "mixed-port", "port", "socks-port", "external-controller", "secret", "allow-lan", "mode", "log-level",
                    "ipv6", "profile", "hosts", "sniffer", "listeners", "routing-mark", "interface-name" }, StringComparer.Ordinal);
                result.IgnoredFields = String.Join("、", root.Children.Keys.Select(Text)
                    .Where(k => k != "proxies" && k != "proxy-groups")
                    .Select(k => knownGlobals.Contains(k) ? k : "其他顶层字段").Distinct());
                if (result.Nodes == null || result.Nodes.Children.Count == 0)
                    throw new InvalidDataException("档案没有内联节点。请先将远程或外部文件 provider 展开为 proxies。");
                if (result.Nodes.Children.Count > 5000 || result.GroupCount > 256)
                    throw new InvalidDataException("最多支持 5000 个节点和 256 个策略组。");
                var definitions = new Dictionary<string, YamlMappingNode>(StringComparer.Ordinal);
                var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (YamlNode node in result.Nodes.Children.Concat(result.Groups.Children))
                {
                    var map = node as YamlMappingNode;
                    string name = Text(Get(map, "name"));
                    if (String.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(Char.IsControl) ||
                        Builtins.Contains(name) || definitions.ContainsKey(name))
                        throw new InvalidDataException("节点或策略组名称为空、重复、过长或占用了内置名称。");
                    definitions.Add(name, map); edges.Add(name, new List<string>());
                }
                foreach (YamlMappingNode node in result.Nodes.Children)
                {
                    string dialer = Text(Get(node, "dialer-proxy"));
                    if (!String.IsNullOrEmpty(dialer)) edges[Text(Get(node, "name"))].Add(dialer);
                    // A portable snapshot must not silently depend on another local file.
                    foreach (string key in new[] { "certificate-path", "private-key-path", "ca-file", "ca-path" })
                        if (Get(node, key) != null)
                            throw new InvalidDataException("节点引用了外部证书或密钥文件；请使用完整的内联配置。");
                }
                foreach (YamlMappingNode group in result.Groups.Children)
                {
                    string type = Text(Get(group, "type"));
                    if (!new[] { "select", "fallback", "url-test", "load-balance" }.Contains(type))
                        throw new InvalidDataException("策略组类型暂不支持；支持 select、fallback、url-test 和 load-balance。");
                    var use = Get(group, "use") as YamlSequenceNode;
                    if ((Get(group, "use") != null && (use == null || use.Children.Count > 0)) || True(group, "include-all-providers"))
                        throw new InvalidDataException("策略组依赖外部 provider。请先展开为内联节点，避免悄悄丢失引用。");
                    if (True(group, "include-all"))
                    {
                        if (Get(root, "proxy-providers") != null) throw new InvalidDataException("include-all 引用了外部 provider，请先展开。");
                        Set(group, "include-all-proxies", new YamlScalarNode("true"));
                    }
                    foreach (YamlNode key in group.Children.Keys.ToList())
                    {
                        string field = Text(key);
                        if (field == "use" || field == "include-all" || field == "include-all-providers") group.Children.Remove(key);
                        else if (!GroupFields.Contains(field)) throw new InvalidDataException("策略组包含不支持的字段；导入已停止，没有静默丢弃组设置。");
                    }
                    var members = Get(group, "proxies") as YamlSequenceNode;
                    if (Get(group, "proxies") != null && members == null) throw new InvalidDataException("策略组 proxies 必须是列表。");
                    var refs = edges[Text(Get(group, "name"))];
                    if (members != null) refs.AddRange(members.Children.Select(Text));
                    if (True(group, "include-all-proxies")) refs.AddRange(result.Nodes.Children.Cast<YamlMappingNode>().Select(n => Text(Get(n, "name"))));
                    if (refs.Count == 0) throw new InvalidDataException("策略组不能为空。");
                    string selected = Text(Get(group, "default-selected"));
                    if (selected.Length > 0 && !refs.Contains(selected)) throw new InvalidDataException("默认选择不属于策略组成员。");
                    string empty = Text(Get(group, "empty-fallback"));
                    if (empty.Length > 0) refs.Add(empty);
                    if (type != "select")
                    {
                        if (Get(group, "url") == null) Set(group, "url", new YamlScalarNode("https://www.gstatic.com/generate_204"));
                        if (Get(group, "interval") == null) Set(group, "interval", new YamlScalarNode("300"));
                        if (Get(group, "timeout") == null) Set(group, "timeout", new YamlScalarNode("5000"));
                    }
                }
                var done = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in edges.Keys) Visit(name, edges, done, new HashSet<string>(StringComparer.Ordinal), 0);
                result.Entry = "Codex Proxy";
                while (definitions.ContainsKey(result.Entry)) result.Entry += " (CPM)";
                return result;
            }
            catch (InvalidDataException) { throw; }
            catch { throw new InvalidDataException("YAML 无效或超出安全解析限制；请检查格式、重复键和引用。内容未写入日志。"); }
        }

        private static void Visit(string name, Dictionary<string, List<string>> edges, HashSet<string> done, HashSet<string> path, int depth)
        {
            if (Builtins.Contains(name)) { if (name == "GLOBAL") throw new InvalidDataException("不能引用 GLOBAL 隐式组。"); return; }
            if (!edges.ContainsKey(name)) throw new InvalidDataException("策略组或拨号链存在找不到的节点／组引用。");
            if (done.Contains(name)) return;
            if (depth > 48 || !path.Add(name)) throw new InvalidDataException("策略组或拨号链存在循环引用，或嵌套超过 48 层。");
            foreach (string target in edges[name]) Visit(target, edges, done, path, depth + 1);
            path.Remove(name); done.Add(name);
        }

        internal string BuildConfig(int port, bool validation)
        {
            int budget = LocalFileImport.MaximumBytes;
            var groups = (YamlSequenceNode)Expand(Groups, new HashSet<YamlNode>(ReferenceComparer.Instance), ref budget, 0);
            if (validation) foreach (YamlMappingNode group in groups.Children)
            { Set(group, "url", new YamlScalarNode("")); Set(group, "interval", new YamlScalarNode("0")); Set(group, "lazy", new YamlScalarNode("true")); }
            var choices = new YamlSequenceNode();
            // Source order is retained; first original group is the initial route.
            foreach (YamlMappingNode map in Groups.Children.Concat(Nodes.Children)) choices.Add(new YamlScalarNode(Text(Get(map, "name"))));
            groups.Add(new YamlMappingNode { { "name", Entry }, { "type", "select" }, { "proxies", choices } });
            var root = new YamlMappingNode { { "proxies", Nodes }, { "proxy-groups", groups } };
            var yaml = new StringBuilder("mode: rule\nlog-level: warning\nfind-process-mode: strict\nallow-lan: false\nipv6: true\nmixed-port: " + port + "\nexternal-controller: \"\"\ndns:\n  enable: false\ntun:\n  enable: false\n");
            yaml.Append(Serialize(root));
            if (validation) yaml.AppendLine("rules: ['MATCH,REJECT']");
            else ProviderConfigFactory.AppendCodexRules(yaml, Entry);
            return yaml.ToString().Replace("\r\n", "\n");
        }

        internal string InitialRoute
        { get { return Text(Get((YamlMappingNode)Groups.Children.Concat(Nodes.Children).First(), "name")); } }

        internal IEnumerable<string[]> DescribeGroups()
        {
            foreach (YamlMappingNode group in Groups.Children)
            {
                var members = Get(group, "proxies") as YamlSequenceNode;
                string refs = members == null ? "" : String.Join("、", members.Children.Take(20).Select(Text));
                if (members != null && members.Children.Count > 20) refs += " …（其余引用仍保留）";
                if (True(group, "include-all-proxies")) refs += " [包含所有内联节点；遵循原筛选条件]";
                yield return new[] { Text(Get(group, "name")), Text(Get(group, "type")), refs };
            }
        }

        internal string Snapshot()
        { return Serialize(new YamlMappingNode { { "proxies", Nodes }, { "proxy-groups", Groups } }); }

        private static string Serialize(YamlNode root)
        {
            var writer = new StringWriter();
            new YamlStream(new YamlDocument(root)).Save(writer, false);
            // The serialized mapping is embedded after manager-owned global fields.
            string text = writer.ToString().Replace("\r\n", "\n");
            if (text.EndsWith("...\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 4);
            return text;
        }

        private static YamlNode Expand(YamlNode node, HashSet<YamlNode> path, ref int budget, int depth)
        {
            budget -= 32;
            if (budget < 0 || depth > 48 || !path.Add(node)) throw new InvalidDataException("YAML 别名存在循环或展开超出限制。");
            try
            {
                var scalar = node as YamlScalarNode;
                if (scalar != null)
                {
                    budget -= Encoding.UTF8.GetByteCount(scalar.Value ?? "");
                    if (budget < 0) throw new InvalidDataException("YAML 别名展开后的内容超过 10 MiB 预算。");
                    return new YamlScalarNode(scalar.Value) { Style = scalar.Style };
                }
                var seq = node as YamlSequenceNode;
                if (seq != null) { var copy = new YamlSequenceNode(); foreach (var child in seq.Children) copy.Add(Expand(child, path, ref budget, depth + 1)); return copy; }
                var map = node as YamlMappingNode;
                if (map == null) throw new InvalidDataException();
                var result = new YamlMappingNode();
                // YAML merge keys: explicit values win, earlier merge maps win.
                var merge = Get(map, "<<");
                if (merge != null)
                {
                    var expanded = Expand(merge, path, ref budget, depth + 1);
                    var maps = expanded as YamlSequenceNode;
                    foreach (var item in maps == null ? new[] { expanded } : maps.Children.ToArray())
                    {
                        var inherited = item as YamlMappingNode;
                        if (inherited == null) throw new InvalidDataException("YAML 合并键必须引用映射。");
                        foreach (var entry in inherited.Children) if (!result.Children.ContainsKey(entry.Key)) result.Add(entry.Key, entry.Value);
                    }
                }
                foreach (var entry in map.Children)
                {
                    if (!(entry.Key is YamlScalarNode)) throw new InvalidDataException("映射键必须是文本。");
                    if (Text(entry.Key) == "<<") continue;
                    result.Children[Expand(entry.Key, path, ref budget, depth + 1)] = Expand(entry.Value, path, ref budget, depth + 1);
                }
                return result;
            }
            finally { path.Remove(node); }
        }
        private sealed class ReferenceComparer : IEqualityComparer<YamlNode>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(YamlNode a, YamlNode b) { return Object.ReferenceEquals(a, b); }
            public int GetHashCode(YamlNode node) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node); }
        }
        private static YamlNode Get(YamlMappingNode map, string name)
        { YamlNode value; return map != null && map.Children.TryGetValue(new YamlScalarNode(name), out value) ? value : null; }
        private static string Text(YamlNode node)
        { if (node == null) return ""; var scalar = node as YamlScalarNode; if (scalar == null) throw new InvalidDataException("预期文本字段。"); return scalar.Value ?? ""; }
        private static bool True(YamlMappingNode map, string key) { return Text(Get(map, key)).Equals("true", StringComparison.OrdinalIgnoreCase); }
        private static void Set(YamlMappingNode map, string key, YamlNode value) { map.Children[new YamlScalarNode(key)] = value; }
    }
}
