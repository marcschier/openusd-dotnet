// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>Stable codes emitted when hdSilk degrades rendered material output.</summary>
public static class SilkRenderDiagnosticCodes
{
    /// <summary>A mesh references a material that is absent from retained scene state.</summary>
    public const string MaterialUnresolved = "OPENUSD_SILK_MATERIAL_UNRESOLVED";

    /// <summary>A retained material uses a surface network hdSilk cannot shade.</summary>
    public const string MaterialUnsupported = "OPENUSD_SILK_MATERIAL_UNSUPPORTED";

    /// <summary>
    /// A retained material's only surface terminal is authored in the MDL render
    /// context and this runtime did not distil it, so the material is not shaded.
    /// </summary>
    /// <remarks>
    /// Reported instead of <see cref="MaterialUnsupported"/> so the cause is named:
    /// the optional openusd_mdl adapter is absent -- which is the state of every
    /// base package -- or the MDL module, material, or authored inputs fall outside
    /// the adapter's accepted distillation subset.
    /// </remarks>
    public const string MaterialMdlUnavailable = "OPENUSD_SILK_MATERIAL_MDL_UNAVAILABLE";

    /// <summary>A referenced texture asset could not be found.</summary>
    public const string TextureAssetNotFound = "OPENUSD_SILK_TEXTURE_ASSET_NOT_FOUND";

    /// <summary>A referenced texture asset could not be decoded.</summary>
    public const string TextureDecodeFailed = "OPENUSD_SILK_TEXTURE_DECODE_FAILED";

    /// <summary>An authored texture fallback value was used.</summary>
    public const string TextureFallbackUsed = "OPENUSD_SILK_TEXTURE_FALLBACK_USED";

    /// <summary>
    /// An authored UsdPreviewSurface <c>displacement</c> input moved the geometry
    /// hdSilk drew, at the named emitted vertex density.
    /// </summary>
    /// <remarks>
    /// Informational rather than silent because the density is load bearing and
    /// the wire carries no refinement level: hdSilk publishes the refined cage
    /// when the display style asks for one and the control cage at complexity
    /// Low, so the number of emitted vertices is the only statement of the
    /// tessellation the height field was sampled at.
    /// </remarks>
    public const string DisplacementApplied = "OPENUSD_SILK_DISPLACEMENT_APPLIED";

    /// <summary>
    /// An authored UsdPreviewSurface <c>displacement</c> input was not applied, so
    /// the undisplaced surface was drawn.
    /// </summary>
    /// <remarks>
    /// Raised with the reason from <see cref="SilkDisplacementFallback"/>: a
    /// topology with no surface to displace along, a two-image composite operand,
    /// a UDIM tile set, a texture-coordinate primvar the mesh does not carry, a
    /// non-finite authored amount, or an image that could not be found or
    /// decoded. hdSilk never substitutes a flat surface silently -- an unmoved
    /// prim whose material asks it to move is reported here.
    /// </remarks>
    public const string DisplacementUnsupported = "OPENUSD_SILK_DISPLACEMENT_UNSUPPORTED";

    /// <summary>
    /// An authored displacement exceeded the per-prim vertex budget or the
    /// decoded displacement image byte budget, so the undisplaced surface was
    /// drawn.
    /// </summary>
    /// <remarks>
    /// Both bounds are checked from counts before an amount array or a retained
    /// image is allocated, so a prim outside them never reaches an allocator.
    /// </remarks>
    public const string DisplacementBudgetExceeded = "OPENUSD_SILK_DISPLACEMENT_BUDGET_EXCEEDED";

    /// <summary>
    /// A displaced prim casts into a published raster shadow map whose light-space
    /// projection was derived from hdSilk's undisplaced caster bounds.
    /// </summary>
    /// <remarks>
    /// The shadow depth pass draws the same displaced vertex buffer the colour
    /// pass draws, so the occluder in the map is the displaced surface. The
    /// frustum that map is rendered with, however, arrives on the wire already
    /// fitted to the bounds of the undisplaced geometry, so a displacement large
    /// enough to push a caster outside those bounds is clipped by the light
    /// frustum rather than shadowed. The magnitude is named here so the risk is
    /// never silent.
    /// </remarks>
    public const string DisplacementShadowBoundsUnverified =
        "OPENUSD_SILK_DISPLACEMENT_SHADOW_BOUNDS_UNVERIFIED";

