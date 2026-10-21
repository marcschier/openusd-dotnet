// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Physics.Extraction;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed partial class OpenUsdWorkspaceSessionBackend
{
    private sealed record PhysicsPreparationCache(
        ulong Revision, double TimeCode, bool FullScene, UsdPhysicsExtractionPage Page);

    private PhysicsPreparationCache? _physicsPreparationCache;

    public ValueTask<WorkspacePhysicsRecording> PreparePhysicsRecordingAsync(
        ulong expectedStageRevision, double expectedMetersPerUnit, string expectedUpAxis, CancellationToken cancellationToken) =>
        PreparePhysicsRecordingAsync(expectedStageRevision, expectedMetersPerUnit, expectedUpAxis, false, cancellationToken);

    public async ValueTask<WorkspacePhysicsRecording> PreparePhysicsRecordingAsync(
        ulong expectedStageRevision, double expectedMetersPerUnit, string expectedUpAxis,
        bool fullScenePhysics, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(expectedMetersPerUnit) || expectedMetersPerUnit <= 0 ||
            expectedUpAxis is not ("X" or "Y" or "Z"))
        {
            throw new ArgumentException("Explicit valid workspace units and up axis are required.");
        }
        await RequirePhysicsRevisionAsync(expectedStageRevision, cancellationToken).ConfigureAwait(false);
        UsdPhysicsTransport transport = await UsdPhysicsTransport.CreateAsync(_scheduler,
            new UsdPhysicsTransportOptions(new UsdPhysicsSessionOptions(
                maxRigidBodies: fullScenePhysics ? 32768 : 4096,
                maxColliders: fullScenePhysics ? 65536 : 4096)),
            cancellationToken).ConfigureAwait(false);
        try
        {
            var extractionOptions = new UsdPhysicsExtractionOptions
            {
                TimeCode = transport.Timeline.StartTimeCode,
                IncludeUnmapped = true,
                SkipGuide = false,
                SkipInvisible = false,
                MaxObjects = 4096,
                MaxProperties = 65536,
                MaxRelationships = 32768,
                MaxTargets = 65536,
                MaxNumbers = 1 << 20,
                MaxTexts = 32768,
                MaxPoints = 1 << 20,
                MaxIndices = 3 << 20,
                MaxDiagnostics = 1024,
                MaxStringBytes = 4 << 20
            };
            if (fullScenePhysics)
            {
                extractionOptions = new UsdPhysicsExtractionOptions
                {
                    TimeCode = transport.Timeline.StartTimeCode,
                    IncludeUnmapped = true,
                    SkipGuide = false,
                    SkipInvisible = false,
                    MaxObjects = 65536
                };
            }
            PhysicsPreparationCache? cached;
            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_closing || _disposed, this);
                if (_physicsPreparationCache is { } previous && previous.Revision != expectedStageRevision)
                {
                    _physicsPreparationCache = null;
                }
                cached = _physicsPreparationCache;
            }
            UsdPhysicsExtractionPage page;
            if (cached is not null && cached.Revision == expectedStageRevision &&
                cached.TimeCode == transport.Timeline.StartTimeCode && cached.FullScene == fullScenePhysics)
            {
                page = cached.Page;
            }
            else
            {
                UsdPhysicsExtractionPage metadata = await UsdPhysicsStageExtractor.ExtractAsync(
                    _scheduler, extractionOptions with { IncludeMeshData = false }, cancellationToken).ConfigureAwait(false);
                RequirePhysicsUnits(metadata, expectedMetersPerUnit, expectedUpAxis);
                page = await UsdPhysicsStageExtractor.PrepareAsync(
                    _scheduler, extractionOptions, null, cancellationToken).ConfigureAwait(false);
            }
            RequirePhysicsUnits(page, expectedMetersPerUnit, expectedUpAxis);
            UsdPhysicsExtractionPageTraits refusedFlags = UsdPhysicsExtractionPageTraits.Truncated |
                UsdPhysicsExtractionPageTraits.HasDisabledObjects;
            if (!fullScenePhysics)
            {
                refusedFlags |= UsdPhysicsExtractionPageTraits.HasInstances;
            }
            if (page.TruncationFlags != 0 || (page.Flags & refusedFlags) != 0)
            {
                throw new NotSupportedException(
                    fullScenePhysics
                        ? "Full-scene physics recording requires a complete extraction without disabled objects."
                        : "Physics recording requires a complete, noninstanced extraction without disabled objects.");
            }
            for (int index = 0; index < page.ObjectCount; index++)
            {
                UsdPhysicsExtractionObject item = page.GetObject(index);
                if (item.IsEnabled && item.Kind == UsdPhysicsExtractionObjectKind.Collider &&
                    item.ParentBodyIndex >= 0 &&
                    (item.Flags & UsdPhysicsExtractionObjectTraits.InstanceProxy) != 0)
                {
                    throw new NotSupportedException(
                        "Moving instance subtrees must be de-instanced in the scenario; static instances remain supported by the full-scene profile.");
                }
                if (item.IsEnabled && item.Kind is
                    UsdPhysicsExtractionObjectKind.ArticulationRoot or
                    UsdPhysicsExtractionObjectKind.CharacterController or
                    UsdPhysicsExtractionObjectKind.Vehicle or
                    UsdPhysicsExtractionObjectKind.ParticleSystem or
                    UsdPhysicsExtractionObjectKind.ParticleSet or
                    UsdPhysicsExtractionObjectKind.SurfaceDeformable or
                    UsdPhysicsExtractionObjectKind.VolumeDeformable)
                {
                    throw new NotSupportedException("This MCP recording profile currently accepts CPU rigid bodies and joints.");
                }
            }
            await RequirePhysicsRevisionAsync(expectedStageRevision, cancellationToken).ConfigureAwait(false);
            await transport.AttachExtractionAsync(page, cancellationToken).ConfigureAwait(false);
            await transport.BuildAsync(cancellationToken).ConfigureAwait(false);
            if (transport.Diagnostics.Entries.Any(static diagnostic =>
                diagnostic.Severity == UsdPhysicsDiagnosticSeverity.Error ||
                diagnostic.Code == "OPENUSD_PHYSICS_COMPOSITION_SKIPPED"))
            {
                throw new NotSupportedException("The native world did not accept the complete requested physics content.");
            }
            if (!transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease initial))
            {
                throw new InvalidOperationException("The physics world published no initial frame.");
            }
            PhysicsRenderBindingTable bindings;
            PhysicsRenderCapacities capacities;
            using (initial)
            {
                UsdPhysicsFrame frame = initial.Frame;
                if (frame.BodyCount == 0 || frame.HasOverflow || frame.BodiesTruncated || frame.DeformationsTruncated ||
                    frame.DeformationCount != 0 || frame.DroppedEventCount != 0)
                {
                    throw new NotSupportedException("The CPU recording world has no complete supported body publication.");
                }
                capacities = new PhysicsRenderCapacities(frame.BodyCapacity);
                bindings = CreateRecordingBindings(page, frame);
            }
            await RequirePhysicsRevisionAsync(expectedStageRevision, cancellationToken).ConfigureAwait(false);
            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_closing || _disposed, this);
                _physicsPreparationCache = new PhysicsPreparationCache(
                    expectedStageRevision, transport.Timeline.StartTimeCode, fullScenePhysics, page);
            }
            return new WorkspacePhysicsRecording(transport, bindings, capacities, expectedStageRevision);
        }
        catch (Exception operationFailure)
        {
            try
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Physics preparation failed and its world could not be disposed.",
                    operationFailure, cleanupFailure);
            }
            throw;
        }
    }

    private static void RequirePhysicsUnits(
        UsdPhysicsExtractionPage page, double expectedMetersPerUnit, string expectedUpAxis)
    {
        if (page.MetersPerUnit != expectedMetersPerUnit ||
            !string.Equals(page.UpAxis.ToString(), expectedUpAxis, StringComparison.Ordinal))
        {
            throw new NotSupportedException(FormattableString.Invariant(
                $"Workspace physics uses metersPerUnit={page.MetersPerUnit:R}, upAxis={page.UpAxis}; source-sublayer root metadata is not inherited. Use open_scene stageSettings to author explicit root settings; recording expectations must match."));
        }
    }

    private async ValueTask RequirePhysicsRevisionAsync(ulong expected, CancellationToken cancellationToken)
    {
        if (await GetStageRevisionAsync(cancellationToken).ConfigureAwait(false) != expected)
        {
            throw new InvalidOperationException("The stage changed during physics recording preparation.");
        }
    }

    private static PhysicsRenderBindingTable CreateRecordingBindings(
        UsdPhysicsExtractionPage page, UsdPhysicsFrame frame)
    {
        var kinds = new HashSet<UsdPhysicsObjectKind>();
        foreach (UsdPhysicsBodyPose body in frame.Bodies)
        {
            kinds.Add(body.Id.Kind);
        }
        var paths = new Dictionary<ulong, string>();
        for (int index = 0; index < page.ObjectCount; index++)
        {
            string path = page.GetObject(index).Path;
            foreach (UsdPhysicsObjectKind kind in kinds)
            {
                ulong id = UsdPhysicsIdentities.ForSimulatedObject(path, kind).Value;
                if (paths.TryGetValue(id, out string? previous) && previous != path)
                {
                    throw new InvalidDataException("Physics recording identities are ambiguous.");
                }
                paths[id] = path;
            }
        }
        var bindings = new PhysicsRenderBindingTable(frame.BodyCapacity);
        foreach (UsdPhysicsBodyPose body in frame.Bodies)
        {
            if (!paths.TryGetValue(body.Id.Value, out string? path) ||
                !bindings.TryBindSubtree(new PhysicsRenderObjectId(body.Id.Value), path, body.Position,
                    new PhysicsRenderOrientation(body.Orientation.X, body.Orientation.Y, body.Orientation.Z, body.Orientation.W)))
            {
                throw new InvalidDataException("A simulated body cannot be bound to its extracted source.");
            }
        }
        return bindings;
    }
}
