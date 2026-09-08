// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Editing;

/// <summary>Bounded target-layer authored editing, separate from composed reads and full-document operations.</summary>
/// <remarks>
/// Resolve, use and dispose layer handles inside a stage scheduler callback. Returned state, snapshots,
/// checkpoints and results are deeply detached. Calls are synchronous and have no in-flight cancellation.
/// Generic resident layers are state/capture-only; mutation requires native owned counted review data.
/// </remarks>
public static class UsdLayerEditingExtensions
{
    /// <summary>Gets an owned handle to the actual registered user-review layer, creating it if needed.</summary>
    /// <remarks>Does not normalize, copy the session container, or return the physics layer.</remarks>
    public static UsdLayer GetUserReviewLayer(this UsdStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return new UsdLayer(OpenUsdNativeRuntime.GetUserReviewLayer(stage.Native));
    }

    /// <summary>Gets an owned handle by exact registered identifier within this stage's local stack.</summary>
    /// <remarks>Does not open files or return arbitrary globally registered nonlocal layers.</remarks>
    public static UsdLayer GetLocalLayer(this UsdStage stage, string identifier)
    {
        ArgumentNullException.ThrowIfNull(stage);
        _ = UsdEditingValidation.Text(identifier, nameof(identifier));
        return new UsdLayer(OpenUsdNativeRuntime.GetLocalEditingLayer(stage.Native, identifier));
    }

    /// <summary>Captures detached lifecycle state without asserting generic imported layers are writable.</summary>
    public static UsdLayerEditingState GetEditingState(this UsdLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return UsdLayerEditCodec.DecodeState(OpenUsdNativeRuntime.GetLayerEditingState(layer.Native));
    }

    /// <summary>Captures exact authored state at 1..256 unique addresses, never composed values.</summary>
    public static UsdLayerAuthoredSnapshot CaptureAuthored(
        this UsdLayer layer, ReadOnlySpan<UsdLayerEditAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(layer);
        byte[] request = UsdLayerEditCodec.EncodeAddresses(addresses);
        UsdLayerAuthoredSnapshot snapshot = UsdLayerEditCodec.DecodeSnapshot(
            OpenUsdNativeRuntime.CaptureLayerAuthored(layer.Native, request));
        if (snapshot.Addresses.Count != addresses.Length)
        {
            throw UsdEditingValidation.InvalidPacket("capture address count differs from request");
        }
        for (int index = 0; index < addresses.Length; index++)
        {
            if (snapshot.Addresses[index] != addresses[index])
            {
                throw UsdEditingValidation.InvalidPacket("capture address order differs from request");
            }
        }
        return snapshot;
    }

    /// <summary>
    /// Conditionally applies all ordered mutations using exact affected-field/declaration comparison.
    /// </summary>
    /// <remarks>
    /// Disjoint changes are allowed despite revision changes. Native failures throw; domain conflicts do not.
    /// The native transaction rolls back affected opinions on failure, not the entire layer.
    /// </remarks>
    public static UsdLayerEditResult CompareAndApply(
        this UsdLayer layer, UsdLayerAuthoredSnapshot expected, ReadOnlySpan<UsdLayerEdit> changes)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(expected);
        byte[] mutations = UsdLayerEditCodec.EncodeEdits(expected, changes);
        return DecodeResult(OpenUsdNativeRuntime.ApplyLayerEdits(layer.Native, expected.Payload, mutations), expected);
    }

    /// <summary>
    /// Conditionally replays an authored snapshot for per-edit undo/redo, preserving unrelated opinions.
    /// </summary>
    /// <remarks>Only safe owned creations can be removed; destructive cleanup is a Conflict.</remarks>
    public static UsdLayerEditResult CompareAndRestore(
        this UsdLayer layer, UsdLayerAuthoredSnapshot expectedCurrent, UsdLayerAuthoredSnapshot restore)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(restore);
        UsdLayerEditCodec.MatchAddresses(expectedCurrent.Addresses, restore.Addresses, nativeResult: false);
        return DecodeResult(
            OpenUsdNativeRuntime.RestoreLayerAuthored(layer.Native, expectedCurrent.Payload, restore.Payload),
            expectedCurrent);
    }

    /// <summary>
    /// Captures a bounded, opaque whole-review checkpoint, preserving admitted unknown authored fields.
    /// </summary>
    /// <remarks>
    /// Only the registered counted user-review layer supports checkpoints. This does not flatten a stage.
    /// </remarks>
    public static UsdLayerCheckpoint CaptureCheckpoint(this UsdLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return UsdLayerEditCodec.DecodeCheckpoint(OpenUsdNativeRuntime.CaptureLayerCheckpoint(layer.Native));
    }

    /// <summary>Conditionally restores whole-review content as an explicit same-document operation.</summary>
    /// <remarks>
    /// Compares entire expected content and current identity. A successful restore changes generation and
    /// invalidates ordinary edit history. No cross-session identity or asset-anchor rebind is supported.
    /// </remarks>
    public static UsdLayerCheckpointRestoreResult RestoreCheckpoint(
        this UsdLayer layer, UsdLayerCheckpoint expectedCurrent, UsdLayerCheckpoint restore)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(restore);
        OpenUsdNativeLayerEditResult native = OpenUsdNativeRuntime.RestoreLayerCheckpoint(
            layer.Native, expectedCurrent.Payload, restore.Payload);
        UsdLayerCheckpoint? after = native.Packet is null ? null : UsdLayerEditCodec.DecodeCheckpoint(native.Packet);
        if (after is not null &&
            (after.Identity.StageId != expectedCurrent.Identity.StageId ||
                after.Identity.LayerId != expectedCurrent.Identity.LayerId ||
                after.Identity.Generation == expectedCurrent.Identity.Generation ||
                after.Revision <= expectedCurrent.Revision ||
                after.Identifier != restore.Identifier || after.AssetAnchor != restore.AssetAnchor))
        {
            throw UsdEditingValidation.InvalidPacket("checkpoint restore result does not match its target");
        }
        return new UsdLayerCheckpointRestoreResult((UsdLayerEditOutcome)native.Outcome, after, native.Diagnostic);
    }

    /// <summary>
    /// Acknowledges publication only if identity, generation, revision and exact content still match.
    /// </summary>
    /// <remarks>
    /// An intervening edit, including edit-then-revert, prevents clearing the logical dirty baseline.
    /// </remarks>
    public static bool AcknowledgeSaved(this UsdLayer layer, UsdLayerCheckpoint captured)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(captured);
        return OpenUsdNativeRuntime.AcknowledgeLayerSaved(layer.Native, captured.Payload);
    }

    private static UsdLayerEditResult DecodeResult(
        OpenUsdNativeLayerEditResult native, UsdLayerAuthoredSnapshot expected)
    {
        UsdLayerAuthoredSnapshot? after = native.Packet is null
            ? null
            : UsdLayerEditCodec.DecodeSnapshot(native.Packet);
        if (after is not null)
        {
            if (after.Identity != expected.Identity || after.Revision < expected.Revision)
            {
                throw UsdEditingValidation.InvalidPacket("transaction result does not match its target");
            }
            UsdLayerEditCodec.MatchAddresses(expected.Addresses, after.Addresses, nativeResult: true);
        }
        return new UsdLayerEditResult((UsdLayerEditOutcome)native.Outcome, after, native.Diagnostic);
    }
}
