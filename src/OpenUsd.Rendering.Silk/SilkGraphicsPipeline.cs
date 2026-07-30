// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using System.Text;
using System.Text.Json;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Shared deterministic lifetime for native resources referenced by submissions.
/// </summary>
public abstract class SilkGraphicsResourceBase : IDisposable
{
    private readonly object _lifetimeGate = new();
    private int _leaseCount;
    private bool _disposeRequested;
    private bool _nativeReleased;

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        bool release;
        lock (_lifetimeGate)
        {
            if (_disposeRequested)
            {
                return;
            }
            _disposeRequested = true;
            release = _leaseCount == 0;
            if (release)
            {
                _nativeReleased = true;
            }
        }
        if (release)
        {
            ReleaseNative();
        }
    }

    /// <summary>Acquires a submission or owner lease.</summary>
    protected IDisposable AcquireResourceLease()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _leaseCount++;
        }
        return new ResourceLease(this);
    }

    /// <summary>Rejects public operations after disposal was requested.</summary>
    protected void ThrowIfResourceDisposed()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
        }
    }

    /// <summary>Releases the native resource after disposal and all leases.</summary>
    protected abstract void ReleaseNative();

    private void ReleaseLease()
    {
        bool release;
        lock (_lifetimeGate)
        {
            _leaseCount--;
            release = _disposeRequested && _leaseCount == 0 && !_nativeReleased;
            if (release)
            {
                _nativeReleased = true;
            }
        }
        if (release)
        {
            ReleaseNative();
        }
    }

    private sealed class ResourceLease(SilkGraphicsResourceBase resource) : IDisposable
    {
        private SilkGraphicsResourceBase? _resource = resource;

        public void Dispose() =>
            Interlocked.Exchange(ref _resource, null)?.ReleaseLease();
    }
}

/// <summary>Shader stages supported by the first graphics pipeline slice.</summary>
public enum SilkShaderStage
{
    /// <summary>Vertex stage.</summary>
    Vertex,

    /// <summary>Fragment or pixel stage.</summary>
    Fragment,

    /// <summary>Compute stage.</summary>
    Compute
}

/// <summary>Checked native shader binary formats.</summary>
public enum SilkShaderBinaryFormat
{
    /// <summary>DirectX intermediate language.</summary>
    Dxil,

    /// <summary>Standard portable intermediate representation for Vulkan.</summary>
    SpirV,

    /// <summary>Pinned Apple Metal library.</summary>
    MetalLibrary
}

/// <summary>Describes one checked shader module.</summary>
public readonly record struct SilkShaderModuleDescriptor(
    SilkShaderStage Stage,
    SilkShaderBinaryFormat Format,
    string EntryPoint,
    ReadOnlyMemory<byte> Code)
{
    /// <summary>Validates module code and entry point.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Stage))
        {
            throw new ArgumentOutOfRangeException(nameof(Stage));
        }
        if (!Enum.IsDefined(Format))
        {
            throw new ArgumentOutOfRangeException(nameof(Format));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(EntryPoint);
        if (Code.IsEmpty)
        {
            throw new ArgumentException("Shader code cannot be empty.", nameof(Code));
        }
    }
}

/// <summary>Backend shader module.</summary>
public interface ISilkGraphicsShaderModule : IDisposable
{
    /// <summary>Gets the module descriptor.</summary>
    SilkShaderModuleDescriptor Descriptor { get; }
}

/// <summary>Shader visibility for one binding.</summary>
[Flags]
public enum SilkShaderStageVisibility
{
    /// <summary>Vertex stage visibility.</summary>
    Vertex = 1,

    /// <summary>Fragment stage visibility.</summary>
    Fragment = 2,

    /// <summary>Compute stage visibility.</summary>
    Compute = 4
}

/// <summary>The kind of resource occupying one binding slot.</summary>
public enum SilkBindingKind
{
    /// <summary>A uniform (constant) buffer.</summary>
    UniformBuffer = 0,

    /// <summary>A texture read through a sampler.</summary>
    SampledTexture = 1,

    /// <summary>A sampler.</summary>
    Sampler = 2
}

