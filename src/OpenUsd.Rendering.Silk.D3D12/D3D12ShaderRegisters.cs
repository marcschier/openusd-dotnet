// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk.D3D12;

/// <summary>
/// Translates renderer-neutral binding numbers into the HLSL registers the checked
/// shaders declare.
/// </summary>
/// <remarks>
/// Separate from <see cref="D3D12SilkGraphicsDevice"/>, and deliberately not gated to
/// Windows, because the translation is a pure table with no Direct3D dependency: keeping
/// it reachable everywhere is what lets the conformance suite compare it against the
/// checked <c>*.reflection.json</c> on every platform, rather than only on a machine
/// with a D3D12 device.
/// </remarks>
internal static class D3D12ShaderRegisters
{
    /// <summary>
    /// Maps an abstract binding onto its HLSL register.
    /// </summary>
    /// <remarks>
    /// Explicit for every binding the checked mesh family declares. The abstract
    /// bindings and the HLSL registers were allocated independently --
    /// <c>eng/shaders/shader-manifest.json</c> is the source of truth for both -- so an
    /// arithmetic rule only happened to agree for the first five texture slots and
    /// silently produced <c>t13</c> for the metallic texture at abstract binding 15.
    /// Layouts outside the checked family (the conformance harness widens a layout with
    /// slots nothing samples) keep the historical identity fallback, which cannot
    /// mis-bind anything the checked shaders read.
    /// </remarks>
    internal static uint Map(SilkBindingSlot slot)
    {
        if (slot.Kind == SilkBindingKind.Sampler)
        {
            return slot.Binding switch
            {
                SilkBindingLayoutDescriptor.BaseColorSamplerBinding => 0,
                SilkBindingLayoutDescriptor.NormalSamplerBinding => 1,
                SilkBindingLayoutDescriptor.RoughnessMetallicSamplerBinding => 2,
                SilkBindingLayoutDescriptor.EmissiveSamplerBinding => 3,
                SilkBindingLayoutDescriptor.VolumeSamplerBinding => 4,
                SilkBindingLayoutDescriptor.MetallicSamplerBinding => 5,
                SilkBindingLayoutDescriptor.OpacitySamplerBinding => 6,
                SilkBindingLayoutDescriptor.OcclusionSamplerBinding => 7,
                SilkBindingLayoutDescriptor.SpecularColorSamplerBinding => 8,
                SilkBindingLayoutDescriptor.ClearcoatSamplerBinding => 9,
                SilkBindingLayoutDescriptor.ClearcoatRoughnessSamplerBinding => 10,
                _ => slot.Binding
            };
        }
        if (slot.Kind == SilkBindingKind.SampledTexture)
        {
            return slot.Binding switch
            {
                SilkBindingLayoutDescriptor.BaseColorTextureBinding => 0,
                SilkBindingLayoutDescriptor.NormalTextureBinding => 1,
                SilkBindingLayoutDescriptor.RoughnessMetallicTextureBinding => 2,
                SilkBindingLayoutDescriptor.EmissiveTextureBinding => 3,
                SilkBindingLayoutDescriptor.MetallicTextureBinding => 4,
                SilkBindingLayoutDescriptor.OpacityTextureBinding => 5,
                SilkBindingLayoutDescriptor.OcclusionTextureBinding => 10,
                SilkBindingLayoutDescriptor.SpecularColorTextureBinding => 11,
                SilkBindingLayoutDescriptor.ClearcoatTextureBinding => 12,
                SilkBindingLayoutDescriptor.ClearcoatRoughnessTextureBinding => 13,
                SilkBindingLayoutDescriptor.VolumeDensityTextureBinding => 9,
                _ => slot.Binding >= 2 ? slot.Binding - 2 : slot.Binding
            };
        }
        return slot.Binding;
    }
}
