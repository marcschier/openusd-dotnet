// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkVolumeCameraPass : IDisposable
{
    private const int FieldStride = 128;
    private const int HeaderSize = checked((int)SilkBindingLayoutDescriptor.VolumeCameraParameterByteSize);
    private readonly ISilkGraphicsDevice _device;
    private readonly SilkShaderBinaryFormat _shaderFormat;
    private readonly SilkSceneGpuResources _gpuResources;
    private readonly Action<Exception>? _reportRetirementFailure;
    private ResourceSet? _published;
    private ResourceSet? _retired;
    private bool _disposed;

    internal SilkVolumeCameraPass(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat,
        SilkSceneGpuResources gpuResources,
        Action<Exception>? reportRetirementFailure = null)
    {
        _device = device;
        _shaderFormat = shaderFormat;
        _gpuResources = gpuResources;
        _reportRetirementFailure = reportRetirementFailure;
    }

    internal Prepared Prepare(
        SilkSceneState scene,
        SilkPhysicsTransformOverrides? overrides,
        uint width,
        uint height,
        bool clipSpaceYPointsDown,
        IReadOnlyList<(SilkMeshData Mesh, SilkMaterialData Material)> fields)
    {
        ValidatePreparation(scene, fields);
        return PrepareValidated(scene, overrides, width, height, clipSpaceYPointsDown, fields);
    }

    internal PreparedPage PreparePage(
        SilkSceneState scene,
        SilkPhysicsTransformOverrides? overrides,
        uint width,
        uint height,
        bool clipSpaceYPointsDown,
        IReadOnlyList<(SilkMeshData Mesh, SilkMaterialData Material)> fields,
        bool requiresRenderCandidate)
    {
        ValidatePreparation(scene, fields);
        return !requiresRenderCandidate && fields.Count == 0
            ? new PreparedPage(this, null)
            : new PreparedPage(this, PrepareValidated(scene, overrides, width, height, clipSpaceYPointsDown, fields));
    }

    private void ValidatePreparation(
        SilkSceneState scene,
        IReadOnlyList<(SilkMeshData Mesh, SilkMaterialData Material)> fields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(fields);
        DrainRetired();
    }

    private Prepared PrepareValidated(
        SilkSceneState scene,
        SilkPhysicsTransformOverrides? overrides,
        uint width,
        uint height,
        bool clipSpaceYPointsDown,
        IReadOnlyList<(SilkMeshData Mesh, SilkMaterialData Material)> fields)
    {
        if (fields.Count == 0)
        {
            return new Prepared(this, null);
        }

        _ = SilkVolumeCameraValidation.ValidateWorkload(fields.Count, width, height);
        for (int i = 0; i < fields.Count; i++)
        {
            SilkVolumeCameraValidation.ValidateMaterialChannels(fields[i].Material);
        }
        bool supportsAtlas = SupportsAtlas();
        var fieldSources = new FieldSource[fields.Count];
        var atlasCandidates = new List<int>(fields.Count);
        for (int i = 0; i < fields.Count; i++)
        {
            (SilkMeshData mesh, SilkMaterialData material) = fields[i];
            ReadOnlySpan<float> density = material.GetScalar(SilkMaterialParameter.VolumeDensity);
            SilkVolumeCameraValidation.ValidateDensityScalar(
                density.IsEmpty ? 0 : density[0], material.Path);
            SilkMaterialTexture? texture = material.GetTexture(SilkMaterialParameter.VolumeDensity);
            SilkVolumeTextureExtent extent = default;
            if (supportsAtlas && texture is { } densityTexture)
            {
                extent = SilkVolumeTextureExtent.Parse(densityTexture.UvPrimvar);
                atlasCandidates.Add(i);
            }
            fieldSources[i] = new FieldSource(mesh, material, texture, extent);
        }

        uint[] longestAxes = fieldSources.Select(static field =>
            Math.Max(1u, Math.Max(field.Extent.Width,
                Math.Max(field.Extent.Height, field.Extent.Depth)))).ToArray();
        SilkVolumeCameraWorkload workload =
            SilkVolumeCameraValidation.ValidateWorkload(longestAxes, width, height);
        foreach (FieldSource field in fieldSources)
        {
            if (field.Extent.Width == 0)
            {
                ValidateFieldDensity(field, new SilkVolumeDensitySamples(1, 1, false),
                    workload.MaximumStepsPerSegment);
            }
        }
        double maximumEyeCoordinate = SilkVolumeCameraValidation.ValidateProjection(
            scene.Frame.Projection.Span, clipSpaceYPointsDown, "frame camera");
        foreach (FieldSource field in fieldSources)
        {
            ReadOnlySpan<double> transform = overrides is null
                ? default : overrides.GetTransform(field.Mesh.Id);
            if (transform.IsEmpty)
            {
                transform = field.Mesh.Transform.Span;
            }
            string description = $"'{field.Mesh.Path}' instance {field.Mesh.InstanceIndex}";
            double[] eyeToObject = SilkVolumeCameraValidation.InvertObjectToEye(
                transform, scene.Frame.View.Span, description);
            SilkVolumeCameraValidation.ValidateRayOrigin(
                eyeToObject, maximumEyeCoordinate, description);
        }

        object[] atlasKey = atlasCandidates
            .Select(index => (object)fieldSources[index].Texture!)
            .ToArray();
        bool reuseAtlas = _published is { AtlasTexture: not null } previous &&
            SameAtlasKey(previous.AtlasKey, atlasKey);
        ResourceSet candidate = CreateResources(
            scene,
            overrides,
            width,
            height,
            clipSpaceYPointsDown,
            fieldSources,
            atlasCandidates,
            atlasKey,
            workload.MaximumStepsPerSegment,
            reuseAtlas ? _published : null);
        return new Prepared(this, candidate);
    }

    private ResourceSet CreateResources(
        SilkSceneState scene,
        SilkPhysicsTransformOverrides? overrides,
        uint width,
        uint height,
        bool clipSpaceYPointsDown,
        FieldSource[] fields,
        List<int> atlasCandidates,
        object[] atlasKey,
        int maximumStepsPerSegment,
        ResourceSet? reusable)
    {
        ResourceSet? resources = null;
        ISilkGraphicsTexture? ownedAtlasCandidate = null;
        IDisposable? ownedAtlasAdmission = null;
        bool ownsAtlasCandidate = false;
        try
        {
            ISilkGraphicsTexture? atlasTexture = null;
            SilkVolumeAtlasRegion[] regions = [];
            uint atlasWidth = 0;
            uint atlasHeight = 0;
            uint atlasDepth = 0;
            SilkVolumeDensitySamples[] densitySamples = [];
            IDisposable? atlasAdmission = null;
            bool ownsAtlas = false;
            if (reusable is not null)
            {
                atlasTexture = reusable.AtlasTexture;
                regions = reusable.AtlasRegions;
                atlasWidth = reusable.AtlasWidth;
                atlasHeight = reusable.AtlasHeight;
                atlasDepth = reusable.AtlasDepth;
                densitySamples = reusable.AtlasDensitySamples;
                if (densitySamples.Length != atlasCandidates.Count)
                {
                    throw new InvalidDataException(
                        "The retained density atlas has no matching density-sample admission metadata.");
                }
                for (int i = 0; i < atlasCandidates.Count; i++)
                {
                    ValidateFieldDensity(fields[atlasCandidates[i]], densitySamples[i], maximumStepsPerSegment);
                }
            }
            else if (atlasCandidates.Count != 0)
            {
                (regions, atlasTexture, atlasWidth, atlasHeight, atlasDepth, atlasAdmission, densitySamples) =
                    CreateAtlas(fields, atlasCandidates, maximumStepsPerSegment);
                ownsAtlas = true;
            }
            if (ownsAtlas)
            {
                ownedAtlasCandidate = atlasTexture;
                ownsAtlasCandidate = true;
                ownedAtlasAdmission = atlasAdmission;
            }

            using IDisposable? metadataReservation = _gpuResources.ReserveVolumeCameraCpu(
                checked((ulong)HeaderSize + (ulong)fields.Length * FieldStride));
            var parameterBytes = new byte[HeaderSize];
            var fieldBytes = new byte[checked(fields.Length * FieldStride)];
            WriteHeader(
                parameterBytes,
                checked((uint)fields.Length),
                width,
                height,
                clipSpaceYPointsDown,
                atlasWidth,
                atlasHeight,
                atlasDepth);
            WriteFields(
                fieldBytes,
                scene,
                overrides,
                fields,
                atlasCandidates,
                regions,
                atlasTexture is not null);

            ISilkGraphicsBuffer? parameterBuffer = null;
            ISilkGraphicsBuffer? fieldBuffer = null;
            ISilkGraphicsBuffer? vertexBuffer = null;
            ISilkGraphicsBuffer? indexBuffer = null;
            ISilkGraphicsTexture? depthTarget = null;
            ISilkGraphicsSampler? sampler = null;
            try
            {
                parameterBuffer = _device.CreateBuffer(
                    HeaderSize,
                    SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
                parameterBuffer.Write(parameterBytes);
                fieldBuffer = _device.CreateBuffer(
                    checked((nuint)fieldBytes.Length),
                    SilkBufferUsage.Storage | SilkBufferUsage.Upload);
                fieldBuffer.Write(fieldBytes);

                Span<byte> vertices = stackalloc byte[3 * 6 * sizeof(float)];
                WriteFullscreenVertices(vertices);
                vertexBuffer = _device.CreateBuffer(
                    checked((nuint)vertices.Length),
                    SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
                vertexBuffer.Write(vertices);
                Span<byte> indices = stackalloc byte[3 * sizeof(uint)];
                BinaryPrimitives.WriteUInt32LittleEndian(indices, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(indices[4..], 1);
                BinaryPrimitives.WriteUInt32LittleEndian(indices[8..], 2);
                indexBuffer = _device.CreateBuffer(
                    checked((nuint)indices.Length),
                    SilkBufferUsage.Index | SilkBufferUsage.Upload);
                indexBuffer.Write(indices);
                depthTarget = _device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(width, height));
                if (atlasTexture is not null)
                {
                    sampler = _device.CreateSampler(SilkSamplerDescriptor.LinearClamp);
                }

                resources = new ResourceSet(
                    _device,
                    _shaderFormat,
                    width,
                    height,
                    parameterBuffer,
                    fieldBuffer,
                    vertexBuffer,
                    indexBuffer,
                    depthTarget,
                    atlasTexture,
                    ownsAtlas,
                    atlasKey,
                    regions,
                    atlasWidth,
                    atlasHeight,
                    atlasDepth,
                    sampler,
                    atlasAdmission,
                    densitySamples);
                parameterBuffer = null;
                fieldBuffer = null;
                vertexBuffer = null;
                indexBuffer = null;
                depthTarget = null;
                sampler = null;
                atlasTexture = null;
                ownedAtlasAdmission = null;
                ownsAtlas = false;
                ownsAtlasCandidate = false;
                return resources;
            }
            finally
            {
                parameterBuffer?.Dispose();
                fieldBuffer?.Dispose();
                vertexBuffer?.Dispose();
                indexBuffer?.Dispose();
                depthTarget?.Dispose();
                sampler?.Dispose();
                if (ownsAtlas)
                {
                    atlasTexture?.Dispose();
                }
            }
        }
        catch
        {
            if (resources is not null)
            {
                resources.Dispose();
            }
            else if (ownsAtlasCandidate)
            {
                ownedAtlasCandidate?.Dispose();
                ownedAtlasAdmission?.Dispose();
            }
            throw;
        }
    }

    private (SilkVolumeAtlasRegion[] Regions, ISilkGraphicsTexture Texture, uint Width, uint Height,
        uint Depth, IDisposable Admission, SilkVolumeDensitySamples[] DensitySamples) CreateAtlas(
        FieldSource[] fields,
        List<int> atlasCandidates,
        int maximumStepsPerSegment)
    {
        var descriptions = new SilkVolumeAtlasInput[atlasCandidates.Count];
        for (int i = 0; i < atlasCandidates.Count; i++)
        {
            descriptions[i] = new SilkVolumeAtlasInput(fields[atlasCandidates[i]].Extent, default);
        }

        SilkVolumeAtlasLayout layout = SilkVolumeAtlas.Plan(descriptions);
        using IDisposable? atlasReservation =
            _gpuResources.ReserveVolumeCameraCpu(checked((ulong)layout.ByteCount));
        IDisposable? gpuAdmission = null;
        var sources = new byte[atlasCandidates.Count][];
        var reservations = new List<IDisposable>(atlasCandidates.Count);
        SilkVolumeAtlas? atlas = null;
        ISilkGraphicsTexture? texture = null;
        try
        {
            gpuAdmission = _gpuResources.ReserveVolumeCameraAtlas(
                layout.Width,
                layout.Height,
                layout.Depth,
                atlasCandidates.Count);
            var densitySamples = new SilkVolumeDensitySamples[atlasCandidates.Count];
            for (int i = 0; i < atlasCandidates.Count; i++)
            {
                SilkMaterialTexture fieldTexture = fields[atlasCandidates[i]].Texture!;
                SilkVolumeTextureExtent extent = fields[atlasCandidates[i]].Extent;
                ulong byteCount = checked(
                    (ulong)extent.Width * extent.Height * extent.Depth * sizeof(float));
                IDisposable? reservation = _gpuResources.ReserveVolumeCameraCpu(byteCount);
                if (reservation is not null)
                {
                    reservations.Add(reservation);
                }
                sources[i] = SilkSceneGpuResources.CopyVolumeCameraField(
                    fieldTexture, extent, out densitySamples[i]);
                ValidateFieldDensity(fields[atlasCandidates[i]], densitySamples[i], maximumStepsPerSegment);
                descriptions[i] = new SilkVolumeAtlasInput(extent, sources[i]);
            }

            atlas = SilkVolumeAtlas.Pack(descriptions);
            if (_device is not ISilkVolumeTextureGraphicsDevice volumeDevice)
            {
                throw new InvalidOperationException("The selected device no longer supports 3D textures.");
            }

            texture = volumeDevice.CreateTexture3D(
                atlas.Width,
                atlas.Height,
                atlas.Depth,
                SilkTextureFormat.R32Float);
            using (ISilkGraphicsCommandList commands = _device.CreateCommandList())
            {
                if (commands is not ISilkVolumeTextureCommandList volumeCommands)
                {
                    throw new InvalidOperationException(
                        "The selected command list cannot upload a density atlas.");
                }
                volumeCommands.UploadTexture3D(texture, atlas.Pixels.Span);
                using ISilkGraphicsSubmission submission = _device.Submit(commands);
                submission.Wait();
            }

            SilkVolumeAtlasRegion[] regions = atlas.Regions.ToArray();
            ISilkGraphicsTexture resultTexture = texture;
            IDisposable resultAdmission = gpuAdmission ??
                throw new InvalidOperationException("The density-atlas GPU reservation was not acquired.");
            texture = null;
            atlas = null;
            gpuAdmission = null;
            return (regions, resultTexture, layout.Width, layout.Height, layout.Depth, resultAdmission, densitySamples);
        }
        catch (SilkCpuTextureBudgetExceededException exception)
        {
            texture?.Dispose();
            throw new InvalidDataException(
                "Density-volume camera preparation exceeded the bounded CPU allocation budget; " +
                "reduce active volume count or grid extents, or configure a larger CPU texture budget.",
                exception);
        }
        catch (SilkGpuTextureBudgetExceededException exception)
        {
            texture?.Dispose();
            throw new InvalidDataException(
                "Density-volume camera preparation exceeded the bounded GPU texture budget; " +
                "reduce active volume count or grid extents, or configure a larger GPU texture budget.",
                exception);
        }
        catch
        {
            texture?.Dispose();
            throw;
        }
        finally
        {
            gpuAdmission?.Dispose();
            foreach (IDisposable reservation in reservations)
            {
                reservation.Dispose();
            }
        }
    }

    private static void ValidateFieldDensity(
        FieldSource field,
        SilkVolumeDensitySamples samples,
        int maximumStepsPerSegment)
    {
        ReadOnlySpan<float> density = field.Material.GetScalar(SilkMaterialParameter.VolumeDensity);
        string description = field.Texture is null
            ? field.Material.Path
            : $"{field.Texture.Asset} (material {field.Material.Path})";
        SilkVolumeCameraValidation.ValidateDensitySignal(
            samples,
            density.IsEmpty ? 0 : density[0],
            SilkSurfaceUniformWriter.GetOpacity(field.Material),
            maximumStepsPerSegment,
            description);
    }

    private bool SupportsAtlas()
    {
        if (_device is not ISilkVolumeTextureGraphicsDevice)
        {
            return false;
        }
        using ISilkGraphicsCommandList commands = _device.CreateCommandList();
        return commands is ISilkVolumeTextureCommandList;
    }

    private static void WriteHeader(
        Span<byte> destination,
        uint fieldCount,
        uint width,
        uint height,
        bool clipSpaceYPointsDown,
        uint atlasWidth,
        uint atlasHeight,
        uint atlasDepth)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, fieldCount);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], width);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], height);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], clipSpaceYPointsDown ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], atlasWidth);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], atlasHeight);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], atlasDepth);
    }

    private static void WriteFields(
        Span<byte> destination,
        SilkSceneState scene,
        SilkPhysicsTransformOverrides? overrides,
        FieldSource[] fields,
        List<int> atlasCandidates,
        SilkVolumeAtlasRegion[] atlasRegions,
        bool atlasAvailable)
    {
        var regionByField = new Dictionary<int, SilkVolumeAtlasRegion>(atlasCandidates.Count);
        for (int i = 0; i < atlasCandidates.Count; i++)
        {
            regionByField.Add(atlasCandidates[i], atlasRegions[i]);
        }

        for (int index = 0; index < fields.Length; index++)
        {
            FieldSource field = fields[index];
            Span<byte> entry = destination.Slice(index * FieldStride, FieldStride);
            ReadOnlySpan<double> objectToWorld = overrides is null
                ? default
                : overrides.GetTransform(field.Mesh.Id);
            if (objectToWorld.IsEmpty)
            {
                objectToWorld = field.Mesh.Transform.Span;
            }
            double[] eyeToObject = SilkVolumeCameraValidation.InvertObjectToEye(
                objectToWorld,
                scene.Frame.View.Span,
                $"'{field.Mesh.Path}' instance {field.Mesh.InstanceIndex} bound to material '{field.Material.Path}'");
            SilkVolumeCameraValidation.WriteEyeToObject(entry[..64], eyeToObject);

            SilkVolumeAtlasRegion region = default;
            bool hasAtlasField = atlasAvailable && regionByField.TryGetValue(index, out region);
            uint width = hasAtlasField ? region.Extent.Width : 1;
            uint height = hasAtlasField ? region.Extent.Height : 1;
            uint depth = hasAtlasField ? region.Extent.Depth : 1;
            uint maxExtent = hasAtlasField
                ? Math.Max(width, Math.Max(height, depth))
                : 1;
            if (hasAtlasField)
            {
                WriteUInt(entry, 64, region.X);
                WriteUInt(entry, 68, region.Y);
                WriteUInt(entry, 72, region.Z);
                WriteUInt(entry, 76, depth);
            }
            WriteUInt(entry, 80, width);
            WriteUInt(entry, 84, height);
            WriteUInt(entry, 88, hasAtlasField ? 1u : 0u);
            WriteUInt(entry, 92, maxExtent);

            ReadOnlySpan<float> diffuse = field.Material.GetScalar(SilkMaterialParameter.DiffuseColor);
            Vector3 tint = SilkVolumeCameraValidation.GetTint(diffuse);
            float opacity = SilkSurfaceUniformWriter.GetOpacity(field.Material);
            WriteSingle(entry, 96, tint.X);
            WriteSingle(entry, 100, tint.Y);
            WriteSingle(entry, 104, tint.Z);
            WriteSingle(entry, 108, opacity);
            ReadOnlySpan<float> density = field.Material.GetScalar(SilkMaterialParameter.VolumeDensity);
            WriteSingle(entry, 112, density.IsEmpty ? 0 : Math.Max(0, density[0]));
        }
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination[offset..],
            BitConverter.SingleToInt32Bits(value));

    private static void WriteUInt(Span<byte> destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], value);

    private static void WriteFullscreenVertices(Span<byte> destination)
    {
        ReadOnlySpan<float> values =
        [
            -1, -1, 0, 0, 0, 1,
             3, -1, 0, 0, 0, 1,
            -1,  3, 0, 0, 0, 1
        ];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                destination[(i * sizeof(float))..],
                BitConverter.SingleToInt32Bits(values[i]));
        }
    }

    private static bool SameAtlasKey(IReadOnlyList<object> left, object[] right)
    {
        if (left.Count != right.Length)
        {
            return false;
        }
        for (int i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
            {
                return false;
            }
        }
        return true;
    }

    internal static void Record(
        Prepared prepared,
        ISilkGraphicsCommandList commands,
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture opaqueDepth,
        ISilkGraphicsBuffer frameBuffer)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(commands);
        ResourceSet resources = prepared.ResourceData ??
            throw new InvalidOperationException("The prepared volume camera pass is empty.");
        resources.EnsurePipeline(colorTarget.Format);

        commands.ClearDepth(resources.DepthTarget, 1);
        commands.BeginRendering(new SilkRenderingDescriptor(colorTarget, resources.DepthTarget));
        commands.SetViewport(new SilkViewport(0, 0, colorTarget.Width, colorTarget.Height));
        commands.SetScissor(new SilkScissor(0, 0, colorTarget.Width, colorTarget.Height));
        commands.SetGraphicsPipeline(resources.Pipeline!);
        commands.SetVertexBuffer(resources.VertexBuffer);
        commands.SetIndexBuffer(resources.IndexBuffer);
        commands.SetUniformBuffer(0, 0, resources.ParameterBuffer);
        commands.SetStorageBuffer(
            0,
            SilkBindingLayoutDescriptor.VolumeCameraFieldBufferBinding,
            resources.FieldBuffer);
        commands.SetStorageBuffer(
            0,
            SilkBindingLayoutDescriptor.FrameParametersBinding,
            frameBuffer);
        commands.SetTexture(
            0,
            SilkBindingLayoutDescriptor.VolumeOpaqueDepthTextureBinding,
            opaqueDepth);
        if (resources.AtlasTexture is not null)
        {
            commands.SetSampler(0, SilkBindingLayoutDescriptor.VolumeSamplerBinding, resources.Sampler!);
            commands.SetTexture(
                0,
                SilkBindingLayoutDescriptor.VolumeDensityTextureBinding,
                resources.AtlasTexture);
        }
        commands.DrawIndexed(3);
        commands.EndRendering();
    }

    private ResourceSet? Publish(ResourceSet? resources)
    {
        ResourceSet? previous = _published;
        if (previous is not null && resources is not null &&
            ReferenceEquals(previous.AtlasTexture, resources.AtlasTexture))
        {
            previous.TransferAtlasOwnershipTo(resources);
        }
        _published = resources;
        return previous;
    }

    private void Retire(ResourceSet? resources)
    {
        if (resources is null)
        {
            return;
        }
        if (_retired is not null)
        {
            throw new InvalidOperationException("A previous volume resource set is still awaiting retirement.");
        }
        _retired = resources;
        DrainRetired();
    }

    private void DrainRetired()
    {
        _retired?.Dispose();
        _retired = null;
    }

    private void RetirePreparedResources(ResourceSet? resources)
    {
        try
        {
            Retire(resources);
        }
        catch (Exception failure) when (_reportRetirementFailure is not null)
        {
            _reportRetirementFailure(failure);
        }
    }

    private static bool TryDispose<T>(T? resource, ref List<Exception>? failures) where T : class, IDisposable
    {
        try
        {
            resource?.Dispose();
            return true;
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
            return false;
        }
    }

    private static void ThrowCleanupFailures(List<Exception>? failures, string message)
    {
        if (failures is { Count: 1 })
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures is not null)
        {
            throw new AggregateException(message, failures);
        }
    }

    public void Dispose()
    {
        if (_disposed && _retired is null && _published is null)
        {
            return;
        }
        _disposed = true;
        List<Exception>? failures = null;
        if (TryDispose(_retired, ref failures))
        {
            _retired = null;
        }
        if (TryDispose(_published, ref failures))
        {
            _published = null;
        }
        ThrowCleanupFailures(failures, "Volume camera resources could not all be released.");
    }

    private sealed record FieldSource(
        SilkMeshData Mesh,
        SilkMaterialData Material,
        SilkMaterialTexture? Texture,
        SilkVolumeTextureExtent Extent);

    internal sealed class Prepared : IDisposable
    {
        private readonly SilkVolumeCameraPass _owner;
        private bool _committed;
        private bool _disposed;
        private ResourceSet? _previous;

        internal Prepared(SilkVolumeCameraPass owner, ResourceSet? resources)
        {
            _owner = owner;
            ResourceData = resources;
        }

        internal ResourceSet? ResourceData { get; }
        internal bool HasPass => ResourceData is not null;

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                return;
            }
            _previous = _owner.Publish(ResourceData);
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner.RetirePreparedResources(_committed ? _previous : ResourceData);
        }
    }

    // An empty update-only page keeps its retirement state on the stack; rendered pages share a candidate.
    internal ref struct PreparedPage(SilkVolumeCameraPass owner, Prepared? prepared)
    {
        private readonly SilkVolumeCameraPass _owner = owner;
        private readonly Prepared? _prepared = prepared;
        private ResourceSet? _previous;
        private bool _committed;
        private bool _disposed;

        internal Prepared RenderCandidate => _prepared ??
            throw new InvalidOperationException("The prepared volume page has no render candidate.");

        internal void Commit()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PreparedPage));
            }
            if (_committed)
            {
                return;
            }
            if (_prepared is not null)
            {
                _prepared.Commit();
            }
            else
            {
                _previous = _owner.Publish(null);
            }
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_prepared is not null)
            {
                _prepared.Dispose();
            }
            else
            {
                _owner.RetirePreparedResources(_committed ? _previous : null);
            }
        }
    }

    internal sealed class ResourceSet : IDisposable
    {
        private readonly ISilkGraphicsDevice _device;
        private readonly SilkShaderBinaryFormat _shaderFormat;
        private bool _ownsAtlas;
        private IDisposable? _atlasAdmissionReservation;
        private ISilkGraphicsShaderModule? _vertexShader;
        private ISilkGraphicsShaderModule? _fragmentShader;
        private ISilkGraphicsBindingLayout? _bindingLayout;
        private ISilkGraphicsShaderProgram? _program;
        private bool _disposed;
        private bool _cleanupCompleted;
        private int _releasedResources;

        internal ResourceSet(
            ISilkGraphicsDevice device,
            SilkShaderBinaryFormat shaderFormat,
            uint width,
            uint height,
            ISilkGraphicsBuffer parameterBuffer,
            ISilkGraphicsBuffer fieldBuffer,
            ISilkGraphicsBuffer vertexBuffer,
            ISilkGraphicsBuffer indexBuffer,
            ISilkGraphicsTexture depthTarget,
            ISilkGraphicsTexture? atlasTexture,
            bool ownsAtlas,
            object[] atlasKey,
            SilkVolumeAtlasRegion[] atlasRegions,
            uint atlasWidth,
            uint atlasHeight,
            uint atlasDepth,
            ISilkGraphicsSampler? sampler,
            IDisposable? atlasAdmissionReservation = null,
            SilkVolumeDensitySamples[]? atlasDensitySamples = null)
        {
            _device = device;
            _shaderFormat = shaderFormat;
            Width = width;
            Height = height;
            ParameterBuffer = parameterBuffer;
            FieldBuffer = fieldBuffer;
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            DepthTarget = depthTarget;
            AtlasTexture = atlasTexture;
            _ownsAtlas = ownsAtlas;
            AtlasKey = atlasKey;
            AtlasRegions = atlasRegions;
            AtlasWidth = atlasWidth;
            AtlasHeight = atlasHeight;
            AtlasDepth = atlasDepth;
            Sampler = sampler;
            _atlasAdmissionReservation = atlasAdmissionReservation;
            AtlasDensitySamples = atlasDensitySamples ?? [];
        }

        internal uint Width { get; }
        internal uint Height { get; }
        internal ISilkGraphicsBuffer ParameterBuffer { get; }
        internal ISilkGraphicsBuffer FieldBuffer { get; }
        internal ISilkGraphicsBuffer VertexBuffer { get; }
        internal ISilkGraphicsBuffer IndexBuffer { get; }
        internal ISilkGraphicsTexture DepthTarget { get; }
        internal ISilkGraphicsTexture? AtlasTexture { get; }
        internal IReadOnlyList<object> AtlasKey { get; }
        internal SilkVolumeAtlasRegion[] AtlasRegions { get; }
        internal SilkVolumeDensitySamples[] AtlasDensitySamples { get; }
        internal uint AtlasWidth { get; }
        internal uint AtlasHeight { get; }
        internal uint AtlasDepth { get; }
        internal ISilkGraphicsSampler? Sampler { get; }
        internal ISilkGraphicsPipeline? Pipeline { get; private set; }
        internal bool IsDisposed => _disposed;
        internal void TransferAtlasOwnershipTo(ResourceSet target)
        {
            if (!_ownsAtlas)
            {
                return;
            }
            _ownsAtlas = false;
            target._ownsAtlas = AtlasTexture is not null;
            target._atlasAdmissionReservation =
                Interlocked.Exchange(ref _atlasAdmissionReservation, null);
        }

        internal void EnsurePipeline(SilkTextureFormat colorFormat)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Pipeline is not null && Pipeline.Descriptor.ColorFormat == colorFormat)
            {
                return;
            }
            DisposePipeline();
            try
            {
                _vertexShader = _device.CreateShaderModule(
                    SilkCheckedShaderAssets.LoadVolumeCameraVertex(_shaderFormat));
                _fragmentShader = _device.CreateShaderModule(
                    SilkCheckedShaderAssets.LoadVolumeCameraFragment(
                        _shaderFormat,
                        densityAtlas: AtlasTexture is not null));
                _bindingLayout = _device.CreateBindingLayout(
                    SilkBindingLayoutDescriptor.VolumeCameraParameters(AtlasTexture is not null));
                _program = _device.CreateShaderProgram(new SilkShaderProgramDescriptor(
                    _vertexShader,
                    _fragmentShader,
                    _bindingLayout));
                Pipeline = _device.CreateGraphicsPipeline(new SilkGraphicsPipelineDescriptor(
                    _program,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    colorFormat,
                    SilkTextureFormat.D32Float,
                    SilkCullMode.None)
                {
                    BlendMode = SilkBlendMode.StraightAlphaOver,
                    DepthWriteEnabled = false
                });
            }
            catch
            {
                DisposePipeline();
                throw;
            }
        }

        private void DisposePipeline(ref List<Exception>? failures)
        {
            if (TryDispose(Pipeline, ref failures))
            {
                Pipeline = null;
            }
            if (TryDispose(_program, ref failures))
            {
                _program = null;
            }
            if (TryDispose(_bindingLayout, ref failures))
            {
                _bindingLayout = null;
            }
            if (TryDispose(_fragmentShader, ref failures))
            {
                _fragmentShader = null;
            }
            if (TryDispose(_vertexShader, ref failures))
            {
                _vertexShader = null;
            }
        }

        private void DisposePipeline()
        {
            List<Exception>? failures = null;
            DisposePipeline(ref failures);
            ThrowCleanupFailures(failures, "Volume camera pipeline resources could not all be released.");
        }

        private void ReleaseResource(IDisposable? resource, int flag, ref List<Exception>? failures)
        {
            if ((_releasedResources & flag) == 0 && TryDispose(resource, ref failures))
            {
                _releasedResources |= flag;
            }
        }

        public void Dispose()
        {
            if (_cleanupCompleted)
            {
                return;
            }
            _disposed = true;
            List<Exception>? failures = null;
            DisposePipeline(ref failures);
            ReleaseResource(ParameterBuffer, 1 << 0, ref failures);
            ReleaseResource(FieldBuffer, 1 << 1, ref failures);
            ReleaseResource(VertexBuffer, 1 << 2, ref failures);
            ReleaseResource(IndexBuffer, 1 << 3, ref failures);
            ReleaseResource(DepthTarget, 1 << 4, ref failures);
            ReleaseResource(Sampler, 1 << 5, ref failures);
            if (_ownsAtlas)
            {
                const int atlasReleased = 1 << 6;
                ReleaseResource(AtlasTexture, atlasReleased, ref failures);
                if ((_releasedResources & atlasReleased) != 0)
                {
                    ReleaseResource(_atlasAdmissionReservation, 1 << 7, ref failures);
                }
            }
            ThrowCleanupFailures(failures, "Volume camera resource set could not all be released.");
            _cleanupCompleted = true;
        }
    }
}
