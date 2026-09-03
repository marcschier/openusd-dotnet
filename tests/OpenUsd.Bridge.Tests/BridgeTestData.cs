// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>Shared fixtures: one instance of every update kind, value kind, and stream frame.</summary>
internal static class BridgeTestData
{
    internal const string BridgeRoot = "/Bridge";
    internal const string RemoteOrigin = "kit-bridge";
    internal const string LocalOrigin = "openusd-local";
    internal const string SessionId = "session-a";

    internal static LiveAuthoringRemoteEpoch Epoch(long epoch, string? sessionId = null) =>
        new(RemoteOrigin, sessionId ?? SessionId, epoch);

    internal static IEnumerable<LiveStageUpdate> AllUpdateKinds()
    {
        yield return new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform");
        yield return new DefinePrimUpdate($"{BridgeRoot}/Untyped");
        yield return new RemovePrimUpdate($"{BridgeRoot}/Old");
        yield return new SetAttributeUpdate(
            $"{BridgeRoot}/Cube",
            "custom:pressure",
            LiveAttributeValue.FromDouble(1.5),
            TimeCode: 12.5);
        yield return new ClearUpdate(
            $"{BridgeRoot}/Cube",
            LiveClearTargetKind.AttributeValue,
            "custom:pressure");
        yield return new SetRelationshipTargetsUpdate(
            $"{BridgeRoot}/Cube",
            "material:binding",
            [$"{BridgeRoot}/Materials/Steel"]);
        yield return new SetReferenceUpdate($"{BridgeRoot}/Ref", "./asset.usda", "/Root");
        yield return new SetPayloadUpdate($"{BridgeRoot}/Pay", "./payload.usda", "/Root");
        yield return new SetActiveUpdate($"{BridgeRoot}/Cube", false);
        yield return new SetInstanceableUpdate($"{BridgeRoot}/Cube", true);
        yield return new SetVariantSelectionUpdate(
            $"{BridgeRoot}/Cube",
            "modelingVariant",
            ["low", "high"],
            "high");
        yield return new SetMetadataUpdate(
            $"{BridgeRoot}/Cube",
            "customVendorKey",
            LiveMetadataValue.FromString("value"));
        yield return new ApiSchemaUpdate(
            $"{BridgeRoot}/Cube",
            "AssetPreviewsAPI",
            LiveApiSchemaOperation.Apply);
        yield return new SetPointInstancerOrientationsUpdate(
            $"{BridgeRoot}/Instancer",
            [new UsdQuatf(1, 0, 0, 0), new UsdQuatf(0.5f, 0.5f, 0.5f, 0.5f)]);
    }

    internal static LiveAttributeValue CreateAttributeValue(LiveAttributeKind kind) => kind switch
    {
        LiveAttributeKind.Boolean => LiveAttributeValue.FromBoolean(true),
        LiveAttributeKind.Int64 => LiveAttributeValue.FromInt64(-42),
        LiveAttributeKind.Double => LiveAttributeValue.FromDouble(2.5),
        LiveAttributeKind.String => LiveAttributeValue.FromString("text"),
        LiveAttributeKind.Token => LiveAttributeValue.FromToken("token"),
        LiveAttributeKind.Vec3f => LiveAttributeValue.FromVec3f(new UsdVec3f(1, 2, 3)),
        LiveAttributeKind.Matrix4d => LiveAttributeValue.FromMatrix4d(new UsdMatrix4d(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16)),
        LiveAttributeKind.Int32Array => LiveAttributeValue.FromInt32Array([1, 2, 3]),
        LiveAttributeKind.FloatArray => LiveAttributeValue.FromFloatArray([1.5f, 2.5f]),
        LiveAttributeKind.DoubleArray => LiveAttributeValue.FromDoubleArray([1.5, 2.5]),
        LiveAttributeKind.Vec2fArray => LiveAttributeValue.FromVec2fArray(
            [new UsdVec2f(1, 2), new UsdVec2f(3, 4)]),
        LiveAttributeKind.Vec3fArray => LiveAttributeValue.FromVec3fArray(
            [new UsdVec3f(1, 2, 3)]),
        LiveAttributeKind.Color3fArray => LiveAttributeValue.FromColor3fArray(
            [new UsdVec3f(0.1f, 0.2f, 0.3f)]),
        LiveAttributeKind.BooleanArray => LiveAttributeValue.FromBooleanArray([true, false]),
        LiveAttributeKind.TokenArray => LiveAttributeValue.FromTokenArray(["a", "b"]),
        LiveAttributeKind.StringArray => LiveAttributeValue.FromStringArray(["a", "b"]),
        _ => throw new NotSupportedException($"The kind '{kind}' has no fixture.")
    };

