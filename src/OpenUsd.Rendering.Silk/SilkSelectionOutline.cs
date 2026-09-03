// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text.Json;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// How a selected surface's outline treats fragments hidden behind occluders.
/// </summary>
public enum SilkSelectionOutlineMode
{
    /// <summary>
    /// Only unoccluded selected fragments contribute, so a selected prim behind
    /// a wall shows no outline at all.
    /// </summary>
    VisibleOnly,

    /// <summary>
    /// Occluded selected fragments contribute too, in a distinct style, so a
    /// selected prim behind a wall stays locatable without losing the ordinary
    /// depth-tested outline where it is visible.
    /// </summary>
    XRay
}

/// <summary>
/// Shared selection-outline policy for the visible-only and x-ray modes.
/// </summary>
public sealed record SilkSelectionOutlineSettings
{
    /// <summary>Gets the narrowest supported physical-pixel outline.</summary>
    public const float MinimumWidth = 1;

    /// <summary>Gets the widest supported physical-pixel outline.</summary>
    public const float MaximumWidth = 16;

    /// <summary>
    /// Gets the default occluded style: a desaturated cyan drawn at reduced
    /// opacity.
    /// </summary>
    /// <remarks>
    /// The occluded style must be distinguishable from the visible style by
    /// something other than brightness, because a viewer who cannot separate the
    /// two hues still has to tell "this part of the selection is in front of the
    /// wall" from "this part is behind it". Cyan against the visible orange is a
    /// hue pair that survives the common forms of colour vision deficiency, and
    /// the lower alpha keeps the occluded outline from competing with the
    /// visible one where both appear in the same image.
    /// </remarks>
    public static SilkColor DefaultOccludedColor { get; } =
        new(0.25f, 0.8f, 1, 0.55f);

    /// <summary>Gets the shared orange, two-physical-pixel, visible-only policy.</summary>
    public static SilkSelectionOutlineSettings Default { get; } = new(
        enabled: true,
        new SilkColor(1, 0.55f, 0, 0.9f),
        width: 2,
        visibleOnly: true);

    /// <summary>Initializes immutable selection-outline settings.</summary>
    public SilkSelectionOutlineSettings(
        bool enabled,
        SilkColor color,
        float width,
        bool visibleOnly)
        : this(
            enabled,
            color,
            width,
            visibleOnly
                ? SilkSelectionOutlineMode.VisibleOnly
                : SilkSelectionOutlineMode.XRay,
            DefaultOccludedColor)
    {
    }

    /// <summary>Initializes immutable selection-outline settings with an explicit mode.</summary>
    public SilkSelectionOutlineSettings(
        bool enabled,
        SilkColor color,
        float width,
        SilkSelectionOutlineMode mode,
        SilkColor occludedColor)
    {
        color.Validate();
        occludedColor.Validate();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (!float.IsFinite(width) ||
            width < MinimumWidth ||
            width > MaximumWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Selection outline width must be between {MinimumWidth} and " +
                $"{MaximumWidth} physical pixels.");
        }

        Enabled = enabled;
        Color = color;
        Width = width;
        Mode = mode;
        OccludedColor = occludedColor;
    }

    /// <summary>Gets whether selection outlining is enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the straight-alpha outline color.</summary>
    public SilkColor Color { get; }

    /// <summary>Gets the edge-kernel radius in physical pixels.</summary>
    public float Width { get; }

    /// <summary>Gets how occluded selected fragments are treated.</summary>
    public SilkSelectionOutlineMode Mode { get; }

    /// <summary>
    /// Gets the straight-alpha color the occluded outline is composited with in
    /// <see cref="SilkSelectionOutlineMode.XRay"/>.
    /// </summary>
    /// <remarks>
    /// It is ignored in <see cref="SilkSelectionOutlineMode.VisibleOnly"/>,
    /// where no occluded outline is composited at all.
    /// </remarks>
    public SilkColor OccludedColor { get; }

    /// <summary>
    /// Gets whether only unoccluded selected fragments may contribute.
    /// </summary>
    /// <remarks>
    /// A value of <see langword="false"/> requests x-ray selection, which draws
    /// the occluded part of the outline in <see cref="OccludedColor"/> under the
    /// ordinary depth-tested outline rather than replacing it.
    /// </remarks>
    public bool VisibleOnly => Mode == SilkSelectionOutlineMode.VisibleOnly;
}

/// <summary>Selection-outline features exposed by one Silk device.</summary>
public readonly record struct SilkSelectionOutlineCapabilities(
    bool SupportsVisibleOnly,
    bool SupportsXRay)
{
    /// <summary>Gets an unsupported capability.</summary>
    public static SilkSelectionOutlineCapabilities Unsupported => new(
        SupportsVisibleOnly: false,
        SupportsXRay: false);

    /// <summary>Gets the initial single-sample visible-only capability.</summary>
    public static SilkSelectionOutlineCapabilities VisibleOnly => new(
        SupportsVisibleOnly: true,
        SupportsXRay: false);

    /// <summary>
    /// Gets the single-sample capability that also composites occluded outlines.
    /// </summary>
    /// <remarks>
    /// X-ray needs no device feature the visible-only composite does not already
    /// have: it is the same checked mask and outline pipelines, run a second
    /// time with a depth tolerance that admits occluded neighbours and a
    /// distinct color. A backend reports it only once it actually records that
    /// second composite.
    /// </remarks>
    public static SilkSelectionOutlineCapabilities Full => new(
        SupportsVisibleOnly: true,
        SupportsXRay: true);

    /// <summary>Validates a coherent capability set.</summary>
    public void Validate()
    {
        if (SupportsXRay && !SupportsVisibleOnly)
        {
            throw new ArgumentException(
                "X-ray selection support requires visible-only selection support.");
        }
    }
}

/// <summary>Latest reason a Silk selection outline did or did not render.</summary>
public enum SilkSelectionOutlineStatus
{
    /// <summary>Selection changed and will be resolved by the next visible render.</summary>
    Pending,

