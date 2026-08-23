// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Names the points a test can inject a failure at, so rollback can be proven without corrupting
/// a real layer or relying on a disk fault.
/// </summary>
internal enum UsdPhysicsBakeFaultPoint
{
    AfterBegin,
    AfterFirstChunk,
    AfterFirstSample,
    BeforeCommit,
    DuringSave
}

/// <summary>
/// Resolves an immutable result batch against its bindings and authors it in bounded, batched
/// chunks, one native call per chunk.
/// </summary>
/// <remarks>
/// Resolution is deliberately separated from authoring: everything that can make a batch invalid
/// (a revision that moved on, an unbound identity, topology that no longer matches) is detected
/// before the first chunk is built, so an invalid batch never partially mutates a layer.
/// </remarks>
internal static class UsdPhysicsBakeEngine
{
    internal const string StaleIdentityCode = "OPENUSD_PHYSICS_BAKE_STALE_IDENTITY";
    internal const string StaleTopologyCode = "OPENUSD_PHYSICS_BAKE_STALE_TOPOLOGY";
    internal const string UnboundIdentityCode = "OPENUSD_PHYSICS_BAKE_UNBOUND_IDENTITY";
    internal const string RecordRejectedCode = "OPENUSD_PHYSICS_BAKE_RECORD_REJECTED";
    internal const string NativeFailureCode = "OPENUSD_PHYSICS_BAKE_NATIVE_FAILURE";
    internal const string RolledBackCode = "OPENUSD_PHYSICS_BAKE_ROLLED_BACK";
    internal const string CanceledCode = "OPENUSD_PHYSICS_BAKE_CANCELED";
    internal const string CapabilityCode = "OPENUSD_PHYSICS_BAKE_UNAVAILABLE";
    internal const string LayerRejectedCode = "OPENUSD_PHYSICS_BAKE_LAYER_REJECTED";
    internal const string UnsupportedDomainCode = "OPENUSD_PHYSICS_BAKE_UNSUPPORTED_DOMAIN";

    private const ulong PhysicsBakeCapability = 1UL << 19;

    /// <summary>Gets a value indicating whether the loaded runtime can author physics pages.</summary>
    internal static bool IsSupported =>
        (OpenUsdNativeRuntime.Capabilities & PhysicsBakeCapability) != 0;

    /// <summary>
    /// One record of a batch that resolved to a concrete stage path, in deterministic order.
    /// </summary>
    internal readonly record struct ResolvedRecord(
        UsdPhysicsObjectId Id,
        string PrimPath,
        UsdPhysicsBodyPose Pose,
        UsdPhysicsPointSample? Sample);

    /// <summary>The outcome of resolving one batch against its bindings.</summary>
    internal sealed record Resolution(
        bool IsValid,
        List<ResolvedRecord> Records,
        List<UsdPhysicsBakeRecordOutcome> Rejections,
        List<UsdPhysicsDiagnostic> Diagnostics);

