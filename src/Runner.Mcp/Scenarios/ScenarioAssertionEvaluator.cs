using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

public sealed class ScenarioAssertionEvaluator
{
    private readonly IScenarioAssertionRpc _rpc;

    public ScenarioAssertionEvaluator(IScenarioAssertionRpc rpc)
    {
        _rpc = rpc;
    }

    public async Task<ScenarioAssertionEvaluationResult> EvaluateAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        return assertion.Type switch
        {
            "draw.contains" => await EvaluateDrawContainsAsync(assertion, cancellationToken),
            "draw.not_contains" => await EvaluateDrawNotContainsAsync(assertion, cancellationToken),
            "draw.text_contains" => await EvaluateDrawTextContainsAsync(assertion, cancellationToken),
            "draw.text_not_contains" => await EvaluateDrawTextNotContainsAsync(assertion, cancellationToken),
            "content.asset" => await EvaluateContentAssetAssertionAsync(assertion, cancellationToken),
            "state.fishing_context" => await EvaluateRpcResultAssertionAsync(assertion.Type, assertion, cancellationToken),
            "state.fishing_table" => await EvaluateRpcResultAssertionAsync(assertion.Type, assertion, cancellationToken),
            "fishing.sample_catch" => await EvaluateRpcResultAssertionAsync(assertion.Type, assertion, cancellationToken),
            "state" => await EvaluateStateAssertionAsync(assertion, cancellationToken),
            _ => ScenarioAssertionEvaluationResult.Fail($"assertion type '{assertion.Type}' is not supported"),
        };
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.contains requires filter");