    /// <summary>No selection items are retained.</summary>
    EmptySelection,

    /// <summary>Outlining is disabled by policy.</summary>
    Disabled,

    /// <summary>The graphics device has no selection-outline capability.</summary>
    UnsupportedDevice,

    /// <summary>The requested x-ray policy is unsupported.</summary>
    XRayUnsupported,

    /// <summary>The visible depth target cannot be sampled.</summary>
    DepthSamplingUnsupported,

    /// <summary>No retained mesh matches the selected authoritative paths.</summary>
    NoMatchingMeshes,

    /// <summary>A visible-only mask and edge composite were recorded.</summary>
    Rendered
}

/// <summary>Cumulative selection-outline state and resource evidence.</summary>
public readonly record struct SilkSelectionOutlineDiagnostics(
    SilkSelectionOutlineStatus Status,
    ulong SelectionRevision,
    int SelectionItemCount,
    int ResolvedMeshCount,
    int MissingPathCount,
    ulong MaskPasses,
    ulong OutlinePasses,
    ulong SelectedDraws,
    ulong PipelineCreations,
    ulong TargetCreations,
    ulong BindingCreations,
    ulong ParameterUploads,
    ulong DeviceInvalidations,
    ulong UnsupportedXRayRequests);

/// <summary>Writes the checked 48-byte selection-outline constant buffer.</summary>
public static class SilkSelectionOutlineUniformWriter
{
    /// <summary>Gets the exact checked constant-buffer byte size.</summary>
    public const int ByteSize = 48;

    /// <summary>
    /// Gets the normalized-depth tolerance used to suppress outlines over nearer
    /// occluders.
    /// </summary>
    public const float DepthEpsilon = 0.00001f;

    /// <summary>
    /// Writes colour, inverse viewport, width, depth tolerance, and the occluded
    /// x-ray colour for the one composite that draws both silhouettes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both colours travel in one buffer because both silhouettes are composited
    /// in one pass. Compositing them one after the other blended the two styles
    /// wherever they overlapped, so a visible edge came out as a mixture rather
    /// than as exactly the colour the visible-only mode draws.
    /// </para>
    /// <para>
    /// In <see cref="SilkSelectionOutlineMode.VisibleOnly"/> the occluded colour
    /// is written with zero alpha. Straight-alpha-over blending then leaves the
    /// target untouched wherever the shader takes its occluded branch, so the
    /// visible-only image is exactly what it was before the branch existed.
    /// </para>
    /// </remarks>
    public static void Write(
        SilkSelectionOutlineSettings settings,
        uint width,
        uint height,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                $"SelectionOutlineParameters requires exactly {ByteSize} bytes.",
                nameof(destination));
        }

        SilkColor color = settings.Color;
        WriteSingle(destination, 0, color.Red);
        WriteSingle(destination, 4, color.Green);
        WriteSingle(destination, 8, color.Blue);
        WriteSingle(destination, 12, color.Alpha);
        WriteSingle(destination, 16, 1f / width);
        WriteSingle(destination, 20, 1f / height);
        WriteSingle(destination, 24, settings.Width);
        WriteSingle(destination, 28, DepthEpsilon);

        SilkColor occluded = settings.OccludedColor;
        bool visibleOnly = settings.Mode == SilkSelectionOutlineMode.VisibleOnly;
        WriteSingle(destination, 32, occluded.Red);
        WriteSingle(destination, 36, occluded.Green);
        WriteSingle(destination, 40, occluded.Blue);
        WriteSingle(destination, 44, visibleOnly ? 0f : occluded.Alpha);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));
}

/// <summary>One reflected D3D and Vulkan shader-resource binding.</summary>
public readonly record struct SilkShaderResourceBindingReflection(
    string D3DRegisterClass,
    uint D3DRegister,
    uint D3DSpace,
    uint VulkanSet,
    uint VulkanBinding);

/// <summary>Validated selection-outline shader ABI.</summary>
public readonly record struct SilkSelectionOutlineReflection(
    SilkShaderResourceBindingReflection MaskTexture,
    SilkShaderResourceBindingReflection VisibleDepthTexture,
    SilkShaderResourceBindingReflection Sampler,
    SilkShaderResourceBindingReflection Parameters,
    uint ColorOffset,
    uint ColorByteSize,
    uint InverseViewportOffset,
    uint InverseViewportByteSize,
    uint WidthOffset,
    uint WidthByteSize,
    uint DepthEpsilonOffset,
    uint DepthEpsilonByteSize,
    uint OccludedColorOffset,
    uint OccludedColorByteSize,
    uint ParameterByteSize,
    bool UsesVertexId);

/// <summary>Checked selection-mask SceneParameters binding.</summary>
public readonly record struct SilkSelectionMaskBindingLayoutDescriptor(
    uint SceneSet,
    uint SceneBinding,
    uint SceneUniformByteSize,
    SilkShaderStageVisibility Visibility)
{
    /// <summary>Gets the checked vertex-only SceneParameters layout.</summary>
    public static SilkSelectionMaskBindingLayoutDescriptor Checked => new(
        0,
        0,
        SilkCheckedShaderAssets.SelectionMaskSceneParameters.ByteSize,
        SilkShaderStageVisibility.Vertex);

    /// <summary>Validates the checked mask binding layout.</summary>
    public void Validate()
    {
        if (SceneSet != 0 ||
            SceneBinding != 0 ||
            SceneUniformByteSize != SilkSceneUniformWriter.ByteSize ||
            Visibility != SilkShaderStageVisibility.Vertex)
        {
            throw new ArgumentException(
                "Selection-mask SceneParameters must use vertex-visible set 0, " +
                "binding 0, and 80 bytes.");
        }
    }
}

