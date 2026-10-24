// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkMeshRenderer
{
    private sealed class FrameCommands(
        ISilkGraphicsDevice device, ISilkGraphicsCommandList first, SilkSceneGpuResources? streamedResources)
        : IDisposable
    {
        private ISilkGraphicsCommandList? _current = first;

        internal ISilkGraphicsCommandList Advance(SilkMaterialData? previous, SilkMaterialData? next)
        {
            ReleaseCurrent(previous, next);
            _current = device.CreateCommandList();
            return _current;
        }

        internal void ReleaseCurrent(SilkMaterialData? previous = null, SilkMaterialData? next = null)
        {
            ISilkGraphicsCommandList? current = Interlocked.Exchange(ref _current, null);
            if (current is null)
            {
                streamedResources?.ReleaseStreamedTextureWorkingSet();
                return;
            }
            try
            {
                current.Dispose();
            }
            finally
            {
                streamedResources?.ReleaseStreamedTextureWorkingSet(previous, next);
            }
        }

        public void Dispose() => ReleaseCurrent();
    }

    private static void BeginMaterialRendering(
        ISilkGraphicsCommandList commands, ISilkGraphicsTexture color, ISilkGraphicsTexture depth)
    {
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        commands.SetViewport(new SilkViewport(0, 0, color.Width, color.Height, 0, 1));
        commands.SetScissor(new SilkScissor(0, 0, color.Width, color.Height));
    }
}
