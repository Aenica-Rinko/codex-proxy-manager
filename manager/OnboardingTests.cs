using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class OnboardingTests
    {
        private static void Check(bool result, string message)
        {
            if (!result) throw new Exception("Onboarding: " + message);
        }
        internal static void Run(string scratch)
        {
            string root = Path.Combine(scratch, "package");
            string user = Path.Combine(scratch, "fresh-user");
            Check(InstallationCheck.Missing(root, user).Count == 3, "fresh package missing list");
            using (var guide = new GettingStartedForm(root, user, true))
            {
                var proceed = (Button)typeof(GettingStartedForm).GetField("proceed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(guide);
                Check(!proceed.Enabled, "missing files must block continue");
            }
            Check(!Directory.Exists(user) && !Directory.Exists(root), "guide must not write files");
            Directory.CreateDirectory(Path.Combine(root, "runtime", "data"));
            File.WriteAllText(Path.Combine(root, "runtime", "mihomo.exe"), "");
            Check(InstallationCheck.Missing(root, user).Count == 3, "empty core accepted");
            File.WriteAllText(Path.Combine(root, "runtime", "mihomo.exe"), "fixture");
            File.WriteAllText(Path.Combine(root, "runtime", "data", "GeoSite.dat"), "fixture");
            Directory.CreateDirectory(Path.Combine(user, "core-data"));
            File.WriteAllText(Path.Combine(user, "core-data", "geoip.metadb"), "fixture");
            Check(InstallationCheck.Missing(root, user).Count == 0, "existing user data fallback");
            using (var guide = new GettingStartedForm(root, user, true))
            {
                var proceed = (Button)typeof(GettingStartedForm).GetField("proceed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(guide);
                Check(proceed.Enabled, "files present continue");
            }
            var defaults = (ManagerSettings)typeof(ProfileManagerForm).GetMethod("LoadSettingsOrDefault", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { user });
            Check(typeof(ManagerSettings).GetProperty("AutoStart") == null, "automatic launch setting still exposed");
            Check(typeof(StartupProfile).GetProperty("AutoStart", BindingFlags.NonPublic | BindingFlags.Instance) == null, "automatic launch still passed to runtime");
            defaults.AutoSelectFastest = true;
            defaults.AutoFailover = true;
            StartupConfiguration.WriteSettings(Path.Combine(user, "settings.json"), defaults);
            string settingsPath = Path.Combine(user, "settings.json");
            string currentJson = File.ReadAllText(settingsPath);
            // Simulate an existing user's old opt-in without using real user data.
            string legacyJson = currentJson.Insert(currentJson.IndexOf('{') + 1, "\"AutoStart\":true,");
            File.WriteAllText(settingsPath, legacyJson);
            var existing = (ManagerSettings)typeof(ProfileManagerForm).GetMethod("LoadSettingsOrDefault", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { user });
            Check(existing.AutoSelectFastest && existing.AutoFailover, "unrelated existing preferences lost");
            Check(File.ReadAllText(settingsPath) == legacyJson, "loading settings rewrote user data");
            StartupConfiguration.WriteSettings(settingsPath, existing);
            Check(!File.ReadAllText(settingsPath).Contains("\"AutoStart\""), "legacy automatic launch persisted after save");
            using (var profiles = (ProfileManagerForm)Activator.CreateInstance(typeof(ProfileManagerForm),
                BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { root, user, false }, null))
            {
                Check(typeof(ProfileManagerForm).GetField("autoStartBox", BindingFlags.NonPublic | BindingFlags.Instance) == null,
                    "automatic launch checkbox still available");
            }
            using (var dashboard = new DashboardForm())
            {
                int requests = 0;
                dashboard.StartRequested += delegate { requests++; };
                Check(requests == 0, "dashboard construction requested application start");
                var start = (Button)typeof(DashboardForm).GetField("startButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dashboard);
                typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(start, new object[] { EventArgs.Empty });
                Check(requests == 1, "manual start click did not raise exactly one request");
            }
            Check(Assembly.GetExecutingAssembly().GetName().Version.ToString() == AppInfo.Version + ".0", "assembly version drift");
            Console.WriteLine("PASS onboarding: installation checks, legacy AutoStart ignored/removed on save, unrelated preferences preserved, manual start event and assembly version");
        }
    }
}
