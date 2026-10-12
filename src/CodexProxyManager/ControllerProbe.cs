using System;
using System.Collections.Generic;

namespace CodexProxyManager
{
    internal static class ControllerProbe
    {
        internal static int Check(string url, string secret, int timeout)
        {
            // Reuse the local-only transport: no inherited system proxy, redirects or raw errors.
            var version = Diagnostics.ReadApi(url, secret, "/version", timeout);
            object value;
            if (!version.TryGetValue("version", out value) || !(value is string) || String.IsNullOrWhiteSpace((string)value))
                throw new System.IO.InvalidDataException();
            var root = Diagnostics.ReadApi(url, secret, "/proxies", timeout);
            var proxies = root.TryGetValue("proxies", out value) ? value as Dictionary<string, object> : null;
            if (proxies == null) throw new System.IO.InvalidDataException();
            int selectors = 0;
            foreach (object entry in proxies.Values)
            {
                var detail = entry as Dictionary<string, object>;
                if (detail != null && detail.TryGetValue("type", out value) &&
                    String.Equals(Convert.ToString(value), "Selector", StringComparison.OrdinalIgnoreCase)) selectors++;
            }
            return selectors;
        }
    }
}