/// <summary>
/// One slot in a material binding layout.
/// </summary>
public readonly record struct SilkBindingSlot(
    uint Set,
    uint Binding,
    SilkBindingKind Kind,
    uint UniformByteSize,
    SilkShaderStageVisibility Visibility)
{
    /// <summary>Validates one slot in isolation.</summary>
    public void Validate()
    {
        if (Kind is not (SilkBindingKind.UniformBuffer or
            SilkBindingKind.SampledTexture or SilkBindingKind.Sampler))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Kind),
                "A binding slot kind must be uniform buffer, sampled texture, or sampler.");
        }
        if (Kind == SilkBindingKind.UniformBuffer)
        {
            if (UniformByteSize == 0 || (UniformByteSize % 16) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(UniformByteSize),
                    "A uniform buffer slot must be a non-zero multiple of 16 bytes.");
            }
        }
        else if (UniformByteSize != 0)
        {
            throw new ArgumentException(
                "Only a uniform buffer slot carries a byte size.",
                nameof(UniformByteSize));
        }
        if (Visibility == 0)
        {
            throw new ArgumentException(
                "A binding slot must be visible to at least one stage.",
                nameof(Visibility));
        }
    }
}

/// <summary>Reflected SceneParameters binding layout.</summary>
public readonly record struct SilkBindingLayoutDescriptor(
    uint Set,
    uint Binding,
    uint UniformByteSize,
    SilkShaderStageVisibility Visibility)
{
    /// <summary>
    /// Gets the material slots, empty for the checked SceneParameters layout.
    /// </summary>
    /// <remarks>
    /// Additive on purpose. The renderer-neutral device interface is implemented by
    /// three backends and several test doubles, so widening the layout through this
    /// descriptor keeps every existing implementation and caller compiling unchanged
    /// while letting a material layout be described at all, which the single
    /// SceneParameters slot could not express.
    /// </remarks>
    public IReadOnlyList<SilkBindingSlot> MaterialSlots { get; init; } = [];

    /// <summary>Creates the checked mesh SceneParameters layout.</summary>
    public static SilkBindingLayoutDescriptor SceneParameters => new(
        0,
        0,
        SilkCheckedShaderAssets.SceneParameters.ByteSize,
        SilkShaderStageVisibility.Vertex | SilkShaderStageVisibility.Fragment);

    /// <summary>Creates a material layout from its slots.</summary>
    public static SilkBindingLayoutDescriptor ForMaterial(
        IReadOnlyList<SilkBindingSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new ArgumentException(
                "A material binding layout requires at least one slot.",
                nameof(slots));
        }
        // Slot 0 stays the SceneParameters uniform so a material pipeline keeps the
        // same scene constants at the same place as every existing pipeline.
        return SceneParameters with { MaterialSlots = slots };
    }

    /// <summary>Validates the layout.</summary>
    public void Validate()
    {
        if (Set != 0 || Binding != 0)
        {
            throw new ArgumentException("SceneParameters must use set 0, binding 0.");
        }
        if (UniformByteSize != 80)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UniformByteSize),
                "SceneParameters must contain exactly 80 bytes.");
        }
        if (Visibility !=
            (SilkShaderStageVisibility.Vertex | SilkShaderStageVisibility.Fragment))
        {
            throw new ArgumentException(
                "SceneParameters must be visible to vertex and fragment stages.",
                nameof(Visibility));
        }
        IReadOnlyList<SilkBindingSlot> slots = MaterialSlots ?? [];
        for (int index = 0; index < slots.Count; index++)
        {
            SilkBindingSlot slot = slots[index];
            slot.Validate();
            if (slot.Set != 0)
            {
                // Every backend binds exactly one set today: Vulkan binds set 0, and
                // D3D12 and Metal have no set concept at all. Accepting a second set
                // would describe a binding no backend could ever reach.
                throw new ArgumentException(
                    "A material slot must use set 0, the only set the backends bind.");
            }
            if (slot.Set == 0 && slot.Binding == 0)
            {
                throw new ArgumentException(
                    "A material slot cannot occupy set 0, binding 0, which is SceneParameters.");
            }
            for (int other = 0; other < index; other++)
            {
                if (slots[other].Set == slot.Set && slots[other].Binding == slot.Binding)
                {
                    throw new ArgumentException(
                        $"Material slots collide at set {slot.Set}, binding {slot.Binding}.");
                }
            }
        }
    }

    /// <summary>
    /// Requires that a material slot of <paramref name="kind"/> exists at the given
    /// set and binding, and returns its index within
    /// <see cref="MaterialSlots"/>.
    /// </summary>
    /// <remarks>
    /// Every backend binds resources through this one check, so a slot mismatch fails
    /// identically everywhere instead of surfacing as a different backend-specific
    /// validation error, or worse, as a silently unbound resource.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No slot of that kind is declared at that set and binding.
    /// </exception>
    public int RequireMaterialSlot(uint set, uint binding, SilkBindingKind kind)
    {
        IReadOnlyList<SilkBindingSlot> slots = MaterialSlots ?? [];
        for (int index = 0; index < slots.Count; index++)
        {
            SilkBindingSlot slot = slots[index];
            if (slot.Set == set && slot.Binding == binding)
            {
                if (slot.Kind != kind)
                {
                    throw new InvalidOperationException(
                        $"Set {set}, binding {binding} is a {slot.Kind} slot, not a {kind} slot.");
                }
                return index;
            }
        }
        throw new InvalidOperationException(
            $"The bound pipeline declares no material slot at set {set}, binding {binding}.");
    }
}

