// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal static class SilkShadowTransmissionWorkspace
{
    internal const ulong DefaultMaximumBytes = 2ul * 1024 * 1024 * 1024;

    internal static ulong AtlasBytes(uint edge, bool transmission) =>
        checked((ulong)edge * edge * (transmission ? 76ul : 4ul));

    internal static ulong PeakBytes(ulong retainedBytes, uint edge, bool transmission, bool allocateCandidate)
    {
        checked
        {
            ulong candidate = allocateCandidate ? AtlasBytes(edge, transmission) : 0;
            ulong probe = transmission ? (ulong)edge * edge * 4 : 0;
            // D3D12 readback rows are 256-byte aligned; this also covers the tighter Vulkan payload.
            ulong staging = transmission ? ((((ulong)edge * 4 + 255) / 256) * 256) * edge : 0;
            return retainedBytes + candidate + probe + staging;
        }
    }

    internal static void Validate(
        ISilkGraphicsDevice device, ulong retainedBytes, uint edge, bool transmission, bool allocateCandidate)
    {
        if (device is ISilkTextureAdmissionDevice { GpuTextureBudget: not null } &&
            device is ISilkStagingAdmissionDevice { GpuStagingBudget: not null } &&
            device is ISilkCpuTextureAdmissionDevice { CpuTextureBudget: not null })
        {
            return;
        }
        ulong peak = PeakBytes(retainedBytes, edge, transmission, allocateCandidate);
        if (peak > DefaultMaximumBytes)
        {
            throw new InvalidOperationException(
                $"ShadowTransmissionWorkspaceLimit: the candidate requires {peak} logical payload bytes " +
                $"including retained atlases and peak probe/readback payload; the per-renderer limit is " +
                $"{DefaultMaximumBytes} bytes when any CPU-texture, GPU-texture or GPU-staging budget is absent. " +
                "Reduce authored shadow resolution or map count, retire unused shadows, or configure all three " +
                "budgets before device allocation. Configured budgets remain in force. The previously published " +
                "atlas is unchanged. This limit does not measure whole-process memory, driver overhead or " +
                "device memory.");
        }
    }
}
