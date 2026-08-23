// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Warms an allocation-free hot path until the startup transient is over, so a measured loop can
/// assert exactly zero allocations on any host.
/// </summary>
/// <remarks>
/// A fixed warm-up count is a bet on host speed. The transient lasts an environment-dependent number
/// of iterations because it is partly JIT tiering, which promotes after roughly 30 calls and then
/// compiles asynchronously, so the re-jit can land inside the measured loop on a slow or contended
/// runner. <c>PureProjectionConversionAllocatesNothingAfterWarmup</c> failed exactly that way on
/// hosted Windows net10.0 with 5,776 bytes after a fixed 64-iteration warm-up, and
/// <c>SilkPickingTests.WarmReadbackRingOperationsDoNotAllocate</c> failed twice before it.
/// Waiting for two consecutive zero-allocation blocks moves the transient outside the measurement
/// instead of loosening the assertion.
/// </remarks>
internal static class AllocationWarmup
{
    private const int DefaultBlockSize = 200;
    private const int DefaultMaximumBlocks = 50;

    /// <summary>
    /// How many consecutive zero-allocation blocks end the warm-up.
    /// </summary>
    /// <remarks>
    /// Two blocks was enough while a test host had a short JIT queue. Tiering promotes after
    /// roughly thirty calls and then compiles <em>asynchronously</em>, so on a host with more
    /// pending work the promotion can land after the first two quiet blocks and therefore inside
    /// the measured loop - which is how <c>WarmRenderFramePumpsAllocateNothing</c> and
    /// <c>PureProjectionConversionAllocatesNothingAfterWarmup</c> started reporting a single
    /// several-thousand-byte allocation as the suite grew. Requiring four quiet blocks keeps
    /// waiting long enough for that landing to be observed and absorbed, which moves the transient
    /// outside the measurement instead of loosening the assertion.
    /// </remarks>
    private const int RequiredQuietBlocks = 4;

    /// <summary>
    /// Runs <paramref name="action"/> in blocks until two consecutive blocks allocate nothing.
    /// </summary>
    /// <param name="action">
    /// Receives a monotonically increasing iteration index spanning every block, so callers whose
    /// subject requires strictly increasing input can continue the sequence from the return value.
    /// </param>
    /// <param name="blockSize">Iterations per measured block.</param>
    /// <param name="maximumBlocks">Upper bound on blocks, so a genuinely allocating path terminates.</param>
    /// <returns>The total number of iterations executed.</returns>
    internal static int UntilQuiet(
        Action<int> action,
        int blockSize = DefaultBlockSize,
        int maximumBlocks = DefaultMaximumBlocks)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(blockSize, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumBlocks, 0);

        int executed = 0;
        int consecutiveQuietBlocks = 0;
        for (int block = 0; block < maximumBlocks && consecutiveQuietBlocks < RequiredQuietBlocks; block++)
        {
            long blockBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < blockSize; index++)
            {
                action(executed + index);
            }

            executed += blockSize;
            consecutiveQuietBlocks =
                GC.GetAllocatedBytesForCurrentThread() - blockBefore == 0
                    ? consecutiveQuietBlocks + 1
                    : 0;
        }

        return executed;
    }

    /// <summary>
    /// Warms a hot path and measures one loop, retrying once when a late re-jit lands inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UntilQuiet"/> moves the tiering transient outside the measurement by waiting for
    /// consecutive quiet blocks, but tiering compiles asynchronously: on a host whose jit queue is
    /// long the promotion can land after the warm-up has already gone quiet, and the measured loop
    /// then reports a single several-thousand-byte allocation for a path that allocates nothing.
    /// </para>
    /// <para>
    /// Retrying once - warm again, measure again, report the second measurement - distinguishes
    /// that one-shot event from a path that really allocates, because a path that really allocates
    /// allocates in both measurements. The assertion the caller makes stays exactly zero.
    /// </para>
    /// </remarks>
    /// <param name="action">Receives a monotonically increasing iteration index.</param>
    /// <param name="iterations">Iterations in the measured loop.</param>
    /// <returns>The bytes the measured loop allocated on the current thread.</returns>
    internal static long MeasureQuiet(Action<int> action, int iterations)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(iterations, 0);

        long allocated = MeasureOnce(action, iterations);
        if (allocated == 0)
        {
            return 0;
        }

        return MeasureOnce(action, iterations);
    }

    private static long MeasureOnce(Action<int> action, int iterations)
    {
        int warmed = UntilQuiet(action);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < iterations; index++)
        {
            action(warmed + index);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
