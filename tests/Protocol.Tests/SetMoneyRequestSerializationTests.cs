using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SetMoneyRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var json = "{\"amount\":5000}";
        var req = JsonSerializer.Deserialize<SetMoneyRequest>(json, ProtocolJson.Options)!;
        Assert.Equal(5000, req.Amount);
    }

    [Fact]
    public void Result_IncludesPreviousAndTickAndOk()
    {
        var r = new SetMoneyResult { Previous = 1000, Tick = 42 };
        var json = JsonSerializer.Serialize(r, ProtocolJson.Options);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"previous\":1000", json);
    }

    [Fact]
    public void SetShopCurrencyRequest_DeserializesFromSnakeCase()
    {
        var json = "{\"currency\":1,\"amount\":10000}";
        var req = JsonSerializer.Deserialize<SetShopCurrencyRequest>(json, ProtocolJson.Options)!;

        Assert.Equal(1, req.Currency);
        Assert.Equal(10000, req.Amount);
    }

    [Fact]
    public void SetShopCurrencyResult_IncludesPreviousCurrentCurrencyAndTickAndOk()
    {
        var r = new SetShopCurrencyResult
        {
            Tick = 42,
            Currency = 1,
            CurrencyName = "star_tokens",
            Previous = 75,
            Amount = 10000,
        };

        var json = JsonSerializer.Serialize(r, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"currency\":1", json);
        Assert.Contains("\"currency_name\":\"star_tokens\"", json);
        Assert.Contains("\"previous\":75", json);
        Assert.Contains("\"amount\":10000", json);
    }
}
