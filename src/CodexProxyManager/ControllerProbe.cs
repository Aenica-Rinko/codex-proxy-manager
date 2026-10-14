using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace CodexProxyManager
{
    internal static class LocalControllerTransport
    {
        internal static string Send(string url, string secret, string path, string method, string body, int timeout)
        {
            string endpoint = StartupConfiguration.NormalizeControllerUrl(url);
            if (String.IsNullOrEmpty(endpoint) || String.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal) || path.IndexOf('\\') >= 0 || timeout <= 0)
                throw new InvalidDataException();
            var request = (HttpWebRequest)WebRequest.Create(endpoint + path);
            request.Proxy = null;
            request.AllowAutoRedirect = false;
            request.Timeout = request.ReadWriteTimeout = timeout;
            request.Method = method;
            if (!String.IsNullOrEmpty(secret)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + secret;
            int expired = 0;
            try
            {
                // A total deadline also bounds a server that slowly drips response bytes.
                using (var deadline = new Timer(delegate
                {
                    Interlocked.Exchange(ref expired, 1);
                    try { request.Abort(); } catch { }
                }, null, timeout, Timeout.Infinite))
                {
                    if (body != null)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(body);
                        request.ContentType = "application/json; charset=utf-8";
                        request.ContentLength = bytes.Length;
                        using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                    }
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300) throw new InvalidDataException();
                        if (response.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException();
                        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            var buffer = new char[4096];
                            var text = new StringBuilder();
                            int read;
                            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                text.Append(buffer, 0, read);
                                if (text.Length > 2 * 1024 * 1024) throw new InvalidDataException();
                            }
                            return text.ToString();
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (Interlocked.CompareExchange(ref expired, 0, 0) != 0)
                    throw new WebException("Local controller deadline exceeded.", WebExceptionStatus.Timeout);
                throw;
            }
        }
    }

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
