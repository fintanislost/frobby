using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class TimeStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var t = new TimeState
        {
            InSave = true,
            Season = "spring",
            DayOfMonth = 5,
            Year = 1,
            TimeOfDay = 600,
            DayOfWeek = "monday",
        };

        var json = JsonSerializer.Serialize(t, ProtocolJson.Options);
        Assert.Contains("\"in_save\":true", json);
        Assert.Contains("\"season\":\"spring\"", json);
        Assert.Contains("\"day_of_month\":5", json);
        Assert.Contains("\"time_of_day\":600", json);
        Assert.Contains("\"day_of_week\":\"monday\"", json);
    }

    [Fact]
    public void Serialize_DefaultInstance_HasInSaveFalse()
    {
        // Locks in the title-screen signal: a freshly-instantiated TimeState defaults to
        // in_save=false, day_of_month=0. Scenario authors querying state.time at the title
        // screen will see this shape and know the date fields are uninitialized.
        var json = JsonSerializer.Serialize(new TimeState(), ProtocolJson.Options);
        Assert.Contains("\"in_save\":false", json);
        Assert.Contains("\"day_of_month\":0", json);
    }
}
