// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The single number a renderer-neutral consumer keys retained GPU state on.
/// </summary>
/// <remarks>
/// <para>
/// A backend publishes more than one generation, because more than one thing
/// can invalidate what the device holds. The picking and selection-outline
/// generations each advance when their own resources are dropped, which
/// includes device loss but is not limited to it, and neither advances for a
/// loss detected on a submission belonging to some other subsystem. The
/// device-loss generation advances on every detected loss and on nothing else.
/// </para>
/// <para>
/// A consumer such as the deformation pass belongs to none of those subsystems
/// and must react to all of them: it has to rebuild what the device wrote after
/// any reset, and it has to tell a lost device apart from a refused allocation
/// when a call of its own fails. Mixing the published generations is what makes
/// one number answer both questions -- any advance moves it, and no advance
/// means nothing was reset.
/// </para>
/// </remarks>
internal static class SilkDeviceGeneration
{
    /// <summary>Reads the combined generation of every reset a device reports.</summary>
    internal static ulong Read(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ulong loss = device is ISilkDeviceLossGraphicsDevice lossDevice
            ? lossDevice.DeviceLossGeneration
            : 0;
        ulong outline = device is ISilkSelectionOutlineGraphicsDevice outlineDevice
            ? outlineDevice.SelectionOutlineDeviceGeneration
            : 0;

        // A mix rather than a sum: two generations that moved in opposite
        // directions by the same amount would cancel in a sum, and the whole
        // point is that any movement is observable. The mix is a plain 64-bit
        // FNV-1a so it is deterministic across processes, which matters because
        // this value is compared against one a previous frame stored.
        ulong mixed = 14695981039346656037UL;
        Mix(ref mixed, loss);
        Mix(ref mixed, outline);
        return mixed;
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash ^= (value >> shift) & 0xFF;
            hash *= 1099511628211UL;
        }
    }
}
