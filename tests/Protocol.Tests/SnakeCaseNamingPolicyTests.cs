using SdvTestFramework.Protocol.Json;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SnakeCaseNamingPolicyTests
{
    [Theory]
    [InlineData("Name", "name")]
    [InlineData("MaxStamina", "max_stamina")]
    [InlineData("SourceRect", "source_rect")]
    [InlineData("MoneyInPurse", "money_in_purse")]
    [InlineData("ID", "id")]
    [InlineData("HTTPServer", "http_server")]
    [InlineData("Already_snake", "already_snake")]
    [InlineData("", "")]
    public void ConvertName_Cases(string input, string expected)
    {
        Assert.Equal(expected, SnakeCaseNamingPolicy.Instance.ConvertName(input));
    }
}