/// <summary>Checked selection-outline sampled-resource layout.</summary>
public readonly record struct SilkSelectionOutlineBindingLayoutDescriptor(
    uint Set,
    uint MaskTextureBinding,
    uint VisibleDepthTextureBinding,
    uint SamplerBinding,
    uint ParametersBinding,
    uint ParameterByteSize)
{
    /// <summary>
    /// Gets t0/t1/s0/b0 for D3D and set-zero bindings 0/1/2/3 for Vulkan.
    /// </summary>
    public static SilkSelectionOutlineBindingLayoutDescriptor Checked
    {
        get
        {
            SilkSelectionOutlineReflection reflection =
                SilkCheckedShaderAssets.SelectionOutline;
            return new SilkSelectionOutlineBindingLayoutDescriptor(
                reflection.MaskTexture.VulkanSet,
                reflection.MaskTexture.VulkanBinding,
                reflection.VisibleDepthTexture.VulkanBinding,
                reflection.Sampler.VulkanBinding,
                reflection.Parameters.VulkanBinding,
                reflection.ParameterByteSize);
        }
    }

    /// <summary>Validates all checked bindings and the 32-byte ABI.</summary>
    public void Validate()
    {
        if (Set != 0 ||
            MaskTextureBinding != 0 ||
            VisibleDepthTextureBinding != 1 ||
            SamplerBinding != 2 ||
            ParametersBinding != 3 ||
            ParameterByteSize != SilkSelectionOutlineUniformWriter.ByteSize)
        {
            throw new ArgumentException(
                "Selection outline resources must use set 0 bindings 0, 1, 2, " +
                "and 3 with a 32-byte parameter buffer.");
        }
    }
}

/// <summary>Raster culling used by the selection mask.</summary>
public enum SilkSelectionCullMode
{
    /// <summary>Rasterizes both front- and back-facing triangles.</summary>
    None
}

/// <summary>Depth comparison used by the visible-only selection mask.</summary>
public enum SilkSelectionDepthCompare
{
    /// <summary>Accepts the selected fragment at retained visible depth.</summary>
    LessEqual
}

/// <summary>Blend policy for the fullscreen outline composite.</summary>
public enum SilkSelectionOutlineBlendMode
{
    /// <summary>Uses source alpha over the retained visible color.</summary>
    StraightAlphaOver
}

/// <summary>Fullscreen primitive generated without vertex buffers.</summary>
public enum SilkSelectionOutlinePrimitive
{
    /// <summary>One triangle generated from SV_VertexID.</summary>
    FullscreenTriangle
}

/// <summary>Primitive topology one selection-mask pipeline rasterizes.</summary>
/// <remarks>
/// The mask is scoped to what the selection actually names. A whole prim or a
/// selected face is a triangle list, a selected authored edge is a line list,
/// and a selected authored point is a point list, so the mask contains exactly
/// the component the outline is drawn around rather than the whole prototype.
/// </remarks>
public enum SilkSelectionMaskPrimitiveTopology
{
    /// <summary>Independent indexed triangles, for a prim or a face.</summary>
    TriangleList,

    /// <summary>Independent indexed lines, for an authored edge.</summary>
    LineList,

    /// <summary>Independent indexed points, for an authored point.</summary>
    PointList
}

/// <summary>
/// Which selection-mask stage one mask pipeline rasterizes with.
/// </summary>
/// <remarks>
/// <para>
/// The stage is not implied by the topology, because both stages draw line and
/// point lists. A whole basis-curve resource, a whole UsdGeomPoints resource,
/// and a wireframe line list are drawn as lines and points by the
/// <see cref="WholeResource"/> stage; a selected authored mesh edge or point is
/// drawn as a line or point by the <see cref="SubprimOverlay"/> stage.
/// </para>
/// <para>
/// The difference is the checked coincident clip-space depth offset. A subprim
/// overlay lies exactly on the triangles it was derived from and loses its own
/// less-equal test at an arbitrary subset of its pixels without the separation.
/// A whole resource has no surface behind it to be separated from, so applying
/// the same offset to it pulls the entire prim toward the viewer and outlines a
/// curve the visible-only mode exists to hide.
/// </para>
/// </remarks>
public enum SilkSelectionMaskStage
{
    /// <summary>Rasterizes a whole rendered resource without any depth offset.</summary>
    WholeResource,

    /// <summary>
    /// Rasterizes an authored mesh edge or point over the surface it lies on,
    /// separated by the checked coincident clip-space offset.
    /// </summary>
    SubprimOverlay
}

