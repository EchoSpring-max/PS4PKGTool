using System;
using System.Globalization;

namespace PS4PKGTool.Utilities.PkgMeta
{
    /// <summary>Converts a SYSTEM_VER SFO value to its user-facing firmware version.</summary>
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
    }
}
