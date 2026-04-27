namespace SdvTestFramework.Protocol;

/// <summary>
/// JSON-RPC 2.0 error codes. Values from the spec plus project-specific extensions listed
/// in <c>docs/rpc-schema.md</c>. Wire values are locked by <c>JsonRpcMessageTests</c>.
/// </summary>
public enum JsonRpcErrorCode
{
    // Standard JSON-RPC 2.0 codes.
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,

    // Project-specific codes (docs/rpc-schema.md §Error codes).
    ScenarioNotActive = -32001,
    FixtureLoadFailed = -32002,
    GameStateInvalid = -32003,
    DeterminismViolation = -32004,
    PatchNotApplied = -32005,
}