        var payload = new
        {
            filter = assertion.Filter,
            min_count = assertion.MinCount,
            message = assertion.Message,
        };
        var response = await _rpc.InvokeAsync(
            "draw.assert_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);

        return EvaluatePassedResult(response, "draw.assert_contains", _ => null);
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawNotContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.not_contains requires filter");

        var payload = new
        {
            filter = assertion.Filter,
            message = assertion.Message,
        };
        var response = await _rpc.InvokeAsync(
            "draw.assert_not_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);

        return EvaluatePassedResult(response, "draw.assert_not_contains", TextNotContainsFailureDetail);
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawTextContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.text_contains requires filter");

        var payload = new
        {
            filter = assertion.Filter,
            min_count = assertion.MinCount,
            max_count = assertion.MaxCount,
            message = assertion.Message,
        };
        var response = await _rpc.InvokeAsync(
            "draw.assert_text_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);

        return EvaluatePassedResult(response, "draw.assert_text_contains", TextContainsFailureDetail);
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawTextNotContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.text_not_contains requires filter");

        var payload = new
        {
            filter = assertion.Filter,
            message = assertion.Message,
        };
        var response = await _rpc.InvokeAsync(
            "draw.assert_text_not_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);

        return EvaluatePassedResult(response, "draw.assert_text_not_contains", TextNotContainsFailureDetail);
    }

    private static ScenarioAssertionEvaluationResult EvaluatePassedResult(
        ScenarioAssertionRpcResult response,
        string method,
        Func<JsonElement, string?> detailFactory)
    {
        if (response.Error is not null)
            return ScenarioAssertionEvaluationResult.Fail(response.Error);
        if (response.Result is not { } root)
            return ScenarioAssertionEvaluationResult.Fail($"{method} returned no result");
        if (!root.TryGetProperty("passed", out var passedElement)
            || passedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return ScenarioAssertionEvaluationResult.Fail($"{method} returned invalid passed value");
        }

        if (passedElement.GetBoolean())
            return ScenarioAssertionEvaluationResult.Pass();

        return ScenarioAssertionEvaluationResult.Fail(
            detailFactory(root) ?? $"{method} returned passed=false");
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateRpcResultAssertionAsync(
        string method,
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        var response = await _rpc.InvokeAsync(method, assertion.Params, cancellationToken);
        if (response.Error is not null)
            return ScenarioAssertionEvaluationResult.Fail(response.Error);
        if (response.Result is not { } root)
            return ScenarioAssertionEvaluationResult.Fail($"{method} returned no result");

        if (string.IsNullOrWhiteSpace(assertion.Expr))
            return ScenarioAssertionEvaluationResult.Pass();

        return EvaluateResultExpression(root, assertion.Expr);
    }

    private static ScenarioAssertionEvaluationResult EvaluateResultExpression(JsonElement root, string expr)
    {
        var trimmed = expr.Trim();
        var pathPattern = @"[A-Za-z_][A-Za-z0-9_]*(?:\[\d+\])?(?:\.[A-Za-z_][A-Za-z0-9_]*(?:\[\d+\])?)*";
        var containsMatch = Regex.Match(
            trimmed,
            $@"^result\.({pathPattern})\s+contains(?:\s+([A-Za-z_][A-Za-z0-9_]*))?\s+(['""])(.*?)\3$");
        if (containsMatch.Success)
        {
            var path = "result." + containsMatch.Groups[1].Value;
            var objectField = containsMatch.Groups[2].Success ? containsMatch.Groups[2].Value : null;
            var literal = containsMatch.Groups[4].Value;

            if (!TryResolveResultPath(root, path, out var array))
                return ScenarioAssertionEvaluationResult.Fail($"{path} was not found");
            if (array.ValueKind != JsonValueKind.Array)
                return ScenarioAssertionEvaluationResult.Fail($"{path} was not an array");

            foreach (var element in array.EnumerateArray())
            {
                if (objectField is null)
                {
                    if (element.ValueKind == JsonValueKind.String
                        && string.Equals(element.GetString(), literal, StringComparison.Ordinal))
                    {
                        return ScenarioAssertionEvaluationResult.Pass();
                    }
                }
                else if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(objectField, out var field)
                    && field.ValueKind == JsonValueKind.String
                    && string.Equals(field.GetString(), literal, StringComparison.Ordinal))
                {
                    return ScenarioAssertionEvaluationResult.Pass();
                }
            }

            return ScenarioAssertionEvaluationResult.Fail($"expected {path} to contain '{literal}'");
        }

        if (!TrySplitEqualityExpression(trimmed, out var negated, out var lhs, out var rhs))
            return ScenarioAssertionEvaluationResult.Fail($"unsupported result expression: {expr}");

        if (!TryResolveResultPath(root, lhs, out var value))
            return ScenarioAssertionEvaluationResult.Fail($"{lhs} was not found");

        var equal = JsonElementEqualsLiteral(value, rhs);
        if (equal is null)
            return ScenarioAssertionEvaluationResult.Fail($"unsupported literal in result expression: {rhs}");

        var result = negated ? !equal.Value : equal.Value;
        return result
            ? ScenarioAssertionEvaluationResult.Pass()
            : ScenarioAssertionEvaluationResult.Fail($"{lhs} did not match {rhs}");
    }

    private static bool TryResolveResultPath(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        var tokens = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens[0] != "result")
            return false;

        value = root;
        for (var index = 1; index < tokens.Length; index++)
        {
            if (!TryReadJsonToken(value, tokens[index], out value))
                return false;
        }

        return true;
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateContentAssetAssertionAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assertion.Asset))
            return ScenarioAssertionEvaluationResult.Fail("content.asset requires asset");

        var request = ProtocolJson.ToElement(new ContentAssetRequest
        {
            Name = assertion.Asset,
            AssetType = assertion.AssetType,
            IncludeKeys = assertion.IncludeKeys ?? false,
            KeysLimit = assertion.KeysLimit,
            EntryKeys = assertion.EntryKeys,
            HashTexture = assertion.HashTexture ?? false,
        });
        var response = await _rpc.InvokeAsync("content.asset", request, cancellationToken);
        if (response.Error is not null)
            return ScenarioAssertionEvaluationResult.Fail(response.Error);
        if (response.Result is not { ValueKind: JsonValueKind.Object } root)
            return ScenarioAssertionEvaluationResult.Fail("content.asset returned no result");

        if (!root.TryGetProperty("exists", out var exists)
            || exists.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return ScenarioAssertionEvaluationResult.Fail(
                $"content.asset returned invalid exists for {assertion.Asset}");
        }

        if (!exists.GetBoolean())
            return ScenarioAssertionEvaluationResult.Fail($"{assertion.Asset} is missing");

        if (string.IsNullOrWhiteSpace(assertion.Expr))
            return ScenarioAssertionEvaluationResult.Pass();

        return EvaluateAssetExpression(root, assertion.Expr);
    }

