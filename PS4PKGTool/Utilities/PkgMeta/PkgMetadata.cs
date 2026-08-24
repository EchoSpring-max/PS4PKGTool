using System.Collections.Generic;
using OrbisPkgTool.Pkg;
using OrbisPkgTool.Sfo;

namespace PS4PKGTool.Utilities.PkgMeta;

/// <summary>
/// Local port of PS4_Tools.PKG.SceneRelated.PKGType. Values are compared as
/// strings against PKGCategory.* constants ("Game", "Patch", "Addon", ...),
/// so the member names must stay identical.
/// </summary>
public enum PkgKind
{
    Game,
    App,
    Addon,
    Patch,
    Unknown,
}

/// <summary>Local port of PS4_Tools.PKG.SceneRelated.PKG_State.</summary>
public enum PkgBuildState
{
    Fake = 0,
    Official = 1,
    Official_DP = 2,
    Unknown = 99,
}

/// <summary>
/// Read-only package metadata matching the shape of
/// PS4_Tools.PKG.SceneRelated.Unprotected_PKG that PS4PKGTool actually
/// consumes. Fully materialized (no open file handles), exactly like the
/// legacy eager Read_PKG loader.
/// </summary>
public sealed class PkgMetadata
{
    public required PkgHeader Header { get; init; }
    public required PkgBuildState BuildState { get; init; }
    public required PkgKind Kind { get; init; }
    public required string ContentId { get; init; }
    public string? RawTitle { get; init; }
    public ParamSfo? ParamSfo { get; init; }
    public IReadOnlyList<SfoTable>? SfoTables { get; init; }
    public byte[]? Icon { get; init; }
    public byte[]? Pic0 { get; init; }
    public byte[]? Pic1 { get; init; }
    public byte[]? TrpData { get; init; }

    /// <summary>Legacy PS4_Title = RawTitle ?? Param.Title.</summary>
    public string PS4_Title => RawTitle ?? ParamSfo?.GetString("TITLE") ?? "";

    /// <summary>Legacy Content_ID = Header.pkg_content_id.</summary>
    public string Content_ID => ContentId;

    /// <summary>
    /// Legacy PKG_Type = GetPkgType(Param.Category); the local Category
    /// string, not OrbisPkgTool.PkgInfo.Type (whose addon classification
    /// differs: Dlc/Wallpaper/Theme).
    /// </summary>
    public PkgKind PKG_Type => Kind;

    /// <summary>Legacy PKGState property.</summary>
    public PkgBuildState PKGState => BuildState;

    /// <summary>
    /// Legacy Region: first char of the content id → region name
    /// (E:EU, U/I:US, J:JAPAN, H:HONG KONG, A:ASIA, K:KOREA, else "").
    /// </summary>
    public string Region =>
        ContentId.Length == 0 ? "" :
        ContentId[0] switch
        {
            'E' => "EU",
            'U' or 'I' => "US",
            'J' => "JAPAN",
            'H' => "HONG KONG",
            'A' => "ASIA",
            'K' => "KOREA",
            _ => "",
        };

    // ── SFO field accessors (legacy Param.* shape) ─────────────────────

    public string TITLEID => ParamSfo?.GetString("TITLE_ID") ?? "";
    public string APP_VER => ParamSfo?.GetString("APP_VER") ?? "";
    public string SfoContentId => ParamSfo?.GetString("CONTENT_ID") ?? "";
    public string Category => ParamSfo?.GetString("CATEGORY") ?? "";

    /// <summary>
    /// Legacy GetPkgType port: param.sfo CATEGORY → kind. "gde"/"gdk" → App,
    /// "gd" → Game, "ac" → Addon, "gp" → Patch, else Unknown.
    /// </summary>
    public static PkgKind GetPkgType(string category) => category switch
    {
        "gde" or "gdk" => PkgKind.App,
        "gd" => PkgKind.Game,
        "ac" => PkgKind.Addon,
        "gp" => PkgKind.Patch,
        _ => PkgKind.Unknown,
    };
}
