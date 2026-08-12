using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Models;

namespace PS4PKGTool.Assets.Registry;

/// <summary>Maps detected formats to handlers. Unknown formats resolve to null —
/// callers fall back to Inspect + ExportRaw.</summary>
public sealed class AssetHandlerRegistry
{
    private readonly Dictionary<string, IAssetHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAssetHandler handler) => _handlers[handler.Format] = handler;

    public IAssetHandler? Get(string format) => _handlers.TryGetValue(format, out var h) ? h : null;

    public bool TryGet(string format, out IAssetHandler? handler) => _handlers.TryGetValue(format, out handler);

    public IReadOnlyCollection<string> Formats => _handlers.Keys;
}
