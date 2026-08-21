using System;
using System.Globalization;

namespace PS4PKGTool.Utilities.PkgMeta
{
    /// <summary>Converts PS4 SFO firmware fields to their user-facing version.</summary>
    public static class PkgSystemVersion
    {
        public static string Format(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "0" || value == "NA")
                return value ?? string.Empty;

            if (value.Contains(".") && value.Length <= 5)
                return value;

            string text = value.Trim();
            NumberStyles style = NumberStyles.Integer;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
                style = NumberStyles.AllowHexSpecifier;
            }

            if (!uint.TryParse(text, style, CultureInfo.InvariantCulture, out uint encoded))
                return value;

            // Some readers return 0xMMmm; others return the full 0xMMmm0000 SFO value.
            uint compact = encoded <= ushort.MaxValue ? encoded : encoded >> 16;
            return $"{(compact >> 8) & 0xFF}.{compact & 0xFF:D2}";
        }

        /// <summary>Formats PUBTOOLINFO's hexadecimal sdk_ver field (for example 09500000 → 9.50).</summary>
        public static string FormatSdkVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (!uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint encoded))
                return string.Empty;

            uint compact = encoded >> 16;
            return $"{(compact >> 8) & 0xFF}.{compact & 0xFF:D2}";
        }
    }
}