    /// <summary>Additional diagnostics were omitted to keep the snapshot bounded.</summary>
    public const string CapacityExceeded = "OPENUSD_SILK_DIAGNOSTIC_CAPACITY_EXCEEDED";

    /// <summary>
    /// A colour-managed display transform named an OpenColorIO configuration that
    /// could not be found or opened, so untransformed linear colour was written.
    /// </summary>
    /// <remarks>
    /// Named rather than silently substituted, because an identity result is
    /// indistinguishable from a successful transform for a config whose display and
    /// view happen to be close to the working space, and a viewer that quietly showed
    /// the wrong image would be exactly the plausible-but-wrong result this profile
    /// exists to prevent.
    /// </remarks>
    public const string DisplayTransformConfigUnavailable =
        "OPENUSD_SILK_DISPLAY_TRANSFORM_CONFIG_UNAVAILABLE";

    /// <summary>
    /// An OpenColorIO configuration was opened but does not contain the requested
    /// source colour space, display, view, or look, so untransformed linear colour was
    /// written.
    /// </summary>
    public const string DisplayTransformUnsupported =
        "OPENUSD_SILK_DISPLAY_TRANSFORM_UNSUPPORTED";

    /// <summary>
    /// A colour-managed display transform was requested on a graphics device with no
    /// display-transform capability, so untransformed linear colour was written.
    /// </summary>
    public const string DisplayTransformDeviceUnsupported =
        "OPENUSD_SILK_DISPLAY_TRANSFORM_DEVICE_UNSUPPORTED";

    /// <summary>A dome light's environment texture could not be found.</summary>
    public const string EnvironmentAssetNotFound = "OPENUSD_SILK_ENVIRONMENT_ASSET_NOT_FOUND";

    /// <summary>A dome light's environment texture could not be decoded.</summary>
    public const string EnvironmentDecodeFailed = "OPENUSD_SILK_ENVIRONMENT_DECODE_FAILED";

    /// <summary>A dome light's environment texture exceeds the decode budget.</summary>
    public const string EnvironmentBudgetExceeded = "OPENUSD_SILK_ENVIRONMENT_BUDGET_EXCEEDED";

    /// <summary>
    /// A dome light declares an image mapping this renderer does not resolve, so its
    /// untextured emission is used instead of integrating the image as if it were
    /// equirectangular.
    /// </summary>
    public const string EnvironmentMappingUnsupported =
        "OPENUSD_SILK_ENVIRONMENT_MAPPING_UNSUPPORTED";

    /// <summary>
    /// A dome light authors emission behaviour hdSilk did not put on the wire, such as
    /// colour temperature or a non-scene pole axis.
    /// </summary>
    public const string EnvironmentFeatureUnsupported =
        "OPENUSD_SILK_ENVIRONMENT_FEATURE_UNSUPPORTED";

    /// <summary>
    /// A dome light authors a non-zero specular contribution that its mean-radiance
    /// ambient fallback cannot resolve.
    /// </summary>
    /// <remarks>
    /// Raised only for a dome that did not reach the prefiltered environment. The
    /// fallback has already collapsed the sky to one colour, so there is no
    /// directionality left to reflect and reflecting the mean would put every
    /// mirror-like surface at the average colour of the sky. A dome the
    /// prefiltered environment does carry resolves its specular contribution and
    /// is silent.
    /// </remarks>
    public const string EnvironmentSpecularUnsupported =
        "OPENUSD_SILK_ENVIRONMENT_SPECULAR_UNSUPPORTED";

    /// <summary>
    /// A textured dome light is beyond the number this renderer composes into one
    /// prefiltered environment, so it falls back to its mean-radiance ambient term.
    /// </summary>
    /// <remarks>
    /// The bake is a sum in world space, so composing several domes is exact
    /// rather than approximate; the bound exists because each dome costs a full
    /// traversal of its own decoded image. Named against the dome that did not fit
    /// rather than reported as a scene-wide count, because which dome lost its
    /// directional response is the part a viewer needs.
    /// </remarks>
    public const string EnvironmentLightingLimitExceeded =
        "OPENUSD_SILK_ENVIRONMENT_LIGHTING_LIMIT_EXCEEDED";

