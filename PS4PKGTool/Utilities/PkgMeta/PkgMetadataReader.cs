using System;
using System.IO;
using System.Linq;
using OrbisPkgTool;
using OrbisPkgTool.Pkg;
using OrbisPkgTool.Trp;

namespace PS4PKGTool.Utilities.PkgMeta;

/// <summary>
/// Loads <see cref="PkgMetadata"/> from a PKG file — the drop-in replacement
/// for PS4_Tools.PKG.SceneRelated.Read_PKG. Metadata (SFO, icon/pic artwork,
/// trophy TRP) is fully materialized eagerly, exactly like the legacy eager
/// loader; no file handles remain open after Read returns.
/// </summary>
public static class PkgMetadataReader
{
    public static PkgMetadata Read(string pkgPath)
    {
        using var reader = new PkgReader(pkgPath);
        return Read(reader);
    }

    /// <summary>Reads metadata through an already-open reader (callers that
    /// need the reader afterwards, e.g. late trophy extraction).</summary>
    public static PkgMetadata Read(PkgReader reader)
    {
        var header = reader.Header;
        var sfo = reader.ReadParamSfo();

        var buildState = DetectBuildState(header);
        string rawTitle = sfo?["TITLE"]?.StringValue;
        var kind = PkgMetadata.GetPkgType(sfo?.GetString("CATEGORY") ?? "");

        byte[]? icon = null, pic0 = null, pic1 = null, trp = null;
        FindEntry(reader, PkgEntryIds.Icon0Png, ref icon);
        FindEntry(reader, PkgEntryIds.Pic0Png, ref pic0);
        FindEntry(reader, PkgEntryIds.Pic1Png, ref pic1);
        FindEntry(reader, PkgEntryIds.Trophy00Trp, ref trp);

        // Legacy fallback: no icon0.png → pull ICON0.PNG out of the trophy pack.
        if ((icon == null || icon.Length == 0) && trp is { Length: > 0 })
        {
            try
            {
                icon = Trp.Read(trp)
                    .FirstOrDefault(e => e.Name.Equals("ICON0.PNG", StringComparison.OrdinalIgnoreCase))
                    ?.Data;
                if (icon is { Length: 0 })
                    icon = null;
            }
            catch
            {
                // A damaged trophy pack must not fail the whole load.
            }
        }

        return new PkgMetadata
        {
            Header = header,
            BuildState = buildState,
            Kind = kind,
            ContentId = header.ContentId,
            RawTitle = rawTitle,
            ParamSfo = sfo,
            SfoTables = sfo?.Tables,
            Icon = icon,
            Pic0 = pic0,
            Pic1 = pic1,
            TrpData = trp,
        };
    }

    private static void FindEntry(PkgReader reader, uint entryId, ref byte[]? slot)
    {
        try
        {
            var data = reader.ExtractEntryBytes(entryId);
            if (data.Length > 0)
                slot = data;
        }
        catch (FileNotFoundException)
        {
            // Entry absent — matches legacy (fields stay null).
        }
    }

    /// <summary>
    /// Official-pkg probes from the legacy reader (decompiled
    /// Read_PKG(Stream)): u16BE@0x04 ∈ {0x8300, 0x8100} → official;
    /// u16BE@0x77 == 0x1E43 (7747) → Official_DP — and the DP probe wins
    /// when both match (legacy: if (flag) official = true; then
    /// num != 7747 ? Official : Official_DP).
    ///
    /// 0x77 straddles header fields: byte 0x77 is the low byte of
    /// ContentType (0x74..0x77) and byte 0x78 the high byte of ContentFlags
    /// (0x78..0x7B). u16BE(0x77) = (b[0x77] &lt;&lt; 8) | b[0x78]
    /// = ((ContentType &amp; 0xFF) &lt;&lt; 8) | (ContentFlags &gt;&gt; 24).
    /// </summary>
    internal static PkgBuildState DetectBuildState(PkgHeader header)
    {
        ushort dpProbe = (ushort)(((header.ContentType & 0xFF) << 8) | (header.ContentFlags >> 24));
        if (dpProbe == 0x1E43)
            return PkgBuildState.Official_DP;

        // u16BE@0x04 = high half of the u32 flags/type field.
        ushort typeProbe = (ushort)(header.Flags >> 16);
        if (typeProbe == 0x8300 || typeProbe == 0x8100)
            return PkgBuildState.Official;

        return PkgBuildState.Fake;
    }
}