/// <summary>Checked single-sample RGBA8/D32 selection mask pipeline.</summary>
public readonly record struct SilkSelectionMaskPipelineDescriptor(
    SilkShaderModuleDescriptor VertexShader,
    SilkShaderModuleDescriptor FragmentShader,
    SilkVertexLayoutDescriptor VertexLayout,
    SilkSelectionMaskBindingLayoutDescriptor BindingLayout,
    SilkTextureFormat ColorFormat,
    SilkTextureFormat DepthFormat,
    uint SampleCount,
    SilkSelectionCullMode CullMode,
    bool BlendEnabled,
    bool DepthTestEnabled,
    bool DepthWriteEnabled,
    SilkSelectionDepthCompare DepthCompare,
    SilkSelectionMaskPrimitiveTopology PrimitiveTopology =
        SilkSelectionMaskPrimitiveTopology.TriangleList,
    SilkSelectionMaskStage Stage = SilkSelectionMaskStage.WholeResource)
{
    /// <summary>Creates the exact checked mask pipeline.</summary>
    public static SilkSelectionMaskPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format) =>
        CreateChecked(format, depthTested: true);

    /// <summary>
    /// Creates the checked mask pipeline for either the depth-tested
    /// visible-only mask or the untested x-ray mask.
    /// </summary>
    /// <remarks>
    /// The visible-only composite needs a mask that only unoccluded selected
    /// fragments reach, so its mask rasterizes with a read-only less-equal depth
    /// test. The x-ray composite needs the whole selected silhouette, including
    /// the part behind an occluder, so its mask rasterizes with no depth test at
    /// all; the composite's own depth comparison is then what separates the
    /// visible part from the occluded one. Everything else -- the stages, the
    /// vertex layout, the formats, the sample count, the culling and the
    /// blending -- is identical, so the two masks differ in exactly one piece of
    /// pipeline state and produce silhouettes a consumer can compare.
    /// </remarks>
    public static SilkSelectionMaskPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format,
        bool depthTested) =>
        CreateChecked(
            format,
            depthTested,
            SilkSelectionMaskPrimitiveTopology.TriangleList);

    /// <summary>
    /// Creates the checked mask pipeline for one depth policy and one emitted
    /// topology, rasterizing a whole rendered resource.
    /// </summary>
    public static SilkSelectionMaskPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format,
        bool depthTested,
        SilkSelectionMaskPrimitiveTopology topology) =>
        CreateChecked(
            format,
            depthTested,
            topology,
            SilkVertexLayoutDescriptor.PositionNormal);

    /// <summary>
    /// Creates the checked mask pipeline for one depth policy, one emitted
    /// topology, and one mesh vertex layout, rasterizing a whole rendered
    /// resource.
    /// </summary>
    /// <remarks>
    /// The layout is a parameter for the same reason the pick pipeline's is: a
    /// textured or normal-mapped mesh has a 32- or 48-byte vertex, and a mask
    /// pipeline pinned to the 24-byte layout would outline a silhouette read
    /// from the wrong offsets.
    /// </remarks>
    public static SilkSelectionMaskPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format,
        bool depthTested,
        SilkSelectionMaskPrimitiveTopology topology,
        SilkVertexLayoutDescriptor vertexLayout) =>
        CreateChecked(
            format,
            depthTested,
            topology,
            vertexLayout,
            SilkSelectionMaskStage.WholeResource);

    /// <summary>
    /// Creates the checked mask pipeline for one depth policy, one emitted
    /// topology, one mesh vertex layout, and one explicit mask stage.
    /// </summary>
    /// <remarks>
    /// The stage is stated by the caller rather than inferred from the topology,
    /// because both stages rasterize line and point lists. A whole basis-curve
    /// or UsdGeomPoints selection is its own surface and must be masked
    /// unbiased, or the visible-only mask would outline a curve standing behind
    /// an occluder; a selected authored mesh edge or point genuinely is coplanar
    /// with its own triangles and must be separated by the checked coincident
    /// offset, which is exactly the offset the subprim pick applies.
    /// </remarks>
    public static SilkSelectionMaskPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format,
        bool depthTested,
        SilkSelectionMaskPrimitiveTopology topology,
        SilkVertexLayoutDescriptor vertexLayout,
        SilkSelectionMaskStage stage)
    {
        if (!Enum.IsDefined(topology))
        {
            throw new ArgumentOutOfRangeException(nameof(topology));
        }
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        if (stage == SilkSelectionMaskStage.SubprimOverlay &&
            topology == SilkSelectionMaskPrimitiveTopology.TriangleList)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                "A triangle-list mask covers the surface itself and must not be " +
                "separated from it.");
        }

        return new(
            SelectMaskVertexStage(format, topology, stage),
            depthTested
                ? SilkCheckedShaderAssets.LoadSelectionMaskFragment(format)
                : SilkCheckedShaderAssets.LoadSelectionMaskOccludedFragment(format),
            vertexLayout,
            SilkSelectionMaskBindingLayoutDescriptor.Checked,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureFormat.D32Float,
            1,
            SilkSelectionCullMode.None,
            BlendEnabled: false,
            DepthTestEnabled: depthTested,
            DepthWriteEnabled: false,
            SilkSelectionDepthCompare.LessEqual,
            topology,
            stage);
    }

    private static SilkShaderModuleDescriptor SelectMaskVertexStage(
        SilkShaderBinaryFormat format,
        SilkSelectionMaskPrimitiveTopology topology,
        SilkSelectionMaskStage stage) =>
        stage == SilkSelectionMaskStage.SubprimOverlay
            ? SilkCheckedShaderAssets.LoadSelectionMaskSubprimVertex(format)
            : topology == SilkSelectionMaskPrimitiveTopology.TriangleList
                ? SilkCheckedShaderAssets.LoadSelectionMaskVertex(format)
                : SilkCheckedShaderAssets.LoadSelectionMaskWholeVertex(format);

    /// <summary>Validates the exact mask stages, formats, and depth policy.</summary>
    public void Validate()
    {
        VertexShader.Validate();
        FragmentShader.Validate();
        if (VertexShader.Stage != SilkShaderStage.Vertex ||
            FragmentShader.Stage != SilkShaderStage.Fragment ||
            VertexShader.Format != FragmentShader.Format)
        {
            throw new ArgumentException(
                "A selection mask pipeline requires matching vertex and fragment formats.");
        }

        string vertexEntry = VertexShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : Stage == SilkSelectionMaskStage.SubprimOverlay
                ? "selectionMaskSubprimVertexMain"
                : PrimitiveTopology == SilkSelectionMaskPrimitiveTopology.TriangleList
                    ? "selectionMaskVertexMain"
                    : "selectionMaskWholeVertexMain";
        string fragmentEntry = FragmentShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : DepthTestEnabled
                ? "selectionMaskFragmentMain"
                : "selectionMaskOccludedFragmentMain";
        if (!string.Equals(VertexShader.EntryPoint, vertexEntry, StringComparison.Ordinal) ||
            !string.Equals(FragmentShader.EntryPoint, fragmentEntry, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The selection mask pipeline must use the checked entry points.");
        }

        VertexLayout.Validate();
        BindingLayout.Validate();
        if (ColorFormat != SilkTextureFormat.Rgba8Unorm ||
            DepthFormat != SilkTextureFormat.D32Float ||
            SampleCount != 1 ||
            CullMode != SilkSelectionCullMode.None ||
            BlendEnabled ||
            DepthWriteEnabled ||
            !Enum.IsDefined(PrimitiveTopology) ||
            !Enum.IsDefined(Stage) ||
            (PrimitiveTopology == SilkSelectionMaskPrimitiveTopology.TriangleList &&
                Stage != SilkSelectionMaskStage.WholeResource) ||
            DepthCompare != SilkSelectionDepthCompare.LessEqual)
        {
            throw new ArgumentException(
                "The selection mask requires single-sample RGBA8/D32 rendering, " +
                "no culling or blending, read-only less-equal depth, and the " +
                "whole-resource stage on the triangle topology.");
        }
    }
}

