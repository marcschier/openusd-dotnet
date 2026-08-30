// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk.Metal;

/// <summary>
/// Translates renderer-neutral binding numbers into the Metal argument indices the
/// checked shaders use.
/// </summary>
/// <remarks>
/// Separate from <see cref="MetalSilkGraphicsDevice"/>, and deliberately not gated to
/// macOS, because the translation is a pure table with no Metal dependency: keeping it
/// reachable everywhere is what lets the conformance suite compare it against the
/// checked <c>*.reflection.json</c> on every platform, rather than only on Apple
/// hardware.
/// </remarks>
internal static class MetalShaderResourceIndices
{
    /// <summary>
    /// Maps an abstract binding onto its Metal argument index.
    /// </summary>
    /// <remarks>
    /// Explicit for every binding the checked mesh family declares. Slang assigns Metal
    /// indices from the HLSL registers in <c>eng/shaders/shader-manifest.json</c>, which
    /// were allocated independently of the abstract bindings, so the old
    /// <c>binding - 2</c> rule only happened to agree for the first four texture slots
    /// and produced index 13 for the metallic texture at abstract binding 15. Layouts
    /// outside the checked family keep the historical identity fallback.
    /// </remarks>
    internal static uint Map(SilkBindingKind kind, uint binding) =>
        kind switch
        {
            SilkBindingKind.SampledTexture => binding switch
            {
                SilkBindingLayoutDescriptor.BaseColorTextureBinding => 0,
                SilkBindingLayoutDescriptor.NormalTextureBinding => 1,
                SilkBindingLayoutDescriptor.RoughnessMetallicTextureBinding => 2,
                SilkBindingLayoutDescriptor.EmissiveTextureBinding => 3,
                SilkBindingLayoutDescriptor.MetallicTextureBinding => 4,
                _ => binding
            },
            SilkBindingKind.Sampler => binding switch
            {
                SilkBindingLayoutDescriptor.BaseColorSamplerBinding => 0,
                SilkBindingLayoutDescriptor.NormalSamplerBinding => 1,
                SilkBindingLayoutDescriptor.RoughnessMetallicSamplerBinding => 2,
                SilkBindingLayoutDescriptor.EmissiveSamplerBinding => 3,
                SilkBindingLayoutDescriptor.VolumeSamplerBinding => 4,
                SilkBindingLayoutDescriptor.MetallicSamplerBinding => 5,
                _ => binding
            },
            _ => binding
        };
}
