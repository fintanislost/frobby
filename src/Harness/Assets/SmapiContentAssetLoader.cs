using System;
using Microsoft.Xna.Framework.Content;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Assets;

public sealed class SmapiContentAssetLoader : IContentAssetLoader
{
    private readonly IGameContentHelper _content;

    public SmapiContentAssetLoader(IGameContentHelper content)
        => _content = content;

    public bool TryLoad<T>(string name, out T? asset) where T : notnull
    {
        try
        {
            var parsed = _content.ParseAssetName(name);
            if (!_content.DoesAssetExist<T>(parsed))
            {
                asset = default;
                return false;
            }

            asset = _content.Load<T>(parsed);
            return true;
        }
        catch (ContentLoadException)
        {
            asset = default;
            return false;
        }
        catch (ArgumentException)
        {
            asset = default;
            return false;
        }
        catch (InvalidCastException)
        {
            asset = default;
            return false;
        }
    }
}