/// <summary>Backend resource binding layout.</summary>
public interface ISilkGraphicsBindingLayout : IDisposable
{
    /// <summary>Gets the reflected descriptor.</summary>
    SilkBindingLayoutDescriptor Descriptor { get; }
}

/// <summary>Describes linked checked shader modules.</summary>
public readonly record struct SilkShaderProgramDescriptor(
    ISilkGraphicsShaderModule VertexShader,
    ISilkGraphicsShaderModule FragmentShader,
    ISilkGraphicsBindingLayout BindingLayout);

/// <summary>Linked shader program.</summary>
public interface ISilkGraphicsShaderProgram : IDisposable
{
    /// <summary>Gets the binding layout.</summary>
    ISilkGraphicsBindingLayout BindingLayout { get; }
}

/// <summary>Vertex semantics supported by the checked mesh shader.</summary>
public enum SilkVertexSemantic
{
    /// <summary>Object-space position.</summary>
    Position,

    /// <summary>Object-space normal.</summary>
    Normal
}

/// <summary>Vertex element formats.</summary>
public enum SilkVertexFormat
{
    /// <summary>Three contiguous 32-bit floating-point values.</summary>
    Float3
}

/// <summary>Describes one vertex attribute.</summary>
public readonly record struct SilkVertexAttributeDescriptor(
    SilkVertexSemantic Semantic,
    uint Location,
    uint Offset,
    SilkVertexFormat Format);

/// <summary>Describes an interleaved vertex buffer.</summary>
public readonly record struct SilkVertexLayoutDescriptor(
    uint Stride,
    IReadOnlyList<SilkVertexAttributeDescriptor> Attributes)
{
    /// <summary>Gets the checked POSITION/NORMAL layout.</summary>
    public static SilkVertexLayoutDescriptor PositionNormal => new(
        24,
        [
            new(SilkVertexSemantic.Position, 0, 0, SilkVertexFormat.Float3),
            new(SilkVertexSemantic.Normal, 1, 12, SilkVertexFormat.Float3)
        ]);

    /// <summary>Validates the checked mesh vertex layout.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Attributes);
        if (Stride != 24 || Attributes.Count != 2 ||
            Attributes[0] !=
                new SilkVertexAttributeDescriptor(
                    SilkVertexSemantic.Position,
                    0,
                    0,
                    SilkVertexFormat.Float3) ||
            Attributes[1] !=
                new SilkVertexAttributeDescriptor(
                    SilkVertexSemantic.Normal,
                    1,
                    12,
                    SilkVertexFormat.Float3))
        {
            throw new ArgumentException(
                "The checked mesh shader requires float3 POSITION at 0 and float3 NORMAL at 12.",
                nameof(Attributes));
        }
    }
}

/// <summary>Describes a color/depth graphics pipeline.</summary>
public readonly record struct SilkGraphicsPipelineDescriptor(
    ISilkGraphicsShaderProgram Program,
    SilkVertexLayoutDescriptor VertexLayout,
    SilkTextureFormat ColorFormat,
    SilkTextureFormat DepthFormat)
{
    /// <summary>Validates formats and vertex input.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Program);
        VertexLayout.Validate();
        if (ColorFormat != SilkTextureFormat.Rgba8Unorm)
        {
            throw new ArgumentException("The color format must be Rgba8Unorm.");
        }
        if (DepthFormat != SilkTextureFormat.D32Float)
        {
            throw new ArgumentException("The depth format must be D32Float.");
        }
    }
}

