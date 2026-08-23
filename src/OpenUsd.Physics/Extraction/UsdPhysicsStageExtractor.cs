// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>
/// Produces one immutable physics extraction page from a stage owned by a scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Extraction is deliberately a single call. The scheduler hands the stage to exactly one
/// native entry point, that entry point traverses the composed stage once, and it returns a
/// pointer-free page. No per prim or per property call ever crosses the boundary, and nothing
/// the caller receives refers back to the stage, so a physics worker can hold a page forever
/// without holding a USD handle.
/// </para>
/// <para>
/// Cancellation is honoured at the scheduler boundary. Once the native traversal starts it runs
/// to completion, because a partially traversed stage cannot produce a deterministic
/// fingerprint.
/// </para>
/// </remarks>
public static class UsdPhysicsStageExtractor
{
    /// <summary>Extracts one physics page from the scheduled stage.</summary>
    /// <param name="scheduler">The scheduler that owns the stage.</param>
    /// <param name="cancellationToken">Cancels the request before the stage is touched.</param>
    /// <returns>The immutable extraction page.</returns>
    public static ValueTask<UsdPhysicsExtractionPage> ExtractAsync(
        UsdStageScheduler scheduler,
        CancellationToken cancellationToken = default) =>
        ExtractAsync(scheduler, UsdPhysicsExtractionOptions.Default, cancellationToken);

    /// <summary>Extracts one physics page from the scheduled stage.</summary>
    /// <param name="scheduler">The scheduler that owns the stage.</param>
    /// <param name="options">The extraction bounds and switches.</param>
    /// <param name="cancellationToken">Cancels the request before the stage is touched.</param>
    /// <returns>The immutable extraction page.</returns>
    public static ValueTask<UsdPhysicsExtractionPage> ExtractAsync(
        UsdStageScheduler scheduler,
        UsdPhysicsExtractionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        PhysicsExtractNativeOptions native = options.ToNative();
        return scheduler.InvokeAsync(
            stage =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Extract(stage, native);
            },
            cancellationToken);
    }

    /// <summary>Extracts one physics page from a stage the caller already owns.</summary>
    /// <param name="stage">The stage to traverse.</param>
    /// <param name="options">The extraction bounds and switches.</param>
    /// <returns>The immutable extraction page.</returns>
    /// <remarks>
    /// This overload exists for callers that already own the stage owner thread, such as a
    /// scheduler callback or a single threaded test. Prefer the scheduler overloads elsewhere.
    /// </remarks>
    public static UsdPhysicsExtractionPage Extract(
        UsdStage stage, UsdPhysicsExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(options);
        return Extract(stage, options.ToNative());
    }

    /// <summary>Gets how many native stage traversals this process has performed.</summary>
    /// <returns>The monotonically increasing traversal count.</returns>
    /// <remarks>
    /// Tests read this before and after an extraction to prove that one extraction of an
    /// arbitrarily large stage advances the counter by exactly one.
    /// </remarks>
    public static ulong GetTraversalCount() => PhysicsExtractNativeMethods.GetTraversalCount();

    /// <summary>Gets how many prims the last completed native traversal visited.</summary>
    /// <returns>The visited prim count of the last traversal.</returns>
    public static ulong GetVisitedPrimCount() =>
        PhysicsExtractNativeMethods.GetVisitedPrimCount();

    private static UsdPhysicsExtractionPage Extract(
        UsdStage stage, PhysicsExtractNativeOptions options) =>
        UsdPhysicsExtractionPage.Adopt(
            PhysicsExtractNativeMethods.Extract(stage.Native, options));
}
