using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>state.mods</c>. Returns the loaded mod UniqueIDs in SMAPI load order.</summary>
/// <remarks>
/// Used by the fixture builder to populate <c>&lt;name&gt;.meta.json</c>'s <c>mods_installed</c>
/// field. Set by <c>ModEntry.Entry</c> via the static <see cref="Registry"/> property.
/// Null registry → empty list (keeps unit tests simple; production always sets it).
/// </remarks>
public static class StateModsHandler
{
    public const string Method = "state.mods";

    /// <summary>Set by <c>ModEntry</c> at startup; mirror of <c>helper.ModRegistry</c>.</summary>
    public static IModRegistry? Registry { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var ids = new List<string>();
        if (Registry is { } reg)
        {
            foreach (var mod in reg.GetAll())
                if (!string.IsNullOrEmpty(mod.Manifest?.UniqueID))
                    ids.Add(mod.Manifest.UniqueID);
        }
        return ProtocolJson.ToElement(new ModsState { Mods = ids.ToArray() });
    }
}
