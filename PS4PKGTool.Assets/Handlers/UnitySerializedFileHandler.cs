using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Containers;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.Models;
using PS4PKGTool.Assets.Unity;

namespace PS4PKGTool.Assets.Handlers;

/// <summary>
/// Unity serialized file (.assets) handler. Detects, inspects, enumerates
/// objects as child sources (Browse), and previews: the whole file renders as
/// an object listing, a Texture2D child decodes to a texture preview. The
/// parsing implementation is isolated behind IUnityAssetBackend - this handler
/// never sees parser types.
/// </summary>
public sealed class UnitySerializedFileHandler : IAssetHandler, IAssetPreviewProvider
{
    public const string FormatId = "unity-serialized-file";

    // Class ids that carry user-visible content worth browsing.
    private static readonly int[] BrowseClasses = { 28, 43, 49, 83, 213 }; // Texture2D, Mesh, TextAsset, AudioClip, Sprite

    private readonly IUnityAssetBackend _backend;

    public UnitySerializedFileHandler() : this(new AssetStudioBackend()) { }

    public UnitySerializedFileHandler(IUnityAssetBackend backend) => _backend = backend;

    public string Format => FormatId;

    public AssetCapabilities GetCapabilities(AssetDetectionResult detection)
        => AssetCapabilities.Inspect | AssetCapabilities.Browse | AssetCapabilities.Preview | AssetCapabilities.ExportRaw;

    public bool IsContainer(AssetDetectionResult detection) => true;

    public Task<AssetDescriptor> InspectAsync(IAssetSource source, AssetDetectionResult detection, CancellationToken ct = default)
    {
        var objects = _backend.ListObjects(source);
        var byClass = objects.GroupBy(o => o.ClassId).OrderByDescending(g => g.Count()).Take(6);

        var meta = new Dictionary<string, string>
        {
            ["Objects"] = objects.Count.ToString(),
            ["Backend"] = _backend.Name,
        };
        foreach (var g in byClass)
            meta[UnitySerializedFile.ClassIdToName(g.Key)] = g.Count().ToString();

        return Task.FromResult(new AssetDescriptor
        {
            Name = source.Name,
            Format = FormatId,
            Engine = "unity",
            Size = source.Length,
            Capabilities = GetCapabilities(detection),
            SourceDescription = source.SourceDescription,
            Metadata = meta,
        });
    }

    public Task<AssetPreview?> PreviewAsync(IAssetSource source, AssetDetectionResult detection, CancellationToken ct = default)
    {
        if (source is UnityObjectAssetSource objectSource)
            return PreviewTextureChildAsync(objectSource);

        // Whole file: object listing as a text preview.
        var objects = _backend.ListObjects(source);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Unity serialized file: {objects.Count} objects");
        sb.AppendLine($"Backend: {_backend.Name}");
        sb.AppendLine();

        foreach (var g in objects.GroupBy(o => o.ClassId).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {UnitySerializedFile.ClassIdToName(g.Key)} x{g.Count()}");

        sb.AppendLine();
        int shown = 0;
        foreach (var o in objects.Where(o => BrowseClasses.Contains(o.ClassId)))
        {
            if (shown++ >= 200) { sb.AppendLine("  ..."); break; }
            sb.AppendLine($"  {o.Name} (pathId {o.PathId}, {o.Size} bytes)");
        }
        return Task.FromResult<AssetPreview?>(AssetPreview.ForText(sb.ToString()));
    }

    private Task<AssetPreview?> PreviewTextureChildAsync(UnityObjectAssetSource objectSource)
    {
        var tex = _backend.ReadTexture2D(objectSource.Parent, objectSource.Object);
        if (tex == null) return Task.FromResult<AssetPreview?>(null);

        if (tex.InlineBytes is not { Length: > 0 })
            throw new UnsupportedAssetException(
                $"Texture '{tex.Name}' streams its data to '{tex.StreamPath}' - preview the .resS companion file instead.");

        var decoded = _backend.DecodeTexture(tex);
        if (decoded == null)
            throw new UnsupportedAssetException(
                $"Texture format {tex.Format} is not decodeable yet - metadata only, raw export available.");

        var preview = new AssetPreview
        {
            Texture = decoded,
            Info = $"{tex.Name} ({tex.Width}x{tex.Height}, {FormatName(tex.Format)}, {tex.MipCount} mip{(tex.MipCount == 1 ? "" : "s")})",
        };
        return Task.FromResult<AssetPreview?>(preview);
    }

    private static string FormatName(int format) => format switch
    {
        1 => "Alpha8", 2 => "ARGB4444", 3 => "RGB24", 4 => "RGBA32", 5 => "ARGB32",
        7 => "RGB565", 8 => "BGR24", 9 => "R16", 10 => "DXT1", 11 => "DXT3",
        12 => "DXT5", 13 => "RGBA4444", 14 => "BGRA32",
        _ => $"format {format}",
    };

    public Task<IReadOnlyList<IAssetSource>> GetChildrenAsync(IAssetSource source, AssetDetectionResult detection, int depth, CancellationToken ct = default)
    {
        var objects = _backend.ListObjects(source);
        var children = objects
            .Where(o => BrowseClasses.Contains(o.ClassId))
            .Select(o => (IAssetSource)new UnityObjectAssetSource(source, o))
            .ToList();
        return Task.FromResult<IReadOnlyList<IAssetSource>>(children);
    }
}
