namespace SdvTestFramework.Harness.Assets;

/// <summary>Small seam for testing runtime content loading without launching Stardew.</summary>
public interface IContentAssetLoader
{
    bool TryLoad<T>(string name, out T? asset) where T : notnull;
}
