using System;
using System.Text;
using System.Text.Json;

namespace SdvTestFramework.Protocol;

/// <summary>
/// Hand-rolled JSON-RPC 2.0 codec. Deliberately small — deterministic field ordering is
/// guaranteed by the write path, and the parse path is permissive about key order but
/// strict about the mandatory shape (jsonrpc == "2.0", method present, id well-typed).
/// </summary>
public static class JsonRpcCodec
{
    private const string Version = "2.0";

    // ---------------- serialize ----------------

    public static string Serialize(JsonRpcRequest req)
    {
        var sb = new StringBuilder(96);
        sb.Append("{\"jsonrpc\":\"").Append(Version).Append('"');
        sb.Append(",\"id\":").Append(req.Id);
        sb.Append(",\"method\":");
        AppendString(sb, req.Method);
        if (req.Params is { } p)
        {
            sb.Append(",\"params\":").Append(CompactJson(p));
        }
        sb.Append('}');
        return sb.ToString();
    }

    public static string Serialize(JsonRpcResponse resp)
    {
        var sb = new StringBuilder(96);
        sb.Append("{\"jsonrpc\":\"").Append(Version).Append('"');
        sb.Append(",\"id\":").Append(resp.Id);
        if (resp.Error is { } err)
        {
            sb.Append(",\"error\":{\"code\":").Append((int)err.Code);
            sb.Append(",\"message\":");
            AppendString(sb, err.Message);
            if (err.Data is { } data) sb.Append(",\"data\":").Append(CompactJson(data));
            sb.Append('}');
        }
        else if (resp.Result is { } result)
        {
            sb.Append(",\"result\":").Append(CompactJson(result));
        }
        else
        {
            // Every response must have exactly one of result/error. An empty success still
            // gets an explicit "result":null so the peer knows it was a success.
            sb.Append(",\"result\":null");
        }
        sb.Append('}');
        return sb.ToString();
    }

    public static string Serialize(JsonRpcNotification note)
    {
        var sb = new StringBuilder(64);
        sb.Append("{\"jsonrpc\":\"").Append(Version).Append('"');
        sb.Append(",\"method\":");
        AppendString(sb, note.Method);
        if (note.Params is { } p)
        {
            sb.Append(",\"params\":").Append(CompactJson(p));
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string CompactJson(JsonElement element)
        => JsonSerializer.Serialize(element);

    // ---------------- parse ----------------

    public static JsonRpcRequest ParseRequest(string json)
    {
        using var doc = ParseDocument(json);
        ValidateVersion(doc.RootElement);

        if (!doc.RootElement.TryGetProperty("method", out var methodEl)
            || methodEl.ValueKind != JsonValueKind.String)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "request missing 'method' (string)");

        if (!doc.RootElement.TryGetProperty("id", out var idEl))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "request missing 'id'");
        if (idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt64(out var id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "request 'id' must be an integer");

        JsonElement? paramsEl = doc.RootElement.TryGetProperty("params", out var p)
            ? p.Clone()
            : null;

        return new JsonRpcRequest
        {
            Id = id,
            Method = methodEl.GetString()!,
            Params = paramsEl,
        };
    }

    public static JsonRpcResponse ParseResponse(string json)
    {
        using var doc = ParseDocument(json);
        ValidateVersion(doc.RootElement);

        if (!doc.RootElement.TryGetProperty("id", out var idEl)
            || idEl.ValueKind != JsonValueKind.Number
            || !idEl.TryGetInt64(out var id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "response 'id' must be an integer");

        bool hasResult = doc.RootElement.TryGetProperty("result", out var resultEl);
        bool hasError = doc.RootElement.TryGetProperty("error", out var errorEl);
        if (hasResult == hasError)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest,
                "response must have exactly one of 'result' or 'error'");

        if (hasError)
        {
            var code = (JsonRpcErrorCode)errorEl.GetProperty("code").GetInt32();
            var message = errorEl.GetProperty("message").GetString() ?? string.Empty;
            JsonElement? data = errorEl.TryGetProperty("data", out var d) ? d.Clone() : null;
            return JsonRpcResponse.Fail(id, new JsonRpcError(code, message, data));
        }

        return new JsonRpcResponse
        {
            Id = id,
            Result = resultEl.ValueKind == JsonValueKind.Null ? null : resultEl.Clone(),
        };
    }

    public static JsonRpcNotification ParseNotification(string json)
    {
        using var doc = ParseDocument(json);
        ValidateVersion(doc.RootElement);

        if (!doc.RootElement.TryGetProperty("method", out var methodEl)
            || methodEl.ValueKind != JsonValueKind.String)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "notification missing 'method'");

        if (doc.RootElement.TryGetProperty("id", out _))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest,
                "notification must not have an 'id'");

        JsonElement? paramsEl = doc.RootElement.TryGetProperty("params", out var p)
            ? p.Clone()
            : null;

        return new JsonRpcNotification
        {
            Method = methodEl.GetString()!,
            Params = paramsEl,
        };
    }

    // ---------------- helpers ----------------

    private static JsonDocument ParseDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new JsonRpcException(JsonRpcErrorCode.ParseError, "invalid JSON: " + ex.Message, ex);
        }
    }

    private static void ValidateVersion(JsonElement root)
    {
        if (!root.TryGetProperty("jsonrpc", out var v) || v.GetString() != Version)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidRequest,
                "missing or wrong 'jsonrpc' field; must be \"2.0\"");
    }

    private static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
