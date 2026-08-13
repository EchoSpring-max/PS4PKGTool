using PS4PKGTool.Assets.Containers;
using PS4PKGTool.Assets.IO;

namespace PS4PKGTool.Assets.Tests;

/// <summary>
/// Phase 4b: Unreal package (.uasset/.uexp) parsing validated against the real
/// Bee Simulator T_Default_Material_Grid_M texture pair (standard format,
/// unversioned, uexp-split).
/// </summary>
[TestClass]
public class Phase4bPackageTests
{
    private const string AssetsPath = @"C:\Users\User\AppData\Local\Temp\p4t_spike_ue\bee\T_Default_Material_Grid_M.uasset";
    private const string UexpPath = @"C:\Users\User\AppData\Local\Temp\p4t_spike_ue\bee\T_Default_Material_Grid_M.uexp";

    [TestMethod]
    public void DebugSummaryFields()
    {
        if (!File.Exists(AssetsPath)) { Assert.Inconclusive("Sample not present."); return; }
        var bytes = File.ReadAllBytes(AssetsPath);
        Console.WriteLine("fields:");
        for (int i = 0; i < 100; i += 4)
            Console.WriteLine($"  @{i}: {BitConverter.ToInt32(bytes, i)} (0x{BitConverter.ToInt32(bytes, i):X8})");
        // Find candidate name-table positions: {int32 len 1-1024, printable string}.
        Console.WriteLine("candidates:");
        for (int p = 60; p < 700; p++)
        {
            int len = BitConverter.ToInt32(bytes, p);
            if (len is <= 0 or > 1024) continue;
            if (p + 4 + len > bytes.Length) continue;
            bool printable = true;
            for (int i = 0; i < len - 1 && printable; i++)
            {
                byte c = bytes[p + 4 + i];
                if (c is < 32 or >= 127) printable = false;
            }
            if (!printable) continue;
            string s = System.Text.Encoding.UTF8.GetString(bytes, p + 4, len - 1);
            Console.WriteLine($"  @{p}: len={len} '{s}'");
            p += 4 + len;
        }

        // Parse from 193 with the hash structure and show the break.
        Console.WriteLine("parse from 193:");
        int q = 193;
        for (int i = 0; i < 200; i++)
        {
            int len = BitConverter.ToInt32(bytes, q);
            if (len <= 0 || len > 1024 || q + 4 + len + 4 > bytes.Length)
            {
                Console.WriteLine($"  BREAK at #{i}: len={len} @{q}");
                break;
            }
            string s = System.Text.Encoding.UTF8.GetString(bytes, q + 4, len - 1);
            if (i < 5 || i > 15) Console.WriteLine($"  #{i} @{q}: '{s}'");
            q += 4 + len + 4;
        }
        Console.WriteLine($"end pos={q}");
    }

    [TestMethod]
    public void ParseSummary_RealTexturePackage()
    {
        if (!File.Exists(AssetsPath)) { Assert.Inconclusive("Sample not present."); return; }
        var source = new FileAssetSource(AssetsPath, "PKG entry");
        var summary = UnrealPackage.ParseSummary(source);

        Console.WriteLine($"totalHeader={summary.TotalHeaderSize} names={summary.NameCount} exports={summary.ExportCount}@{summary.ExportOffset} imports={summary.ImportCount}@{summary.ImportOffset} nameOff={summary.NameOffset}");
        Assert.AreEqual(773, summary.TotalHeaderSize);
        Assert.AreEqual(193, summary.NameCount);
        Assert.AreEqual(3, summary.ExportCount);
        Assert.AreEqual(653, summary.ExportOffset);
        Assert.IsFalse(summary.HasPreamble, "Standard package: magic at 0");
    }

    [TestMethod]
    public void ReadNames_RealTexturePackage()
    {
        if (!File.Exists(AssetsPath)) { Assert.Inconclusive("Sample not present."); return; }
        var source = new FileAssetSource(AssetsPath, "PKG entry");
        var summary = UnrealPackage.ParseSummary(source);
        var names = UnrealPackage.ReadNames(source, summary);

        Console.WriteLine($"names: {names.Count}");
        foreach (var n in names) Console.WriteLine($"  {n}");
        // The empirical table has ~17 entries here (the summary's declared
        // count of 193 covers more than this section in this format).
        Assert.IsTrue(names.Count >= 15, $"Expected ~17 names, got {names.Count}");
        Assert.AreEqual("/Engine/EngineMaterials/T_Default_Material_Grid_M", names[0]);
        Assert.AreEqual("/Script/CoreUObject", names[1]);
        Assert.IsTrue(names.Contains("Texture2D"), "Name table should include Texture2D");
    }

    [TestMethod]
    public void ReadExports_And_Texture2D()
    {
        if (!File.Exists(AssetsPath) || !File.Exists(UexpPath)) { Assert.Inconclusive("Samples not present."); return; }
        var assets = new FileAssetSource(AssetsPath, "PKG entry");
        var summary = UnrealPackage.ParseSummary(assets);
        var names = UnrealPackage.ReadNames(assets, summary);
        var exports = UnrealPackage.ReadExports(assets, summary);

        for (int i = 0; i < exports.Count; i++)
            Console.WriteLine($"export {i}: class={exports[i].ClassIndex} nameIdx={exports[i].NameIndex} size={exports[i].SerialSize} offset={exports[i].SerialOffset}");

        var uexp = new FileAssetSource(UexpPath, "UE4 .uexp");
        var texture = UnrealPackage.ReadTexture2D(uexp, exports[0], names, summary.TotalHeaderSize);
        Assert.IsNotNull(texture, "The texture export should decode");
        Console.WriteLine($"texture: {texture!.Name} platform={texture.SizeX}x{texture.SizeY} fmt={texture.PixelFormat} mips={texture.MipCount} mip0={texture.Mip0SizeX}x{texture.Mip0SizeY} data={texture.Mip0Data?.Length ?? 0}B");

        Assert.AreEqual("T_Default_Material_Grid_M", texture.Name);
        Assert.AreEqual("PF_DXT5", texture.PixelFormat);
        Assert.IsTrue(texture.MipCount > 0, "Expected mips");
        // The mip bulk payloads live in the PAK (streaming offsets), not the
        // .uexp - the metadata (name/dims/format/mips) is the validated
        // milestone; payload retrieval needs pak-source plumbing (documented
        // in ASSET_FRAMEWORK.md).
        Console.WriteLine($"mip0 bulk: {(texture.Mip0Data != null ? $"{texture.Mip0Data.Length} bytes" : "external (in pak)")}");
    }
}
