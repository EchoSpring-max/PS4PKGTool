using PS4PKGTool.Assets.Abstractions;

namespace PS4PKGTool.Assets.IO;

/// <summary>IAssetSource over a physical filesystem file.</summary>
public sealed class FileAssetSource : IAssetSource
{
    private readonly string _path;

    public FileAssetSource(string path, string? sourceDescription = "filesystem file")
    {
        _path = path;
        Name = Path.GetFileName(path);
        Length = new FileInfo(path).Length;
        SourceDescription = sourceDescription;
    }

    public string Name { get; }
    public long Length { get; }
    public string? SourceDescription { get; }

    public Stream OpenRead() => new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public Stream OpenRead(long offset, long length)
    {
        var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            fs.Seek(offset, SeekOrigin.Begin);
            return new BoundedReadStream(fs, length, leaveOpen: false);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }
}
