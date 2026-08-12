using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Detection;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.IO;
using PS4PKGTool.Assets.Models;
using PS4PKGTool.Assets.Registry;

namespace PS4PKGTool.Assets.Tests;

[TestClass]
public class Phase0AcceptanceTests
{
    private static (AssetInspectionService service, AssetDetectorRegistry detectors, AssetHandlerRegistry handlers) CreateService(int maxDepth = AssetInspectionService.DefaultMaxDepth)
    {
        var detectors = new AssetDetectorRegistry();
        var handlers = new AssetHandlerRegistry();
        var service = new AssetInspectionService(detectors, handlers, maxDepth);
        return (service, detectors, handlers);
    }

    // ── Test A: physical file → detector → handler → metadata ────────────
    [TestMethod]
    public async Task TestA_File_Detect_Inspect_Metadata()
    {
        var (service, detectors, handlers) = CreateService();
        detectors.Register(new SampleDetector());
        handlers.Register(new SampleHandler());

        string path = Path.Combine(Path.GetTempPath(), $"p4t0_a_{Guid.NewGuid():N}.sample");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        try
        {
            var source = new FileAssetSource(path);
            var descriptor = await service.InspectAsync(source);

            Assert.IsNotNull(descriptor);
            Assert.AreEqual("sample", descriptor!.Format);
            Assert.AreEqual(4, descriptor.Size);
            Assert.AreEqual("true", descriptor.Metadata["fake"]);
        }
        finally { File.Delete(path); }
    }

    // ── Test B: parent container → child source → detector → child handler ─
    [TestMethod]
    public async Task TestB_Container_Child_Inspect()
    {
        var (service, detectors, handlers) = CreateService();
        detectors.Register(new SampleDetector());
        detectors.Register(new ContainerDetector());
        handlers.Register(new SampleHandler());
        handlers.Register(new PackHandler(_ => new IAssetSource[]
        {
            new MemoryAssetSource(new byte[] { 9, 9 }, "inner.sample", "pack member"),
        }));

        var pack = new MemoryAssetSource(new byte[] { 0, 0, 0, 0 }, "game.pack", "PKG entry");
        var packDescriptor = await service.InspectAsync(pack);
        Assert.AreEqual("pack", packDescriptor!.Format);

        var children = await service.GetChildrenAsync(pack, new AssetDetectionResult { Format = "pack" }, depth: 0);
        Assert.AreEqual(1, children.Count);

        var childDescriptor = await service.InspectAsync(children[0]);
        Assert.AreEqual("sample", childDescriptor!.Format);
        Assert.AreEqual("pack member", childDescriptor.SourceDescription);
    }

    // ── Test C: nested container A → container B → asset ─────────────────
    [TestMethod]
    public async Task TestC_NestedContainers()
    {
        var (service, detectors, handlers) = CreateService();
        detectors.Register(new SampleDetector());
        detectors.Register(new ContainerDetector());
        handlers.Register(new SampleHandler());
        handlers.Register(new PackHandler(_ => new IAssetSource[]
        {
            new MemoryAssetSource(new byte[] { 1 }, "level2.pack", "nested container"),
        }));
        handlers.Register(new PackHandler(_ => new IAssetSource[]
        {
            new MemoryAssetSource(new byte[] { 2 }, "deep.sample", "leaf asset"),
        }));

        // Simulate two container levels manually (distinct handlers per level).
        var root = new MemoryAssetSource(new byte[] { 0 }, "root.pack");
        var level2 = (await service.GetChildrenAsync(root, new AssetDetectionResult { Format = "pack" }, depth: 0))[0];
        var leaf = (await service.GetChildrenAsync(level2, new AssetDetectionResult { Format = "pack" }, depth: 1))[0];

        var descriptor = await service.InspectAsync(leaf);
        Assert.AreEqual("sample", descriptor!.Format);
        Assert.AreEqual("leaf asset", descriptor.SourceDescription);
    }

    // ── Test D: multi-GB simulated source — no full-size allocation ───────
    [TestMethod]
    public async Task TestD_LargeVirtualSource_NoFullAllocation()
    {
        // A 4 GB virtual source backed by 4 real bytes: detection and
        // inspection must touch only bounded slices, never 4 GB.
        var huge = new VirtualAssetSource(new byte[] { 1, 2, 3, 4 }, 4L * 1024 * 1024 * 1024, "huge.sample", "virtual 4GB");

        var (service, detectors, handlers) = CreateService();
        detectors.Register(new SampleDetector());
        handlers.Register(new SampleHandler());

        var descriptor = await service.InspectAsync(huge);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual(4L * 1024 * 1024 * 1024, descriptor!.Size);
        // Inspection must not have materialized the full 4 GB — bounded reads only.
        Assert.IsTrue(huge.BytesMaterialized < 4L * 1024 * 1024, $"materialized {huge.BytesMaterialized} bytes");
    }

    // ── Test E: cancellation ──────────────────────────────────────────────
    [TestMethod]
    public async Task TestE_Cancellation()
    {
        var (service, detectors, handlers) = CreateService();
        detectors.Register(new SampleDetector());
        handlers.Register(new SampleHandler());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var source = new MemoryAssetSource(new byte[] { 1, 2, 3 }, "x.sample");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.InspectAsync(source, cts.Token));
    }

    // ── Test F: malformed input never propagates a fatal exception ────────
    [TestMethod]
    public async Task TestF_MalformedInput_StructuredError()
    {
        var (service, detectors, handlers) = CreateService();
        detectors.Register(new CorruptDetector());
        handlers.Register(new CorruptHandler());

        var source = new MemoryAssetSource(new byte[] { 0xFF, 0xFF }, "bad.corrupt");
        var ex = await Assert.ThrowsAsync<CorruptAssetException>(
            () => service.InspectAsync(source));

        // Structured error, not a raw exception type.
        Assert.IsInstanceOfType<AssetException>(ex);
    }

    // ── Test G: unknown format still permits raw export ───────────────────
    [TestMethod]
    public async Task TestG_UnknownFormat_RawExport()
    {
        var (service, detectors, handlers) = CreateService();
        detectors.Register(new SampleDetector()); // only knows .sample
        detectors.Register(new ExtensionFallbackDetector()); // catches everything else
        handlers.Register(new SampleHandler());

        var source = new MemoryAssetSource(new byte[] { 5, 6, 7, 8 }, "mystery.xyz");
        var descriptor = await service.InspectAsync(source);

        Assert.IsNotNull(descriptor);
        Assert.AreEqual("xyz", descriptor!.Format);            // extension fallback
        Assert.IsTrue(descriptor.Capabilities.HasFlag(AssetCapabilities.ExportRaw));
        Assert.IsFalse(descriptor.Capabilities.HasFlag(AssetCapabilities.Preview));

        string outPath = Path.Combine(Path.GetTempPath(), $"p4t0_g_{Guid.NewGuid():N}.bin");
        try
        {
            await RawAssetExporter.Instance.ExportRawAsync(source, descriptor, outPath);
            CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 8 }, File.ReadAllBytes(outPath));
        }
        finally { File.Delete(outPath); }
    }
}