/// <summary>Checked fullscreen straight-alpha outline pipeline.</summary>
public readonly record struct SilkSelectionOutlinePipelineDescriptor(
    SilkShaderModuleDescriptor VertexShader,
    SilkShaderModuleDescriptor FragmentShader,
    SilkSelectionOutlineBindingLayoutDescriptor BindingLayout,
    SilkTextureFormat ColorFormat,
    uint SampleCount,
    SilkSelectionOutlinePrimitive Primitive,
    SilkSelectionOutlineBlendMode BlendMode,
    bool DepthTestEnabled)
{
    /// <summary>Creates the exact checked fullscreen pipeline.</summary>
    public static SilkSelectionOutlinePipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format) =>
        new(
            SilkCheckedShaderAssets.LoadSelectionOutlineVertex(format),
            SilkCheckedShaderAssets.LoadSelectionOutlineFragment(format),
            SilkSelectionOutlineBindingLayoutDescriptor.Checked,
            SilkTextureFormat.Rgba8Unorm,
            1,
            SilkSelectionOutlinePrimitive.FullscreenTriangle,
            SilkSelectionOutlineBlendMode.StraightAlphaOver,
            DepthTestEnabled: false);

    /// <summary>Validates exact stages, bindings, target, blend, and primitive.</summary>
    public void Validate()
    {
        VertexShader.Validate();
        FragmentShader.Validate();
        if (VertexShader.Stage != SilkShaderStage.Vertex ||
            FragmentShader.Stage != SilkShaderStage.Fragment ||
            VertexShader.Format != FragmentShader.Format)
        {
            throw new ArgumentException(
                "A selection outline pipeline requires matching vertex and fragment formats.");
        }

        string vertexEntry = VertexShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : "selectionOutlineVertexMain";
        string fragmentEntry = FragmentShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : "selectionOutlineFragmentMain";
        if (!string.Equals(VertexShader.EntryPoint, vertexEntry, StringComparison.Ordinal) ||
            !string.Equals(FragmentShader.EntryPoint, fragmentEntry, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The selection outline pipeline must use the checked entry points.");
        }

        BindingLayout.Validate();
        if (!SilkTextureFormats.IsColorRenderTarget(ColorFormat) ||
            SampleCount != 1 ||
            Primitive != SilkSelectionOutlinePrimitive.FullscreenTriangle ||
            BlendMode != SilkSelectionOutlineBlendMode.StraightAlphaOver ||
            DepthTestEnabled)
        {
            throw new ArgumentException(
                "The selection outline requires one fullscreen triangle, " +
                "a supported single-sample color target, straight-alpha-over blending, " +
                "and no depth test.");
        }
    }
}

/// <summary>Backend mask pipeline created from checked shader artifacts.</summary>
public interface ISilkSelectionMaskGraphicsPipeline : IDisposable
{
    /// <summary>Gets the exact checked descriptor.</summary>
    SilkSelectionMaskPipelineDescriptor Descriptor { get; }
}

/// <summary>Backend outline pipeline created from checked shader artifacts.</summary>
public interface ISilkSelectionOutlineGraphicsPipeline : IDisposable
{
    /// <summary>Gets the exact checked descriptor.</summary>
    SilkSelectionOutlinePipelineDescriptor Descriptor { get; }
}

/// <summary>Persistent sampled mask/depth/sampler/parameter binding.</summary>
public interface ISilkSelectionOutlineBinding : IDisposable
{
    /// <summary>Gets the resources retained by this binding.</summary>
    SilkSelectionOutlineBindingDescriptor Descriptor { get; }
}

/// <summary>Resources sampled by the fullscreen outline fragment shader.</summary>
public readonly record struct SilkSelectionOutlineBindingDescriptor(
    ISilkGraphicsTexture MaskTexture,
    ISilkGraphicsTexture VisibleDepthTexture,
    ISilkGraphicsSampler Sampler,
    ISilkGraphicsBuffer Parameters)
{
    /// <summary>Validates formats, usage, dimensions, sampler, and buffer size.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(MaskTexture);
        ArgumentNullException.ThrowIfNull(VisibleDepthTexture);
        ArgumentNullException.ThrowIfNull(Sampler);
        ArgumentNullException.ThrowIfNull(Parameters);
        if (MaskTexture.Format != SilkTextureFormat.Rgba8Unorm ||
            (MaskTexture.Usage & SilkTextureUsage.ColorRenderTarget) == 0 ||
            (MaskTexture.Usage & SilkTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "The selection mask must be a sampled RGBA8 color target.",
                nameof(MaskTexture));
        }
        if (VisibleDepthTexture.Format != SilkTextureFormat.D32Float ||
            (VisibleDepthTexture.Usage & SilkTextureUsage.DepthRenderTarget) == 0 ||
            (VisibleDepthTexture.Usage & SilkTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "The visible depth texture must be a sampled D32 depth target.",
                nameof(VisibleDepthTexture));
        }
        if (MaskTexture.Width != VisibleDepthTexture.Width ||
            MaskTexture.Height != VisibleDepthTexture.Height)
        {
            throw new ArgumentException(
                "Selection mask and visible depth dimensions must match.");
        }
        if (Sampler.Descriptor != SilkSamplerDescriptor.NearestClamp)
        {
            throw new ArgumentException(
                "The selection outline requires the checked nearest clamp sampler.",
                nameof(Sampler));
        }
        if (Parameters.Size != SilkSelectionOutlineUniformWriter.ByteSize ||
            (Parameters.Usage & SilkBufferUsage.Uniform) == 0 ||
            (Parameters.Usage & SilkBufferUsage.Upload) == 0)
        {
            throw new ArgumentException(
                "SelectionOutlineParameters must be a reusable uploadable 32-byte " +
                "uniform buffer.",
                nameof(Parameters));
        }
    }
}

