using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SecretNotePlayerStateSerializationTests
{
    [Fact]
    public void PlayerState_SerializesSecretNotesSeenAsSnakeCase()
    {
        var p = new PlayerState
        {
            Name = "Tester",
            Location = "Desert",
            Tile = new TilePoint { X = 9, Y = 43 },
            SecretNotesSeen = new() { 18, 25 },
        };

        var json = JsonSerializer.Serialize(p, ProtocolJson.Options);

        Assert.Contains("\"secret_notes_seen\":[18,25]", json);
        Assert.DoesNotContain("SecretNotesSeen", json);
    }

    [Fact]
    public void AddSecretNoteSeenRequest_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<AddSecretNoteSeenRequest>(
            "{\"id\":18}",
            ProtocolJson.Options)!;

        Assert.Equal(18, req.Id);
    }
}
