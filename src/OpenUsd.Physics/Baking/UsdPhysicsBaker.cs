// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Interop;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Bakes simulated results into a writable, file-backed destination layer as one all-or-nothing
/// transaction per destination.
/// </summary>
/// <remarks>
/// <para>
/// A bake is an explicit user operation and behaves like one. Everything that could fail is checked
/// before the first mutation: the destination must resolve, be in the stage local layer stack, be
/// file backed, be unmuted, permit editing and saving, and never be the root or session layer, and
/// every record must resolve to a prim that is neither an instance proxy nor inside a prototype and
/// whose sample capacity matches the composed topology.
/// </para>
/// <para>
/// Before the first sample is authored the destination layer's complete content is snapshotted. Any
/// failure, cancellation, or save error restores that snapshot exactly, so a failed bake leaves the
/// destination byte-for-byte as it was. The stage root layer is never reloaded, the session overlay
/// is never touched, and the caller's edit target is restored by the runtime after every chunk.
/// </para>
/// </remarks>
public sealed class UsdPhysicsBaker : IDisposable
{
    private readonly UsdStageScheduler _scheduler;
    private readonly UsdPhysicsBakePageBuilder _builder = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Initializes a baker over one scheduler.</summary>
    /// <param name="scheduler">The scheduler that owns the stage every bake is batched on.</param>
    public UsdPhysicsBaker(UsdStageScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    /// <summary>Gets a value indicating whether the loaded runtime can bake physics results.</summary>
    public static bool IsSupported => UsdPhysicsBakeEngine.IsSupported;

    /// <summary>
    /// Injects a failure at one authoring stage so rollback can be verified without a disk fault.
    /// </summary>
    internal Func<UsdPhysicsBakeFaultPoint, Exception?>? FaultInjector { get; set; }

    /// <summary>
    /// Validates a destination and one batch without mutating the stage in any way.
    /// </summary>
    /// <param name="spec">The bake request to validate.</param>
    /// <param name="batch">A representative batch whose records are validated.</param>
    /// <param name="bindings">The identity bindings the batch was produced against.</param>
    /// <param name="cancellationToken">Cancels the preflight.</param>
    /// <returns>The immutable preflight result.</returns>
    public async ValueTask<UsdPhysicsBakePreflightResult> PreflightAsync(
        UsdPhysicsBakeSpec spec,
        UsdPhysicsResultBatch batch,
        UsdPhysicsBakeBindings bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var diagnostics = new List<UsdPhysicsDiagnostic>();
        if (!IsSupported)
        {
            diagnostics.Add(Diagnostic(
                UsdPhysicsBakeEngine.CapabilityCode,
                "The loaded native runtime does not provide batched physics authoring."));
            return new UsdPhysicsBakePreflightResult(
                false,
                new UsdPhysicsBakeLayerInfo(
                    spec.DestinationLayerIdentifier,
                    false, false, false, false, false, false, false, false, false, false),
                0,
                [],
                new UsdPhysicsDiagnostics(diagnostics));
        }

        UsdPhysicsBakeEngine.Resolution resolution =
            UsdPhysicsBakeEngine.Resolve(batch, bindings);
        diagnostics.AddRange(resolution.Diagnostics);

        (UsdPhysicsBakeLayerInfo layer, double start, double end, double stride) =
            await _scheduler.InvokeAsync(
                stage => (
                    UsdPhysicsBakeEngine.DescribeLayer(stage, spec.DestinationLayerIdentifier),
                    spec.StartTimeCode ?? stage.StartTimeCode,
                    spec.EndTimeCode ?? stage.EndTimeCode,
                    ResolveStride(spec)),
                cancellationToken).ConfigureAwait(false);

        bool layerOk = ValidateLayer(layer, diagnostics);
        int sampleCount = CountSamples(start, end, stride);
        var outcomes = new List<UsdPhysicsBakeRecordOutcome>(resolution.Rejections);

        if (layerOk && resolution.IsValid && resolution.Records.Count > 0)
        {
            UsdPhysicsBakePageFlags flags = UsdPhysicsBakeEngine.BuildPageFlags(
                spec.Options, timeSample: true, preflightOnly: true, forbidRootLayer: false);
            UsdPhysicsBakeEngine.ChunkResult chunk = await _scheduler.EditAsync(
                stage => UsdPhysicsBakeEngine.AuthorChunk(
                    stage,
                    spec.DestinationLayerIdentifier,
                    _builder,
                    resolution.Records,
                    0,
                    resolution.Records.Count,
                    spec.Options,
                    flags,
                    start,
                    unchecked((uint)batch.IdentityRevision)),
                UsdStageInvalidationKind.Property,
                cancellationToken).ConfigureAwait(false);

            for (int index = 0; index < chunk.Outcomes.Count; ++index)
            {
                UsdPhysicsBakeRecordOutcome outcome = chunk.Outcomes[index];
                if (outcome.Status == UsdPhysicsBakeRecordStatus.Applied)
                {
                    continue;
                }
                outcomes.Add(outcome);
                diagnostics.Add(UsdPhysicsBakeEngine.DescribeRejection(
                    outcome,
                    resolution.Records[index].PrimPath,
                    spec.DestinationLayerIdentifier));
            }
        }

        bool canBake = layerOk && resolution.IsValid && outcomes.Count == 0 && sampleCount > 0;
        if (sampleCount <= 0)
        {
            diagnostics.Add(Diagnostic(
                UsdPhysicsBakeEngine.LayerRejectedCode,
                "The requested time range and stride select no samples."));
        }

        return new UsdPhysicsBakePreflightResult(
            canBake, layer, sampleCount, outcomes, new UsdPhysicsDiagnostics(diagnostics));
    }

    /// <summary>
    /// Commits exactly one batch to the destination layer as a single time sample.
    /// </summary>
    /// <param name="spec">The bake request naming the destination and options.</param>
    /// <param name="batch">The complete batch to author at its own time code.</param>
    /// <param name="bindings">The identity bindings the batch was produced against.</param>
    /// <param name="cancellationToken">Cancels the commit, which rolls the destination back.</param>
    /// <returns>The immutable bake result.</returns>
    public ValueTask<UsdPhysicsBakeTransactionResult> CommitFrameAsync(
        UsdPhysicsBakeSpec spec,
        UsdPhysicsResultBatch batch,
        UsdPhysicsBakeBindings bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(batch);
        return BakeAsync(
            spec,
            new SingleBatchSource(batch),
            bindings,
            progress: null,
            singleTimeCode: batch.TimeCode,
            cancellationToken);
    }

    /// <summary>
    /// Bakes every sample of a time range into the destination layer as one transaction.
    /// </summary>
    /// <param name="spec">The bake request naming the destination, range, stride, and options.</param>
    /// <param name="source">The source that supplies one complete batch per sampled time code.</param>
    /// <param name="bindings">The identity bindings every batch was produced against.</param>
    /// <param name="progress">
    /// Receives progress between bounded chunks, or <see langword="null"/> for no progress.
    /// </param>
    /// <param name="cancellationToken">Cancels the bake, which rolls the destination back.</param>
    /// <returns>The immutable bake result.</returns>
    public ValueTask<UsdPhysicsBakeTransactionResult> BakeAsync(
        UsdPhysicsBakeSpec spec,
        IUsdPhysicsBakeSource source,
        UsdPhysicsBakeBindings bindings,
        IProgress<UsdPhysicsBakeProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BakeAsync(spec, source, bindings, progress, singleTimeCode: null, cancellationToken);

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

    private async ValueTask<UsdPhysicsBakeTransactionResult> BakeAsync(
        UsdPhysicsBakeSpec spec,
        IUsdPhysicsBakeSource source,
        UsdPhysicsBakeBindings bindings,
        IProgress<UsdPhysicsBakeProgress>? progress,
        double? singleTimeCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var diagnostics = new List<UsdPhysicsDiagnostic>();
        if (!IsSupported)
        {
            diagnostics.Add(Diagnostic(
                UsdPhysicsBakeEngine.CapabilityCode,
                "The loaded native runtime does not provide batched physics authoring."));
            return Failure(
                UsdPhysicsBakeStatus.NotSupported,
                new UsdPhysicsBakeLayerInfo(
                    spec.DestinationLayerIdentifier,
                    false, false, false, false, false, false, false, false, false, false),
                diagnostics,
                rolledBack: false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ulong transaction = 0;
        var layer = new UsdPhysicsBakeLayerInfo(
            spec.DestinationLayerIdentifier,
            false, false, false, false, false, false, false, false, false, false);
        try
        {
            double[] timeCodes;
            (layer, timeCodes) = await _scheduler.InvokeAsync(
                stage =>
                {
                    UsdPhysicsBakeLayerInfo info =
                        UsdPhysicsBakeEngine.DescribeLayer(stage, spec.DestinationLayerIdentifier);
                    double[] samples = singleTimeCode is { } only
                        ? [only]
                        : BuildTimeCodes(
                            spec.StartTimeCode ?? stage.StartTimeCode,
                            spec.EndTimeCode ?? stage.EndTimeCode,
                            ResolveStride(spec));
                    return (info, samples);
                },
                cancellationToken).ConfigureAwait(false);

            if (!ValidateLayer(layer, diagnostics) || timeCodes.Length == 0)
            {
                if (timeCodes.Length == 0)
                {
                    diagnostics.Add(Diagnostic(
                        UsdPhysicsBakeEngine.LayerRejectedCode,
                        "The requested time range and stride select no samples."));
                }
                return Failure(
                    UsdPhysicsBakeStatus.Failed, layer, diagnostics, rolledBack: false);
            }

            transaction = await _scheduler.EditAsync(
                stage => OpenUsdNativeRuntime.PhysicsBakeBegin(
                    stage.Native, spec.DestinationLayerIdentifier),
                UsdStageInvalidationKind.Property,
                cancellationToken).ConfigureAwait(false);

            ThrowInjectedFault(UsdPhysicsBakeFaultPoint.AfterBegin);

            UsdPhysicsBakePageFlags flags = UsdPhysicsBakeEngine.BuildPageFlags(
                spec.Options, timeSample: true, preflightOnly: false, forbidRootLayer: false);

            var completedSamples = 0;
            var completedRecords = 0;
            var authoredAttributes = 0;
            var outcomes = new List<UsdPhysicsBakeRecordOutcome>();

            foreach (double timeCode in timeCodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UsdPhysicsResultBatch? batch =
                    await source.GetBatchAsync(timeCode, cancellationToken).ConfigureAwait(false);
                if (batch is null)
                {
                    diagnostics.Add(Diagnostic(
                        UsdPhysicsBakeEngine.UnsupportedDomainCode,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The bake source produced no results for time code {timeCode}.")));
                    return await RollbackAsync(
                        spec, transaction, layer, diagnostics, UsdPhysicsBakeStatus.Failed)
                        .ConfigureAwait(false);
                }

                UsdPhysicsBakeEngine.Resolution resolution =
                    UsdPhysicsBakeEngine.Resolve(batch, bindings);
                if (!resolution.IsValid)
                {
                    diagnostics.AddRange(resolution.Diagnostics);
                    outcomes.Clear();
                    outcomes.AddRange(resolution.Rejections);
                    return await RollbackAsync(
                        spec, transaction, layer, diagnostics, UsdPhysicsBakeStatus.Failed,
                        outcomes).ConfigureAwait(false);
                }

                outcomes.Clear();
                for (int offset = 0;
                     offset < resolution.Records.Count;
                     offset += spec.Options.ChunkSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(
                        spec.Options.ChunkSize, resolution.Records.Count - offset);
                    int chunkOffset = offset;

                    UsdPhysicsBakeEngine.ChunkResult chunk = await _scheduler.EditAsync(
                        stage => UsdPhysicsBakeEngine.AuthorChunk(
                            stage,
                            spec.DestinationLayerIdentifier,
                            _builder,
                            resolution.Records,
                            chunkOffset,
                            count,
                            spec.Options,
                            flags,
                            timeCode,
                            unchecked((uint)batch.IdentityRevision)),
                        UsdStageInvalidationKind.Property,
                        cancellationToken).ConfigureAwait(false);

                    outcomes.AddRange(chunk.Outcomes);
                    authoredAttributes += chunk.Authored;
                    completedRecords += chunk.Applied;

                    if (!chunk.Succeeded)
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
                                outcome,
                                resolution.Records[chunkOffset + index].PrimPath,
                                spec.DestinationLayerIdentifier));
                        }
                        if (chunk.Message is { Length: > 0 } message)
                        {
                            diagnostics.Add(
                                Diagnostic(UsdPhysicsBakeEngine.NativeFailureCode, message));
                        }
                        return await RollbackAsync(
                            spec, transaction, layer, diagnostics, UsdPhysicsBakeStatus.Failed,
                            outcomes).ConfigureAwait(false);
                    }

                    ThrowInjectedFault(UsdPhysicsBakeFaultPoint.AfterFirstChunk);
                    progress?.Report(new UsdPhysicsBakeProgress(
                        completedSamples, timeCodes.Length, timeCode, completedRecords));
                }

                ++completedSamples;
                ThrowInjectedFault(UsdPhysicsBakeFaultPoint.AfterFirstSample);
                progress?.Report(new UsdPhysicsBakeProgress(
                    completedSamples, timeCodes.Length, timeCode, completedRecords));
            }

            ThrowInjectedFault(UsdPhysicsBakeFaultPoint.BeforeCommit);

            bool save = spec.Save && layer.IsFileBacked && layer.IsSaveable;
            ulong committing = transaction;

            // Runs immediately before the native commit performs the save, so a test can
            // make the destination genuinely unwritable and exercise the real save
            // failure path instead of a simulated one.
            ThrowInjectedFault(UsdPhysicsBakeFaultPoint.DuringSave);

            await _scheduler.EditAsync(
                stage =>
                {
                    OpenUsdNativeRuntime.PhysicsBakeCommit(
                        stage.Native, spec.DestinationLayerIdentifier, committing, save);
                    return true;
                },
                UsdStageInvalidationKind.Property,
                cancellationToken).ConfigureAwait(false);
            transaction = 0;

            return new UsdPhysicsBakeTransactionResult(
                UsdPhysicsBakeStatus.Completed,
                layer,
                completedSamples,
                completedRecords,
                authoredAttributes,
                wasRolledBack: false,
                wasSaved: save,
                outcomes,
                new UsdPhysicsDiagnostics(diagnostics));
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(Diagnostic(
                UsdPhysicsBakeEngine.CanceledCode,
                "The bake was canceled; the destination layer was restored."));
            return await RollbackAsync(
                spec, transaction, layer, diagnostics, UsdPhysicsBakeStatus.Canceled)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            diagnostics.Add(Diagnostic(
                UsdPhysicsBakeEngine.NativeFailureCode, exception.Message));
            return await RollbackAsync(
                spec, transaction, layer, diagnostics, UsdPhysicsBakeStatus.Failed)
                .ConfigureAwait(false);
        }
        finally
        {
            if (transaction != 0)
            {
                OpenUsdNativeRuntime.PhysicsBakeRelease(transaction);
            }
            _gate.Release();
        }
    }

    private async ValueTask<UsdPhysicsBakeTransactionResult> RollbackAsync(
        UsdPhysicsBakeSpec spec,
        ulong transaction,
        UsdPhysicsBakeLayerInfo layer,
        List<UsdPhysicsDiagnostic> diagnostics,
        UsdPhysicsBakeStatus status,
        List<UsdPhysicsBakeRecordOutcome>? outcomes = null)
    {
        var rolledBack = false;
        if (transaction != 0)
        {
            try
            {
                // Rollback must run even when the caller's token is already canceled, otherwise a
                // canceled bake would leave half its samples behind.
                await _scheduler.EditAsync(
                    stage =>
                    {
                        OpenUsdNativeRuntime.PhysicsBakeRollback(
                            stage.Native, spec.DestinationLayerIdentifier, transaction);
                        return true;
                    },
                    UsdStageInvalidationKind.Full,
                    CancellationToken.None).ConfigureAwait(false);
                rolledBack = true;
                diagnostics.Add(new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Information,
                    UsdPhysicsDiagnosticCategory.Bake,
                    UsdPhysicsBakeEngine.RolledBackCode,
                    $"'{spec.DestinationLayerIdentifier}' was restored to the content it held " +
                    "before the bake began."));
            }
            catch (Exception exception) when (exception is not ObjectDisposedException)
            {
                diagnostics.Add(Diagnostic(
                    UsdPhysicsBakeEngine.NativeFailureCode,
                    $"The destination layer could not be restored: {exception.Message}"));
            }
        }

        return new UsdPhysicsBakeTransactionResult(
            status,
            layer,
            0,
            0,
            0,
            rolledBack,
            wasSaved: false,
            outcomes ?? [],
            new UsdPhysicsDiagnostics(diagnostics));
    }

    private void ThrowInjectedFault(UsdPhysicsBakeFaultPoint point)
    {
        if (FaultInjector?.Invoke(point) is { } exception)
        {
            throw exception;
        }
    }

    private static bool ValidateLayer(
        UsdPhysicsBakeLayerInfo layer, List<UsdPhysicsDiagnostic> diagnostics)
    {
        int before = diagnostics.Count;
        if (!layer.Exists)
        {
            diagnostics.Add(Reject(layer, "it does not resolve to a layer"));
        }
        else
        {
            if (!layer.IsLocal)
            {
                diagnostics.Add(Reject(layer, "it is not in the stage local layer stack"));
            }
            if (layer.IsRootLayer)
            {
                diagnostics.Add(Reject(layer, "a bake never authors into the stage root layer"));
            }
            if (layer.IsSessionLayer)
            {
                diagnostics.Add(Reject(layer, "a bake never authors into the session layer"));
            }
            if (layer.IsAnonymous || !layer.IsFileBacked)
            {
                diagnostics.Add(Reject(layer, "it is not file backed"));
            }
            if (layer.IsMuted)
            {
                diagnostics.Add(Reject(layer, "the stage currently mutes it"));
            }
            if (!layer.IsEditable)
            {
                diagnostics.Add(Reject(layer, "it does not permit editing"));
            }
            if (!layer.IsSaveable)
            {
                diagnostics.Add(Reject(layer, "it is read only and cannot be saved"));
            }
        }

        return diagnostics.Count == before;
    }

    private static UsdPhysicsDiagnostic Reject(UsdPhysicsBakeLayerInfo layer, string reason) =>
        Diagnostic(
            UsdPhysicsBakeEngine.LayerRejectedCode,
            $"'{layer.Identifier}' cannot be a bake destination because {reason}.");

    private static UsdPhysicsDiagnostic Diagnostic(string code, string message) =>
        new(
            UsdPhysicsDiagnosticSeverity.Error,
            UsdPhysicsDiagnosticCategory.Bake,
            code,
            message);

    private static UsdPhysicsBakeTransactionResult Failure(
        UsdPhysicsBakeStatus status,
        UsdPhysicsBakeLayerInfo layer,
        List<UsdPhysicsDiagnostic> diagnostics,
        bool rolledBack) =>
        new(
            status,
            layer,
            0,
            0,
            0,
            rolledBack,
            wasSaved: false,
            [],
            new UsdPhysicsDiagnostics(diagnostics));

    /// <summary>
    /// Resolves the sample stride in time codes. An unset stride samples every time code, which is
    /// the stage's own sampling rate because a stage advances one time code per
    /// <see cref="UsdStage.TimeCodesPerSecond"/>th of a second.
    /// </summary>
    private static double ResolveStride(UsdPhysicsBakeSpec spec) => spec.SampleStride ?? 1.0;

    private static int CountSamples(double start, double end, double stride)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || end < start || stride <= 0)
        {
            return 0;
        }
        return (int)Math.Floor(((end - start) / stride) + 1e-9) + 1;
    }

    private static double[] BuildTimeCodes(double start, double end, double stride)
    {
        int count = CountSamples(start, end, stride);
        if (count <= 0)
        {
            return [];
        }

        var samples = new double[count];
        for (int index = 0; index < count; ++index)
        {
            // Deriving each time code from the index instead of accumulating keeps the sample grid
            // exact, so two bakes of the same range author identical time codes.
            samples[index] = start + (index * stride);
        }
        return samples;
    }

    private sealed class SingleBatchSource(UsdPhysicsResultBatch batch) : IUsdPhysicsBakeSource
    {
        public ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
            double timeCode, CancellationToken cancellationToken) =>
            ValueTask.FromResult<UsdPhysicsResultBatch?>(batch);
    }
}
