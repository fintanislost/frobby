using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

public static class ScenarioFixtureStageName
{
    public static string For(string fixtureName, ScenarioSaveOverrides? overrides)
    {
        if (string.IsNullOrWhiteSpace(fixtureName))
            throw new ArgumentException("Fixture name must not be blank.", nameof(fixtureName));
        if (overrides?.FarmType is null)
            return fixtureName;

        var json = JsonSerializer.Serialize(overrides, ProtocolJson.Options);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{fixtureName}__frobby_{hex[..10]}";
    }
}
