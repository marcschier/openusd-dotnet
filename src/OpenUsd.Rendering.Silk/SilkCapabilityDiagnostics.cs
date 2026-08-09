// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal static class SilkCapabilityDiagnostics
{
    internal static string DescriptorIndexedTextureTablesSetupFailed(
        string backend,
        Exception exception) =>
        $"{backend} descriptor-indexed texture tables unavailable: setup failed " +
        $"({exception.GetType().Name}: {exception.Message}).";
}
