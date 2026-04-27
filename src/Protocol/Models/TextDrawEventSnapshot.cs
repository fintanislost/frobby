using System;
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response for <c>draw.text_snapshot</c>. Array of captured text draw events + meta.</summary>
public sealed class TextDrawEventSnapshot
{
    public List<TextDrawEventDto> Events { get; set; } = new();
    public TextDrawSnapshotMetadata Meta { get; set; } = new();
}

/// <summary>Wire shape for a captured <c>SpriteBatch.DrawString</c> call.</summary>
public sealed class TextDrawEventDto
{
    public int Tick { get; set; }
    public int Call { get; set; }
    public string Text { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int[] Color { get; set; } = Array.Empty<int>();
    public float LayerDepth { get; set; }
}

/// <summary>Envelope meta for <see cref="TextDrawEventSnapshot"/>.</summary>
public sealed class TextDrawSnapshotMetadata
{
    public int Ticks { get; set; }
    public int Events { get; set; }
    public int Dropped { get; set; }
}
