// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Applies complete simulation result batches to the removable physics session overlay so a running
/// simulation can be previewed without ever touching authored scene description.
/// </summary>
/// <remarks>
/// <para>
/// Every opinion the preview authors lands in the overlay's physics layer, which is the strongest
/// temporary layer on the stage and is never saved. The stage root layer, every referenced layer,
/// the session layer, and the overlay's user layer are never written by a preview, so clearing the
/// preview restores exactly what the user and the session authored.
/// </para>
/// <para>
/// A batch is applied whole: if any identity is unbound or any topology revision has moved on, the
/// preview rejects the batch and authors nothing, because a half-applied frame would show a pose
/// that never existed.
/// </para>
/// </remarks>
public sealed class UsdPhysicsPreviewApplier : IDisposable
{
    private readonly UsdStageScheduler _scheduler;
    private readonly UsdSessionOverlay _overlay;
    private readonly UsdPhysicsBakePageBuilder _builder = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Initializes a preview applier over one scheduler and session overlay.</summary>
    /// <param name="scheduler">The scheduler that owns the stage every apply is batched on.</param>
    /// <param name="overlay">The session overlay whose physics layer receives preview opinions.</param>
    /// <param name="options">The authoring options, or <see langword="null"/> for the defaults.</param>
    public UsdPhysicsPreviewApplier(
        UsdStageScheduler scheduler,
        UsdSessionOverlay overlay,
        UsdPhysicsBakeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(overlay);
        _scheduler = scheduler;
        _overlay = overlay;
        Options = options ?? UsdPhysicsBakeOptions.Default;
    }

    /// <summary>Gets the authoring options this applier uses.</summary>
    public UsdPhysicsBakeOptions Options { get; }

    /// <summary>Gets a value indicating whether the loaded runtime can author physics previews.</summary>
    public static bool IsSupported => UsdPhysicsBakeEngine.IsSupported;

    /// <summary>
    /// Applies one complete result batch to the physics overlay layer.
    /// </summary>
    /// <param name="batch">The immutable batch to author.</param>
    /// <param name="bindings">The identity bindings the batch was produced against.</param>
    /// <param name="cancellationToken">Cancels the apply before the first chunk is authored.</param>
    /// <returns>The immutable preview result.</returns>
    public async ValueTask<UsdPhysicsPreviewResult> ApplyAsync(
        UsdPhysicsResultBatch batch,
        UsdPhysicsBakeBindings bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsSupported)
        {
            return Failed(
                UsdPhysicsBakeStatus.NotSupported,
                UsdPhysicsBakeEngine.CapabilityCode,
                "The loaded native runtime does not provide batched physics authoring.");
        }

        UsdPhysicsBakeEngine.Resolution resolution =
            UsdPhysicsBakeEngine.Resolve(batch, bindings);
        if (!resolution.IsValid)
        {
            return new UsdPhysicsPreviewResult(
                UsdPhysicsBakeStatus.Failed,
                0,
                0,
                resolution.Rejections.Count,
                0,
                resolution.Rejections,
                new UsdPhysicsDiagnostics(resolution.Diagnostics));
        }

