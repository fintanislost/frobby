using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class TimeNextDayResultSerializationTests
{
    [Fact]
    public void Result_SerializesNewDateAsSnakeCase()
    {
        var result = new TimeNextDayResult
        {
            Tick = 90123,
            Year = 1,
            Season = "spring",
            DayOfMonth = 2,
            TimeOfDay = 600,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":90123", json);
        Assert.Contains("\"year\":1", json);
        Assert.Contains("\"season\":\"spring\"", json);
        Assert.Contains("\"day_of_month\":2", json);
        Assert.Contains("\"time_of_day\":600", json);
    }

    [Fact]
    public void Result_DeserializesFromSnakeCase()
    {
        var result = JsonSerializer.Deserialize<TimeNextDayResult>(
            "{\"ok\":true,\"tick\":90123,\"year\":1,\"season\":\"spring\",\"day_of_month\":2,\"time_of_day\":600}",
            ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(90123, result.Tick);
        Assert.Equal(1, result.Year);
        Assert.Equal("spring", result.Season);
        Assert.Equal(2, result.DayOfMonth);
        Assert.Equal(600, result.TimeOfDay);
    }
}