    /// <summary>
    /// Resolves every record of a batch to a stage path, rejecting the batch whole when any
    /// identity or topology revision has moved on.
    /// </summary>
    internal static Resolution Resolve(
        UsdPhysicsResultBatch batch,
        UsdPhysicsBakeBindings bindings)
    {
        var records = new List<ResolvedRecord>(batch.RecordCount);
        var rejections = new List<UsdPhysicsBakeRecordOutcome>();
        var diagnostics = new List<UsdPhysicsDiagnostic>();

        if (batch.IdentityRevision != bindings.IdentityRevision)
        {
            diagnostics.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Error,
                UsdPhysicsDiagnosticCategory.Bake,
                StaleIdentityCode,
                $"The batch was produced at identity revision {batch.IdentityRevision} but the " +
                $"bindings describe revision {bindings.IdentityRevision}; nothing was authored."));
            return new Resolution(false, records, rejections, diagnostics);
        }

        foreach (UsdPhysicsBodyPose pose in batch.Bodies)
        {
            if (!bindings.TryGetBinding(pose.Id, out UsdPhysicsBakeBinding binding))
            {
                rejections.Add(new UsdPhysicsBakeRecordOutcome(
                    pose.Id, UsdPhysicsBakeRecordStatus.IdentityUnbound));
                diagnostics.Add(new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Error,
                    UsdPhysicsDiagnosticCategory.Bake,
                    UnboundIdentityCode,
                    $"The simulated identity {pose.Id} is not bound to an extracted prim.",
                    pose.Id));
                continue;
            }

            if (binding.InstanceIndex >= 0)
            {
                // A point-instancer instance has no prim of its own, so authoring a transform for
                // it would either do nothing or move every sibling instance.
                rejections.Add(new UsdPhysicsBakeRecordOutcome(
                    pose.Id, UsdPhysicsBakeRecordStatus.InstanceProxy, binding.InstanceIndex));
                diagnostics.Add(new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Error,
                    UsdPhysicsDiagnosticCategory.Bake,
                    RecordRejectedCode,
                    $"The identity {pose.Id} maps to instance {binding.InstanceIndex} of " +
                    $"'{binding.PrimPath}'; per-instance authoring would corrupt the shared " +
                    "prototype and is refused.",
                    pose.Id));
                continue;
            }

            records.Add(new ResolvedRecord(pose.Id, binding.PrimPath, pose, null));
        }

        foreach (UsdPhysicsPointSample sample in batch.PointSamples)
        {
            if (!bindings.TryGetBinding(sample.Id, out UsdPhysicsBakeBinding binding))
            {
                rejections.Add(new UsdPhysicsBakeRecordOutcome(
                    sample.Id, UsdPhysicsBakeRecordStatus.IdentityUnbound));
                diagnostics.Add(new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Error,
                    UsdPhysicsDiagnosticCategory.Bake,
                    UnboundIdentityCode,
                    $"The simulated identity {sample.Id} is not bound to an extracted prim.",
                    sample.Id));
                continue;
            }

            if (binding.TopologyRevision != sample.TopologyRevision)
            {
                rejections.Add(new UsdPhysicsBakeRecordOutcome(
                    sample.Id, UsdPhysicsBakeRecordStatus.StaleTopology));
                diagnostics.Add(new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Error,
                    UsdPhysicsDiagnosticCategory.Bake,
                    StaleTopologyCode,
                    $"The point sample for {sample.Id} was produced at topology revision " +
                    $"{sample.TopologyRevision} but '{binding.PrimPath}' was extracted at " +
                    $"revision {binding.TopologyRevision}; nothing was authored.",
                    sample.Id));
                continue;
            }

            records.Add(new ResolvedRecord(
                sample.Id, binding.PrimPath, default, sample));
        }

        // Deterministic authoring order: path first, then identity, so identical inputs always
        // produce an identical destination layer.
        records.Sort(static (left, right) =>
        {
            int byPath = string.CompareOrdinal(left.PrimPath, right.PrimPath);
            if (byPath != 0)
            {
                return byPath;
            }
            int byKind = (left.Sample is null ? 0 : 1).CompareTo(right.Sample is null ? 0 : 1);
            return byKind != 0 ? byKind : left.Id.Value.CompareTo(right.Id.Value);
        });

        return new Resolution(rejections.Count == 0, records, rejections, diagnostics);
    }

    /// <summary>Converts an immutable pose into the row-vector matrix USD authors.</summary>
    internal static UsdMatrix4d ToMatrix(in UsdPhysicsBodyPose pose)
    {
        UsdPhysicsOrientation q = pose.Orientation;
        double length = Math.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));
        double scale = length > 0 ? 1.0 / length : 1.0;
        double x = q.X * scale;
        double y = q.Y * scale;
        double z = q.Z * scale;
        double w = q.W * scale;

        return new UsdMatrix4d(
            1 - (2 * ((y * y) + (z * z))), 2 * ((x * y) + (w * z)), 2 * ((x * z) - (w * y)), 0,
            2 * ((x * y) - (w * z)), 1 - (2 * ((x * x) + (z * z))), 2 * ((y * z) + (w * x)), 0,
            2 * ((x * z) + (w * y)), 2 * ((y * z) - (w * x)), 1 - (2 * ((x * x) + (y * y))), 0,
            pose.Position.X, pose.Position.Y, pose.Position.Z, 1);
    }

    /// <summary>Builds the page flags one authoring call runs under.</summary>
    internal static UsdPhysicsBakePageFlags BuildPageFlags(
        UsdPhysicsBakeOptions options,
        bool timeSample,
        bool preflightOnly,
        bool forbidRootLayer)
    {
        UsdPhysicsBakePageFlags flags = UsdPhysicsBakePageFlags.Atomic;
        if (timeSample)
        {
            flags |= UsdPhysicsBakePageFlags.TimeSample;
        }
        if (preflightOnly)
        {
            flags |= UsdPhysicsBakePageFlags.PreflightOnly;
        }
        if (forbidRootLayer)
        {
            flags |= UsdPhysicsBakePageFlags.ForbidRootLayer;
        }
        if (options.TransformSpace == UsdPhysicsBakeTransformSpace.World)
        {
            flags |= UsdPhysicsBakePageFlags.ResetXformStack;
        }
        if (options.WriteExtents)
        {
            flags |= UsdPhysicsBakePageFlags.Extent;
        }
        if (options.WriteSimulationMetadata)
        {
            flags |= UsdPhysicsBakePageFlags.SimulationMetadata;
        }
        flags |= options.ExistingSamplePolicy switch
        {
            UsdPhysicsBakeExistingSamplePolicy.Skip => UsdPhysicsBakePageFlags.SkipExistingSample,
            UsdPhysicsBakeExistingSamplePolicy.Reject => UsdPhysicsBakePageFlags.RejectExistingSample,
            _ => UsdPhysicsBakePageFlags.None
        };
        return flags;
    }

    /// <summary>Stages one resolved record into the page builder.</summary>
    internal static void Stage(
        UsdPhysicsBakePageBuilder builder,
        in ResolvedRecord record,
        UsdPhysicsBakeOptions options)
    {
        if (record.Sample is UsdPhysicsPointSample sample)
        {
            builder.AddPoints(
                record.Id.Value,
                record.PrimPath,
                sample.Points,
                sample.Velocities,
                sample.FaceVertexCounts,
                sample.FaceVertexIndices,
                options.WriteVelocities);
            return;
        }

        UsdMatrix4d matrix = ToMatrix(record.Pose);
        builder.AddTransform(
            record.Id.Value,
            record.PrimPath,
            matrix,
            record.Pose.LinearVelocity,
            record.Pose.AngularVelocity,
            options.WriteVelocities,
            record.Pose.IsKinematic,
            record.Pose.IsSleeping);
    }

    /// <summary>Describes a layer for preflight and result reporting.</summary>
    internal static UsdPhysicsBakeLayerInfo DescribeLayer(UsdStage stage, string identifier)
    {
        ulong flags = OpenUsdNativeRuntime.PhysicsBakeDescribeLayer(stage.Native, identifier);
        return new UsdPhysicsBakeLayerInfo(
            identifier,
            (flags & (1UL << 0)) != 0,
            (flags & (1UL << 1)) != 0,
            (flags & (1UL << 2)) != 0,
            (flags & (1UL << 3)) != 0,
            (flags & (1UL << 4)) != 0,
            (flags & (1UL << 5)) != 0,
            (flags & (1UL << 6)) != 0,
            (flags & (1UL << 7)) != 0,
            (flags & (1UL << 8)) != 0,
            (flags & (1UL << 9)) != 0);
    }

    /// <summary>The outcome of authoring one chunk.</summary>
    internal sealed record ChunkResult(
        bool Succeeded,
        int Applied,
        int Skipped,
        int Rejected,
        int Authored,
        List<UsdPhysicsBakeRecordOutcome> Outcomes,
        string? Message,
        ulong BeforeChangeSerial = 0,
        ulong AfterChangeSerial = 0) : IUsdDetachedResult;

    /// <summary>
    /// Authors one bounded chunk of resolved records through a single native call.
    /// </summary>
    internal static ChunkResult AuthorChunk(
        UsdStage stage,
        string layerIdentifier,
        UsdPhysicsBakePageBuilder builder,
        List<ResolvedRecord> records,
        int offset,
        int count,
        UsdPhysicsBakeOptions options,
        UsdPhysicsBakePageFlags flags,
        double timeCode,
        uint revision)
    {
        builder.Reset();
        for (int index = 0; index < count; ++index)
        {
            Stage(builder, records[offset + index], options);
        }

        ReadOnlySpan<byte> page = builder.Build(flags, timeCode, revision);
        Span<byte> results = builder.ResultSize <= 1024
            ? stackalloc byte[builder.ResultSize]
            : new byte[builder.ResultSize];
        Span<UsdPhysicsObjectKind> kinds = count <= 256
            ? stackalloc UsdPhysicsObjectKind[count]
            : new UsdPhysicsObjectKind[count];
        for (int index = 0; index < count; ++index)
        {
            kinds[index] = records[offset + index].Id.Kind;
        }

        // Read on the scheduler's own thread, immediately around the only call that can move the
        // serial. The scheduler samples the same value just before and just after this callback, so
        // the pair reported here is exactly the pair it publishes for this edit.
        ulong before = stage.ChangeSerial;
        ChunkResult chunk = AuthorPage(stage, layerIdentifier, page, results, kinds);
        return chunk with
        {
            BeforeChangeSerial = before,
            AfterChangeSerial = stage.ChangeSerial
        };
    }

    /// <summary>
    /// Authors one already serialized page through the single native batched authoring call.
    /// </summary>
    /// <remarks>
    /// Every page reaches the runtime through this one funnel, so the runtime only ever has to
    /// defend one entry point against malformed or hostile page content.
    /// </remarks>
    internal static ChunkResult AuthorPage(
        UsdStage stage,
        string layerIdentifier,
        ReadOnlySpan<byte> page,
        Span<byte> results,
        ReadOnlySpan<UsdPhysicsObjectKind> kinds)
    {
        int count = kinds.Length;
        OpenUsdNativeStatus status = OpenUsdNativeRuntime.PhysicsBakeAuthorPage(
            stage.Native, layerIdentifier, page, results, out _, out string? message);

        var outcomes = new List<UsdPhysicsBakeRecordOutcome>(count);
        if (!UsdPhysicsBakeResultPage.TryRead(results, out UsdPhysicsBakeResultPage result))
        {
            return new ChunkResult(
                false, 0, 0, count, 0, outcomes,
                message ?? "The runtime did not report a physics authoring result.");
        }

        for (int index = 0; index < result.RecordCount && index < count; ++index)
        {
            outcomes.Add(result.GetOutcome(index, kinds[index]));
        }

        return new ChunkResult(
            status == OpenUsdNativeStatus.Ok,
            result.AppliedCount,
            result.SkippedCount,
            result.RejectedCount,
            result.AuthoredCount,
            outcomes,
            status == OpenUsdNativeStatus.Ok ? null : message);
    }

    /// <summary>Builds the diagnostic explaining one rejected record.</summary>
    internal static UsdPhysicsDiagnostic DescribeRejection(
        UsdPhysicsBakeRecordOutcome outcome, string primPath, string layerIdentifier) =>
        new(
            UsdPhysicsDiagnosticSeverity.Error,
            UsdPhysicsDiagnosticCategory.Bake,
            RecordRejectedCode,
            $"'{primPath}' was refused by the runtime with {outcome.Status} (detail " +
            $"{outcome.Detail}) while authoring into '{layerIdentifier}'.",
            outcome.Id);
}
