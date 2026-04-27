using System.Text.Json;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

/// <summary>Round-trips for the four JSON-RPC 2.0 message shapes we use on the wire.</summary>
public class JsonRpcMessageTests
{
    [Fact]
    public void Request_RoundTrips()
    {
        var req = new JsonRpcRequest
        {
            Id = 1,
            Method = "scenario.begin",
            Params = JsonDocument.Parse("""{"name":"x","seed":42}""").RootElement,
        };

        var json = JsonRpcCodec.Serialize(req);

        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"method\":\"scenario.begin\"", json);
        Assert.Contains("\"seed\":42", json);

        var parsed = JsonRpcCodec.ParseRequest(json);
        Assert.Equal(1L, parsed.Id);
        Assert.Equal("scenario.begin", parsed.Method);
        Assert.Equal("x", parsed.Params!.Value.GetProperty("name").GetString());
        Assert.Equal(42, parsed.Params.Value.GetProperty("seed").GetInt32());
    }

    [Fact]
    public void SuccessResponse_RoundTrips()
    {
        var resp = JsonRpcResponse.Ok(id: 7, result: JsonDocument.Parse("""{"tick":84200}""").RootElement);

        var json = JsonRpcCodec.Serialize(resp);
        Assert.Contains("\"id\":7", json);
        Assert.Contains("\"result\":{\"tick\":84200}", json);
        Assert.DoesNotContain("\"error\"", json);

        var parsed = JsonRpcCodec.ParseResponse(json);
        Assert.Equal(7L, parsed.Id);
        Assert.Null(parsed.Error);
        Assert.Equal(84200, parsed.Result!.Value.GetProperty("tick").GetInt32());
    }

    [Fact]
    public void ErrorResponse_RoundTrips()
    {
        var resp = JsonRpcResponse.Fail(
            id: 9,
            error: new JsonRpcError(JsonRpcErrorCode.ScenarioNotActive, "no scenario", data: null));

        var json = JsonRpcCodec.Serialize(resp);
        Assert.Contains("\"id\":9", json);
        Assert.Contains("\"code\":-32001", json);
        Assert.Contains("\"message\":\"no scenario\"", json);
        Assert.DoesNotContain("\"result\"", json);

        var parsed = JsonRpcCodec.ParseResponse(json);
        Assert.Equal(9L, parsed.Id);
        Assert.Null(parsed.Result);
        Assert.Equal(JsonRpcErrorCode.ScenarioNotActive, parsed.Error!.Code);
        Assert.Equal("no scenario", parsed.Error.Message);
    }

    [Fact]
    public void Notification_RoundTrips_NoId()
    {
        var note = new JsonRpcNotification
        {
            Method = "ready",
            Params = JsonDocument.Parse("""{"version":"0.1.0"}""").RootElement,
        };

        var json = JsonRpcCodec.Serialize(note);
        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.Contains("\"method\":\"ready\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.DoesNotContain("\"id\"", json);

        var parsed = JsonRpcCodec.ParseNotification(json);
        Assert.Equal("ready", parsed.Method);
        Assert.Equal("0.1.0", parsed.Params!.Value.GetProperty("version").GetString());
    }

    [Fact]
    public void Request_NullParams_OmittedFromSerialization()
    {
        var req = new JsonRpcRequest { Id = 1, Method = "state.time", Params = null };
        var json = JsonRpcCodec.Serialize(req);
        Assert.DoesNotContain("\"params\"", json);
    }

    [Fact]
    public void ParseRequest_MissingMethod_Throws()
    {
        var json = """{"jsonrpc":"2.0","id":1}""";
        Assert.Throws<JsonRpcException>(() => JsonRpcCodec.ParseRequest(json));
    }

    [Fact]
    public void ParseRequest_MalformedJson_Throws_ParseErrorCode()
    {
        var ex = Assert.Throws<JsonRpcException>(() => JsonRpcCodec.ParseRequest("{not json"));
        Assert.Equal(JsonRpcErrorCode.ParseError, ex.Code);
    }

    [Fact]
    public void ParseRequest_WrongVersion_Throws_InvalidRequestCode()
    {
        var json = """{"jsonrpc":"1.0","id":1,"method":"x"}""";
        var ex = Assert.Throws<JsonRpcException>(() => JsonRpcCodec.ParseRequest(json));
        Assert.Equal(JsonRpcErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void StandardErrorCodes_AreCorrect()
    {
        // Lock down the wire values per JSON-RPC 2.0 spec + docs/rpc-schema.md.
        Assert.Equal(-32700, (int)JsonRpcErrorCode.ParseError);
        Assert.Equal(-32600, (int)JsonRpcErrorCode.InvalidRequest);
        Assert.Equal(-32601, (int)JsonRpcErrorCode.MethodNotFound);
        Assert.Equal(-32602, (int)JsonRpcErrorCode.InvalidParams);
        Assert.Equal(-32603, (int)JsonRpcErrorCode.InternalError);
    }

    [Fact]
    public void CustomErrorCodes_MatchSchema()
    {
        // From docs/rpc-schema.md.
        Assert.Equal(-32001, (int)JsonRpcErrorCode.ScenarioNotActive);
        Assert.Equal(-32002, (int)JsonRpcErrorCode.FixtureLoadFailed);
        Assert.Equal(-32003, (int)JsonRpcErrorCode.GameStateInvalid);
        Assert.Equal(-32004, (int)JsonRpcErrorCode.DeterminismViolation);
        Assert.Equal(-32005, (int)JsonRpcErrorCode.PatchNotApplied);
    }
}
