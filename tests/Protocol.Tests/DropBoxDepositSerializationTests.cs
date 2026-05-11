using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class DropBoxDepositSerializationTests
{
    [Fact]
    public void Request_SerializesSnakeCaseFields()
    {
        var json = JsonSerializer.Serialize(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "AndyChest",
            QualifiedId = "(O)388",
            Count = 25,
        }, ProtocolJson.Options);

        Assert.Contains("\"order_key\":\"Andy\"", json);
        Assert.Contains("\"drop_box\":\"AndyChest\"", json);
        Assert.Contains("\"qualified_id\":\"(O)388\"", json);
        Assert.Contains("\"count\":25", json);
    }

    [Fact]
    public void Result_SerializesBeforeAfterCounts()
    {
        var json = JsonSerializer.Serialize(new DropBoxDepositResult
        {
            Ok = true,
            OrderKey = "Andy",
            DropBox = "AndyChest",
            DepositedCount = 25,
            ObjectiveIndex = 0,
            BeforeCount = 0,
            AfterCount = 25,
            Item = new SpecialOrderItemSummary { QualifiedId = "(O)388", Name = "Wood", Stack = 25 },
        }, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"order_key\":\"Andy\"", json);
        Assert.Contains("\"deposited_count\":25", json);
        Assert.Contains("\"objective_index\":0", json);
        Assert.Contains("\"before_count\":0", json);
        Assert.Contains("\"after_count\":25", json);
        Assert.Contains("\"qualified_id\":\"(O)388\"", json);
    }
}
