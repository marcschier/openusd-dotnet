// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Geom;

#pragma warning disable CS1591

namespace OpenUsd.Cesium;

public sealed record CesiumAssetRequest(string Method, string Url, ReadOnlyMemory<byte> Body);

public sealed record CesiumAssetResponse(ushort StatusCode, string ContentType, byte[] Body);

public interface ICesiumAssetAccessor
{
    CesiumAssetResponse Request(CesiumAssetRequest request);
}

public sealed class CesiumFileAssetAccessor : ICesiumAssetAccessor
{
    private readonly string _rootDirectory;

    public CesiumFileAssetAccessor(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public CesiumAssetResponse Request(CesiumAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return new CesiumAssetResponse(405, "text/plain", []);
        }

        string relative = request.Url;
        if (Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri))
        {
            relative = uri.IsFile ? uri.LocalPath : uri.AbsolutePath.TrimStart('/');
        }
        relative = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(_rootDirectory, relative));
        if (!path.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return new CesiumAssetResponse(404, "text/plain", []);
        }

        return new CesiumAssetResponse(200, GetContentType(path), File.ReadAllBytes(path));
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".glb" => "model/gltf-binary",
        ".gltf" => "model/gltf+json",
        _ => "application/octet-stream"
    };
}

public sealed class CesiumTilesetOptions
{
    public double MaximumScreenSpaceError { get; set; } = 16;
}

public readonly record struct CesiumViewState(
    UsdVec3d PositionEcef,
    UsdVec3d DirectionEcef,
    UsdVec3d UpEcef,
    double ViewportWidth,
    double ViewportHeight,
    double HorizontalFovRadians,
    double VerticalFovRadians);

public readonly record struct CesiumUpdateResult(
    int TilesToRenderCount,
    int LoadedTileCount,
    float LoadProgress);

