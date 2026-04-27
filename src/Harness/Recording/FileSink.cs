using System.IO;

namespace SdvTestFramework.Harness.Recording;

/// <summary>Production <see cref="IFileSink"/> backed by <see cref="File.WriteAllText(string,string)"/>.</summary>
public sealed class FileSink : IFileSink
{
    public void Write(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, contents);
    }
}
