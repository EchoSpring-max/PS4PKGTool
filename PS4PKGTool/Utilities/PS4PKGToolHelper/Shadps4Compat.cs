using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// shadPS4 compatibility lookup, per operating system. The database is
    /// the official shadps4-compatibility/shadps4-game-compatibility GitHub
    /// repo - each game/OS combo is a GitHub Issue titled
    /// "CUSAxxxxx - Title", labeled with its status
    /// (status-playable/ingame/menus/boots/nothing) and its operating
    /// system (os-windows/os-linux/os-macOS). Issues without an os-* label
    /// are ignored (no guessing). This class downloads the issues once
    /// (manual fetch), caches a { CUSA -> { os -> status } } map in
    /// AppData\shadps4.json, and looks up statuses by Title ID + OS.
    /// </summary>
    public static class Shadps4Compat
    {
        private const string RepoApi = "https://api.github.com/repos/shadps4-compatibility/shadps4-game-compatibility/issues";
        private static readonly string CachePath = Helper.AppDataDirectory + "shadps4.json";
        private static readonly Regex CusaRegex = new(@"^(CUSA\d{5})", RegexOptions.Compiled);
        private static readonly string[] Labels =
            { "status-playable", "status-ingame", "status-menus", "status-boots", "status-nothing" };

        private static Dictionary<string, Dictionary<string, string>> _cache;

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

        /// <summary>Display name of an OS key ("windows" -> "Windows").</summary>
        public static string OsDisplay(string os) => NormalizeOs(os) switch
        {
            "linux" => "Linux",
            "macos" => "macOS",
            _ => "Windows",
        };

        /// <summary>Returns the status ("Playable", "In-Game", ...) for a Title ID on the given OS, or "" if unknown.</summary>
        public static string Lookup(string titleId, string os)
        {
            if (string.IsNullOrEmpty(titleId)) return "";
            if (_cache == null) LoadCache();
            if (_cache != null && _cache.TryGetValue(titleId, out var byOs)
                && byOs.TryGetValue(NormalizeOs(os), out var status))
                return status;
            return "";
        }

        public static void LoadCache()
        {
            _cache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(CachePath)) return;
                var entries = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(CachePath));
                if (entries == null) return;

                foreach (var kv in entries)
                {
                    var byOs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (kv.Value is string legacyStatus)
                    {
                        // Flat pre-OS cache ({CUSA -> status}): migrate as the
                        // Windows layer - those entries were Windows reports.
                        if (legacyStatus.Length > 0) byOs["windows"] = legacyStatus;
                    }
                    else if (kv.Value is JObject jo)
                    {
                        foreach (var osKv in jo)
                            byOs[NormalizeOs(osKv.Key)] = osKv.Value?.ToString() ?? "";
                    }
                    _cache[kv.Key] = byOs;
                }
            }
            catch { }
        }

        /// <summary>
        /// Downloads the compatibility database from GitHub Issues and writes the
        /// cache file. Returns (count, error) - error is null on success.
        /// </summary>
        public static async Task<(int count, string error)> DownloadAsync(IProgress<string> progress = null)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
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
                            // Collect the OS labels; issues without an os-*
                            // label are ignored (don't guess the OS).
                            var osTags = new List<string>();
                            foreach (var lbl in issue.labels)
                            {
                                string os = OsFromLabel((string)(lbl.name ?? ""));
                                if (os != null && !osTags.Contains(os)) osTags.Add(os);
                            }
                            if (osTags.Count == 0) continue;

                            string title = (string)(issue.title ?? "");
                            var m = CusaRegex.Match(title);
                            if (!m.Success) continue;

                            string status = LabelToStatus(label);
                            if (!result.TryGetValue(m.Groups[1].Value, out var byOs))
                            {
                                byOs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                result[m.Groups[1].Value] = byOs;
                            }
                            foreach (string os in osTags) byOs[os] = status;
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

        /// <summary>Maps an os-* label to a normalized OS key, or null when the label is not an OS label.</summary>
        private static string OsFromLabel(string label) => (label ?? "").ToLowerInvariant() switch
        {
            "os-windows" => "windows",
            "os-linux" => "linux",
            "os-macos" => "macos",
            _ => null
        };

        /// <summary>Normalizes an OS key ("mac"/"macos"/anything unknown -&gt; defaults).</summary>
        private static string NormalizeOs(string os) => (os ?? "").Trim().ToLowerInvariant() switch
        {
            "linux" => "linux",
            "macos" => "macos",
            "mac" => "macos",
            _ => "windows",
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
