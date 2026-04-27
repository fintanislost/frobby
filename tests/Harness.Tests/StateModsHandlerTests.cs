using System.Collections.Generic;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateModsHandlerTests
{
    // Shim stand-in for IModRegistry's GetAll() surface — the handler only cares about
    // iterating mods and reading UniqueID, so we fake just enough.
    private sealed class FakeModInfo : IModInfo
    {
        public FakeModInfo(string uniqueId) { Manifest = new FakeManifest(uniqueId); }
        public IManifest Manifest { get; }
        public bool IsContentPack => false;
    }

    private sealed class FakeManifest : IManifest
    {
        public FakeManifest(string uniqueId) { UniqueID = uniqueId; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public ISemanticVersion Version { get; set; } = null!;
        public ISemanticVersion? MinimumApiVersion { get; set; }
        public ISemanticVersion? MinimumGameVersion { get; set; }
        public string UniqueID { get; }
        public string? EntryDll { get; set; }
        public IManifestContentPackFor? ContentPackFor { get; set; }
        public IManifestDependency[] Dependencies { get; set; } = System.Array.Empty<IManifestDependency>();
        public string[] UpdateKeys { get; set; } = System.Array.Empty<string>();
        public IDictionary<string, object> ExtraFields { get; set; } = new Dictionary<string, object>();
    }

    private sealed class FakeRegistry : IModRegistry
    {
        private readonly List<IModInfo> _mods;
        public FakeRegistry(params string[] uniqueIds)
        {
            _mods = new List<IModInfo>();
            foreach (var id in uniqueIds) _mods.Add(new FakeModInfo(id));
        }
        // IModLinked
        public string ModID => "SdvTestFramework.Harness";
        // IModRegistry
        public IEnumerable<IModInfo> GetAll() => _mods;
        public IModInfo? Get(string uniqueID) => null;
        public IModInfo? GetFromNamespacedId(string? namespacedId, bool requirePrefix = false) => null;
        public bool IsLoaded(string uniqueID) => false;
        public T? GetApi<T>(string uniqueID) where T : class => null;
        public object? GetApi(string uniqueID) => null;
    }

    [Fact]
    public void Handle_NoRegistry_ReturnsEmptyList()
    {
        StateModsHandler.Registry = null;
        var resp = StateModsHandler.Handle(null);
        var state = System.Text.Json.JsonSerializer.Deserialize<ModsState>(
            resp.GetRawText(), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
        Assert.NotNull(state);
        Assert.Empty(state!.Mods);
    }

    [Fact]
    public void Handle_RegistryWithMods_ReturnsAllUniqueIds()
    {
        try
        {
            StateModsHandler.Registry = new FakeRegistry("A.B", "C.D", "E.F");
            var resp = StateModsHandler.Handle(null);
            var state = System.Text.Json.JsonSerializer.Deserialize<ModsState>(
                resp.GetRawText(), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
            Assert.NotNull(state);
            Assert.Equal(new[] { "A.B", "C.D", "E.F" }, state!.Mods);
        }
        finally { StateModsHandler.Registry = null; }
    }
}
