// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk.Metal;

/// <summary>
/// Decides whether a binding layout may be bound through a Metal argument buffer.
/// </summary>
/// <remarks>
/// Separate from <see cref="MetalSilkGraphicsDevice"/>, and deliberately not gated to
/// macOS, because the decision is a pure function of the layout and the checked shader
/// ABI. Keeping it reachable everywhere is what lets the conformance suite compare it
/// against the checked <c>*.metal</c> sources on every platform rather than only on Apple
/// hardware -- which matters more here than usual, because the failure it prevents
/// produces no error at all. Metal does not report a texture argument left unbound, and it
/// does not report a fragment argument buffer nobody reads; the draw simply samples
/// something undefined and still returns an image.
/// </remarks>
internal static class MetalArgumentBufferCompatibility
{
    /// <summary>
    /// Whether every checked Metal program consumes its textures and samplers as direct
    /// entry-point arguments rather than through an argument buffer.
    /// </summary>
    /// <remarks>
    /// True for every program in <c>eng/shaders/checked/*.metal</c> today: each declares
    /// its resources as <c>[[texture(n)]]</c> and <c>[[sampler(n)]]</c> parameters, and
    /// none declares an argument buffer. While that holds, writing one with
    /// <c>SetFragmentBuffer</c> binds a buffer nothing reads and leaves the direct
    /// arguments unset. This is the single switch to flip when a checked program opts in,
    /// and the conformance suite reads the checked sources to prove the switch still
    /// matches them.
    /// </remarks>
    internal static bool CheckedProgramsUseDirectArguments { get; } = true;

    private const string DirectArgumentReason =
        "no checked Metal program declares an argument buffer; every one takes its " +
        "textures and samplers as direct [[texture(n)]] and [[sampler(n)]] arguments, " +
        "so an argument buffer would leave them unbound";

    /// <summary>
    /// Reports why material textures cannot be bound through a descriptor-indexed table
    /// on this backend at all, independently of any one layout.
    /// </summary>
    /// <remarks>
    /// The capability answers "can material textures be bound through a descriptor-indexed
    /// table". Answering it with the hardware tier would advertise a path no draw can
    /// take, because every layout that carries a material texture is declined below.
    /// </remarks>
    internal static bool TryGetCapabilityRejectionReason(out string reason)
    {
        if (CheckedProgramsUseDirectArguments)
        {
            reason = DirectArgumentReason;
            return true;
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>
    /// Reports why <paramref name="layout"/> cannot be bound through an argument buffer.
    /// </summary>
    /// <returns><see langword="true"/> when the layout must be bound directly.</returns>
    /// <remarks>
    /// Two independent reasons, ordered most specific first, and both are kept even though
    /// the second currently subsumes the first. The first is a permanent property of the
    /// sampled density volume; the second is a property of today's checked shaders that a
    /// future program could lift. Collapsing them would quietly re-expose the volume to an
    /// argument buffer the moment the general rule changed.
    /// </remarks>
    internal static bool TryGetRejectionReason(
        SilkBindingLayoutDescriptor layout,
        out string reason)
    {
        IReadOnlyList<SilkBindingSlot> slots = layout.MaterialSlots ?? [];
        bool declaresTextureOrSampler = false;
        foreach (SilkBindingSlot slot in slots)
        {
            if (slot.Kind is SilkBindingKind.SampledTexture &&
                slot.Binding == SilkBindingLayoutDescriptor.VolumeDensityTextureBinding)
            {
                // MTLArgumentDescriptor carries an explicit TextureType, and a descriptor
                // built for Type2D does not describe a texture3d argument. Encoding the
                // density grid as a 2D texture is not a lesser image, it is an undefined
                // read: the shader samples whatever the mismatched descriptor resolves to
                // and still produces a plausible-looking volume.
                reason =
                    "the layout declares the sampled density 3D texture at binding " +
                    $"{SilkBindingLayoutDescriptor.VolumeDensityTextureBinding}, and a " +
                    "Metal argument descriptor cannot describe it as the texture3d the " +
                    "checked mesh.volume.fragment program declares";
                return true;
            }
            declaresTextureOrSampler |=
                slot.Kind is SilkBindingKind.SampledTexture or SilkBindingKind.Sampler;
        }

        if (CheckedProgramsUseDirectArguments && declaresTextureOrSampler)
        {
            reason = DirectArgumentReason;
            return true;
        }

        reason = string.Empty;
        return false;
    }
}
