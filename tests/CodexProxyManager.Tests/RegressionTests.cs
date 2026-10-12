using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexProxyManager
{
    internal static class RegressionTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static string RegistrySnapshot()
        {
            var values = new SortedDictionary<string, string>();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.InternetSettings))
            {
                foreach (string name in key.GetValueNames())
                    values[name] = key.GetValueKind(name) + ":" + Json.Serialize(key.GetValue(name));
            }
            return Json.Serialize(values);
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        }

        private static void Invoke(object instance, string name)
        {
            instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args[0].StartsWith("--proxy-server="))
            {
                // Child process verifies the actual OS environment/argument inheritance.
                Console.Write(Json.Serialize(new Dictionary<string, string>
                {
                    { "HTTP_PROXY", Environment.GetEnvironmentVariable("HTTP_PROXY") },
                    { "HTTPS_PROXY", Environment.GetEnvironmentVariable("HTTPS_PROXY") },
                    { "ALL_PROXY", Environment.GetEnvironmentVariable("ALL_PROXY") },
                    { "NO_PROXY", Environment.GetEnvironmentVariable("NO_PROXY") },
                    { "Arguments", String.Join(" ", args) }
                }));
                return 0;
            }
            if (args.Length > 0 && args[0] == "--watchdog")
            {
                RecoveryWatchdog.Run(args);
                return 0;
            }

            string root = Path.GetFullPath(args[0]);
            string before = RegistrySnapshot();
            string scratch = Path.Combine(Path.GetTempPath(), "CodexProxyRegression-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            object context = null;
            try
            {
                NodeCatalogTests.Run(scratch);
                OnboardingTests.Run(scratch);
                ControllerProbeTests.Run(scratch);
                if (Array.IndexOf(args, "--unit-only") >= 0)
                {
                    Assert(before == RegistrySnapshot(), "Unit tests changed Windows proxy settings");
                    Console.WriteLine("ALL UNIT TESTS PASSED (network integration tests not run)");
                    return 0;
                }
                string executable = ProcessProxyLauncher.FindCodexExecutable();
                Assert(File.Exists(executable), "Registered Codex executable discovery failed");
                Console.WriteLine("PASS registered Codex executable discovered");

                string parentHttp = Environment.GetEnvironmentVariable("HTTP_PROXY");
                string parentNoProxy = Environment.GetEnvironmentVariable("NO_PROXY");
                ProcessStartInfo info = ProcessProxyLauncher.BuildStartInfo(
                    Assembly.GetExecutingAssembly().Location, 27899);
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                using (Process child = Process.Start(info))
                {
                    var output = child.StandardOutput.ReadToEndAsync();
                    Assert(child.WaitForExit(10000), "Child launch timed out");
                    var report = Json.Deserialize<Dictionary<string, string>>(output.Result);
                    foreach (string key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
                        Assert(report[key] == "http://127.0.0.1:27899", "Missing child proxy: " + key);
                    Assert(report["NO_PROXY"] == "localhost,127.0.0.1,::1", "Loopback bypass missing");
                    Assert(report["Arguments"].Contains("--proxy-server=http://127.0.0.1:27899"), "Electron proxy argument missing");
                }
                Assert(parentHttp == Environment.GetEnvironmentVariable("HTTP_PROXY") &&
                    parentNoProxy == Environment.GetEnvironmentVariable("NO_PROXY"), "Parent environment changed");
                Console.WriteLine("PASS real child gets proxy arguments/environment; parent environment unchanged");

                var testApp = new ManagedApplication { Id = "probe", Name = "Test app",
                    Executable = Assembly.GetExecutingAssembly().Location, LaunchMode = "environment", ProfileId = "second" };
                ProcessStartInfo environmentInfo = ProcessProxyLauncher.BuildStartInfo(testApp.Executable, 27899, "environment");
                environmentInfo.CreateNoWindow = true;
                environmentInfo.RedirectStandardOutput = true;
                using (Process child = Process.Start(environmentInfo))
                {
                    var output = child.StandardOutput.ReadToEndAsync();
                    Assert(child.WaitForExit(10000), "Environment-only child timed out");
                    var report = Json.Deserialize<Dictionary<string, string>>(output.Result);
                    Assert(report["Arguments"] == "" && report["HTTPS_PROXY"] == "http://127.0.0.1:27899", "Environment mode contract failed");
                }
                string testSettings = Path.Combine(scratch, "settings.json");
                var settings = new ManagerSettings { SchemaVersion = 1, ActiveProfileId = "first",
                    Profiles = new List<ProxyProfileDefinition> { new ProxyProfileDefinition { Id = "first" }, new ProxyProfileDefinition { Id = "second" } } };
                StartupConfiguration.WriteSettings(testSettings, settings);
                settings = StartupConfiguration.ReadSettings(testSettings);
                Assert(settings.ActiveApplicationId == "codex" && settings.Profiles.Count == 2, "Legacy settings migration lost profiles");
                settings.Applications.Add(testApp);
                settings.ActiveApplicationId = testApp.Id;
                StartupConfiguration.WriteSettings(testSettings, settings);
                settings = StartupConfiguration.ReadSettings(testSettings);
                Assert(ApplicationConfiguration.ResolveProfile(settings, settings.Applications[1]).Id == "second", "Application profile binding lost");
                settings.Profiles.RemoveAt(1);
                ApplicationConfiguration.Normalize(settings);
                Assert(ApplicationConfiguration.ResolveProfile(settings, settings.Applications[1]).Id == "first", "Deleted profile fallback failed");
                Console.WriteLine("PASS environment-only child; legacy migration, app persistence, profile binding and deletion fallback");

                int mixed = FreePort();
                int controller = FreePort();
                while (controller == mixed) controller = FreePort();
                foreach (string name in new[] { "GeoSite.dat", "geoip.metadb" })
                    File.Copy(Path.Combine(root, "runtime", "data", name), Path.Combine(scratch, name));
                string config = Path.Combine(scratch, "config.yaml");
                File.WriteAllText(config, MihomoConfigFactory.BuildLocalUpstream("http", "127.0.0.1", 9, "", "", mixed)
                    .Replace("Codex Proxy", "Fixture Private Selector"), new UTF8Encoding(false));
                string statePath = Path.Combine(scratch, "state.json");
                string log = Path.Combine(scratch, "manager.log");
                // Run the real core start/stop code without a dashboard or launching the active Codex session.
                context = FormatterServices.GetUninitializedObject(typeof(ManagerContext));
                Set(context, "corePath", Path.Combine(root, "runtime", "mihomo.exe"));
                Set(context, "coreHome", scratch);
                Set(context, "configPath", config);
                Set(context, "statePath", statePath);
                Set(context, "managerLog", log);
                Set(context, "coreOutLog", Path.Combine(scratch, "out.log"));
                Set(context, "coreErrLog", Path.Combine(scratch, "err.log"));
                Set(context, "mixedPort", mixed);
                Set(context, "controllerPort", controller);
                Set(context, "controllerSecret", "isolated-regression-secret");
                Invoke(context, "StartCore");
                Process core = (Process)typeof(ManagerContext).GetField("coreProcess", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(context);
                Program.WriteState(statePath, new ProxyState { NetworkMode = "process", CorePid = core.Id });
                using (var client = new WebClient())
                {
                    client.Proxy = null;
                    client.Headers[HttpRequestHeader.Authorization] = "Bearer isolated-regression-secret";
                    Assert(client.DownloadString("http://127.0.0.1:" + controller + "/version").Contains("version"), "Core API failed");
                }
                Assert(before == RegistrySnapshot(), "Core startup changed Windows proxy settings");
                DiagnosticsTests.Run(new DiagnosticInput
                {
                    CorePath = Path.Combine(root, "runtime", "mihomo.exe"), ConfigPath = config, CoreHome = scratch, StatePath = statePath,
                    CoreUrl = "http://127.0.0.1:" + controller, NodeUrl = "http://127.0.0.1:" + controller,
                    CoreSecret = "isolated-regression-secret", NodeSecret = "isolated-regression-secret", GroupName = "Fixture Private Selector",
                    MixedPort = mixed, SessionActive = true, CoreAlive = true, ApplicationRunning = true,
                    ProcessNames = new[] { "RegressionTests.exe" }
                });
                Assert(before == RegistrySnapshot(), "Diagnostics changed Windows proxy settings");
                Invoke(context, "EndSessionCore");
                Assert(!File.Exists(statePath), "Session state was not removed");
                Assert(before == RegistrySnapshot(), "Normal stop changed Windows proxy settings");
                Console.WriteLine("PASS real Mihomo startup/API/normal stop; Windows Internet Settings byte-equivalent");

                var upstream = new TcpListener(IPAddress.Loopback, 0);
                upstream.Start();
                try
                {
                    int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
                    string template = MihomoConfigFactory.BuildLocalUpstream("http", "127.0.0.1", upstreamPort, "", "", mixed);
                    string metadata = "# PROCESS-NAME,ChatGPT.exe,KEEP-COMMENT\n";
                    template = metadata + template;
                    string sessionYaml = ApplicationConfiguration.BuildSessionConfig(template, testApp);
                    Assert(sessionYaml.StartsWith(metadata), "Rule transform changed non-rule content");
                    Assert(ApplicationConfiguration.BuildSessionConfig(template, ApplicationConfiguration.DefaultApplication()) == template, "Codex routing changed");
                    File.WriteAllText(config, template, new UTF8Encoding(false));
                    string sessionPath = Path.Combine(scratch, "session-config.yaml");
                    File.WriteAllText(sessionPath, sessionYaml, new UTF8Encoding(false));
                    Set(context, "sessionConfigPath", sessionPath);
                    Invoke(context, "StartCore");
                    var reply = Task.Run(delegate
                    {
                        using (TcpClient socket = upstream.AcceptTcpClient())
                        using (var stream = socket.GetStream())
                        {
                            stream.ReadTimeout = 5000;
                            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                            string firstLine = reader.ReadLine();
                            string line;
                            do { line = reader.ReadLine(); } while (!String.IsNullOrEmpty(line));
                            if (firstLine != null && firstLine.StartsWith("CONNECT "))
                            {
                                byte[] tunnel = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                                stream.Write(tunnel, 0, tunnel.Length);
                                do { line = reader.ReadLine(); } while (!String.IsNullOrEmpty(line));
                            }
                            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 8\r\nConnection: close\r\n\r\nproxy-ok");
                            stream.Write(response, 0, response.Length);
                        }
                    });
                    var request = (HttpWebRequest)WebRequest.Create("http://203.0.113.1/app-routing-check");
                    request.Proxy = new WebProxy("http://127.0.0.1:" + mixed);
                    request.Timeout = 8000;
                    using (WebResponse response = request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                        Assert(reader.ReadToEnd() == "proxy-ok", "Custom application request did not reach upstream proxy");
                    Assert(reply.Wait(5000), "Test upstream did not complete");
                    Invoke(context, "EndSessionCore");
                    Assert(File.ReadAllText(config) == template && !File.Exists(sessionPath), "Session routing modified original profile or left session copy");
                    Assert(before == RegistrySnapshot(), "Custom application routing changed Windows proxy settings");
                    Console.WriteLine("PASS custom application traffic reaches test upstream; original profile and Windows proxy preserved");
                }
                finally { upstream.Stop(); }

                Program.WriteState(statePath, new ProxyState { NetworkMode = "process", CorePid = Int32.MaxValue });
                RecoveryWatchdog.Run(new[] { "--watchdog", Int32.MaxValue.ToString(), Int32.MaxValue.ToString(), statePath, log });
                Assert(!File.Exists(statePath), "Crash cleanup left state behind");
                Assert(before == RegistrySnapshot(), "Crash cleanup changed Windows proxy settings");
                Console.WriteLine("PASS crash cleanup preserves Windows proxy settings");

                // A watchdog for an older session must not erase the next session's state.
                Program.WriteState(statePath, new ProxyState { NetworkMode = "process", CorePid = Int32.MaxValue - 1 });
                RecoveryWatchdog.Run(new[] { "--watchdog", Process.GetCurrentProcess().Id.ToString(), Int32.MaxValue.ToString(), statePath, log });
                Assert(File.Exists(statePath), "Old watchdog removed a new session");
                Console.WriteLine("PASS old watchdog exits without touching a newer session");
                Console.WriteLine("ALL REGRESSIONS PASSED");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                if (context != null) { try { Invoke(context, "StopCore"); } catch { } }
                // Only the unique temporary directory created by this test is removed.
                try { Directory.Delete(scratch, true); } catch { }
            }
        }
    }
}