        string layerIdentifier = _overlay.PhysicsLayerIdentifier;
        UsdPhysicsBakePageFlags flags = UsdPhysicsBakeEngine.BuildPageFlags(
            Options, timeSample: false, preflightOnly: false, forbidRootLayer: true);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var edits = new List<UsdPhysicsPreviewEdit>();
        try
        {
            var applied = 0;
            var skipped = 0;
            var rejected = 0;
            var authored = 0;
            var outcomes = new List<UsdPhysicsBakeRecordOutcome>(resolution.Records.Count);
            var diagnostics = new List<UsdPhysicsDiagnostic>(resolution.Diagnostics);

            for (int offset = 0; offset < resolution.Records.Count; offset += Options.ChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(Options.ChunkSize, resolution.Records.Count - offset);
                int chunkOffset = offset;

                UsdPhysicsBakeEngine.ChunkResult chunk = await _scheduler.EditAsync(
                    stage => UsdPhysicsBakeEngine.AuthorChunk(
                        stage,
                        layerIdentifier,
                        _builder,
                        resolution.Records,
                        chunkOffset,
                        count,
                        Options,
                        flags,
                        batch.TimeCode,
                        unchecked((uint)batch.IdentityRevision)),
                    UsdStageInvalidationKind.Property,
                    cancellationToken).ConfigureAwait(false);

                applied += chunk.Applied;
                skipped += chunk.Skipped;
                rejected += chunk.Rejected;
                authored += chunk.Authored;
                outcomes.AddRange(chunk.Outcomes);
                Record(edits, chunk.BeforeChangeSerial, chunk.AfterChangeSerial,
                    UsdStageInvalidationKind.Property);

                if (!chunk.Succeeded)
                {
                    AddChunkDiagnostics(
                        diagnostics, chunk, resolution.Records, chunkOffset, layerIdentifier);
                    return new UsdPhysicsPreviewResult(
                        UsdPhysicsBakeStatus.Failed,
                        applied,
                        skipped,
                        rejected,
                        authored,
                        outcomes,
                        new UsdPhysicsDiagnostics(diagnostics),
                        edits);
                }
            }

            return new UsdPhysicsPreviewResult(
                UsdPhysicsBakeStatus.Completed,
                applied,
                skipped,
                rejected,
                authored,
                outcomes,
                new UsdPhysicsDiagnostics(diagnostics),
                edits);
        }
        catch (OperationCanceledException)
        {
            return Failed(
                UsdPhysicsBakeStatus.Canceled,
                UsdPhysicsBakeEngine.CanceledCode,
                "The physics preview was canceled before it finished applying the batch.",
                edits);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Clears every preview opinion from the physics overlay layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Any opinion a user authored while the overlay was active is first migrated into the overlay's
    /// user layer, so stopping or resetting a simulation never discards the user's own edits.
    /// </para>
    /// <para>
    /// Contamination detection, contamination migration, and the physics layer clear all run inside
    /// one scheduled edit, because all three touch the scheduler-owned stage and must not interleave
    /// with other stage work. They therefore also produce a single reported change.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the clear before it runs.</param>
    /// <returns>The immutable clear result.</returns>
    public async ValueTask<UsdPhysicsPreviewClearResult> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSupported)
        {
            return new UsdPhysicsPreviewClearResult(UsdPhysicsBakeStatus.NotSupported, false);
        }

        string layerIdentifier = _overlay.PhysicsLayerIdentifier;
        UsdSessionOverlay overlay = _overlay;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (bool migrated, ulong before, ulong after) = await _scheduler.EditAsync(
                stage =>
                {
                    ulong beforeSerial = stage.ChangeSerial;
                    var didMigrate = false;
                    if (overlay.DetectContamination())
                    {
                        overlay.MigrateContamination();
                        didMigrate = true;
                    }

                    OpenUsdNativeRuntime.PhysicsBakeClearLayer(stage.Native, layerIdentifier);
                    return (didMigrate, beforeSerial, stage.ChangeSerial);
                },
                UsdStageInvalidationKind.Full,
                cancellationToken).ConfigureAwait(false);

            var edits = new List<UsdPhysicsPreviewEdit>(1);
            Record(edits, before, after, UsdStageInvalidationKind.Full);
            return new UsdPhysicsPreviewClearResult(
                UsdPhysicsBakeStatus.Completed, migrated, edits);
        }
        catch (OperationCanceledException)
        {
            return new UsdPhysicsPreviewClearResult(UsdPhysicsBakeStatus.Canceled, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _builder.Dispose();
        _gate.Dispose();
    }

    private static void AddChunkDiagnostics(
        List<UsdPhysicsDiagnostic> diagnostics,
        UsdPhysicsBakeEngine.ChunkResult chunk,
        List<UsdPhysicsBakeEngine.ResolvedRecord> records,
        int offset,
        string layerIdentifier)
    {
        for (int index = 0; index < chunk.Outcomes.Count; ++index)
        {
            UsdPhysicsBakeRecordOutcome outcome = chunk.Outcomes[index];
            if (outcome.Status is UsdPhysicsBakeRecordStatus.Applied or
                UsdPhysicsBakeRecordStatus.Skipped)
            {
                continue;
            }
            diagnostics.Add(UsdPhysicsBakeEngine.DescribeRejection(
                outcome, records[offset + index].PrimPath, layerIdentifier));
        }

        if (chunk.Message is { Length: > 0 } message)
        {
            diagnostics.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Error,
                UsdPhysicsDiagnosticCategory.Bake,
                UsdPhysicsBakeEngine.NativeFailureCode,
                message));
        }
    }

    private static void Record(
        List<UsdPhysicsPreviewEdit> edits,
        ulong before,
        ulong after,
        UsdStageInvalidationKind invalidation)
    {
        if (after != before)
        {
            edits.Add(new UsdPhysicsPreviewEdit(before, after, invalidation));
        }
    }

    private static UsdPhysicsPreviewResult Failed(
        UsdPhysicsBakeStatus status,
        string code,
        string message,
        IEnumerable<UsdPhysicsPreviewEdit>? edits = null) =>
        new(
            status,
            0,
            0,
            0,
            0,
            [],
            new UsdPhysicsDiagnostics(
            [
                new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Error,
                    UsdPhysicsDiagnosticCategory.Bake,
                    code,
                    message)
            ]),
            edits);
}
