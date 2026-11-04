// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk.Metal;

internal static class MetalGraphicsShaderLibrary
{
    internal static T Load<T>(
        SilkShaderModuleDescriptor descriptor,
        Func<T> loadPinned,
        Func<SilkShaderModuleDescriptor, T> compileSource) =>
        descriptor.EntryPoint == "main"
            ? compileSource(descriptor)
            : loadPinned();
}
