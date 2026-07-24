// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text.Json;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Shared visible-only selection-outline policy.
/// </summary>
public sealed record SilkSelectionOutlineSettings
{
    /// <summary>Gets the narrowest supported physical-pixel outline.</summary>
    public const float MinimumWidth = 1;

    /// <summary>Gets the widest supported physical-pixel outline.</summary>
    public const float MaximumWidth = 16;

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
    {
        color.Validate();
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
        VisibleOnly = visibleOnly;
    }

    /// <summary>Gets whether selection outlining is enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the straight-alpha outline color.</summary>
    public SilkColor Color { get; }

    /// <summary>Gets the edge-kernel radius in physical pixels.</summary>
    public float Width { get; }

    /// <summary>
    /// Gets whether only unoccluded selected fragments may contribute.
    /// </summary>
    /// <remarks>
    /// A value of <see langword="false"/> requests x-ray selection, which is
    /// explicitly unsupported by the initial Silk capability.
    /// </remarks>
    public bool VisibleOnly { get; }
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

/// <summary>Writes the checked 32-byte selection-outline constant buffer.</summary>
public static class SilkSelectionOutlineUniformWriter
{
    /// <summary>Gets the exact checked constant-buffer byte size.</summary>
    public const int ByteSize = 32;

    /// <summary>
    /// Gets the normalized-depth tolerance used to suppress outlines over nearer
    /// occluders.
    /// </summary>
    public const float DepthEpsilon = 0.00001f;

    /// <summary>Writes color, inverse viewport, width, and depth tolerance.</summary>
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

        WriteSingle(destination, 0, settings.Color.Red);
        WriteSingle(destination, 4, settings.Color.Green);
        WriteSingle(destination, 8, settings.Color.Blue);
        WriteSingle(destination, 12, settings.Color.Alpha);
        WriteSingle(destination, 16, 1f / width);
        WriteSingle(destination, 20, 1f / height);
        WriteSingle(destination, 24, settings.Width);
        WriteSingle(destination, 28, DepthEpsilon);
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

/// <summary>Checked single-sample RGBA8/D32 visible-only mask pipeline.</summary>
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
    SilkSelectionDepthCompare DepthCompare)
{
    /// <summary>Creates the exact checked mask pipeline.</summary>
    public static SilkSelectionMaskPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format) =>
        new(
            SilkCheckedShaderAssets.LoadSelectionMaskVertex(format),
            SilkCheckedShaderAssets.LoadSelectionMaskFragment(format),
            SilkVertexLayoutDescriptor.PositionNormal,
            SilkSelectionMaskBindingLayoutDescriptor.Checked,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureFormat.D32Float,
            1,
            SilkSelectionCullMode.None,
            BlendEnabled: false,
            DepthTestEnabled: true,
            DepthWriteEnabled: false,
            SilkSelectionDepthCompare.LessEqual);

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
            : "selectionMaskVertexMain";
        string fragmentEntry = FragmentShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : "selectionMaskFragmentMain";
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
            !DepthTestEnabled ||
            DepthWriteEnabled ||
            DepthCompare != SilkSelectionDepthCompare.LessEqual)
        {
            throw new ArgumentException(
                "The selection mask requires single-sample RGBA8/D32 rendering, " +
                "no culling or blending, and read-only less-equal depth.");
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
        if (ColorFormat != SilkTextureFormat.Rgba8Unorm ||
            SampleCount != 1 ||
            Primitive != SilkSelectionOutlinePrimitive.FullscreenTriangle ||
            BlendMode != SilkSelectionOutlineBlendMode.StraightAlphaOver ||
            DepthTestEnabled)
        {
            throw new ArgumentException(
                "The selection outline requires one fullscreen triangle, " +
                "single-sample RGBA8 straight-alpha-over blending, and no depth test.");
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
    /// <summary>Validates an RGBA8 color render target that will be preserved and blended.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(VisibleColorAttachment);
        if (VisibleColorAttachment.Format != SilkTextureFormat.Rgba8Unorm ||
            (VisibleColorAttachment.Usage & SilkTextureUsage.ColorRenderTarget) == 0)
        {
            throw new ArgumentException(
                "The visible outline target must be an RGBA8 color target.",
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
        if (fragment.RootElement.GetProperty("resources").GetArrayLength() != 0)
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
            fields.GetArrayLength() != 4 ||
            !HasSelectionField(fields[0], "outlineColor", "vector", 4, 0, 16) ||
            !HasSelectionField(fields[1], "inverseViewportSize", "vector", 2, 16, 8) ||
            !HasSelectionField(fields[2], "outlineWidthPixels", "scalar", 1, 24, 4) ||
            !HasSelectionField(fields[3], "depthEpsilon", "scalar", 1, 28, 4))
        {
            throw new InvalidDataException(
                "Checked SelectionOutlineParameters must contain float4 color, " +
                "float2 inverse viewport, float width, and float depth epsilon.");
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
