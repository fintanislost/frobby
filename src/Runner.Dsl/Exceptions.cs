using System;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// Base exception for RPC failures surfaced to DSL callers. Subclasses provide typed
/// handling for the common error codes; the base covers everything else.
/// </summary>
public class SdvRpcException : Exception
{
    public string Method { get; }
    public JsonRpcErrorCode Code { get; }

    public SdvRpcException(string method, JsonRpcErrorCode code, string message)
        : base($"RPC '{method}' failed ({code}): {message}")
    {
        Method = method;
        Code = code;
    }

    /// <summary>
    /// Construct the right subclass for the error code — callers can <c>catch (SdvGameStateInvalidException)</c>
    /// when they expect a precondition fail.
    /// </summary>
    public static SdvRpcException Create(string method, JsonRpcError error) => error.Code switch
    {
        JsonRpcErrorCode.GameStateInvalid => new SdvGameStateInvalidException(method, error.Message),
        JsonRpcErrorCode.InvalidParams    => new SdvInvalidParamsException(method, error.Message),
        JsonRpcErrorCode.InternalError    => new SdvInternalErrorException(method, error.Message),
        _ => new SdvRpcException(method, error.Code, error.Message),
    };
}

/// <summary>Thrown when an RPC precondition fails (e.g. <c>freeze.begin</c> without an active scenario).</summary>
public sealed class SdvGameStateInvalidException : SdvRpcException
{
    public SdvGameStateInvalidException(string method, string message)
        : base(method, JsonRpcErrorCode.GameStateInvalid, message) { }
}

/// <summary>Thrown when an RPC's params fail validation (wrong type, out of range, etc.).</summary>
public sealed class SdvInvalidParamsException : SdvRpcException
{
    public SdvInvalidParamsException(string method, string message)
        : base(method, JsonRpcErrorCode.InvalidParams, message) { }
}

/// <summary>Thrown when the harness hits an internal error (reflection failure, file I/O, etc.).</summary>
public sealed class SdvInternalErrorException : SdvRpcException
{
    public SdvInternalErrorException(string method, string message)
        : base(method, JsonRpcErrorCode.InternalError, message) { }
}
