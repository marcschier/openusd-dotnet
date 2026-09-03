// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// Produces the opaque origin identifiers that name one publisher on a live-authoring session.
/// </summary>
/// <remarks>
/// <para>
/// An origin identifier is not decoration. It decides which inbound deltas are suppressed as echoes
/// of local edits, and it is part of the idempotency key a publisher derives, so two publishers that
/// share one identifier suppress each other's edits and derive colliding keys that a peer's ledger
/// reads as replays. Both faults are silent: an edit simply never lands.
/// </para>
/// <para>
/// A generated identifier is therefore unique to one publisher instance in one process, not merely
/// to the machine or the application. It stays inside
/// <see cref="LiveAuthoringValidation.MaxOpaqueIdLength"/>, contains only ASCII, and is well-formed
/// UTF-16, so it round-trips through every wire contract that carries it.
/// </para>
/// </remarks>
public static class LiveAuthoringOriginId
{
    private static int _instanceCounter;

    /// <summary>
    /// Creates an opaque origin identifier that no other publisher instance in this process, and no
    /// publisher in any other process, will produce.
    /// </summary>
    /// <remarks>
    /// The identifier names the process, the instance sequence within it, and eight bytes of
    /// cryptographic entropy. The process identifier and counter make collisions inside one machine
    /// impossible rather than unlikely; the entropy covers the case that matters across machines
    /// and across process-identifier reuse after a restart. It is stable for the lifetime of the
    /// publisher that took it and is never reproduced by a later call.
    /// </remarks>
    /// <returns>A bounded, opaque, process-instance-unique origin identifier.</returns>
    public static string CreateProcessInstanceUnique()
    {
        Span<byte> entropy = stackalloc byte[8];
        RandomNumberGenerator.Fill(entropy);
        int instance = Interlocked.Increment(ref _instanceCounter);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"openusd-origin-{Environment.ProcessId}-{instance}-{Convert.ToHexString(entropy)}");
    }

    /// <summary>
    /// Resolves the origin identifier one publisher will use, and validates whatever it resolves to.
    /// </summary>
    /// <param name="configured">The explicitly configured identifier, or <see langword="null"/>.</param>
    /// <param name="factory">The factory consulted when nothing is configured.</param>
    /// <param name="parameterName">The parameter name reported on a rejection.</param>
    /// <returns>The validated origin identifier.</returns>
    /// <remarks>
    /// A factory is host code, so what it returns is validated exactly as a configured value is: a
    /// generated identifier must never be one the wire contract would refuse.
    /// </remarks>
    internal static string Resolve(
        string? configured,
        Func<string>? factory,
        string parameterName)
    {
        string resolved = configured ?? (factory ?? CreateProcessInstanceUnique)();
        LiveAuthoringValidation.ValidateOpaqueIdentity(
            resolved,
            parameterName,
            "A local origin identifier");
        return resolved;
    }
}