/// <summary>Backend graphics pipeline.</summary>
public interface ISilkGraphicsPipeline : IDisposable
{
    /// <summary>Gets the pipeline descriptor.</summary>
    SilkGraphicsPipelineDescriptor Descriptor { get; }
}

/// <summary>Offscreen color and depth attachments.</summary>
public readonly record struct SilkRenderingDescriptor(
    ISilkGraphicsTexture ColorAttachment,
    ISilkGraphicsTexture DepthAttachment);

/// <summary>Floating-point viewport.</summary>
public readonly record struct SilkViewport(
    float X,
    float Y,
    float Width,
    float Height,
    float MinDepth = 0,
    float MaxDepth = 1)
{
    /// <summary>Validates finite positive dimensions and normalized depth.</summary>
    public void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) ||
            !float.IsFinite(Width) || !float.IsFinite(Height) ||
            Width <= 0 || Height <= 0 ||
            !float.IsFinite(MinDepth) || !float.IsFinite(MaxDepth) ||
            MinDepth < 0 || MaxDepth > 1 || MinDepth > MaxDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }
    }
}

/// <summary>Integer pixel scissor rectangle with a top-left-origin pixel coordinate.</summary>
public readonly record struct SilkScissor(int X, int Y, uint Width, uint Height)
{
    /// <summary>Validates positive dimensions and non-negative origin.</summary>
    public void Validate()
    {
        if (X < 0 || Y < 0 || Width == 0 || Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }
    }
}

/// <summary>Validated reflected SceneParameters ABI.</summary>
public readonly record struct SilkSceneParametersReflection(
    bool RowMajor,
    uint MatrixOffset,
    uint MatrixByteSize,
    uint TintOffset,
    uint TintByteSize,
    uint ByteSize);

/// <summary>Validated reflected PickParameters ABI and primitive-ID input.</summary>
public readonly record struct SilkPickParametersReflection(
    uint Set,
    uint Binding,
    uint TokenOffset,
    uint TokenByteSize,
    uint ByteSize,
    bool UsesPrimitiveId);

/// <summary>NativeAOT-safe loader for checked shader artifacts embedded in production assemblies.</summary>
public static partial class SilkCheckedShaderAssets
{
    private const string Prefix = "OpenUsd.Rendering.Silk.Shaders.";
    private static readonly Lazy<SilkSceneParametersReflection> ReflectionValue =
        new(LoadAndValidateReflection);
    private static readonly Lazy<SilkComputeReflection> ComputeReflectionValue =
        new(LoadAndValidateComputeReflection);
    private static readonly Lazy<SilkPickParametersReflection> PickReflectionValue =
        new(LoadAndValidatePickReflection);

    /// <summary>Gets the validated reflected constant-buffer ABI.</summary>
    public static SilkSceneParametersReflection SceneParameters => ReflectionValue.Value;

    /// <summary>Gets the validated checked compute ABI.</summary>
    public static SilkComputeReflection Compute => ComputeReflectionValue.Value;

    /// <summary>Gets the validated checked pick ABI.</summary>
    public static SilkPickParametersReflection PickParameters =>
        PickReflectionValue.Value;

    /// <summary>Gets whether a validated pinned Metal library pair is deployed.</summary>
    public static bool HasPinnedMetalLibrary => TryLoadPinnedMetalLibrary(out _);