    private static ScenarioAssertionEvaluationResult EvaluateAssetExpression(JsonElement assetRoot, string expr)
    {
        var trimmed = expr.Trim();
        var containsMatch = Regex.Match(
            trimmed,
            @"^asset\.([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+contains(?:\s+([A-Za-z_][A-Za-z0-9_]*))?\s+(['""])(.*?)\3$");
        if (containsMatch.Success)
        {
            var path = "asset." + containsMatch.Groups[1].Value;
            var objectField = containsMatch.Groups[2].Success ? containsMatch.Groups[2].Value : null;
            var literal = containsMatch.Groups[4].Value;

            if (!TryResolveAssetPath(assetRoot, path, out var array))
                return ScenarioAssertionEvaluationResult.Fail($"{path} was not found");
            if (array.ValueKind != JsonValueKind.Array)
                return ScenarioAssertionEvaluationResult.Fail($"{path} was not an array");

            foreach (var element in array.EnumerateArray())
            {
                if (objectField is null)
                {
                    if (element.ValueKind == JsonValueKind.String
                        && string.Equals(element.GetString(), literal, StringComparison.Ordinal))
                    {
                        return ScenarioAssertionEvaluationResult.Pass();
                    }
                }
                else if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(objectField, out var field)
                    && field.ValueKind == JsonValueKind.String
                    && string.Equals(field.GetString(), literal, StringComparison.Ordinal))
                {
                    return ScenarioAssertionEvaluationResult.Pass();
                }
            }

            return ScenarioAssertionEvaluationResult.Fail($"expected {path} to contain '{literal}'");
        }

        if (!TrySplitEqualityExpression(trimmed, out var negated, out var lhs, out var rhs))
            return ScenarioAssertionEvaluationResult.Fail($"unsupported content.asset expression: {expr}");

        if (!TryResolveAssetPath(assetRoot, lhs, out var value))
            return ScenarioAssertionEvaluationResult.Fail($"{lhs} was not found");

        var equal = JsonElementEqualsLiteral(value, rhs);
        if (equal is null)
            return ScenarioAssertionEvaluationResult.Fail(
                $"unsupported literal in content.asset expression: {rhs}");

