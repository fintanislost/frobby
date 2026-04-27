namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>fixture.save</c>.</summary>
public sealed class FixtureSaveRequest
{
    /// <summary>Destination save-folder name in SDV's saves dir. Typically matches the fixture name.</summary>
    public string Name { get; set; } = string.Empty;
}