public sealed record CesiumTileImportResult(string RootPath, int MeshCount, IReadOnlyList<string> PrimPaths)
{
    // A record's synthesized Equals compares IReadOnlyList by reference, so two
    // imports of the same tile would never compare equal. Same defect the
    // OpenUsd snapshot records carry a guard for; that guard enumerates the
    // OpenUsd assembly only, so it does not reach this one.
    /// <inheritdoc />
    public bool Equals(CesiumTileImportResult? other) =>
        other is not null &&
        RootPath == other.RootPath &&
        MeshCount == other.MeshCount &&
        PrimPaths.SequenceEqual(other.PrimPaths);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(RootPath);
        hash.Add(MeshCount);
        foreach (string path in PrimPaths)
        {
            hash.Add(path);
        }
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(CesiumTileImportResult)} {{ {nameof(RootPath)} = {RootPath}, " +
        $"{nameof(MeshCount)} = {MeshCount}, " +
        $"{nameof(PrimPaths)} = [{string.Join(", ", PrimPaths)}] }}";
}

public sealed unsafe partial class CesiumTileset : IDisposable
{
    private readonly ICesiumAssetAccessor _assetAccessor;
    private readonly GCHandle _self;
    private readonly ConcurrentQueue<CapturedMesh> _pendingMeshes = new();
    private nint _handle;
    private bool _disposed;

    public CesiumTileset(
        string tilesetUrl,
        ICesiumAssetAccessor assetAccessor,
        CesiumTilesetOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tilesetUrl);
        _assetAccessor = assetAccessor ?? throw new ArgumentNullException(nameof(assetAccessor));
        _self = GCHandle.Alloc(this);
        NativeAssetAccessor nativeAsset = new()
        {
            StructSize = (uint)sizeof(NativeAssetAccessor),
            Version = 1,
            UserData = GCHandle.ToIntPtr(_self),
            Request = &AssetRequest
        };
        NativeRendererCallbacks renderer = new()
        {
            StructSize = (uint)sizeof(NativeRendererCallbacks),
            Version = 1,
            UserData = GCHandle.ToIntPtr(_self),
            PrepareLoadThread = &PrepareLoadThread,
            PrepareMainThread = &PrepareMainThread,
            FreeResources = &FreeResources,
            MeshPrimitiveLoadThread = &MeshPrimitiveLoadThread
        };
        NativeTaskProcessor tasks = new()
        {
            StructSize = (uint)sizeof(NativeTaskProcessor),
            Version = 1,
            UserData = GCHandle.ToIntPtr(_self),
            StartTask = &StartTask
        };
        NativeTilesetOptions nativeOptions = new()
        {
            StructSize = (uint)sizeof(NativeTilesetOptions),
            Version = 1,
            MaximumScreenSpaceError = options?.MaximumScreenSpaceError ?? 16
        };
        Span<byte> errorBytes = stackalloc byte[1024];
        fixed (byte* error = errorBytes)
        {
            NativeErrorBuffer errorBuffer = new(error, (nuint)errorBytes.Length);
            CesiumStatus status = NativeMethods.Create(
                tilesetUrl,
                &nativeAsset,
                &renderer,
                &tasks,
                &nativeOptions,
                out _handle,
                &errorBuffer);
            ThrowIfFailed(status, errorBytes, errorBuffer);
        }
    }

    public CesiumUpdateResult UpdateView(CesiumViewState viewState, float deltaTimeSeconds = 0.016f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeViewState nativeView = new()
        {
            StructSize = (uint)sizeof(NativeViewState),
            Version = 1,
            PositionEcef = ToNative(viewState.PositionEcef),
            DirectionEcef = ToNative(viewState.DirectionEcef),
            UpEcef = ToNative(viewState.UpEcef),
            ViewportWidth = viewState.ViewportWidth,
            ViewportHeight = viewState.ViewportHeight,
            HorizontalFovRadians = viewState.HorizontalFovRadians,
            VerticalFovRadians = viewState.VerticalFovRadians
        };
        NativeUpdateResult result = new() { StructSize = (uint)sizeof(NativeUpdateResult) };
        Span<byte> errorBytes = stackalloc byte[1024];
        fixed (byte* error = errorBytes)
        {
            NativeErrorBuffer errorBuffer = new(error, (nuint)errorBytes.Length);
            CesiumStatus status = NativeMethods.UpdateView(
                _handle,
                &nativeView,
                1,
                deltaTimeSeconds,
                &result,
                &errorBuffer);
            ThrowIfFailed(status, errorBytes, errorBuffer);
        }
        return new CesiumUpdateResult(result.TilesToRenderCount, result.LoadedTileCount, result.LoadProgress);
    }

    public CesiumTileImportResult ImportVisibleTiles(UsdStage stage, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(rootPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        UsdGeomXform root = stage.DefineXform(rootPath);
        List<CapturedMesh> meshes = [];
        while (_pendingMeshes.TryDequeue(out CapturedMesh? mesh))
        {
            meshes.Add(mesh);
        }
        if (meshes.Count == 0)
        {
            return new CesiumTileImportResult(rootPath, 0, []);
        }

        UsdVec3d origin = meshes[0].Transform.ExtractTranslation();
        root.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(origin));
        List<string> primPaths = new(meshes.Count);
        for (int index = 0; index < meshes.Count; index++)
        {
            string path = $"{rootPath}/TileMesh_{index}";
            CapturedMesh mesh = meshes[index];
            UsdGeomMesh usdMesh = stage.DefineMesh(path);
            usdMesh.SetPoints(mesh.Points);
            usdMesh.SetTopology(mesh.FaceVertexCounts, mesh.FaceVertexIndices);
            AuthorOptionalAttributes(usdMesh, mesh);
            usdMesh.Xformable.SetLocalTransform(Rebase(mesh.Transform, origin));
            primPaths.Add(path);
        }
        return new CesiumTileImportResult(rootPath, primPaths.Count, primPaths);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_handle != 0)
        {
            Span<byte> errorBytes = stackalloc byte[1024];
            fixed (byte* error = errorBytes)
            {
                NativeErrorBuffer errorBuffer = new(error, (nuint)errorBytes.Length);
                _ = NativeMethods.Release(_handle, &errorBuffer);
            }
            _handle = 0;
        }
        _self.Free();
    }

    internal static CesiumTileImportResult AuthorCapturedMeshes(
        UsdStage stage,
        string rootPath,
        IReadOnlyList<CapturedMesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(rootPath);
        UsdGeomXform root = stage.DefineXform(rootPath);
        if (meshes.Count == 0)
        {
            return new CesiumTileImportResult(rootPath, 0, []);
        }
        UsdVec3d origin = meshes[0].Transform.ExtractTranslation();
        root.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(origin));
        List<string> paths = new(meshes.Count);
        for (int i = 0; i < meshes.Count; i++)
        {
            string path = $"{rootPath}/TileMesh_{i}";
            UsdGeomMesh mesh = stage.DefineMesh(path);
            mesh.SetPoints(meshes[i].Points);
            mesh.SetTopology(meshes[i].FaceVertexCounts, meshes[i].FaceVertexIndices);
            AuthorOptionalAttributes(mesh, meshes[i]);
            mesh.Xformable.SetLocalTransform(Rebase(meshes[i].Transform, origin));
            paths.Add(path);
        }
        return new CesiumTileImportResult(rootPath, paths.Count, paths);
    }

    internal sealed record CapturedMesh(
        UsdMatrix4d Transform,
        UsdVec3f[] Points,
        int[] FaceVertexCounts,
        int[] FaceVertexIndices,
        UsdVec3f[] Normals,
        UsdVec2f[] TexCoords0);

    private void CaptureMesh(NativeMeshPrimitive* primitive)
    {
        if (primitive == null || primitive->Positions == null || primitive->Transform == null ||
            primitive->FaceVertexCounts == null || primitive->FaceVertexIndices == null)
        {
            return;
        }
        UsdVec3f[] points = new ReadOnlySpan<UsdVec3f>(
            primitive->Positions,
            checked((int)primitive->PositionCount)).ToArray();
        int[] counts = new ReadOnlySpan<int>(
            primitive->FaceVertexCounts,
            checked((int)primitive->FaceCount)).ToArray();
        int[] indices = new ReadOnlySpan<int>(
            primitive->FaceVertexIndices,
            checked((int)primitive->FaceVertexIndexCount)).ToArray();
        UsdVec3f[] normals = primitive->Normals == null || primitive->NormalCount == 0
            ? []
            : new ReadOnlySpan<UsdVec3f>(primitive->Normals, checked((int)primitive->NormalCount)).ToArray();
        UsdVec2f[] texCoords0 = primitive->TexCoords0 == null || primitive->TexCoord0Count == 0
            ? []
            : new ReadOnlySpan<UsdVec2f>(primitive->TexCoords0, checked((int)primitive->TexCoord0Count)).ToArray();
        _pendingMeshes.Enqueue(new CapturedMesh(
            ToManaged(*primitive->Transform),
            points,
            counts,
            indices,
            normals,
            texCoords0));
    }

    private CesiumAssetResponse RequestAsset(string method, string url, ReadOnlySpan<byte> body) =>
        _assetAccessor.Request(new CesiumAssetRequest(method, url, body.ToArray()));

    private static UsdMatrix4d Rebase(UsdMatrix4d value, UsdVec3d origin) => new(
        value.M00, value.M01, value.M02, value.M03,
        value.M10, value.M11, value.M12, value.M13,
        value.M20, value.M21, value.M22, value.M23,
        value.M30 - origin.X, value.M31 - origin.Y, value.M32 - origin.Z, value.M33);

    private static void AuthorOptionalAttributes(UsdGeomMesh mesh, CapturedMesh captured)
    {
        if (captured.Normals.Length != 0)
        {
            mesh.SetNormals(captured.Normals, UsdGeomInterpolation.Vertex);
        }
        if (captured.TexCoords0.Length != 0)
        {
            UsdGeomPrimvar st = new UsdGeomPrimvarsAPI(mesh.Prim).CreatePrimvar(
                "st",
                UsdGeomInterpolation.Vertex,
                elementSize: 2);
            st.SetVec2fArray(captured.TexCoords0);
        }
    }

    private static NativeVec3d ToNative(UsdVec3d value) => new(value.X, value.Y, value.Z);

    private static UsdMatrix4d ToManaged(NativeMatrix4d value) => new(
        value.Values[0], value.Values[1], value.Values[2], value.Values[3],
        value.Values[4], value.Values[5], value.Values[6], value.Values[7],
        value.Values[8], value.Values[9], value.Values[10], value.Values[11],
        value.Values[12], value.Values[13], value.Values[14], value.Values[15]);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static CesiumStatus AssetRequest(
        nint userData,
        byte* method,
        byte* url,
        byte* requestData,
        nuint requestSize,
        NativeAssetResponse* response,
        NativeErrorBuffer* error)
    {
        _ = error;
        try
        {
            CesiumTileset owner = FromHandle(userData);
            CesiumAssetResponse managed = owner.RequestAsset(
                Marshal.PtrToStringUTF8((nint)method) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)url) ?? string.Empty,
                new ReadOnlySpan<byte>(requestData, checked((int)requestSize)));
            byte[] body = managed.Body;
            byte* nativeBody = null;
            if (body.Length != 0)
            {
                nativeBody = (byte*)NativeMemory.Alloc((nuint)body.Length);
                new ReadOnlySpan<byte>(body).CopyTo(new Span<byte>(nativeBody, body.Length));
            }
            response->StructSize = (uint)sizeof(NativeAssetResponse);
            response->StatusCode = managed.StatusCode;
            response->ContentType = Marshal.StringToCoTaskMemUTF8(managed.ContentType);
            response->Data = nativeBody;
            response->DataSize = (nuint)body.Length;
            response->FreeData = &FreeAssetResponse;
            response->UserData = response->ContentType;
            return CesiumStatus.Ok;
        }
        catch
        {
            return CesiumStatus.NativeError;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeAssetResponse(nint userData, byte* data, nuint dataSize)
    {
        _ = dataSize;
        NativeMemory.Free(data);
        Marshal.FreeCoTaskMem(userData);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint PrepareLoadThread(
        nint userData,
        NativeTileLoadResult* loadResult,
        NativeMatrix4d* transform,
        NativeErrorBuffer* error)
    {
        _ = userData;
        _ = loadResult;
        _ = transform;
        _ = error;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint PrepareMainThread(nint userData, nint loadThreadResource, NativeErrorBuffer* error)
    {
        _ = userData;
        _ = error;
        return loadThreadResource;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeResources(nint userData, nint loadThreadResource, nint mainThreadResource)
    {
        _ = userData;
        _ = loadThreadResource;
        _ = mainThreadResource;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MeshPrimitiveLoadThread(nint userData, NativeMeshPrimitive* primitive) =>
        FromHandle(userData).CaptureMesh(primitive);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StartTask(nint userData, nint task)
    {
        _ = userData;
        ThreadPool.QueueUserWorkItem(static taskHandle =>
        {
            nint handle = (nint)taskHandle!;
            _ = NativeMethods.ExecuteTask(handle);
            NativeMethods.DestroyTask(handle);
        }, task);
    }

    private static CesiumTileset FromHandle(nint userData) =>
        (CesiumTileset)GCHandle.FromIntPtr(userData).Target!;

    private static void ThrowIfFailed(CesiumStatus status, ReadOnlySpan<byte> errorBytes, NativeErrorBuffer error)
    {
        if (status == CesiumStatus.Ok)
        {
            return;
        }
        string message = error.Required == 0
            ? $"Cesium native call failed with status {status}."
            : Marshal.PtrToStringUTF8((nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(errorBytes))) ??
                $"Cesium native call failed with status {status}.";
        throw new InvalidOperationException(message);
    }

    private enum CesiumStatus : int
    {
        Ok = 0,
        NativeError = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeVec3d(double x, double y, double z)
    {
        public readonly double X = x;
        public readonly double Y = y;
        public readonly double Z = z;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMatrix4d
    {
        public fixed double Values[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeErrorBuffer(byte* data, nuint capacity)
    {
        public readonly byte* Data = data;
        public readonly nuint Capacity = capacity;
        public readonly nuint Required = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAssetResponse
    {
        public uint StructSize;
        public ushort StatusCode;
        public nint ContentType;
        public byte* Data;
        public nuint DataSize;
        public delegate* unmanaged[Cdecl]<nint, byte*, nuint, void> FreeData;
        public nint UserData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTileLoadResult
    {
        public uint StructSize;
        public uint Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMeshPrimitive
    {
        public uint StructSize;
        public uint Version;
        public uint MeshIndex;
        public uint PrimitiveIndex;
        public NativeMatrix4d* Transform;
        public UsdVec3f* Positions;
        public nuint PositionCount;
        public int* FaceVertexCounts;
        public nuint FaceCount;
        public int* FaceVertexIndices;
        public nuint FaceVertexIndexCount;
        public UsdVec3f* Normals;
        public nuint NormalCount;
        public UsdVec2f* TexCoords0;
        public nuint TexCoord0Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAssetAccessor
    {
        public uint StructSize;
        public uint Version;
        public nint UserData;
        public delegate* unmanaged[Cdecl]<
            nint,
            byte*,
            byte*,
            byte*,
            nuint,
            NativeAssetResponse*,
            NativeErrorBuffer*,
            CesiumStatus> Request;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRendererCallbacks
    {
        public uint StructSize;
        public uint Version;
        public nint UserData;
        public delegate* unmanaged[Cdecl]<
            nint,
            NativeTileLoadResult*,
            NativeMatrix4d*,
            NativeErrorBuffer*,
            nint> PrepareLoadThread;
        public delegate* unmanaged[Cdecl]<nint, nint, NativeErrorBuffer*, nint> PrepareMainThread;
        public delegate* unmanaged[Cdecl]<nint, nint, nint, void> FreeResources;
        public nint AttachRaster;
        public nint DetachRaster;
        public delegate* unmanaged[Cdecl]<nint, NativeMeshPrimitive*, void> MeshPrimitiveLoadThread;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTaskProcessor
    {
        public uint StructSize;
        public uint Version;
        public nint UserData;
        public delegate* unmanaged[Cdecl]<nint, nint, void> StartTask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTilesetOptions
    {
        public uint StructSize;
        public uint Version;
        public double MaximumScreenSpaceError;
        public int PreloadAncestors;
        public int PreloadSiblings;
        public int ForbidHoles;
        public nint MessageCallback;
        public nint MessageUserData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeViewState
    {
        public uint StructSize;
        public uint Version;
        public NativeVec3d PositionEcef;
        public NativeVec3d DirectionEcef;
        public NativeVec3d UpEcef;
        public double ViewportWidth;
        public double ViewportHeight;
        public double HorizontalFovRadians;
        public double VerticalFovRadians;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeUpdateResult
    {
        public uint StructSize;
        public uint Version;
        public int TilesToRenderCount;
        public int WorkerQueue;
        public int MainQueue;
        public uint TilesVisited;
        public uint CulledTilesVisited;
        public uint TilesCulled;
        public uint MaxDepthVisited;
        public int FrameNumber;
        public int LoadedTileCount;
        public float LoadProgress;
    }

    [SuppressMessage("Usage", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes")]
    private static unsafe partial class NativeMethods
    {
        private const string LibraryName = "openusd_cesium";

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_cesium_tileset_create",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial CesiumStatus Create(
            string url,
            NativeAssetAccessor* assetAccessor,
            NativeRendererCallbacks* rendererCallbacks,
            NativeTaskProcessor* taskProcessor,
            NativeTilesetOptions* options,
            out nint tileset,
            NativeErrorBuffer* error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_cesium_tileset_update_view")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial CesiumStatus UpdateView(
            nint tileset,
            NativeViewState* viewStates,
            nuint viewStateCount,
            float deltaTimeSeconds,
            NativeUpdateResult* result,
            NativeErrorBuffer* error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_cesium_tileset_release")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial CesiumStatus Release(nint tileset, NativeErrorBuffer* error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_cesium_task_execute")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial CesiumStatus ExecuteTask(nint task);

        [LibraryImport(LibraryName, EntryPoint = "openusd_cesium_task_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void DestroyTask(nint task);
    }
}
