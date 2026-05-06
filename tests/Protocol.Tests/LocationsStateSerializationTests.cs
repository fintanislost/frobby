using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class LocationsStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var state = new LocationsState
        {
            Locations = new()
            {
                new LocationSummary
                {
                    Name = "Custom_TownEast",
                    UniqueName = "Custom_TownEast",
                    IsOutdoors = true,
                    MapWidth = 90,
                    MapHeight = 64,
                    WarpCount = 5,
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"locations\":[{", json);
        Assert.Contains("\"name\":\"Custom_TownEast\"", json);
        Assert.Contains("\"unique_name\":\"Custom_TownEast\"", json);
        Assert.Contains("\"is_outdoors\":true", json);
        Assert.Contains("\"map_width\":90", json);
        Assert.Contains("\"map_height\":64", json);
        Assert.Contains("\"warp_count\":5", json);
    }
}
