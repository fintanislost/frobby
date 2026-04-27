namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>fixture.save</c>.</summary>
public sealed class FixtureSaveResult : MutatorOk
{
    /// <summary>Absolute path to the save directory produced by <c>SaveGame.Save()</c>.</summary>
    public string SavePath { get; set; } = string.Empty;
}