    /// <summary>
    /// The prefiltered environment could not be built or allocated, so every
    /// textured dome light falls back to its mean-radiance ambient term.
    /// </summary>
    /// <remarks>
    /// Raised when the composed environment exceeds its prefilter or cache byte
    /// budget, or when the device refused the two environment textures -- a device
    /// loss mid-frame, or one that cannot allocate a filtered half-float image.
    /// Scene-wide rather than per-dome because the failure is a property of the
    /// composed environment rather than of any one dome in it.
    /// </remarks>
    public const string EnvironmentLightingUnavailable =
        "OPENUSD_SILK_ENVIRONMENT_LIGHTING_UNAVAILABLE";


    /// <summary>
    /// The light-link table exceeded the page budget, so the prims that did not fit
    /// stay linked to every light.
    /// </summary>
    public const string LightLinkTruncated = "OPENUSD_SILK_LIGHT_LINK_TRUNCATED";

    /// <summary>
    /// The scene authors more dome lights than the bounded dome table admits, so
    /// no dome is individually addressable and every dome lights every prim.
    /// </summary>
    /// <remarks>
    /// All-or-nothing by design. Publishing bits for the domes that fit would
    /// make some of a scene's skies maskable and the rest not, and no sum of the
    /// two halves is the authored image; withholding the table degrades exactly to
    /// the behaviour that preceded dome linking and names the loss instead.
    /// </remarks>
    public const string LightLinkDomeBudget = "OPENUSD_SILK_LIGHT_LINK_DOME_BUDGET";

    /// <summary>
    /// The per-dome prefiltered environment groups a scene's dome linking needs
    /// do not fit the prefiltered byte budget, so every prim receives the composed
    /// sky of every dome.
    /// </summary>
    /// <remarks>
    /// The exact subset that survives, rather than a refusal: the scene keeps its
    /// directional response and loses only the per-dome selection of it, and each
    /// dome's ambient contribution is still masked. Falling all the way back to
    /// the mean-radiance term would have cost the scene its sky as well as its
    /// linking.
    /// </remarks>
    public const string EnvironmentDomeLinkUnavailable =
        "OPENUSD_SILK_ENVIRONMENT_DOME_LINK_UNAVAILABLE";

    /// <summary>
    /// A prim whose UsdLux linking excludes a light binds a material drawn through a
    /// runtime-generated MaterialX fragment. Those fragments carry MaterialX's own
    /// lighting model rather than the checked mesh permutation's frame light loop, so
    /// the per-draw light mask never reaches them and the prim is lit by every light.
    /// </summary>
    public const string LightLinkGeneratedShaderUnsupported =
        "OPENUSD_SILK_LIGHT_LINK_GENERATED_SHADER_UNSUPPORTED";

    /// <summary>
    /// A direct light authors <c>inputs:shadow:enable</c> but got no shadow map.
    /// </summary>
    /// <remarks>
    /// Raised with the reason the map was not produced: a light type with no exact
    /// light-space projection, published geometry with no world extent to derive one
    /// from, a table over the page map budget, or a device that cannot record a
    /// depth-only pass. A light that got its map is silent, because the image shows the
    /// occlusion.
    /// </remarks>
    public const string ShadowUnsupported = "OPENUSD_SILK_SHADOW_UNSUPPORTED";

    /// <summary>
    /// A prim that would cast a shadow was excluded from every shadow map because its
    /// material is opacity-masked.
    /// </summary>
    /// <remarks>
    /// The depth-only shadow caster program binds no material and cannot discard a
    /// fragment, so an alpha-tested cutout or a blended surface would cast the solid
    /// shadow of its geometry rather than of its visible coverage -- a leaf card would
    /// shadow as an opaque quad. That is the plausible-but-wrong image this profile
    /// exists to prevent, so the caster is dropped from the map and named here instead.
    /// The prim is still lit and still receives shadows normally.
    /// </remarks>
    public const string ShadowCasterUnsupported = "OPENUSD_SILK_SHADOW_CASTER_UNSUPPORTED";

    /// <summary>
    /// A configured decoded-CPU or estimated-GPU texture residency budget was violated. Reported
    /// either when a single texture cache entry's own size alone exceeds a budget (the entry is
    /// still evicted rather than retained past the trim point) or when the current frame's pinned
    /// working set alone exceeds a budget with no stale entry left to evict (nothing is evicted,
    /// since every remaining entry is still referenced by the frame(s) recorded since the
    /// previous trim).
    /// </summary>
    public const string TextureBudgetExceeded = "OPENUSD_SILK_TEXTURE_BUDGET_EXCEEDED";
}
