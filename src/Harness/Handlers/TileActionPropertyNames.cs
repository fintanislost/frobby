using System;
using System.Collections.Generic;
using System.Linq;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Harness.Handlers;

internal static class TileActionPropertyNames
{
    public const string Action = "Action";
    public const string TouchAction = "TouchAction";

    public static readonly IReadOnlyList<string> DefaultOrder = new[] { Action, TouchAction };

    public static List<string> Resolve(List<string>? requested, string paramName)
    {
        if (requested is null || requested.Count == 0)
            return DefaultOrder.ToList();

        var properties = new List<string>();
        foreach (var property in requested)
        {
            var normalized = Normalize(property, paramName);
            if (!properties.Contains(normalized, StringComparer.Ordinal))
                properties.Add(normalized);
        }

        return properties;
    }

    public static string Normalize(string? property, string paramName)
    {
        if (string.IsNullOrWhiteSpace(property))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"params.{paramName} must be Action or TouchAction");

        if (string.Equals(property, Action, StringComparison.Ordinal))
            return Action;
        if (string.Equals(property, TouchAction, StringComparison.Ordinal))
            return TouchAction;

        throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"params.{paramName} must be Action or TouchAction");
    }
}
