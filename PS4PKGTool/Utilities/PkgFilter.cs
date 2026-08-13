using System.Collections.Generic;

namespace PS4PKGTool.Utilities
{
    /// <summary>
    /// Builds the single effective RowFilter for the PKG grid. All filter
    /// controls (PKG type, compatibility status, search) feed this one
    /// method so filters COMPOSE (AND) instead of overwriting each other.
    /// Column visibility does not affect filtering - the DataTable columns
    /// exist regardless of the DataGridView presentation state.
    /// </summary>
    public static class PkgFilter
    {
        /// <summary>Sentinel compat filter: games with no compatibility entry.</summary>
        public const string CompatUnknown = "Unknown";

        public static readonly string[] CompatOptions =
            { "Playable", "In-Game", "Menus", "Boots", "Nothing", CompatUnknown };

        /// <summary>
        /// Builds the RowFilter expression. Empty string = no filter.
        /// </summary>
        public static string BuildExpression(string? typeCategory, string? compatStatus, string? searchText)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(typeCategory))
                parts.Add($"[{PkgColumns.Category}] LIKE '%{Escape(typeCategory)}%'");

            if (!string.IsNullOrWhiteSpace(compatStatus))
            {
                if (compatStatus == CompatUnknown)
                    parts.Add($"([{PkgColumns.Shadps4}] IS NULL OR [{PkgColumns.Shadps4}] = '')");
                else
                    parts.Add($"[{PkgColumns.Shadps4}] = '{Escape(compatStatus)}'");
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string t = Escape(searchText);
                parts.Add($"([{PkgColumns.Filename}] LIKE '%{t}%' OR [{PkgColumns.Title}] LIKE '%{t}%' " +
                          $"OR [{PkgColumns.TitleId}] LIKE '%{t}%' OR [{PkgColumns.ContentId}] LIKE '%{t}%')");
            }

            return parts.Count == 0 ? "" : string.Join(" AND ", parts);
        }

        private static string Escape(string value) => value.Replace("'", "''");
    }
}
