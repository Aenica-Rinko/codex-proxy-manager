using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class NodeCatalogTests
    {
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception("Node catalog: " + message);
        }
        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }
        internal static void Run(string scratch)
        {
            var nodes = new List<NodeInfo> {
                new NodeInfo { Name = "Beta 香港", Type = "Vless", Delay = 0 },
                new NodeInfo { Name = "alpha", Type = "Hysteria2", Delay = 90 },
                new NodeInfo { Name = "Alpha", Type = "Socks5", Delay = -1 },
                new NodeInfo { Name = "slow", Type = "Vless", Delay = 300 } };
            var favorites = new HashSet<string>(new[] { "alpha" }, StringComparer.Ordinal);
            Check(NodeCatalog.Select(nodes, " VLESS ", false, favorites, 0).Count == 2, "protocol search");
            Check(NodeCatalog.Select(nodes, "香港", false, favorites, 0).Count == 1, "Unicode search");
            Check(NodeCatalog.Select(nodes, "", true, favorites, 0).Single().Name == "alpha", "exact favorite identity");
            Check(NodeCatalog.Select(nodes, "", false, favorites, 2).Select(n => n.Name).Take(2).SequenceEqual(new[] { "alpha", "slow" }), "unmeasured delays must sort last");
            Check(NodeCatalog.Select(nodes, "", false, favorites, 1).First().Name == "alpha", "stable name sort");
            Check(nodes[0].Name == "Beta 香港" && nodes.Count == 4, "source list mutated");
            string path = Path.Combine(scratch, "node-preferences.json");
            var first = new NodePreferences(path, "profile-a");
            var second = new NodePreferences(path, "profile-b");
            Check(first.Load().Count == 0 && !File.Exists(path), "reading should not create data");
            first.Toggle("alpha");
            second.Toggle("Beta 香港");
            Check(new NodePreferences(path, "profile-a").Load().SetEquals(new[] { "alpha" }), "restart persistence / profile isolation");
            Check(second.Load().SetEquals(new[] { "Beta 香港" }), "second profile persistence");
            first.Toggle("temporarily missing");
            Check(first.Load().Contains("temporarily missing"), "unavailable node favorites must be retained");
            first.Toggle("alpha");
            Check(!first.Load().Contains("alpha"), "unfavorite");
            string valid = File.ReadAllText(path);
            File.WriteAllText(path, "not json");
            bool rejected = false;
            try { first.Toggle("new"); } catch { rejected = true; }
            Check(rejected && File.ReadAllText(path) == "not json", "corrupt preferences must not be overwritten");
            File.WriteAllText(path, valid);

            using (var form = new DashboardForm())
            {
                form.CreateControl();
                var list = Field<ListView>(form, "nodeList");
                IntPtr listHandle = list.Handle; // Selection collections need a native handle even in a hidden test form.
                var search = Field<TextBox>(form, "nodeSearch");
                var only = Field<CheckBox>(form, "favoritesOnly");
                var sort = Field<ComboBox>(form, "nodeSort");
                form.SetNodeProfile(scratch, "profile-b");
                form.UpdateNodes(nodes, "alpha");
                Check(list.Items.Count == 4, "initial render");
                list.Items[1].Selected = true;
                sort.SelectedIndex = 2;
                Check(list.SelectedItems.Count == 1 && (string)list.SelectedItems[0].Tag == "alpha", "selection must follow identity after sort");
                search.Text = "香港";
                Check(list.Items.Count == 1 && list.SelectedItems.Count == 0, "hidden selection must not remain switchable");
                Check(Field<Label>(form, "nodeCount").Text.Contains("1 / 4") && Field<Label>(form, "nodeCount").Text.Contains("当前节点"), "filter count / hidden current warning");
                form.UpdateNodes(nodes, "alpha");
                Check(list.Items.Count == 1, "refresh lost filter");
                search.Clear();
                only.Checked = true;
                Check(list.Items.Count == 1 && (string)list.Items[0].Tag == "Beta 香港", "favorites UI");
                only.Checked = false;
                Check(list.Items.Count == 4, "clearing filter lost nodes");
                form.SetNodeProfile(scratch, "profile-a");
                Check(list.Items.Count == 0 && !only.Checked && search.Text == "", "profile change must clear stale view");
            }
            Console.WriteLine("PASS node catalog: search, sorting, stable selection, refresh, favorites persistence/isolation, corrupt-file preservation and filter reset");
        }
    }
}