    /// <summary>Loads the checked mesh vertex module.</summary>
    public static SilkShaderModuleDescriptor LoadMeshVertex(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule("mesh", SilkShaderStage.Vertex, format, "vertexMain");

    /// <summary>Loads the checked mesh fragment module.</summary>
    public static SilkShaderModuleDescriptor LoadMeshFragment(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule("mesh", SilkShaderStage.Fragment, format, "fragmentMain");

    /// <summary>Loads the checked pick vertex module.</summary>
    public static SilkShaderModuleDescriptor LoadPickVertex(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "pick",
            SilkShaderStage.Vertex,
            format,
            "pickVertexMain");

    /// <summary>Loads the checked pick fragment module.</summary>
    public static SilkShaderModuleDescriptor LoadPickFragment(
        SilkShaderBinaryFormat format) =>
        LoadGraphicsModule(
            "pick",
            SilkShaderStage.Fragment,
            format,
            "pickFragmentMain");

    /// <summary>Loads the checked fill compute module.</summary>
    public static SilkShaderModuleDescriptor LoadComputeFill(
        SilkShaderBinaryFormat format) =>
        LoadComputeModule("fill", "fillMain", format);

    /// <summary>Loads the checked scale compute module.</summary>
    public static SilkShaderModuleDescriptor LoadComputeScale(
        SilkShaderBinaryFormat format) =>
        LoadComputeModule("scale", "scaleMain", format);

    private static SilkShaderModuleDescriptor LoadGraphicsModule(
        string program,
        SilkShaderStage stage,
        SilkShaderBinaryFormat format,
        string entryPoint)
    {
        string stageName = stage == SilkShaderStage.Vertex ? "vertex" : "fragment";
        byte[] code = format switch
        {
            SilkShaderBinaryFormat.Dxil => LoadEmbedded($"{program}.{stageName}.dxil"),
            SilkShaderBinaryFormat.SpirV => LoadEmbedded($"{program}.{stageName}.spv"),
            SilkShaderBinaryFormat.MetalLibrary => LoadPinnedMetalLibrary(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        string nativeEntryPoint = format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : entryPoint;
        return new SilkShaderModuleDescriptor(stage, format, nativeEntryPoint, code);
    }

    private static SilkShaderModuleDescriptor LoadComputeModule(
        string operation,
        string entryPoint,
        SilkShaderBinaryFormat format)
    {
        byte[] code = format switch
        {
            SilkShaderBinaryFormat.Dxil => LoadEmbedded($"compute.{operation}.dxil"),
            SilkShaderBinaryFormat.SpirV => LoadEmbedded($"compute.{operation}.spv"),
            SilkShaderBinaryFormat.MetalLibrary => LoadPinnedMetalLibrary(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        string nativeEntryPoint = format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : entryPoint;
        return new SilkShaderModuleDescriptor(
            SilkShaderStage.Compute,
            format,
            nativeEntryPoint,
            code);
    }

    private static SilkSceneParametersReflection LoadAndValidateReflection()
    {
        byte[] vertex = LoadEmbedded("mesh.vertex.reflection.json");
        byte[] fragment = LoadEmbedded("mesh.fragment.reflection.json");
        byte[] manifest = LoadEmbedded("manifest.json");
        if (!Encoding.UTF8.GetString(manifest).Contains(
            "-matrix-layout-row-major",
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("Checked shaders are not row-major.");
        }
        SilkSceneParametersReflection vertexLayout = ParseReflection(vertex);
        SilkSceneParametersReflection fragmentLayout = ParseReflection(fragment);
        if (vertexLayout != fragmentLayout ||
            vertexLayout != new SilkSceneParametersReflection(
                true,
                0,
                64,
                64,
                16,
                80))
        {
            throw new InvalidDataException(
                "Checked shader reflection does not match the SceneParameters ABI.");
        }
        return vertexLayout;
    }

    private static SilkPickParametersReflection LoadAndValidatePickReflection()
    {
        SilkSceneParametersReflection vertexLayout = ParseReflection(
            LoadEmbedded("pick.vertex.reflection.json"));
        if (vertexLayout != SceneParameters)
        {
            throw new InvalidDataException(
                "Checked pick vertex reflection does not match SceneParameters.");
        }

        using JsonDocument document = JsonDocument.Parse(
            LoadEmbedded("pick.fragment.reflection.json"));
        JsonElement root = document.RootElement;
        JsonElement resource = root.GetProperty("resources")[0];
        if (resource.GetProperty("name").GetString() != "PickParameters")
        {
            throw new InvalidDataException(
                "Checked pick reflection is missing PickParameters.");
        }

        JsonElement bindings = resource.GetProperty("bindings");
        JsonElement d3d = bindings.GetProperty("d3d");
        JsonElement vulkan = bindings.GetProperty("vulkan");
        if (d3d.GetProperty("registerClass").GetString() != "b" ||
            d3d.GetProperty("register").GetUInt32() != 1 ||
            d3d.GetProperty("space").GetUInt32() != 0 ||
            vulkan.GetProperty("set").GetUInt32() != 0 ||
            vulkan.GetProperty("binding").GetUInt32() != 1)
        {
            throw new InvalidDataException(
                "Checked PickParameters must use b1, space 0 and set 0, binding 1.");
        }

        ValidatePickParameterShape(resource.GetProperty("shape"));
        ValidatePickParameterShape(resource.GetProperty("vulkanLayout"));
        bool usesPrimitiveId = false;
        foreach (JsonElement input in root.GetProperty("stageInputs").EnumerateArray())
        {
            JsonElement semantic = input.GetProperty("semantic");
            if (semantic.GetProperty("name").GetString() == "SV_PRIMITIVEID")
            {
                JsonElement type = input.GetProperty("type");
                usesPrimitiveId =
                    semantic.GetProperty("systemValue").GetBoolean() &&
                    input.GetProperty("location").ValueKind == JsonValueKind.Null &&
                    type.GetProperty("kind").GetString() == "scalar" &&
                    type.GetProperty("scalarType").GetString() == "uint32";
            }
        }
        if (!usesPrimitiveId)
        {
            throw new InvalidDataException(
                "Checked pick fragment reflection must consume SV_PrimitiveID.");
        }

        return new SilkPickParametersReflection(
            0,
            1,
            0,
            16,
            16,
            true);
    }

    private static SilkComputeReflection LoadAndValidateComputeReflection()
    {
        SilkComputeReflection fill = ParseComputeReflection(
            LoadEmbedded("compute.fill.reflection.json"));
        SilkComputeReflection scale = ParseComputeReflection(
            LoadEmbedded("compute.scale.reflection.json"));
        var expected = new SilkComputeReflection(
            0,
            0,
            16,
            0,
            1,
            8,
            16,
            64,
            1,
            1);
        if (fill != scale || fill != expected)
        {
            throw new InvalidDataException(
                "Checked compute reflection does not match the outputValues and " +
                "ComputeParameters ABI.");
        }
        return fill;
    }

    private static SilkSceneParametersReflection ParseReflection(ReadOnlySpan<byte> json)
    {
        using JsonDocument document = JsonDocument.Parse(json.ToArray());
        JsonElement shape = document.RootElement
            .GetProperty("resources")[0]
            .GetProperty("shape");
        JsonElement fields = shape.GetProperty("elementType").GetProperty("fields");
        JsonElement matrixLayout = fields[0].GetProperty("layout");
        JsonElement tintLayout = fields[1].GetProperty("layout");
        return new SilkSceneParametersReflection(
            true,
            matrixLayout.GetProperty("offset").GetUInt32(),
            matrixLayout.GetProperty("size").GetUInt32(),
            tintLayout.GetProperty("offset").GetUInt32(),
            tintLayout.GetProperty("size").GetUInt32(),
            shape.GetProperty("size").GetUInt32());
    }

    private static SilkComputeReflection ParseComputeReflection(ReadOnlySpan<byte> json)
    {
        using JsonDocument document = JsonDocument.Parse(json.ToArray());
        JsonElement root = document.RootElement;
        JsonElement threadGroup = root.GetProperty("entryPoint").GetProperty("threadGroupSize");
        JsonElement resources = root.GetProperty("resources");
        JsonElement storage = resources[0];
        JsonElement uniform = resources[1];
        if (storage.GetProperty("name").GetString() != "outputValues" ||
            uniform.GetProperty("name").GetString() != "ComputeParameters")
        {
            throw new InvalidDataException("Checked compute resources are out of order.");
        }
        JsonElement storageD3D = storage.GetProperty("bindings").GetProperty("d3d");
        JsonElement uniformD3D = uniform.GetProperty("bindings").GetProperty("d3d");
        JsonElement storageBindings = storage.GetProperty("bindings").GetProperty("vulkan");
        JsonElement uniformBindings = uniform.GetProperty("bindings").GetProperty("vulkan");
        JsonElement storageShape = storage.GetProperty("shape");
        JsonElement storageVulkanShape = storage.GetProperty("vulkanLayout");
        JsonElement uniformShape = uniform.GetProperty("shape");
        JsonElement uniformVulkanShape = uniform.GetProperty("vulkanLayout");
        ValidateD3DBinding(storageD3D, "u", 0);
        ValidateD3DBinding(uniformD3D, "b", 1);
        ValidateStorageShape(storageShape);
        ValidateStorageShape(storageVulkanShape);
        ValidateParameterShape(uniformShape, 8);
        ValidateParameterShape(uniformVulkanShape, 16);
        return new SilkComputeReflection(
            storageBindings.GetProperty("set").GetUInt32(),
            storageBindings.GetProperty("binding").GetUInt32(),
            storageShape.GetProperty("elementStride").GetUInt32(),
            uniformBindings.GetProperty("set").GetUInt32(),
            uniformBindings.GetProperty("binding").GetUInt32(),
            uniformShape.GetProperty("size").GetUInt32(),
            uniformVulkanShape.GetProperty("size").GetUInt32(),
            threadGroup[0].GetUInt32(),
            threadGroup[1].GetUInt32(),
            threadGroup[2].GetUInt32());
    }

    private static SilkComputeReflection ParseComputeReflectionForTesting(byte[] json) =>
        ParseComputeReflection(json);

    private static void ValidateD3DBinding(
        JsonElement binding,
        string registerClass,
        uint register)
    {
        if (binding.GetProperty("registerClass").GetString() != registerClass ||
            binding.GetProperty("register").GetUInt32() != register ||
            binding.GetProperty("space").GetUInt32() != 0)
        {
            throw new InvalidDataException(
                $"Checked compute D3D binding must be {registerClass}{register}, space 0.");
        }
    }

    private static void ValidateStorageShape(JsonElement shape)
    {
        JsonElement elementType = shape.GetProperty("elementType");
        JsonElement scalar = elementType.GetProperty("elementType");
        if (shape.GetProperty("kind").GetString() != "structuredBuffer" ||
            shape.GetProperty("access").GetString() != "readWrite" ||
            shape.GetProperty("elementStride").GetUInt32() != 16 ||
            elementType.GetProperty("kind").GetString() != "vector" ||
            elementType.GetProperty("elementCount").GetUInt32() != 4 ||
            scalar.GetProperty("kind").GetString() != "scalar" ||
            scalar.GetProperty("scalarType").GetString() != "float32")
        {
            throw new InvalidDataException(
                "Checked outputValues must be a read-write float4 structured buffer with stride 16.");
        }
    }

    private static void ValidateParameterShape(JsonElement shape, uint size)
    {
        JsonElement fields = shape.GetProperty("elementType").GetProperty("fields");
        if (shape.GetProperty("kind").GetString() != "constantBuffer" ||
            shape.GetProperty("access").GetString() != "constant" ||
            shape.GetProperty("size").GetUInt32() != size ||
            fields.GetArrayLength() != 2 ||
            !HasScalarField(fields[0], "elementCount", "uint32", 0) ||
            !HasScalarField(fields[1], "scale", "float32", 4))
        {
            throw new InvalidDataException(
                $"Checked ComputeParameters must contain uint elementCount at 0 and " +
                $"float scale at 4 in a {size}-byte constant buffer.");
        }
    }

    private static void ValidatePickParameterShape(JsonElement shape)
    {
        JsonElement fields = shape.GetProperty("elementType").GetProperty("fields");
        JsonElement field = fields[0];
        JsonElement type = field.GetProperty("type");
        JsonElement elementType = type.GetProperty("elementType");
        JsonElement layout = field.GetProperty("layout");
        if (shape.GetProperty("kind").GetString() != "constantBuffer" ||
            shape.GetProperty("access").GetString() != "constant" ||
            shape.GetProperty("size").GetUInt32() != 16 ||
            fields.GetArrayLength() != 1 ||
            field.GetProperty("name").GetString() != "pickToken" ||
            type.GetProperty("kind").GetString() != "vector" ||
            type.GetProperty("elementCount").GetUInt32() != 4 ||
            elementType.GetProperty("kind").GetString() != "scalar" ||
            elementType.GetProperty("scalarType").GetString() != "uint32" ||
            layout.GetProperty("offset").GetUInt32() != 0 ||
            layout.GetProperty("size").GetUInt32() != 16)
        {
            throw new InvalidDataException(
                "Checked PickParameters must contain one 16-byte uint4 at offset zero.");
        }
    }

    private static bool HasScalarField(
        JsonElement field,
        string name,
        string scalarType,
        uint offset)
    {
        JsonElement type = field.GetProperty("type");
        JsonElement layout = field.GetProperty("layout");
        return field.GetProperty("name").GetString() == name &&
            type.GetProperty("kind").GetString() == "scalar" &&
            type.GetProperty("scalarType").GetString() == scalarType &&
            layout.GetProperty("offset").GetUInt32() == offset &&
            layout.GetProperty("size").GetUInt32() == 4;
    }

    private static byte[] LoadEmbedded(string name)
    {
        Assembly assembly = typeof(SilkCheckedShaderAssets).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(Prefix + name) ??
            throw new InvalidDataException($"Missing embedded checked shader asset '{name}'.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
