// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Bridge.Protocol;

/// <summary>Identifies why the codec refused to encode or decode a message.</summary>
/// <remarks>
/// Every code names a specific, actionable cause. There is no catch-all "invalid message" code,
/// because a caller that cannot tell a bound breach from an unknown update kind cannot decide
/// whether to resync, drop the peer, or upgrade.
/// </remarks>
public enum BridgeWireErrorCode
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The bytes are not a valid protobuf message for the expected type.</summary>
    MalformedPayload,

    /// <summary>The encoded frame exceeds <see cref="BridgeProtocol.MaxFrameBytes"/>.</summary>
    FrameTooLarge,

    /// <summary>A required field was absent.</summary>
    MissingField,

    /// <summary>A field carried a value outside its documented range.</summary>
    FieldOutOfRange,

    /// <summary>
    /// A message exceeded a negotiated or local bound: too many updates, too many collection
    /// elements, an over-long string, or an oversized estimated payload.
    /// </summary>
    LimitExceeded,

    /// <summary>The update oneof was unset or carried a case this version does not know.</summary>
    UnknownUpdateKind,

    /// <summary>The value oneof was unset or carried a case this version does not know.</summary>
    UnknownValueKind,

    /// <summary>An enum field was unspecified or carried a value this version does not know.</summary>
    UnknownEnumValue,

    /// <summary>The change-stream frame oneof was unset or carried an unknown case.</summary>
    UnknownStreamFrame,

    /// <summary>
    /// The message tried to express a whole-overlay replacement inside a delta. A full replacement
    /// is a snapshot; nesting one in a delta is rejected rather than reinterpreted.
    /// </summary>
    OverlayReplacementNotAllowed,

    /// <summary>The peer's protocol major version is not supported.</summary>
    UnsupportedVersion
}

/// <summary>
/// A bounded, redacted description of a codec failure. <see cref="Detail"/> never carries a
/// credential, an authored value, or a decoded payload fragment: it names the field or bound that
/// failed so an operator can act without the message content leaking into a log.
/// </summary>
public readonly record struct BridgeWireError(BridgeWireErrorCode Code, string Detail)
{
    /// <summary>The maximum length of <see cref="Detail"/>.</summary>
    public const int MaxDetailLength = 256;

    /// <summary>Gets the successful, empty error.</summary>
    public static BridgeWireError None { get; } = new(BridgeWireErrorCode.None, string.Empty);

    /// <summary>Gets whether this value describes a failure.</summary>
    public bool IsError => Code != BridgeWireErrorCode.None;

    /// <summary>Creates a bounded error, truncating an over-long detail.</summary>
    public static BridgeWireError Create(BridgeWireErrorCode code, string detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        string bounded = detail.Length <= MaxDetailLength
            ? detail
            : detail[..MaxDetailLength];
        return new BridgeWireError(code, bounded);
    }

    /// <inheritdoc/>
    public override string ToString() => IsError ? $"{Code}: {Detail}" : nameof(None);
}

/// <summary>Thrown when a caller encodes a message the contract cannot represent.</summary>
/// <remarks>
/// Decoding never throws this: an inbound message is untrusted input, so every decode path returns
/// a <see cref="BridgeWireError"/> instead. Encoding is a local programming error, so it throws.
/// </remarks>
public sealed class BridgeProtocolException : InvalidOperationException
{
    /// <summary>Initializes an exception carrying a bounded protocol error.</summary>
    public BridgeProtocolException(BridgeWireError error)
        : base(error.ToString()) => Error = error;

    /// <summary>Initializes an exception with a message only.</summary>
    public BridgeProtocolException(string message)
        : base(message) =>
        Error = BridgeWireError.Create(BridgeWireErrorCode.MalformedPayload, message);

    /// <summary>Initializes an exception with a message and an inner exception.</summary>
    public BridgeProtocolException(string message, Exception innerException)
        : base(message, innerException) =>
        Error = BridgeWireError.Create(BridgeWireErrorCode.MalformedPayload, message);

    /// <summary>Initializes an exception with no detail.</summary>
    public BridgeProtocolException()
        : this("The bridge protocol operation failed.")
    {
    }

    /// <summary>Gets the bounded, redacted protocol error.</summary>
    public BridgeWireError Error { get; }
}
