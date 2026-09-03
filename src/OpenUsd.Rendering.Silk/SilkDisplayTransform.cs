// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text.Json;

namespace OpenUsd.Rendering.Silk;

/// <summary>Latest reason a colour-managed display transform did or did not run.</summary>
public enum SilkDisplayTransformStatus
{
    /// <summary>No display transform is configured.</summary>
    Inactive,

    /// <summary>The graphics device has no display-transform capability.</summary>
    UnsupportedDevice,

    /// <summary>The OpenColorIO configuration could not be found or opened.</summary>
    ConfigUnavailable,

    /// <summary>The configuration does not contain the requested display, view, or look.</summary>
    TransformUnsupported,

    /// <summary>A fullscreen display-transform pass was recorded.</summary>
    Applied
}

/// <summary>Cumulative display-transform state and resource evidence.</summary>
/// <remarks>
/// <see cref="RequestKey"/> is the <see cref="RenderDisplayTransform.CacheKey"/> of the
/// transform these diagnostics describe, or <see langword="null"/> when none has been
/// requested. It exists so a consumer can tell a refusal of the transform it currently
/// wants from a refusal of one it has already replaced: without it, a stale failure
/// observed after a newer request succeeded reads exactly like a current failure.
/// </remarks>
public readonly record struct SilkDisplayTransformDiagnostics(
    SilkDisplayTransformStatus Status,
    int LatticeSize,
    long LatticeByteSize,
    ulong Passes,
    ulong LatticeBuilds,
    ulong LatticeCacheHits,
    ulong LatticeUploads,
    ulong PipelineCreations,
    ulong BindingCreations,
    ulong IntermediateCreations,
    ulong ParameterUploads,
    ulong DeviceInvalidations,
    ulong Failures,
    string? RequestKey = null);

/// <summary>Writes the checked 32-byte display-transform constant buffer.</summary>
public static class SilkDisplayTransformUniformWriter
{
    /// <summary>Gets the exact checked constant-buffer byte size.</summary>
    public const int ByteSize = 32;

    /// <summary>
    /// Writes the exposure scale, shaper interval, and lattice grid constants.
    /// </summary>
    /// <param name="exposure">The exposure adjustment in stops.</param>
    /// <param name="shaperMinimumLog2">The lower shaper bound in stops.</param>
    /// <param name="shaperRangeLog2">The shaper interval width in stops.</param>
    /// <param name="latticeSize">The lattice edge length.</param>
    /// <param name="flipVertically">
    /// Whether the backend's framebuffer origin is opposite the fullscreen triangle's
    /// clip-space Y direction, which is true exactly when
    /// <see cref="ISilkGraphicsDevice.ClipSpaceYPointsDown"/> is set.
    /// </param>
    /// <param name="destination">The exactly 32-byte destination.</param>
    /// <exception cref="ArgumentException">The destination is not 32 bytes.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A value is not finite, the shaper interval is not positive, or the lattice edge is
    /// outside the supported range.
    /// </exception>
    public static void Write(
        float exposure,
        float shaperMinimumLog2,
        float shaperRangeLog2,
        int latticeSize,
        bool flipVertically,
        Span<byte> destination)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                $"DisplayTransformParameters requires exactly {ByteSize} bytes.",
                nameof(destination));
        }
        if (!float.IsFinite(exposure))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure), "Exposure must be finite.");
        }
        if (!float.IsFinite(shaperMinimumLog2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(shaperMinimumLog2),
                "The lower shaper bound must be finite.");
        }
        if (!float.IsFinite(shaperRangeLog2) || shaperRangeLog2 <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shaperRangeLog2),
                "The shaper interval must be finite and positive.");
        }
        if (latticeSize is < RenderDisplayTransform.MinimumLatticeSize or
            > RenderDisplayTransform.MaximumLatticeSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latticeSize),
                latticeSize,
                "The lattice edge is outside the supported range.");
        }

        float exposureScale = MathF.Pow(2, exposure);
        if (!float.IsFinite(exposureScale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(exposure),
                exposure,
                "The computed exposure scale is not finite.");
        }

        WriteSingle(destination, 0, exposureScale);
        WriteSingle(destination, 4, shaperMinimumLog2);
        WriteSingle(destination, 8, shaperRangeLog2);
        WriteSingle(destination, 12, latticeSize);
        WriteSingle(destination, 16, 1f / (latticeSize * (float)latticeSize));
        WriteSingle(destination, 20, 1f / latticeSize);
        WriteSingle(destination, 24, latticeSize - 1);
        WriteSingle(destination, 28, flipVertically ? 1f : 0f);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));
}

