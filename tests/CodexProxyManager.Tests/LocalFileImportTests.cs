// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class LocalFileImportTests
    {
        private const string Nodes = "proxies:\n  - {name: '本地节点', type: socks5, server: 127.0.0.1, port: 9}\n";
        private static void Check(bool ok, string message) { if (!ok) throw new Exception("Local import: " + message); }
        private static void Rejected(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Check(rejected, message);
        }
        internal static void Run(string scratch)
        {
            string folder = Path.Combine(scratch, "文件导入测试");
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "代理档案.yaml");
            string original = Nodes + "tun: {enable: true}\nallow-lan: true\nmixed-port: 12345\nrules: ['MATCH,REJECT']\n";
            File.WriteAllText(source, original, new UTF8Encoding(true));
            byte[] before = File.ReadAllBytes(source);
            Check(LocalFileImport.ReadSnapshot(source) == original, "UTF8 BOM decoding");
            string unicode = Path.Combine(folder, "utf16.yml");
            File.WriteAllText(unicode, Nodes, Encoding.Unicode);
            Check(LocalFileImport.ReadSnapshot(unicode) == Nodes, "UTF16 BOM decoding");
            string invalid = Path.Combine(folder, "invalid.txt");
            File.WriteAllBytes(invalid, new byte[] { 255, 0, 1 });
            Rejected(delegate { LocalFileImport.ReadSnapshot(invalid); }, "invalid encoding accepted");
            File.WriteAllText(invalid, "\0binary");
            Rejected(delegate { LocalFileImport.ReadSnapshot(invalid); }, "NUL content accepted");
            File.WriteAllText(invalid, " ");
            Rejected(delegate { LocalFileImport.ReadSnapshot(invalid); }, "empty content accepted");
            using (var file = new FileStream(invalid, FileMode.Create)) file.SetLength(LocalFileImport.MaximumBytes + 1L);
            Rejected(delegate { LocalFileImport.ReadSnapshot(invalid); }, "oversize file accepted");
            string json = Path.Combine(folder, "other-client.json");
            File.WriteAllText(json, "{}");
            Rejected(delegate { LocalFileImport.ReadSnapshot(json); }, "JSON accepted");
            Rejected(delegate { LocalFileImport.ReadSnapshot(source + ":extra"); }, "ADS accepted");
            Rejected(delegate { LocalFileImport.ReadSnapshot(Path.Combine(folder, "absent.yaml")); }, "missing accepted");
            string user = Path.Combine(folder, "user");
            using (var failed = LocalFileImport.Prepare(user, source, 27890, delegate(string value) { Check(value == original, "snapshot mismatch"); return 1; }))
            {
                Check(File.Exists(failed.ConfigPath) && File.Exists(failed.ProviderPath), "private output missing");
                string config = File.ReadAllText(failed.ConfigPath);
                Check(config.Contains("allow-lan: false") && config.Contains("mixed-port: 27890") && config.Contains("PROCESS-NAME,ChatGPT.exe"), "scoped config missing");
                Check(!config.Contains("enable: true\ntun") && !config.Contains("MATCH,REJECT") && !config.Contains("mixed-port: 12345"), "source global settings copied as config");
                Check(File.ReadAllText(failed.ProviderPath) == original, "snapshot not preserved");
            }
            Check(Directory.GetFiles(Path.Combine(user, "profiles"), "*", SearchOption.AllDirectories).Length == 0, "uncommitted config retained");
            Check(Directory.GetFiles(Path.Combine(user, "core-data", "providers")).Length == 0, "uncommitted provider retained");
            using (var saved = LocalFileImport.Prepare(user, source, 27890, delegate { return 1; }))
            {
                saved.Committed = true;
                saved.Dispose();
                Check(File.Exists(saved.ConfigPath), "committed profile deleted");
                File.WriteAllText(source, "proxies: []\n");
                Check(File.ReadAllText(saved.ProviderPath) == original, "source changes affected snapshot");
                File.WriteAllBytes(source, before);
            }
            Rejected(delegate { LocalFileImport.Prepare(user, source, 27890, delegate { return 0; }); }, "empty provider accepted");
            string blocked = Path.Combine(folder, "blocked-user");
            Directory.CreateDirectory(blocked);
            File.WriteAllText(Path.Combine(blocked, "core-data"), "existing-file");
            Rejected(delegate { LocalFileImport.Prepare(blocked, source, 27890, delegate { return 1; }); }, "save failure accepted");
            Check(File.ReadAllText(Path.Combine(blocked, "core-data")) == "existing-file" &&
                Directory.GetDirectories(Path.Combine(blocked, "profiles")).Length == 0, "failed write damaged existing file or left profile");
            Check(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(source)), "source changed");
            using (var form = (ProfileManagerForm)Activator.CreateInstance(typeof(ProfileManagerForm), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { folder, user, false }, null))
            {
                var sources = (ComboBox)typeof(ProfileManagerForm).GetField("sourceTypeBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Check(sources.Items.Count == 5 && sources.Items[4].ToString() == "本地文件导入", "source entry missing");
                sources.SelectedIndex = 4;
                var field = (TextBox)typeof(ProfileManagerForm).GetField("filePathBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Check(field.ReadOnly && field.Width > 0, "file selection field invalid");
                Check(!File.Exists(Path.Combine(user, "settings.json")), "form construction changed settings");
            }
            Console.WriteLine("PASS local file import: Unicode/size/type safety, immutable source snapshot, private scoped config, rollback/commit and UI source entry");
        }

        internal static void RunCore(string scratch, string projectRoot)
        {
            string user = Path.Combine(scratch, "local-import-core");
            Directory.CreateDirectory(user);
            string source = Path.Combine(user, "nodes.yaml");
            File.WriteAllText(source, Nodes + "tun: {enable: true}\nexternal-controller: 0.0.0.0:1\ndns: {enable: true}\nproxy-providers: {ignored: {type: http, url: 'https://invalid.example.test/not-called'}}\n");
            using (var imported = LocalFileImport.Prepare(user, source, 27890, delegate(string text) { return LocalFileImport.ValidateWithCore(projectRoot, user, text); }))
                Check(imported.NodeCount == 1, "real core ignored valid inline node");
            string uri = "socks5://127.0.0.1:9#fixture-local\n";
            Check(LocalFileImport.ValidateWithCore(projectRoot, user, StrategyTests.Nodes + StrategyTests.Groups) == 2, "core rejected retained strategy graph");
            Check(LocalFileImport.ValidateWithCore(projectRoot, user, uri) == 1, "URI parser failed");
            Check(LocalFileImport.ValidateWithCore(projectRoot, user, Convert.ToBase64String(Encoding.UTF8.GetBytes(uri))) == 1, "Base64 parser failed");
            Rejected(delegate { LocalFileImport.ValidateWithCore(projectRoot, user, "proxy-providers: {only: {type: http, url: 'https://invalid.example.test/not-called'}}\n"); }, "remote-only YAML accepted");
            Check(Directory.GetDirectories(user, "import-check-*").Length == 0, "validation private staging retained");
            Console.WriteLine("PASS local file core validation: YAML nodes only, URI/Base64, remote-only rejection and private staging cleanup; no target launched");
        }
    }
}
