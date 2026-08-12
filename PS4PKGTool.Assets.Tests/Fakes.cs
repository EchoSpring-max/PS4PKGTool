using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.Models;

namespace PS4PKGTool.Assets.Tests;

/// <summary>In-memory source (test double) that counts how many bytes were ever materialized.</summary>
public sealed class MemoryAssetSource : IAssetSource
{
    private readonly byte[] _data;

    public MemoryAssetSource(byte[] data, string name = "mem", string? description = "memory source")
    {
        _data = data;
        Name = name;
        SourceDescription = description;
    }

    public long BytesMaterialized { get; private set; }

    public string Name { get; }
    public long Length => _data.Length;
    public string? SourceDescription { get; }

    public Stream OpenRead()
    {
        BytesMaterialized += _data.Length;
        return new MemoryStream(_data);
    }

    public Stream OpenRead(long offset, long length)
    {
        BytesMaterialized += length;
        return new MemoryStream(_data, (int)offset, (int)length);
    }
}

/// <summary>
/// A source that CLAIMS a huge virtual length (e.g. 4 GB) but is backed by a
/// tiny real buffer. Any full OpenRead() would over-read — bounded slices are
/// the only valid access. Tracks bytes materialized to prove no full allocation.
/// </summary>
public sealed class VirtualAssetSource : IAssetSource
{
    private readonly byte[] _backing;

    public VirtualAssetSource(byte[] backing, long virtualLength, string name = "virtual", string? description = "virtual source")
    {
        _backing = backing;
        Length = virtualLength;
        Name = name;
        SourceDescription = description;
    }

    public long BytesMaterialized { get; private set; }
    public string Name { get; }
    public long Length { get; }
    public string? SourceDescription { get; }

    public Stream OpenRead()
    {
        BytesMaterialized += Length; // record the (forbidden) full read
        return new MemoryStream(_backing);
    }

    public Stream OpenRead(long offset, long length)
    {
        BytesMaterialized += length;
        if (offset >= _backing.Length) return new MemoryStream(Array.Empty<byte>());
        int take = (int)Math.Min(length, _backing.Length - offset);
        return new MemoryStream(_backing, (int)offset, take);
    }
}

/// <summary>Fake detector for the "sample" format — always claims it with fixed evidence.</summary>
public sealed class SampleDetector : IAssetDetector
{
    public int Order => 0;
    public AssetDetectionResult? Detect(IAssetSource source, CancellationToken ct = default)
        => source.Name.EndsWith(".sample", StringComparison.OrdinalIgnoreCase)
            ? new AssetDetectionResult { Format = "sample", Confidence = 0.9, Evidence = new[] { "extension .sample" } }
            : null;
}

/// <summary>Fake handler that reports capabilities and exposes children for container tests.</summary>
public sealed class SampleHandler : IAssetHandler
{
    private readonly Func<AssetDetectionResult, IReadOnlyList<IAssetSource>>? _children;

    public SampleHandler(bool container = false, Func<AssetDetectionResult, IReadOnlyList<IAssetSource>>? children = null)
    {
        IsContainerResult = container;
        _children = children;
    }

    public bool IsContainerResult { get; }
    public string Format => "sample";
    public int InspectCalls { get; private set; }

    public AssetCapabilities GetCapabilities(AssetDetectionResult detection)
        => IsContainerResult
            ? AssetCapabilities.Inspect | AssetCapabilities.Browse | AssetCapabilities.ExportRaw
            : AssetCapabilities.Inspect | AssetCapabilities.Preview | AssetCapabilities.Decode | AssetCapabilities.ExportRaw | AssetCapabilities.ExportConverted;

    public bool IsContainer(AssetDetectionResult detection) => IsContainerResult;

    public Task<AssetDescriptor> InspectAsync(IAssetSource source, AssetDetectionResult detection, CancellationToken ct = default)
    {
        InspectCalls++;
        return Task.FromResult(new AssetDescriptor
        {
            Name = source.Name,
            Format = Format,
            Size = source.Length,
            Capabilities = GetCapabilities(detection),
            SourceDescription = source.SourceDescription,
            Metadata = new Dictionary<string, string> { ["fake"] = "true" },
        });
    }

    public Task<IReadOnlyList<IAssetSource>> GetChildrenAsync(IAssetSource source, AssetDetectionResult detection, int depth, CancellationToken ct = default)
        => Task.FromResult(_children?.Invoke(detection) ?? (IReadOnlyList<IAssetSource>)Array.Empty<IAssetSource>());
}

/// <summary>Fake handler that always throws CorruptAssetException — for error isolation tests.</summary>
public sealed class CorruptHandler : IAssetHandler
{
    public string Format => "corrupt";
    public AssetCapabilities GetCapabilities(AssetDetectionResult detection) => AssetCapabilities.Inspect | AssetCapabilities.ExportRaw;
    public bool IsContainer(AssetDetectionResult detection) => false;

    public Task<AssetDescriptor> InspectAsync(IAssetSource source, AssetDetectionResult detection, CancellationToken ct = default)
        => throw new CorruptAssetException("simulated corrupt input");

    public Task<IReadOnlyList<IAssetSource>> GetChildrenAsync(IAssetSource source, AssetDetectionResult detection, int depth, CancellationToken ct = default)
        => throw new CorruptAssetException("simulated corrupt container");
}

/// <summary>Detector for the "corrupt" format.</summary>
public sealed class CorruptDetector : IAssetDetector
{
    public int Order => 0;
    public AssetDetectionResult? Detect(IAssetSource source, CancellationToken ct = default)
        => source.Name.EndsWith(".corrupt", StringComparison.OrdinalIgnoreCase)
            ? new AssetDetectionResult { Format = "corrupt", Confidence = 0.9 }
            : null;
}

/// <summary>Container detector that exposes child ".sample" sources.</summary>
public sealed class ContainerDetector : IAssetDetector
{
    public int Order => 0;
    public AssetDetectionResult? Detect(IAssetSource source, CancellationToken ct = default)
        => source.Name.EndsWith(".pack", StringComparison.OrdinalIgnoreCase)
            ? new AssetDetectionResult { Format = "pack", Confidence = 1.0, Evidence = new[] { "extension .pack" } }
            : null;
}

public sealed class PackHandler : IAssetHandler
{
    private readonly Func<int, IReadOnlyList<IAssetSource>> _childrenByDepth;

    public PackHandler(Func<int, IReadOnlyList<IAssetSource>> childrenByDepth)
    {
        _childrenByDepth = childrenByDepth;
        Format = "pack";
    }

    public string Format { get; }
    public AssetCapabilities GetCapabilities(AssetDetectionResult detection) => AssetCapabilities.Inspect | AssetCapabilities.Browse | AssetCapabilities.ExportRaw;
    public bool IsContainer(AssetDetectionResult detection) => true;

    public Task<AssetDescriptor> InspectAsync(IAssetSource source, AssetDetectionResult detection, CancellationToken ct = default)
        => Task.FromResult(new AssetDescriptor
        {
            Name = source.Name,
            Format = Format,
            Size = source.Length,
            Capabilities = GetCapabilities(detection),
        });

    public Task<IReadOnlyList<IAssetSource>> GetChildrenAsync(IAssetSource source, AssetDetectionResult detection, int depth, CancellationToken ct = default)
        => Task.FromResult(_childrenByDepth(depth));
}