        var result = negated ? !equal.Value : equal.Value;
        return result
            ? ScenarioAssertionEvaluationResult.Pass()
            : ScenarioAssertionEvaluationResult.Fail($"{lhs} did not match {rhs}");
    }

    private static bool TryResolveAssetPath(JsonElement assetRoot, string path, out JsonElement value)
    {
        value = default;
        var tokens = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens[0] != "asset")
            return false;
        if (tokens.Length == 1)
        {
            value = assetRoot;
            return true;
        }
        if (assetRoot.ValueKind != JsonValueKind.Object)
            return false;

        var index = 1;
        if (tokens[index] == "summary")
        {
            if (!assetRoot.TryGetProperty("summary", out value))
                return false;
            index++;
        }
        else if (assetRoot.TryGetProperty(tokens[index], out value))
        {
            index++;
        }
        else
        {
            if (!assetRoot.TryGetProperty("summary", out value))
                return false;
        }

        for (; index < tokens.Length; index++)
        {
            if (!TryReadJsonToken(value, tokens[index], out value))
                return false;
        }

        return true;
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateStateAssertionAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assertion.Expr))
            return ScenarioAssertionEvaluationResult.Fail("state assertion requires expr");

        var expr = assertion.Expr.Trim();
        var containsMatch = Regex.Match(
            expr,
            @"^state\.(?<method>[A-Za-z_][A-Za-z0-9_]*)\.(?<array>[A-Za-z_][A-Za-z0-9_]*)\s+(?:(?<not>not)\s+)?contains(?:\s+(?<field>[A-Za-z_][A-Za-z0-9_]*))?\s+(?:(?<quote>['""])(?<quoted_literal>.*?)\k<quote>|(?<bare_literal>-?\d+|true|false))$");
        if (containsMatch.Success)
            return await EvaluateStateContainsAssertionAsync(assertion, containsMatch, cancellationToken);

        if (!TrySplitEqualityExpression(expr, out var negated, out var lhs, out var rhs))
            return ScenarioAssertionEvaluationResult.Fail($"unsupported state expression: {assertion.Expr}");

        var pathTokens = lhs.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (pathTokens.Length < 3 || pathTokens[0] != "state")
            return ScenarioAssertionEvaluationResult.Fail($"unsupported state path: {lhs}");

        var method = $"state.{pathTokens[1]}";
        var response = await _rpc.InvokeAsync(method, assertion.Params, cancellationToken);
        if (response.Error is not null)
            return ScenarioAssertionEvaluationResult.Fail(response.Error);
        if (response.Result is not { } root)
            return ScenarioAssertionEvaluationResult.Fail($"{method} returned no result");

        if (!TryResolveStatePath(root, pathTokens, out var value, out var pathFailure))
            return ScenarioAssertionEvaluationResult.Fail(pathFailure);

        var equal = JsonElementEqualsLiteral(value, rhs);
        if (equal is null)
            return ScenarioAssertionEvaluationResult.Fail($"unsupported literal in state expression: {rhs}");

        var result = negated ? !equal.Value : equal.Value;
        if (result)
            return ScenarioAssertionEvaluationResult.Pass();

        var relationship = negated ? "matched forbidden" : "did not match";
        return ScenarioAssertionEvaluationResult.Fail($"{lhs} {relationship} {rhs}");
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateStateContainsAssertionAsync(
        ScenarioAssertion assertion,
        Match containsMatch,
        CancellationToken cancellationToken)
    {
        var methodName = containsMatch.Groups["method"].Value;
        var method = $"state.{methodName}";
        var arrayProperty = containsMatch.Groups["array"].Value;
        var objectField = containsMatch.Groups["field"].Success ? containsMatch.Groups["field"].Value : null;
        var quotedLiteral = containsMatch.Groups["quoted_literal"];
        var literal = quotedLiteral.Success
            ? quotedLiteral.Value
            : containsMatch.Groups["bare_literal"].Value;
        var literalIsQuoted = quotedLiteral.Success;
        var negatedContains = containsMatch.Groups["not"].Success;

        var response = await _rpc.InvokeAsync(method, assertion.Params, cancellationToken);
        if (response.Error is not null)
            return ScenarioAssertionEvaluationResult.Fail(response.Error);
        if (response.Result is not { } root)
            return ScenarioAssertionEvaluationResult.Fail($"{method} returned no result");

        var arrayPath = $"state.{methodName}.{arrayProperty}";
        if (!root.TryGetProperty(arrayProperty, out var array) || array.ValueKind != JsonValueKind.Array)
            return ScenarioAssertionEvaluationResult.Fail($"{arrayPath} was not an array");

        var matched = false;
        foreach (var element in array.EnumerateArray())
        {
            if (objectField is null)
            {
                matched = JsonElementMatchesContainsLiteral(element, literal, literalIsQuoted);
            }
            else
            {
                matched = element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(objectField, out var field)
                    && JsonElementMatchesContainsLiteral(field, literal, literalIsQuoted);
            }

            if (matched)
                break;
        }

        var passed = negatedContains ? !matched : matched;
        if (passed)
            return ScenarioAssertionEvaluationResult.Pass();

        return ScenarioAssertionEvaluationResult.Fail(negatedContains
            ? $"expected {arrayPath} not to contain {FormatContainsLiteral(literal, literalIsQuoted)}"
            : $"expected {arrayPath} to contain {FormatContainsLiteral(literal, literalIsQuoted)}");
    }

    private static bool JsonElementMatchesContainsLiteral(JsonElement element, string literal, bool literalIsQuoted)
    {
        if (literalIsQuoted)
        {
            return element.ValueKind == JsonValueKind.String
                && string.Equals(element.GetString(), literal, StringComparison.Ordinal);
        }

        if (long.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intLiteral))
        {
            return element.ValueKind == JsonValueKind.Number
                && element.TryGetInt64(out var actual)
                && actual == intLiteral;
        }

        if (bool.TryParse(literal, out var boolLiteral))
        {
            return (element.ValueKind == JsonValueKind.True && boolLiteral)
                || (element.ValueKind == JsonValueKind.False && !boolLiteral);
        }

        return false;
    }

    private static string FormatContainsLiteral(string literal, bool literalIsQuoted)
        => literalIsQuoted ? $"'{literal}'" : literal;

    private static bool TryResolveStatePath(
        JsonElement root,
        string[] pathTokens,
        out JsonElement value,
        out string detail)
    {
        value = root;
        detail = string.Empty;

        for (var index = 2; index < pathTokens.Length; index++)
        {
            var token = pathTokens[index];
            var indexedMatch = Regex.Match(token, @"^([A-Za-z_][A-Za-z0-9_]*)\[(\d+)\]$");
            if (indexedMatch.Success)
            {
                var fieldName = indexedMatch.Groups[1].Value;
                var arrayPath = $"{StatePathPrefix(pathTokens, index - 1)}.{fieldName}";
                if (value.ValueKind != JsonValueKind.Object)
                {
                    detail = $"{StatePathPrefix(pathTokens, index - 1)} was not an object";
                    return false;
                }
                if (!value.TryGetProperty(fieldName, out var array))
                {
                    detail = $"{arrayPath} was not found";
                    return false;
                }
                if (array.ValueKind != JsonValueKind.Array)
                {
                    detail = $"{arrayPath} was not an array";
                    return false;
                }

                var arrayIndex = int.Parse(indexedMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                if (arrayIndex >= array.GetArrayLength())
                {
                    detail = $"{arrayPath}[{arrayIndex}] was out of range";
                    return false;
                }

                value = array[arrayIndex];
            }
            else
            {
                var path = StatePathPrefix(pathTokens, index);
                if (value.ValueKind != JsonValueKind.Object)
                {
                    detail = $"{StatePathPrefix(pathTokens, index - 1)} was not an object";
                    return false;
                }
                if (!value.TryGetProperty(token, out var nested))
                {
                    detail = $"{path} was not found";
                    return false;
                }

                value = nested;
            }
        }

        return true;
    }

    private static string StatePathPrefix(string[] tokens, int inclusiveIndex)
        => string.Join(".", tokens, 0, inclusiveIndex + 1);

    private static bool TryReadJsonToken(JsonElement current, string token, out JsonElement value)
    {
        value = default;
        var match = Regex.Match(token, @"^([A-Za-z_][A-Za-z0-9_]*)(?:\[(\d+)\])?$");
        if (!match.Success)
            return false;

        if (current.ValueKind != JsonValueKind.Object)
            return false;
        if (!current.TryGetProperty(match.Groups[1].Value, out value))
            return false;

        if (!match.Groups[2].Success)
            return true;

        if (value.ValueKind != JsonValueKind.Array)
            return false;
        var index = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (index >= value.GetArrayLength())
            return false;
        value = value[index];
        return true;
    }

    private static bool TrySplitEqualityExpression(
        string expr,
        out bool negated,
        out string lhs,
        out string rhs)
    {
        negated = false;
        lhs = string.Empty;
        rhs = string.Empty;

        var neqIdx = expr.IndexOf("!=", StringComparison.Ordinal);
        var eqIdx = expr.IndexOf("==", StringComparison.Ordinal);
        if (neqIdx >= 0 && (eqIdx < 0 || neqIdx < eqIdx))
        {
            negated = true;
            lhs = expr.Substring(0, neqIdx).Trim();
            rhs = expr.Substring(neqIdx + 2).Trim();
            return true;
        }

        if (eqIdx >= 0)
        {
            lhs = expr.Substring(0, eqIdx).Trim();
            rhs = expr.Substring(eqIdx + 2).Trim();
            return true;
        }

        return false;
    }

    private static bool? JsonElementEqualsLiteral(JsonElement value, string rhs)
    {
        if ((rhs.StartsWith('\'') && rhs.EndsWith('\'')) ||
            (rhs.StartsWith('"') && rhs.EndsWith('"')))
        {
            var literal = rhs.Substring(1, rhs.Length - 2);
            return value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), literal, StringComparison.Ordinal);
        }

        if (long.TryParse(rhs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intLiteral))
        {
            return value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var actual)
                && actual == intLiteral;
        }

        if (bool.TryParse(rhs, out var boolLiteral))
        {
            return (value.ValueKind == JsonValueKind.True && boolLiteral)
                || (value.ValueKind == JsonValueKind.False && !boolLiteral);
        }

        return null;
    }

    private static string? TextContainsFailureDetail(JsonElement result)
    {
        if (TryGetInt(result, "matched_count", out var matched) &&
            TryGetInt(result, "min_count", out var min))
        {
            if (matched < min)
                return $"matched {matched} < {min}";

            if (TryGetInt(result, "max_count", out var max) && matched > max)
                return $"matched {matched} > {max}";
        }
        return null;
    }

    private static string? TextNotContainsFailureDetail(JsonElement result)
    {
        if (TryGetInt(result, "matched_count", out var matched))
            return $"matched {matched}";
        return null;
    }

    private static bool TryGetInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
    }
}
