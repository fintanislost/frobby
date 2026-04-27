using System;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>time.set</c>. Writes <c>Game1.timeOfDay</c> / <c>Game1.dayOfMonth</c> /
/// <c>Game1.season</c> / <c>Game1.year</c> directly. All fields optional; at least one
/// must be provided. Validates inputs before mutating.
/// </summary>
/// <remarks>
/// Param validation runs before <see cref="RpcPreconditions.RequireWorldReady"/> so callers
/// see informative "bad input" errors even at title screen, rather than the generic world-not-
/// ready message masking an invalid request.
/// </remarks>
public static class TimeSetHandler
{
    public const string Method = "time.set";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<TimeSetRequest>(paramsElement);

        // Param validation BEFORE precondition so callers see "bad input" errors even at
        // title screen — more helpful than the generic RequireWorldReady message.
        if (req.Time is null && req.Day is null && req.Season is null && req.Year is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "at least one of {time, day, season, year} must be provided");

        if (req.Time is { } t && (t < 600 || t >= 2600 || (t % 100) >= 60))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"time must be HHMM with H in [6,26) and M < 60 (got {t})");

        if (req.Day is { } d && (d < 1 || d > 28))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"day must be 1-28 (got {d})");

        if (req.Year is { } y && y < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"year must be >= 1 (got {y})");

        string? normalizedSeason = null;
        if (req.Season is { } s)
        {
            normalizedSeason = s.ToLowerInvariant();
            if (normalizedSeason is not "spring" and not "summer" and not "fall" and not "winter")
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    $"season must be one of (spring, summer, fall, winter) (got '{s}')");
        }

        RpcPreconditions.RequireWorldReady();

        if (req.Time is { } tv) Game1.timeOfDay = tv;
        if (req.Day is { } dv) Game1.dayOfMonth = dv;
        if (req.Year is { } yv) Game1.year = yv;
        if (normalizedSeason is { } sv)
        {
            // SDV 1.6 exposes Game1.season (Season enum). Set it directly; WorldDate.Season
            // is backed by Game1.season so both stay in sync automatically.
            Game1.season = sv switch
            {
                "spring" => Season.Spring,
                "summer" => Season.Summer,
                "fall"   => Season.Fall,
                "winter" => Season.Winter,
                _ => throw new InvalidOperationException("unreachable — season already validated above"),
            };
        }

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
