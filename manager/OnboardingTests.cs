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
            Check(!defaults.AutoStart, "new user must opt into automatic launch");
            defaults.AutoStart = true;
            StartupConfiguration.WriteSettings(Path.Combine(user, "settings.json"), defaults);
            var existing = (ManagerSettings)typeof(ProfileManagerForm).GetMethod("LoadSettingsOrDefault", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { user });
            Check(existing.AutoStart, "existing user preference lost");
            Check(Assembly.GetExecutingAssembly().GetName().Version.ToString() == AppInfo.Version + ".0", "assembly version drift");
            Console.WriteLine("PASS onboarding: read-only missing/empty checks, cached data, continue gate, first-run defaults, existing preference and assembly version");
        }
    }
}
