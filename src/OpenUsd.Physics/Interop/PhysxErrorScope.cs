// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Reads the caller-owned error buffer every entry point writes its failure message into.
/// </summary>
/// <remarks>
/// The buffer is always owned and sized by the caller, so the runtime never allocates a message and
/// never hands back memory the caller has to free. A truncated message is reported honestly by the
/// native <c>required</c> field, and this helper reports the truncation instead of silently
/// shortening the message.
/// </remarks>
internal static unsafe class PhysxErrorScope
{
    /// <summary>The error buffer size every interop call site reserves on the stack.</summary>
    internal const int DefaultCapacity = 512;

    /// <summary>Decodes the message the runtime wrote into a caller-owned error buffer.</summary>
    internal static string Describe(PhysxStatus status, in PhysxErrorBuffer error)
    {
        string message = Decode(in error);
        return message.Length == 0 ? Describe(status) : message;
    }

    /// <summary>Describes a status code without a runtime message.</summary>
    internal static string Describe(PhysxStatus status) => status switch
    {
        PhysxStatus.Ok => "The operation succeeded.",
        PhysxStatus.InvalidArgument => "The operation rejected an invalid argument.",
        PhysxStatus.BufferTooSmall => "A caller-owned buffer was too small.",
        PhysxStatus.NativeError => "The native physics runtime reported an error.",
        PhysxStatus.VersionMismatch => "The native physics runtime requires an exact ABI match.",
        PhysxStatus.InvalidPage => "The build page was rejected by the native validator.",
        PhysxStatus.InvalidState => "The operation is not valid for the current world state.",
        PhysxStatus.Unsupported => "The native physics runtime does not support the operation.",
        PhysxStatus.CapacityExceeded => "A caller-declared capacity was exceeded.",
        _ => string.Create(CultureInfo.InvariantCulture, $"The native physics runtime returned status {(int)status}.")
    };

    /// <summary>Builds a diagnostic from a failed call.</summary>
    internal static UsdPhysicsDiagnostic ToDiagnostic(
        PhysxStatus status,
        UsdPhysicsDiagnosticCategory category,
        string code,
        in PhysxErrorBuffer error,
        UsdPhysicsObjectId? objectId = null) =>
        new(
            status == PhysxStatus.Ok ? UsdPhysicsDiagnosticSeverity.Information : UsdPhysicsDiagnosticSeverity.Error,
            category,
            code,
            Describe(status, in error),
            objectId);

    private static string Decode(in PhysxErrorBuffer error)
    {
        if (error.Data is null || error.Capacity == 0)
        {
            return string.Empty;
        }

        int capacity = (int)Math.Min((ulong)error.Capacity, int.MaxValue);
        ReadOnlySpan<byte> bytes = new(error.Data, capacity);
        int terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            bytes = bytes[..terminator];
        }
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        string message = Encoding.UTF8.GetString(bytes);
        return error.Required > error.Capacity
            ? message + " (truncated)"
            : message;
    }
}
