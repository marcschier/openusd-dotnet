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
    private const int RequiredQuietBlocks = 2;

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
}