/// <summary>Mask target and loaded visible depth for one selection-mask pass.</summary>
public readonly record struct SilkSelectionMaskRenderingDescriptor(
    ISilkGraphicsTexture MaskAttachment,
    ISilkGraphicsTexture VisibleDepthAttachment)
{
    /// <summary>Validates the reusable mask and read-only sampled visible depth.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(MaskAttachment);
        ArgumentNullException.ThrowIfNull(VisibleDepthAttachment);
        if (MaskAttachment.Format != SilkTextureFormat.Rgba8Unorm ||
            (MaskAttachment.Usage & SilkTextureUsage.ColorRenderTarget) == 0 ||
            (MaskAttachment.Usage & SilkTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "The selection mask attachment must be a sampled RGBA8 color target.",
                nameof(MaskAttachment));
        }
        if (VisibleDepthAttachment.Format != SilkTextureFormat.D32Float ||
            (VisibleDepthAttachment.Usage & SilkTextureUsage.DepthRenderTarget) == 0 ||
            (VisibleDepthAttachment.Usage & SilkTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "The visible depth attachment must be a sampled D32 depth target.",
                nameof(VisibleDepthAttachment));
        }
        if (MaskAttachment.Width != VisibleDepthAttachment.Width ||
            MaskAttachment.Height != VisibleDepthAttachment.Height)
        {
            throw new ArgumentException(
                "Selection mask and visible depth dimensions must match.");
        }
    }
}

/// <summary>Loaded visible color target for the fullscreen edge composite.</summary>
public readonly record struct SilkSelectionOutlineRenderingDescriptor(
    ISilkGraphicsTexture VisibleColorAttachment)
{
    /// <summary>Validates a color render target that will be preserved and blended.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(VisibleColorAttachment);
        if (!SilkTextureFormats.IsColorRenderTarget(VisibleColorAttachment.Format) ||
            (VisibleColorAttachment.Usage & SilkTextureUsage.ColorRenderTarget) == 0)
        {
            throw new ArgumentException(
                "The visible outline target must use a supported color format.",
                nameof(VisibleColorAttachment));
        }
    }
}

/// <summary>
/// Optional RHI capability implemented by devices with the shared selection
/// mask and fullscreen composite.
/// </summary>
public interface ISilkSelectionOutlineGraphicsDevice
{
    /// <summary>Gets the generation that owns all selection-outline resources.</summary>
    ulong SelectionOutlineDeviceGeneration { get; }

    /// <summary>Gets immutable visible-only/x-ray support.</summary>
    SilkSelectionOutlineCapabilities SelectionOutlineCapabilities { get; }

    /// <summary>Creates the checked visible-only mask pipeline.</summary>
    ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor);

    /// <summary>Creates the checked fullscreen edge-composite pipeline.</summary>
    ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
        SilkSelectionOutlinePipelineDescriptor descriptor);

    /// <summary>Creates one persistent sampled-resource binding.</summary>
    ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
        SilkSelectionOutlineBindingDescriptor descriptor);
}

/// <summary>
/// Optional commands exposed by a selection-outline-capable command list.
/// </summary>
public interface ISilkSelectionOutlineGraphicsCommandList
{
    /// <summary>
    /// Begins a pass that loads the already-cleared mask and visible depth,
    /// treats depth as read-only, and stores both attachments.
    /// </summary>
    void BeginSelectionMaskRendering(
        SilkSelectionMaskRenderingDescriptor descriptor);

    /// <summary>Sets the checked selected-mesh mask pipeline.</summary>
    void SetSelectionMaskGraphicsPipeline(
        ISilkSelectionMaskGraphicsPipeline pipeline);

    /// <summary>
    /// Begins a color-only pass that loads and preserves the visible target.
    /// </summary>
    void BeginSelectionOutlineRendering(
        SilkSelectionOutlineRenderingDescriptor descriptor);

    /// <summary>Sets the checked fullscreen edge-composite pipeline.</summary>
    void SetSelectionOutlineGraphicsPipeline(
        ISilkSelectionOutlineGraphicsPipeline pipeline);

    /// <summary>Sets the cached sampled mask/depth/sampler/parameter binding.</summary>
    void SetSelectionOutlineBinding(ISilkSelectionOutlineBinding binding);

    /// <summary>Draws exactly one three-vertex fullscreen triangle.</summary>
    void DrawSelectionOutlineFullscreenTriangle();
}

public static partial class SilkCheckedShaderAssets
{
    private static readonly Lazy<SilkSceneParametersReflection>
        SelectionMaskReflectionValue = new(LoadAndValidateSelectionMaskReflection);
    private static readonly Lazy<SilkSelectionOutlineReflection>
        SelectionOutlineReflectionValue = new(LoadAndValidateSelectionOutlineReflection);

    /// <summary>Gets the validated selection-mask SceneParameters ABI.</summary>
    public static SilkSceneParametersReflection SelectionMaskSceneParameters =>
        SelectionMaskReflectionValue.Value;

    /// <summary>Gets the validated sampled-resource and parameter ABI.</summary>
    public static SilkSelectionOutlineReflection SelectionOutline =>
        SelectionOutlineReflectionValue.Value;