/// <summary>Validated fullscreen display-transform shader ABI.</summary>
public readonly record struct SilkDisplayTransformReflection(
    SilkShaderResourceBindingReflection SceneColorTexture,
    SilkShaderResourceBindingReflection LatticeTexture,
    SilkShaderResourceBindingReflection Sampler,
    SilkShaderResourceBindingReflection Parameters,
    uint ExposureShaperOffset,
    uint ExposureShaperByteSize,
    uint LatticeGridOffset,
    uint LatticeGridByteSize,
    uint ParameterByteSize,
    bool UsesVertexId);

/// <summary>Checked display-transform sampled-resource layout.</summary>
public readonly record struct SilkDisplayTransformBindingLayoutDescriptor(
    uint Set,
    uint SceneColorTextureBinding,
    uint LatticeTextureBinding,
    uint SamplerBinding,
    uint ParametersBinding,
    uint ParameterByteSize)
{
    /// <summary>
    /// Gets t0/t1/s0/b0 for D3D and set-zero bindings 0/1/2/3 for Vulkan.
    /// </summary>
    public static SilkDisplayTransformBindingLayoutDescriptor Checked
    {
        get
        {
            SilkDisplayTransformReflection reflection =
                SilkCheckedShaderAssets.DisplayTransform;
            return new SilkDisplayTransformBindingLayoutDescriptor(
                reflection.SceneColorTexture.VulkanSet,
                reflection.SceneColorTexture.VulkanBinding,
                reflection.LatticeTexture.VulkanBinding,
                reflection.Sampler.VulkanBinding,
                reflection.Parameters.VulkanBinding,
                reflection.ParameterByteSize);
        }
    }

    /// <summary>Validates all checked bindings and the 32-byte ABI.</summary>
    public void Validate()
    {
        if (Set != 0 ||
            SceneColorTextureBinding != 0 ||
            LatticeTextureBinding != 1 ||
            SamplerBinding != 2 ||
            ParametersBinding != 3 ||
            ParameterByteSize != SilkDisplayTransformUniformWriter.ByteSize)
        {
            throw new ArgumentException(
                "Display transform resources must use set 0 bindings 0, 1, 2, and 3 " +
                "with a 32-byte parameter buffer.");
        }
    }
}

/// <summary>Checked fullscreen display-transform pipeline.</summary>
public readonly record struct SilkDisplayTransformPipelineDescriptor(
    SilkShaderModuleDescriptor VertexShader,
    SilkShaderModuleDescriptor FragmentShader,
    SilkDisplayTransformBindingLayoutDescriptor BindingLayout,
    SilkTextureFormat ColorFormat,
    uint SampleCount)
{
    /// <summary>Creates the exact checked fullscreen pipeline.</summary>
    public static SilkDisplayTransformPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format) =>
        new(
            SilkCheckedShaderAssets.LoadDisplayTransformVertex(format),
            SilkCheckedShaderAssets.LoadDisplayTransformFragment(format),
            SilkDisplayTransformBindingLayoutDescriptor.Checked,
            SilkTextureFormat.Rgba8Unorm,
            1);

    /// <summary>Validates exact stages, bindings, target, and sample count.</summary>
    public void Validate()
    {
        VertexShader.Validate();
        FragmentShader.Validate();
        if (VertexShader.Stage != SilkShaderStage.Vertex ||
            FragmentShader.Stage != SilkShaderStage.Fragment ||
            VertexShader.Format != FragmentShader.Format)
        {
            throw new ArgumentException(
                "A display transform pipeline requires matching vertex and fragment formats.");
        }

        string vertexEntry = VertexShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : "displayTransformVertexMain";
        string fragmentEntry = FragmentShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : "displayTransformFragmentMain";
        if (!string.Equals(VertexShader.EntryPoint, vertexEntry, StringComparison.Ordinal) ||
            !string.Equals(FragmentShader.EntryPoint, fragmentEntry, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The display transform pipeline must use the checked entry points.");
        }

        BindingLayout.Validate();
        if (!SilkTextureFormats.IsColorRenderTarget(ColorFormat) || SampleCount != 1)
        {
            throw new ArgumentException(
                "The display transform requires a supported single-sample color target.");
        }
    }
}

