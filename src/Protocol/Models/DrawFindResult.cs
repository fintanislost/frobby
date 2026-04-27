using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response for <c>draw.find</c>. Array of matching DrawEventDto + count.</summary>
public sealed class DrawFindResult
{
    public List<DrawEventDto> Events { get; set; } = new();
    public int Count { get; set; }
}