    /// <summary>Loads the checked line and point selection-mask vertex module.</summary>
    /// <remarks>
    /// A selected edge or point is coplanar with the surface it came from, so
    /// the mask needs exactly the clip-space depth offset the subprim pick uses
    /// -- or the outline would flicker at the pixels the pick answers -- and the
    /// explicit one-pixel point size SPIR-V and Metal require, or a selected
    /// point would produce no mask coverage at all.
    /// </remarks>
    public static SilkShaderModuleDescriptor LoadSelectionMaskSubprimVertex(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModuleByName(
            "selection.mask.subprim.vertex",
            SilkShaderStage.Vertex,
            format,
            "selectionMaskSubprimVertexMain");

    /// <summary>
    /// Loads the checked whole line and point resource selection-mask vertex
    /// module.
    /// </summary>
    /// <remarks>
    /// A whole basis-curve or UsdGeomPoints resource is its own surface, not a
    /// component coplanar with one, so its mask must be rasterized unbiased --
    /// otherwise the visible-only mask would outline a curve standing behind an
    /// occluder. It still needs the explicit one-pixel point size SPIR-V and
    /// Metal require, or a selected point cloud would produce no mask coverage.
    /// </remarks>
    public static SilkShaderModuleDescriptor LoadSelectionMaskWholeVertex(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModuleByName(
            "selection.mask.whole.vertex",
            SilkShaderStage.Vertex,
            format,
            "selectionMaskWholeVertexMain");

    /// <summary>Loads the checked selection-mask vertex module.</summary>
    public static SilkShaderModuleDescriptor LoadSelectionMaskVertex(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "selection.mask",
            SilkShaderStage.Vertex,
            format,
            "selectionMaskVertexMain");

    /// <summary>Loads the checked selection-mask fragment module.</summary>
    public static SilkShaderModuleDescriptor LoadSelectionMaskFragment(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "selection.mask",
            SilkShaderStage.Fragment,
            format,
            "selectionMaskFragmentMain");

    /// <summary>
    /// Loads the checked occluded selection-mask fragment module, which writes
    /// the whole selected silhouette into the mask's green channel.
    /// </summary>
    /// <remarks>
    /// The x-ray composite reads both silhouettes at once, so they share one
    /// mask texture and differ by channel: this pass runs first and writes green
    /// only, and the depth-tested visible pass runs second and writes every
    /// channel. That order is correct because the visible silhouette is a subset
    /// of the whole one, so green survives wherever this pass set it.
    /// </remarks>
    public static SilkShaderModuleDescriptor LoadSelectionMaskOccludedFragment(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModuleByName(
            "selection.mask.occluded.fragment",
            SilkShaderStage.Fragment,
            format,
            "selectionMaskOccludedFragmentMain");

    /// <summary>Loads the checked fullscreen-outline vertex module.</summary>
    public static SilkShaderModuleDescriptor LoadSelectionOutlineVertex(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "selection.outline",
            SilkShaderStage.Vertex,
            format,
            "selectionOutlineVertexMain");

    /// <summary>Loads the checked fullscreen-outline fragment module.</summary>
    public static SilkShaderModuleDescriptor LoadSelectionOutlineFragment(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "selection.outline",
            SilkShaderStage.Fragment,
            format,
            "selectionOutlineFragmentMain");

    private static SilkSceneParametersReflection LoadAndValidateSelectionMaskReflection()
    {
        SilkSceneParametersReflection vertex = ParseReflection(
            LoadEmbedded("selection.mask.vertex.reflection.json"));
        using JsonDocument fragment = JsonDocument.Parse(
            LoadEmbedded("selection.mask.fragment.reflection.json"));
        using JsonDocument occludedFragment = JsonDocument.Parse(
            LoadEmbedded("selection.mask.occluded.fragment.reflection.json"));
        if (fragment.RootElement.GetProperty("resources").GetArrayLength() != 0 ||
            occludedFragment.RootElement.GetProperty("resources").GetArrayLength() != 0)
        {
            throw new InvalidDataException(
                "Checked selection-mask fragment reflection must contain no resources.");
        }
        if (vertex != SceneParameters)
        {
            throw new InvalidDataException(
                "Checked selection-mask vertex reflection does not match SceneParameters.");
        }
        return vertex;
    }

    private static SilkSelectionOutlineReflection LoadAndValidateSelectionOutlineReflection()
    {
        using JsonDocument vertexDocument = JsonDocument.Parse(
            LoadEmbedded("selection.outline.vertex.reflection.json"));
        using JsonDocument fragmentDocument = JsonDocument.Parse(
            LoadEmbedded("selection.outline.fragment.reflection.json"));
        JsonElement vertex = vertexDocument.RootElement;
        JsonElement fragment = fragmentDocument.RootElement;
        if (vertex.GetProperty("resources").GetArrayLength() != 0)
        {
            throw new InvalidDataException(
                "Checked selection-outline vertex reflection must contain no resources.");
        }

        bool usesVertexId = false;
        foreach (JsonElement input in vertex.GetProperty("stageInputs").EnumerateArray())
        {
            JsonElement semantic = input.GetProperty("semantic");
            if (semantic.GetProperty("name").GetString() == "SV_VERTEXID")
            {
                JsonElement type = input.GetProperty("type");
                usesVertexId =
                    semantic.GetProperty("systemValue").GetBoolean() &&
                    input.GetProperty("location").ValueKind == JsonValueKind.Null &&
                    type.GetProperty("kind").GetString() == "scalar" &&
                    type.GetProperty("scalarType").GetString() == "uint32";
            }
        }
        if (!usesVertexId)
        {
            throw new InvalidDataException(
                "Checked selection-outline vertex reflection must consume SV_VertexID.");
        }

        JsonElement resources = fragment.GetProperty("resources");
        if (resources.GetArrayLength() != 4 ||
            resources[0].GetProperty("name").GetString() != "selectionMask" ||
            resources[1].GetProperty("name").GetString() != "visibleDepth" ||
            resources[2].GetProperty("name").GetString() != "selectionSampler" ||
            resources[3].GetProperty("name").GetString() != "SelectionOutlineParameters")
        {
            throw new InvalidDataException(
                "Checked selection-outline fragment resources are out of order.");
        }

        SilkShaderResourceBindingReflection mask = ParseSelectionBinding(
            resources[0],
            "t",
            0,
            0);
        SilkShaderResourceBindingReflection depth = ParseSelectionBinding(
            resources[1],
            "t",
            1,
            1);
        SilkShaderResourceBindingReflection sampler = ParseSelectionBinding(
            resources[2],
            "s",
            0,
            2);
        SilkShaderResourceBindingReflection parameters = ParseSelectionBinding(
            resources[3],
            "b",
            0,
            3);
        ValidateSelectionTextureShape(resources[0], "selectionMask", "float32", 4);
        ValidateSelectionTextureShape(resources[1], "visibleDepth", "float32", 1);
        ValidateSelectionSamplerShape(resources[2]);
        JsonElement parameterShape = resources[3].GetProperty("shape");
        JsonElement vulkanParameterShape = resources[3].GetProperty("vulkanLayout");
        ValidateSelectionParameterShape(parameterShape);
        ValidateSelectionParameterShape(vulkanParameterShape);

        return new SilkSelectionOutlineReflection(
            mask,
            depth,
            sampler,
            parameters,
            0,
            16,
            16,
            8,
            24,
            4,
            28,
            4,
            32,
            16,
            SilkSelectionOutlineUniformWriter.ByteSize,
            usesVertexId);
    }

    private static SilkShaderResourceBindingReflection ParseSelectionBinding(
        JsonElement resource,
        string registerClass,
        uint register,
        uint binding)
    {
        JsonElement bindings = resource.GetProperty("bindings");
        JsonElement d3d = bindings.GetProperty("d3d");
        JsonElement vulkan = bindings.GetProperty("vulkan");
        if (d3d.GetProperty("registerClass").GetString() != registerClass ||
            d3d.GetProperty("register").GetUInt32() != register ||
            d3d.GetProperty("space").GetUInt32() != 0 ||
            vulkan.GetProperty("set").GetUInt32() != 0 ||
            vulkan.GetProperty("binding").GetUInt32() != binding)
        {
            throw new InvalidDataException(
                $"Checked selection resource '{resource.GetProperty("name").GetString()}' " +
                "has the wrong D3D or Vulkan binding.");
        }
        return new SilkShaderResourceBindingReflection(
            registerClass,
            register,
            0,
            0,
            binding);
    }

    private static void ValidateSelectionTextureShape(
        JsonElement resource,
        string name,
        string scalarType,
        uint componentCount)
    {
        ValidateSelectionTextureShapeCore(resource.GetProperty("shape"), name, scalarType, componentCount);
        ValidateSelectionTextureShapeCore(
            resource.GetProperty("vulkanLayout"),
            name,
            scalarType,
            componentCount);
    }

    private static void ValidateSelectionTextureShapeCore(
        JsonElement shape,
        string name,
        string scalarType,
        uint componentCount)
    {
        JsonElement type = shape.GetProperty("elementType");
        bool typeMatches;
        if (componentCount == 1)
        {
            typeMatches =
                type.GetProperty("kind").GetString() == "scalar" &&
                type.GetProperty("scalarType").GetString() == scalarType;
        }
        else
        {
            JsonElement elementType = type.GetProperty("elementType");
            typeMatches =
                type.GetProperty("kind").GetString() == "vector" &&
                type.GetProperty("elementCount").GetUInt32() == componentCount &&
                elementType.GetProperty("kind").GetString() == "scalar" &&
                elementType.GetProperty("scalarType").GetString() == scalarType;
        }
        if (shape.GetProperty("kind").GetString() != "texture2D" ||
            shape.GetProperty("access").GetString() != "read" ||
            !typeMatches)
        {
            throw new InvalidDataException(
                $"Checked {name} must be a read-only two-dimensional float texture.");
        }
    }

    private static void ValidateSelectionSamplerShape(JsonElement resource)
    {
        ValidateSelectionSamplerShapeCore(resource.GetProperty("shape"));
        ValidateSelectionSamplerShapeCore(resource.GetProperty("vulkanLayout"));
    }

    private static void ValidateSelectionSamplerShapeCore(JsonElement shape)
    {
        if (shape.GetProperty("kind").GetString() != "sampler" ||
            shape.GetProperty("access").GetString() != "read")
        {
            throw new InvalidDataException(
                "Checked selectionSampler must be a read-only sampler.");
        }
    }

    private static void ValidateSelectionParameterShape(JsonElement shape)
    {
        JsonElement fields = shape.GetProperty("elementType").GetProperty("fields");
        if (shape.GetProperty("kind").GetString() != "constantBuffer" ||
            shape.GetProperty("access").GetString() != "constant" ||
            shape.GetProperty("size").GetUInt32() !=
                SilkSelectionOutlineUniformWriter.ByteSize ||
            fields.GetArrayLength() != 5 ||
            !HasSelectionField(fields[0], "outlineColor", "vector", 4, 0, 16) ||
            !HasSelectionField(fields[1], "inverseViewportSize", "vector", 2, 16, 8) ||
            !HasSelectionField(fields[2], "outlineWidthPixels", "scalar", 1, 24, 4) ||
            !HasSelectionField(fields[3], "depthEpsilon", "scalar", 1, 28, 4) ||
            !HasSelectionField(fields[4], "occludedOutlineColor", "vector", 4, 32, 16))
        {
            throw new InvalidDataException(
                "Checked SelectionOutlineParameters must contain float4 color, " +
                "float2 inverse viewport, float width, float depth epsilon, and " +
                "float4 occluded color.");
        }
    }

    private static bool HasSelectionField(
        JsonElement field,
        string name,
        string kind,
        uint componentCount,
        uint offset,
        uint size)
    {
        JsonElement type = field.GetProperty("type");
        JsonElement layout = field.GetProperty("layout");
        if (field.GetProperty("name").GetString() != name ||
            type.GetProperty("kind").GetString() != kind ||
            layout.GetProperty("offset").GetUInt32() != offset ||
            layout.GetProperty("size").GetUInt32() != size)
        {
            return false;
        }
        if (kind == "scalar")
        {
            return type.GetProperty("scalarType").GetString() == "float32";
        }
        JsonElement elementType = type.GetProperty("elementType");
        return type.GetProperty("elementCount").GetUInt32() == componentCount &&
            elementType.GetProperty("kind").GetString() == "scalar" &&
            elementType.GetProperty("scalarType").GetString() == "float32";
    }
}
