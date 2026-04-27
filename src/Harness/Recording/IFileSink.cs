namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Abstraction over "write these bytes to this path". Real impl does <c>File.WriteAllText</c>;
/// tests substitute a collecting shim so no actual disk writes happen during unit tests.
/// </summary>
public interface IFileSink
{
    /// <summary>Write UTF-8 text to the given absolute path. Creates parent directories as needed.</summary>
    void Write(string path, string contents);
}
