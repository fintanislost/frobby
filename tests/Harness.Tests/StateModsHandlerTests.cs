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
        public FakeModInfo(
            string uniqueId,
            string name = "",
            string version = "",
            bool isContentPack = false,
            string? contentPackFor = null)
        {
            Manifest = new FakeManifest(uniqueId)
            {
                Name = name,
                Version = new FakeSemanticVersion(version),
                ContentPackFor = contentPackFor is null ? null : new FakeContentPackFor(contentPackFor),
            };
            IsContentPack = isContentPack;
        }

        public IManifest Manifest { get; }
        public bool IsContentPack { get; }
    }

    private sealed class FakeSemanticVersion : ISemanticVersion
    {
        private readonly string _value;
        public FakeSemanticVersion(string value) { _value = value; }
        public int MajorVersion => 0;
        public int MinorVersion => 0;
        public int PatchVersion => 0;
        public string? PrereleaseTag => null;
        public string? BuildMetadata => null;
        public int CompareTo(ISemanticVersion? other) => 0;
        public bool Equals(ISemanticVersion? other) => ReferenceEquals(this, other);
        public bool IsBetween(string? minVersion, string? maxVersion) => false;
        public bool IsBetween(ISemanticVersion? minVersion, ISemanticVersion? maxVersion) => false;
        public bool IsNewerThan(string? version) => false;
        public bool IsNewerThan(ISemanticVersion? version) => false;
        public bool IsNonStandard() => false;
        public bool IsOlderThan(string? version) => false;
        public bool IsOlderThan(ISemanticVersion? version) => false;
        public bool IsPrerelease() => false;
        public override string ToString() => _value;
    }

    private sealed class FakeContentPackFor : IManifestContentPackFor
    {
        public FakeContentPackFor(string uniqueId) { UniqueID = uniqueId; }
        public string UniqueID { get; }
        public ISemanticVersion? MinimumVersion { get; set; }
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

        public FakeRegistry(params IModInfo[] mods)
        {
            _mods = new List<IModInfo>(mods);
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
        Assert.Empty(state!.UniqueIds);
        Assert.Empty(state.Mods);
    }

    [Fact]
    public void Handle_RegistryWithMods_ReturnsMetadataAndUniqueIds()
    {
        try
        {
            StateModsHandler.Registry = new FakeRegistry(
                new FakeModInfo("A.B", "Alpha Beta", "1.2.3"),
                new FakeModInfo("C.D", "Charlie Delta", "2.0.0"));
            var resp = StateModsHandler.Handle(null);
            var state = System.Text.Json.JsonSerializer.Deserialize<ModsState>(
                resp.GetRawText(), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
            Assert.NotNull(state);
            Assert.Equal(new[] { "A.B", "C.D" }, state!.UniqueIds);
            Assert.Equal("A.B", state.Mods[0].UniqueId);
            Assert.Equal("Alpha Beta", state.Mods[0].Name);
            Assert.Equal("1.2.3", state.Mods[0].Version);
        }
        finally { StateModsHandler.Registry = null; }
    }

    [Fact]
    public void Handle_ContentPack_PopulatesContentPackMetadata()
    {
        try
        {
            StateModsHandler.Registry = new FakeRegistry(
                new FakeModInfo(
                    "Example.ContentPack",
                    "Example Content Pack",
                    "1.0.0",
                    isContentPack: true,
                    contentPackFor: "Pathoschild.ContentPatcher"));
            var resp = StateModsHandler.Handle(null);
            var state = System.Text.Json.JsonSerializer.Deserialize<ModsState>(
                resp.GetRawText(), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
            Assert.NotNull(state);
            Assert.True(state!.Mods[0].IsContentPack);
            Assert.Equal("Pathoschild.ContentPatcher", state.Mods[0].ContentPackFor);
        }
        finally { StateModsHandler.Registry = null; }
    }
}