/// <summary>Backend display-transform pipeline created from checked artifacts.</summary>
public interface ISilkDisplayTransformGraphicsPipeline : IDisposable
{
    /// <summary>Gets the exact checked descriptor.</summary>
    SilkDisplayTransformPipelineDescriptor Descriptor { get; }
}

/// <summary>Resources sampled by the fullscreen display-transform fragment shader.</summary>
public readonly record struct SilkDisplayTransformBindingDescriptor(
    ISilkGraphicsTexture SceneColorTexture,
    ISilkGraphicsTexture LatticeTexture,
    ISilkGraphicsSampler Sampler,
    ISilkGraphicsBuffer Parameters)
{
    /// <summary>Validates formats, usage, sampler, and buffer size.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(SceneColorTexture);
        ArgumentNullException.ThrowIfNull(LatticeTexture);
        ArgumentNullException.ThrowIfNull(Sampler);
        ArgumentNullException.ThrowIfNull(Parameters);
        if (!SilkTextureFormats.IsFloatingPointColor(SceneColorTexture.Format) ||
            (SceneColorTexture.Usage & SilkTextureUsage.ColorRenderTarget) == 0 ||
            (SceneColorTexture.Usage & SilkTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "The display transform source must be a sampled floating-point colour target.",
                nameof(SceneColorTexture));
        }
        if (LatticeTexture.Format != SilkTextureFormat.Rgba8Unorm ||
            (LatticeTexture.Usage & SilkTextureUsage.Sampled) == 0 ||
            (LatticeTexture.Usage & SilkTextureUsage.CopyDestination) == 0)
        {
            throw new ArgumentException(
                "The display transform lattice must be an uploadable sampled RGBA8 texture.",
                nameof(LatticeTexture));
        }
        if (LatticeTexture.Height < RenderDisplayTransform.MinimumLatticeSize ||
            LatticeTexture.Height > RenderDisplayTransform.MaximumLatticeSize ||
            LatticeTexture.Width != LatticeTexture.Height * LatticeTexture.Height)
        {
            throw new ArgumentException(
                "The display transform lattice must be a bounded size-by-size-squared strip.",
                nameof(LatticeTexture));
        }
        if (Sampler.Descriptor != SilkSamplerDescriptor.LinearClamp)
        {
            throw new ArgumentException(
                "The display transform requires the checked linear clamp sampler.",
                nameof(Sampler));
        }
        if (Parameters.Size != SilkDisplayTransformUniformWriter.ByteSize ||
            (Parameters.Usage & SilkBufferUsage.Uniform) == 0 ||
            (Parameters.Usage & SilkBufferUsage.Upload) == 0)
        {
            throw new ArgumentException(
                "DisplayTransformParameters must be a reusable uploadable 32-byte uniform buffer.",
                nameof(Parameters));
        }
    }
}

/// <summary>Persistent sampled scene/lattice/sampler/parameter binding.</summary>
public interface ISilkDisplayTransformBinding : IDisposable
{
    /// <summary>Gets the resources retained by this binding.</summary>
    SilkDisplayTransformBindingDescriptor Descriptor { get; }
}

