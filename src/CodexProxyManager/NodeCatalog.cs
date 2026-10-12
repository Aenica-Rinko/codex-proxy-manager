using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexProxyManager
{
    internal static class NodeCatalog
    {
        // Display filtering never changes the manager's complete node list or node identities.
        internal static List<NodeInfo> Select(IEnumerable<NodeInfo> source, string query,
            bool favoritesOnly, ISet<string> favorites, int sort)
        {
            query = (query ?? "").Trim();
            IEnumerable<NodeInfo> nodes = source.Where(n =>
                (!favoritesOnly || favorites.Contains(n.Name)) &&
                (query.Length == 0 || n.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (n.Type ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            if (sort == 1) nodes = nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
            if (sort == 2) nodes = nodes.OrderBy(n => n.Delay > 0 ? n.Delay : Int32.MaxValue)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
            return nodes.ToList();
        }
    }

    internal sealed class NodePreferences
    {
        private readonly string path;
        private readonly string profileId;
        internal NodePreferences(string path, string profileId)
        {
            this.path = path;
            this.profileId = profileId;
        }

        private Dictionary<string, List<string>> Read()
        {
            if (!File.Exists(path)) return new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException();
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path));
            if (data == null || data.Any(p => p.Value == null || p.Value.Any(n => n == null)))
                throw new InvalidDataException();
            return data;
        }

        internal HashSet<string> Load()
        {
            List<string> names;
            return new HashSet<string>(Read().TryGetValue(profileId, out names) ? names : new List<string>(), StringComparer.Ordinal);
        }

        internal HashSet<string> Toggle(string name)
        {
            if (String.IsNullOrEmpty(profileId) || String.IsNullOrEmpty(name)) throw new InvalidDataException();
            var data = Read(); // Read again so another profile's saved favorites are retained.
            List<string> previous;
            var names = new HashSet<string>(data.TryGetValue(profileId, out previous) ? previous : new List<string>(), StringComparer.Ordinal);
            if (!names.Remove(name)) names.Add(name);
            data[profileId] = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
            string json = new JavaScriptSerializer().Serialize(data);
            if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024) throw new InvalidDataException();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return names;
        }
    }
}
