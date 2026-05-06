using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ContentAssetHandlerTests
{
    [Fact]
    public void Handle_WithoutLoader_ThrowsGameStateInvalid()
    {
        ContentAssetHandler.Loader = null;
        var p = JsonDocument.Parse("{\"name\":\"Data/Locations\",\"asset_type\":\"data\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => ContentAssetHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("content loader", ex.Message);
    }

    [Fact]
    public void Handle_WithLoader_ReturnsProjectedAsset()
    {
        var loader = new FakeLoader();
        loader.Add("Data/Test", new System.Collections.Generic.Dictionary<string, string>
        {
            ["Alpha"] = "One",
        });
        ContentAssetHandler.Loader = loader;
        var p = JsonDocument.Parse("{\"name\":\"Data/Test\",\"asset_type\":\"data\",\"include_keys\":true}").RootElement;

        var result = ContentAssetHandler.Handle(p);

        Assert.True(result.GetProperty("exists").GetBoolean());
        Assert.Equal("data", result.GetProperty("kind").GetString());
        Assert.Equal(1, result.GetProperty("summary").GetProperty("count").GetInt32());
    }

    private sealed class FakeLoader : IContentAssetLoader
    {
        private readonly System.Collections.Generic.Dictionary<(System.Type Type, string Name), object> _assets = new();

        public void Add<T>(string name, T asset) where T : notnull
            => _assets[(typeof(T), name)] = asset;

        public bool TryLoad<T>(string name, out T? asset) where T : notnull
        {
            if (_assets.TryGetValue((typeof(T), name), out var value))
            {
                asset = (T)value;
                return true;
            }
            asset = default;
            return false;
        }
    }
}