    internal static LiveMetadataValue CreateMetadataValue(LiveMetadataKind kind) => kind switch
    {
        LiveMetadataKind.Boolean => LiveMetadataValue.FromBoolean(true),
        LiveMetadataKind.Int64 => LiveMetadataValue.FromInt64(7),
        LiveMetadataKind.Double => LiveMetadataValue.FromDouble(0.25),
        LiveMetadataKind.String => LiveMetadataValue.FromString("metadata"),
        _ => throw new NotSupportedException($"The kind '{kind}' has no fixture.")
    };

    /// <summary>
    /// Describes an update as a stable string. Records compare structurally except for their
    /// collection members, which compare by reference, so a round trip is compared by description
    /// rather than by a reference equality that would pass for the wrong reason.
    /// </summary>
    internal static string Describe(LiveStageUpdate update) => update switch
    {
        SetRelationshipTargetsUpdate value =>
            $"{value.PrimPath}|{value.RelationshipName}|{string.Join(',', value.Targets)}",
        SetVariantSelectionUpdate value =>
            $"{value.PrimPath}|{value.VariantSetName}|{string.Join(',', value.KnownVariants)}|" +
            $"{value.Selection}",
        SetPointInstancerOrientationsUpdate value =>
            $"{value.PrimPath}|{string.Join(
                ',',
                value.Orientations.Select(o => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{o.Real}:{o.X}:{o.Y}:{o.Z}")))}",
        SetAttributeUpdate value =>
            $"{value.PrimPath}|{value.AttributeName}|{value.Value.Kind}|{value.TimeCode}",
        _ => update.ToString()!
    };

    internal static LiveAuthoringSnapshot Snapshot(
        long sequence = 0,
        long epoch = 1,
        IReadOnlyList<LiveStageUpdate>? extraUpdates = null,
        string? sessionId = null)
    {
        List<LiveStageUpdate> updates = [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")];
        if (extraUpdates is not null)
        {
            updates.AddRange(extraUpdates);
        }

        return new LiveAuthoringSnapshot(Epoch(epoch, sessionId), sequence, BridgeRoot, updates);
    }

    internal static LiveAuthoringDelta Delta(
        long sequence = 1,
        long epoch = 1,
        double pressure = 1.5,
        string? sessionId = null) =>
        new(
            Epoch(epoch, sessionId),
            sequence,
            [
                new SetAttributeUpdate(
                    $"{BridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(pressure))
            ],
            correlationId: $"remote-{sequence.ToString(CultureInfo.InvariantCulture)}",
            originId: RemoteOrigin);

    // The frame factories are internal to the protocol package and visible here through
    // InternalsVisibleTo, so a test builds the exact frame a transport would hand to the client.
    internal static BridgeStreamFrame SnapshotFrame(
        long sequence = 0,
        long epoch = 1,
        string? sessionId = null) =>
        BridgeStreamFrame.ForSnapshot(Snapshot(sequence, epoch, extraUpdates: null, sessionId));

    internal static BridgeStreamFrame DeltaFrame(
        long sequence = 1,
        long epoch = 1,
        string? sessionId = null) =>
        BridgeStreamFrame.ForDelta(Delta(sequence, epoch, sessionId: sessionId));

    /// <summary>Creates a resync demand frame for an arbitrary epoch.</summary>
    internal static BridgeStreamFrame ResyncFrame(
        BridgeResyncReason reason,
        long epoch = 1,
        string? sessionId = null) =>
        BridgeStreamFrame.ForResync(Epoch(epoch, sessionId), reason, "resync");

    /// <summary>Creates a delta whose only update needs an optional capability.</summary>
    internal static BridgeStreamFrame CapabilityBoundDeltaFrame(long sequence = 1, long epoch = 1) =>
        BridgeStreamFrame.ForDelta(new LiveAuthoringDelta(
            Epoch(epoch),
            sequence,
            [
                new ApiSchemaUpdate(
                    $"{BridgeRoot}/Cube",
                    "AssetPreviewsAPI",
                    LiveApiSchemaOperation.Apply)
            ],
            correlationId: $"remote-{sequence.ToString(CultureInfo.InvariantCulture)}",
            originId: RemoteOrigin));

    internal static BridgeStreamFrame AcknowledgementFrame() =>
        BridgeStreamFrame.ForAcknowledgement(new LiveAuthoringSessionResult(
            LiveAuthoringSessionOutcome.Applied,
            LiveAuthoringSessionRejection.None,
            1,
            LiveAuthoringSessionState.Synchronized,
            1,
            1,
            "correlation",
            null));

    internal static BridgeStreamFrame KeepAliveFrame() =>
        BridgeStreamFrame.ForKeepAlive(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
}
