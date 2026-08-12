using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    /// <summary>
    /// shadPS4 compatibility lookup. The database is the official
    /// shadps4-compatibility/shadps4-game-compatibility GitHub repo -
    /// each game is a GitHub Issue titled "CUSAxxxxx - Title", labeled
    /// with its status (status-playable/ingame/menus/boots/nothing).
    /// This class downloads the issues once (manual fetch), caches a
    /// { CUSA → status } map in AppData\shadps4.json, and looks up
    /// statuses by Title ID.
    /// </summary>
    public static class Shadps4Compat
    {
        private const string RepoApi = "https://api.github.com/repos/shadps4-compatibility/shadps4-game-compatibility/issues";
        private static readonly string CachePath = Helper.AppDataDirectory + "shadps4.json";
        private static readonly Regex CusaRegex = new(@"^(CUSA\d{5})", RegexOptions.Compiled);
        private static readonly string[] Labels =
            { "status-playable", "status-ingame", "status-menus", "status-boots", "status-nothing" };

        private static Dictionary<string, string> _cache;

        public static bool CacheExists => File.Exists(CachePath);

        public static DateTime? LastDownload
        {
            get
            {
                try { return File.Exists(CachePath) ? (DateTime?)File.GetLastWriteTime(CachePath) : null; }
                catch { return null; }
            }
        }

        public static int CacheCount
        {
            get
            {
                if (_cache == null) LoadCache();
                return _cache?.Count ?? 0;
            }
        }

        /// <summary>Returns the status ("Playable", "In-Game", ...) for a Title ID, or "" if unknown.</summary>
        public static string Lookup(string titleId)
        {
            if (string.IsNullOrEmpty(titleId)) return "";
            if (_cache == null) LoadCache();
            return _cache != null && _cache.TryGetValue(titleId, out var status) ? status : "";
        }

        public static void LoadCache()
        {
            _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(CachePath)) return;
                var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(CachePath));
                if (entries != null)
                    foreach (var kv in entries) _cache[kv.Key] = kv.Value;
            }
            catch { }
        }

        /// <summary>
        /// Downloads the compatibility database from GitHub Issues and writes the
        /// cache file. Returns (count, error) - error is null on success.
        /// </summary>
        public static async Task<(int count, string error)> DownloadAsync(IProgress<string> progress = null)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "PS4-PKG-Tool");
                http.Timeout = TimeSpan.FromSeconds(90);

                foreach (var label in Labels)
                {
                    int page = 1;
                    while (true)
                    {
                        string url = $"{RepoApi}?state=all&labels={label}&per_page=100&page={page}";
                        string json;
                        using (var resp = await http.GetAsync(url))
                        {
                            if (!resp.IsSuccessStatusCode)
                                return (result.Count, $"GitHub API error: {(int)resp.StatusCode}");
                            json = await resp.Content.ReadAsStringAsync();
                        }

                        var issues = JsonConvert.DeserializeObject<List<dynamic>>(json);
                        if (issues == null || issues.Count == 0) break;

                        foreach (var issue in issues)
                        {
                            string title = (string)(issue.title ?? "");
                            var m = CusaRegex.Match(title);
                            if (m.Success)
                                result[m.Groups[1].Value] = LabelToStatus(label);
                        }

                        if (issues.Count < 100) break;
                        page++;
                    }
                    progress?.Report($"{label.Replace("status-", "")}: {result.Count} games so far");
                }

                _cache = result;
                File.WriteAllText(CachePath, JsonConvert.SerializeObject(result, Formatting.Indented));
                return (result.Count, null);
            }
            catch (Exception ex)
            {
                return (result.Count, ex.Message);
            }
        }

        private static string LabelToStatus(string label) => label switch
        {
            "status-playable" => "Playable",
            "status-ingame" => "In-Game",
            "status-menus" => "Menus",
            "status-boots" => "Boots",
            "status-nothing" => "Nothing",
            _ => label
        };

        /// <summary>Status color for the grid cell (readable on the dark theme).</summary>
        public static Color StatusColor(string status) => status switch
        {
            "Playable" => Color.FromArgb(102, 187, 106),  // green
            "In-Game" => Color.FromArgb(255, 213, 79),    // yellow
            "Menus" => Color.FromArgb(255, 167, 38),      // orange
            "Boots" => Color.FromArgb(239, 83, 80),       // red
            "Nothing" => Color.FromArgb(158, 158, 158),   // gray
            _ => Color.FromArgb(158, 158, 158)
        };
    }
}