/// <summary>Attachments and sampled sources for one display-transform pass.</summary>
/// <remarks>
/// The sampled sources are named at the start of the pass, not when the binding is
/// set, because a backend has to move them into a shader-readable layout before the
/// pass begins: Vulkan cannot transition an image layout inside a dynamic rendering
/// scope, so a descriptor that only named the target would make correct Vulkan
/// recording impossible.
/// </remarks>
public readonly record struct SilkDisplayTransformRenderingDescriptor(
    ISilkGraphicsTexture ColorAttachment,
    ISilkGraphicsTexture SceneColorTexture,
    ISilkGraphicsTexture LatticeTexture)
{
    /// <summary>Validates the target and both sampled sources.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(ColorAttachment);
        ArgumentNullException.ThrowIfNull(SceneColorTexture);
        ArgumentNullException.ThrowIfNull(LatticeTexture);
        if (!SilkTextureFormats.IsColorRenderTarget(ColorAttachment.Format) ||
            (ColorAttachment.Usage & SilkTextureUsage.ColorRenderTarget) == 0)
        {
            throw new ArgumentException(
                "The display transform target must use a supported colour format.",
                nameof(ColorAttachment));
        }
        if (!SilkTextureFormats.IsFloatingPointColor(SceneColorTexture.Format) ||
            (SceneColorTexture.Usage & SilkTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "The display transform source must be a sampled floating-point colour target.",
                nameof(SceneColorTexture));
        }
        if (LatticeTexture.Format != SilkTextureFormat.Rgba8Unorm ||
            (LatticeTexture.Usage & SilkTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "The display transform lattice must be a sampled RGBA8 texture.",
                nameof(LatticeTexture));
        }
        if (ReferenceEquals(ColorAttachment, SceneColorTexture))
        {
            throw new ArgumentException(
                "The display transform cannot sample the target it writes.",
                nameof(SceneColorTexture));
        }
        if (ColorAttachment.Width != SceneColorTexture.Width ||
            ColorAttachment.Height != SceneColorTexture.Height)
        {
            throw new ArgumentException(
                "The display transform target and source dimensions must match.",
                nameof(SceneColorTexture));
        }
    }
}

/// <summary>
/// Optional RHI capability implemented by devices that can record the shared
/// fullscreen display-transform composite.
/// </summary>
public interface ISilkDisplayTransformGraphicsDevice
{
    /// <summary>Gets the generation that owns all display-transform resources.</summary>
    ulong DisplayTransformDeviceGeneration { get; }

    /// <summary>Creates the checked fullscreen display-transform pipeline.</summary>
    ISilkDisplayTransformGraphicsPipeline CreateDisplayTransformGraphicsPipeline(
        SilkDisplayTransformPipelineDescriptor descriptor);

    /// <summary>Creates one persistent sampled-resource binding.</summary>
    ISilkDisplayTransformBinding CreateDisplayTransformBinding(
        SilkDisplayTransformBindingDescriptor descriptor);
}

/// <summary>
/// Optional commands exposed by a display-transform-capable command list.
/// </summary>
public interface ISilkDisplayTransformGraphicsCommandList
{
    /// <summary>
    /// Begins a colour-only pass that discards the previous contents of the display
    /// target, which the fullscreen triangle overwrites completely, and transitions
    /// the sampled scene colour and lattice for fragment reads.
    /// </summary>
    void BeginDisplayTransformRendering(
        SilkDisplayTransformRenderingDescriptor descriptor);

    /// <summary>Sets the checked fullscreen display-transform pipeline.</summary>
    void SetDisplayTransformGraphicsPipeline(
        ISilkDisplayTransformGraphicsPipeline pipeline);

    /// <summary>Sets the cached sampled scene/lattice/sampler/parameter binding.</summary>
    void SetDisplayTransformBinding(ISilkDisplayTransformBinding binding);

