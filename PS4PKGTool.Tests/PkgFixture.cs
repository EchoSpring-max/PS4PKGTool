using OrbisPkgTool.Pkg;
using OrbisPkgTool.Sfo;
using System.Text;

namespace PS4PKGTool.Tests;

/// <summary>Builds a real (fake-keyset) PKG via OrbisPkgTool.PkgBuilder,
/// the same fixture shape OrbisPkgTool's own regression suite uses.</summary>
internal sealed class PkgFixture : IDisposable
{
    private readonly string _root;

    private PkgFixture(string root) => _root = root;

    public string PackagePath { get; private set; } = string.Empty;

    /// <summary>Files that were written into Image0, with their exact bytes
    /// (for round-trip extraction assertions).</summary>
    public IReadOnlyList<(string Path, byte[] Data)> Files { get; private set; } =
        Array.Empty<(string, byte[])>();

    public static PkgFixture Create(string directoryName,
        params (string Path, int Size)[] files)
    {
        var random = new Random(0x5034);
        return CreateCore(directoryName, "game.pkg", null, files.Select(f =>
            (f.Path, Data: DataBytes(f.Size, random))).ToArray());
    }

    public static PkgFixture Create(string directoryName, string fileName,
        params (string Path, int Size)[] files)
    {
        var random = new Random(0x5034);
        return CreateCore(directoryName, fileName, null, files.Select(f =>
            (f.Path, Data: DataBytes(f.Size, random))).ToArray());
    }

    public static PkgFixture CreateWithPasscode(string directoryName, string passcode,
        params (string Path, int Size)[] files)
    {
        var random = new Random(0x5034);
        return CreateCore(directoryName, "game.pkg", passcode, files.Select(f =>
            (f.Path, Data: DataBytes(f.Size, random))).ToArray());
    }

    /// <summary>Creates a PKG that also carries the given Sc0 system files
    /// (icon0.png, pic0.png, pic1.png, trophy/trophy00.trp, ...) — they are
    /// written under Image0/sce_sys/ and PkgBuilder picks them up either from
    /// the GP4 listing or its sce_sys folder scan.</summary>
    public static PkgFixture CreateWithSceSys(string directoryName,
        params (string Path, byte[] Data)[] sceSysFiles)
    {
        return CreateCore(directoryName, "game.pkg", null,
            Array.Empty<(string Path, byte[] Data)>(), sceSysFiles);
    }

    private static PkgFixture CreateCore(string directoryName, string fileName,
        string? passcode, (string Path, byte[] Data)[] files,
        (string Path, byte[] Data)[]? sceSysFiles = null)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "p4t-fixture-" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(root, directoryName);
        string image0 = Path.Combine(dir, "Image0");
        Directory.CreateDirectory(image0);
        foreach (var (p, d) in files)
        {
            string full = Path.Combine(image0, p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, d);
        }
        // additional Sc0 system files (icon0.png, trophy/trophy00.trp, ...)
        if (sceSysFiles != null)
        {
            foreach (var (p, d) in sceSysFiles)
            {
                string full = Path.Combine(image0, "sce_sys", p.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, d);
            }
        }
        // mandatory sce_sys/param.sfo
        var sfo = ParamSfo.CreateGameTemplate("Fix", "CUSA09999",
            "EP0001-CUSA09999_00-FIX0000000000001");
        string sfoPath = Path.Combine(image0, "sce_sys", "param.sfo");
        Directory.CreateDirectory(Path.GetDirectoryName(sfoPath)!);
        File.WriteAllBytes(sfoPath, sfo.Serialize());

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<psproject fmt=\"gp4\" version=\"1.0\">");
        sb.AppendLine("  <volume><volume_type>pkg_ps4_app</volume_type><package>");
        sb.AppendLine("      <content_id>EP0001-CUSA09999_00-FIX0000000000001</content_id>");
        sb.AppendLine($"      <passcode>{passcode}</passcode>");
        sb.AppendLine("      <storage_type>digital25</storage_type><app_type>full</app_type>");
        sb.AppendLine("      <version>01.00</version><title_id>CUSA09999</title_id>");
        sb.AppendLine("      <title>Fix</title><app_version>01.00</app_version>");
        sb.AppendLine("    </package></volume>");
        sb.AppendLine("  <files>");
        sb.AppendLine("    <file><entry path=\"sce_sys/param.sfo\" /><orig_path>sce_sys/param.sfo</orig_path></file>");
        foreach (var (p, _) in files)
            sb.AppendLine($"    <file><entry path=\"{p}\" /><orig_path>{p}</orig_path></file>");
        sb.AppendLine("  </files>");
        sb.AppendLine("</psproject>");
        string gp4 = Path.Combine(dir, "project.gp4");
        File.WriteAllText(gp4, sb.ToString());

        string pkg = Path.Combine(dir, fileName);
        PkgBuilder.Build(gp4, image0, pkg, new BuildOptions());
        return new PkgFixture(root) { PackagePath = pkg, Files = files };
    }

    private static byte[] DataBytes(int size, Random random)
    {
        var data = new byte[size];
        random.NextBytes(data);
        return data;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