    /// <summary>Draws exactly one three-vertex fullscreen triangle.</summary>
    void DrawDisplayTransformFullscreenTriangle();
}

public static partial class SilkCheckedShaderAssets
{
    private static readonly Lazy<SilkDisplayTransformReflection>
        DisplayTransformReflectionValue = new(LoadAndValidateDisplayTransformReflection);

    /// <summary>Gets the validated display-transform sampled-resource and parameter ABI.</summary>
    public static SilkDisplayTransformReflection DisplayTransform =>
        DisplayTransformReflectionValue.Value;

    /// <summary>Loads the checked display-transform vertex module.</summary>
    public static SilkShaderModuleDescriptor LoadDisplayTransformVertex(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "display.transform",
            SilkShaderStage.Vertex,
            format,
            "displayTransformVertexMain");

    /// <summary>Loads the checked display-transform fragment module.</summary>
    public static SilkShaderModuleDescriptor LoadDisplayTransformFragment(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "display.transform",
            SilkShaderStage.Fragment,
            format,
            "displayTransformFragmentMain");

    private static SilkDisplayTransformReflection LoadAndValidateDisplayTransformReflection()
    {
        using JsonDocument vertexDocument = JsonDocument.Parse(
            LoadEmbedded("display.transform.vertex.reflection.json"));
        using JsonDocument fragmentDocument = JsonDocument.Parse(
            LoadEmbedded("display.transform.fragment.reflection.json"));
        JsonElement vertex = vertexDocument.RootElement;
        JsonElement fragment = fragmentDocument.RootElement;
        if (vertex.GetProperty("resources").GetArrayLength() != 0)
        {
            throw new InvalidDataException(
                "Checked display-transform vertex reflection must contain no resources.");
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
                "Checked display-transform vertex reflection must consume SV_VertexID.");
        }

        JsonElement resources = fragment.GetProperty("resources");
        if (resources.GetArrayLength() != 4 ||
            resources[0].GetProperty("name").GetString() != "sceneColor" ||
            resources[1].GetProperty("name").GetString() != "displayLut" ||
            resources[2].GetProperty("name").GetString() != "displaySampler" ||
            resources[3].GetProperty("name").GetString() != "DisplayTransformParameters")
        {
            throw new InvalidDataException(
                "Checked display-transform fragment resources are out of order.");
        }

        SilkShaderResourceBindingReflection sceneColor = ParseSelectionBinding(
            resources[0],
            "t",
            0,
            0);
        SilkShaderResourceBindingReflection lattice = ParseSelectionBinding(
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
        ValidateSelectionTextureShape(resources[0], "sceneColor", "float32", 4);
        ValidateSelectionTextureShape(resources[1], "displayLut", "float32", 4);
        ValidateSelectionSamplerShape(resources[2]);
        ValidateDisplayTransformParameterShape(resources[3].GetProperty("shape"));
        ValidateDisplayTransformParameterShape(resources[3].GetProperty("vulkanLayout"));

        return new SilkDisplayTransformReflection(
            sceneColor,
            lattice,
            sampler,
            parameters,
            0,
            16,
            16,
            16,
            SilkDisplayTransformUniformWriter.ByteSize,
            usesVertexId);
    }

    private static void ValidateDisplayTransformParameterShape(JsonElement shape)
    {
        JsonElement fields = shape.GetProperty("elementType").GetProperty("fields");
        if (shape.GetProperty("kind").GetString() != "constantBuffer" ||
            shape.GetProperty("access").GetString() != "constant" ||
            shape.GetProperty("size").GetUInt32() !=
                SilkDisplayTransformUniformWriter.ByteSize ||
            fields.GetArrayLength() != 2 ||
            !HasSelectionField(fields[0], "exposureShaper", "vector", 4, 0, 16) ||
            !HasSelectionField(fields[1], "lutGrid", "vector", 4, 16, 16))
        {
            throw new InvalidDataException(
                "Checked DisplayTransformParameters must be a 32-byte constant buffer of " +
                "two four-component vectors.");
        }
    }
}
