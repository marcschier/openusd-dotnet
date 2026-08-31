// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Interop;

/// <summary>
/// Owns one native resolver-context binding.
/// </summary>
/// <remarks>
/// The native binding is a thread-local <c>ArResolverContextBinder</c>, so it may only be released
/// on the thread that created it and only after every binding created after it. The type therefore
/// has no finalizer on purpose: releasing an abandoned binding from the finalizer thread would
/// unbind a context that thread never bound, which corrupts unrelated resolution instead of
/// leaking one handle.
/// </remarks>
internal sealed class OpenUsdNativeResolverBinding : IDisposable
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private nint _handle;

    internal OpenUsdNativeResolverBinding(nint handle)
    {
        _handle = handle;
    }

    /// <summary>Releases the native binding.</summary>
    /// <remarks>
    /// The handle is cleared only after the native unbind succeeds. A wrong-thread or
    /// out-of-order release therefore leaves the binding intact and retryable from the owner
    /// thread instead of forgetting a handle the native side still owns. Both violations surface
    /// as <see cref="InvalidOperationException"/>, because both describe a caller that used the
    /// binding wrongly rather than a native failure.
    /// </remarks>
    public void Dispose()
    {
        nint handle = _handle;
        if (handle == 0)
        {
            return;
        }
        if (_threadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "A resolver-context binding must be released on the thread that created it.");
        }

        try
        {
            OpenUsdNativeRuntime.UnbindResolverContext(handle);
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
        {
            // The only invalid-argument result the unbind export produces for a live handle is a
            // release that is not the newest binding on this thread. The native side left the
            // binding bound and owned, so this stays retryable in the right order.
            throw new InvalidOperationException(
                "A resolver-context binding must be released before the bindings created after " +
                "it. The binding is still bound and can be released in order.",
                exception);
        }

        _handle = 0;
    }
}

public static unsafe partial class OpenUsdNativeRuntime
{
    private const int ResolvedAssetStringsPerRecord = 5;

    /// <summary>Gets the type name of the primary resolver selected by the loaded plugins.</summary>
    internal static string ResolverPrimaryTypeName
    {
        get
        {
            EnsureCompatibleAbi();
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.ResolverGetPrimaryTypeName(
                    null,
                    0,
                    out nuint required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall || required == 0 ||
                    required > int.MaxValue)
                {
                    throw CreateNativeException(status, errorBytes, error);
                }

                byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
                fixed (byte* pointer = bytes)
                {
                    status = NativeMethods.ResolverGetPrimaryTypeName(
                        pointer,
                        required,
                        out nuint written,
                        ref error);
                    if (status != OpenUsdNativeStatus.Ok || written != required)
                    {
                        throw CreateNativeException(status, errorBytes, error);
                    }
                }
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
            }
        }
    }

    internal static string[] GetResolverUriSchemes() =>
        GetStringList(NativeMethods.ResolverGetUriSchemes);

    internal static string[] GetResolverAvailableTypeNames() =>
        GetStringList(NativeMethods.ResolverGetAvailableTypeNames);

    internal static OpenUsdNativePlugin[] GetRegisteredPlugins()
    {
        string[] values = GetStringList(NativeMethods.GetRegisteredPlugins);
        if (values.Length % 5 != 0)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an invalid plugin list.");
        }

        var plugins = new OpenUsdNativePlugin[values.Length / 5];
        for (int i = 0; i < plugins.Length; i++)
        {
            int field = i * 5;
            plugins[i] = new OpenUsdNativePlugin(
                values[field],
                values[field + 1],
                string.Equals(values[field + 2], "loaded", StringComparison.Ordinal),
                values[field + 3],
                values[field + 4]);
        }
        return plugins;
    }

    internal static OpenUsdNativeResolverContext CreateResolverContext(
        ReadOnlySpan<string> contextStrings)
    {
        if (contextStrings.Length % 2 != 0)
        {
            throw new ArgumentException(
                "Resolver-context strings must be scheme and context-string pairs.",
                nameof(contextStrings));
        }
        EnsureCompatibleAbi();

        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(contextStrings);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* dataPointer = data)
        fixed (nuint* offsetPointer = offsets)
        fixed (byte* errorPointer = errorBytes)
        {
            var view = new NativeStringListView
            {
                StructSize = (uint)sizeof(NativeStringListView),
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetPointer,
                OffsetsSize = checked((nuint)offsets.Length * (nuint)sizeof(nuint)),
                Count = (nuint)offsets.Length
            };
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ResolverContextCreate(
                ref view,
                out nint context,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeResolverContext(context);
        }
    }

    internal static OpenUsdNativeResolverContext CreateResolverContextForAsset(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        EnsureCompatibleAbi();

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ResolverContextCreateForAsset(
                assetPath,
                out nint context,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeResolverContext(context);
        }
    }

    internal static string GetResolverContextDebugString(OpenUsdNativeResolverContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var lease = new SafeHandleLease(context);
        nint handle = lease.Handle;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ResolverContextGetDebugString(
                handle,
                null,
                0,
                out nuint required,
                ref error);
            if (status != OpenUsdNativeStatus.BufferTooSmall || required == 0 ||
                required > int.MaxValue)
            {
                throw CreateNativeException(status, errorBytes, error);
            }

            byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
            fixed (byte* pointer = bytes)
            {
                status = NativeMethods.ResolverContextGetDebugString(
                    handle,
                    pointer,
                    required,
                    out nuint written,
                    ref error);
                if (status != OpenUsdNativeStatus.Ok || written != required)
                {
                    throw CreateNativeException(status, errorBytes, error);
                }
            }
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
        }
    }

    internal static bool IsResolverContextEmpty(OpenUsdNativeResolverContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var lease = new SafeHandleLease(context);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ResolverContextIsEmpty(
                lease.Handle,
                out int value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value != 0;
        }
    }

    internal static void RefreshResolverContext(OpenUsdNativeResolverContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var lease = new SafeHandleLease(context);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ResolverContextRefresh(
                lease.Handle,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeResolverBinding BindResolverContext(
        OpenUsdNativeResolverContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var lease = new SafeHandleLease(context);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ResolverContextBind(
                lease.Handle,
                out nint binding,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeResolverBinding(binding);
        }
    }

    internal static void UnbindResolverContext(nint binding)
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ResolverContextUnbind(binding, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void ReleaseResolverContext(nint context) =>
        NativeMethods.ResolverContextRelease(context);

    internal static OpenUsdNativeResolvedAsset[] ResolveAssets(
        ReadOnlySpan<string> assetPaths,
        OpenUsdNativeResolverContext? context,
        string? anchorAssetPath)
    {
        EnsureCompatibleAbi();
        if (anchorAssetPath is not null)
        {
            NativeStringValidation.ThrowIfContainsNull(anchorAssetPath, nameof(anchorAssetPath));
        }

        if (context is null)
        {
            return ResolveAssetsCore(assetPaths, 0, anchorAssetPath);
        }

        using var lease = new SafeHandleLease(context);
        return ResolveAssetsCore(assetPaths, lease.Handle, anchorAssetPath);
    }

    private static OpenUsdNativeResolvedAsset[] ResolveAssetsCore(
        ReadOnlySpan<string> assetPaths,
        nint contextHandle,
        string? anchorAssetPath)
    {
        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(assetPaths);
        var view = new NativeResolvedAssetView
        {
            StructSize = (uint)sizeof(NativeResolvedAssetView),
            Version = 1
        };
        nint list = 0;
        try
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* dataPointer = data)
            fixed (nuint* offsetPointer = offsets)
            fixed (byte* errorPointer = errorBytes)
            {
                var paths = new NativeStringListView
                {
                    StructSize = (uint)sizeof(NativeStringListView),
                    Data = dataPointer,
                    DataSize = (nuint)data.Length,
                    Offsets = offsetPointer,
                    OffsetsSize = checked((nuint)offsets.Length * (nuint)sizeof(nuint)),
                    Count = (nuint)offsets.Length
                };
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.ResolverResolve(
                    ref paths,
                    contextHandle,
                    anchorAssetPath,
                    out list,
                    ref view,
                    ref error);
                if (status != OpenUsdNativeStatus.Ok && list != 0)
                {
                    NativeMethods.ResolvedAssetListRelease(list);
                    list = 0;
                }
                ThrowIfFailed(status, errorBytes, error);
            }

            return DecodeResolvedAssetView(assetPaths, view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.ResolvedAssetListRelease(list);
            }
        }
    }

    internal static OpenUsdNativeStage OpenStageWithContext(
        string path,
        OpenUsdNativeResolverContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);
        EnsureCompatibleAbi();

        using var lease = new SafeHandleLease(context);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageOpenWithContext(
                path,
                lease.Handle,
                out nint stage,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeStage(stage);
        }
    }

    private static OpenUsdNativeResolvedAsset[] DecodeResolvedAssetView(
        ReadOnlySpan<string> assetPaths,
        NativeResolvedAssetView view)
    {
        if (view.Version != 1 || view.StructSize < sizeof(NativeResolvedAssetView) ||
            view.RecordsSize != view.RecordCount * (nuint)sizeof(OpenUsdNativeResolvedAssetRecord) ||
            view.OffsetsSize != view.StringCount * (nuint)sizeof(nuint) ||
            view.DataSize > int.MaxValue || view.RecordCount > int.MaxValue ||
            view.StringCount > int.MaxValue ||
            view.RecordCount != (nuint)assetPaths.Length ||
            view.StringCount != view.RecordCount * ResolvedAssetStringsPerRecord)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an invalid resolved-asset buffer.");
        }
        if (view.RecordCount == 0)
        {
            return [];
        }

        var records = new ReadOnlySpan<OpenUsdNativeResolvedAssetRecord>(
            view.Records,
            (int)view.RecordCount);
        string[] strings = NativePackedStringListDecoder.Decode(
            new ReadOnlySpan<byte>(view.Data, (int)view.DataSize),
            new ReadOnlySpan<nuint>(view.Offsets, (int)view.StringCount),
            "resolved-asset buffer");
        var assets = new OpenUsdNativeResolvedAsset[records.Length];
        for (int i = 0; i < records.Length; i++)
        {
            OpenUsdNativeResolvedAssetRecord record = records[i];
            if (record.IdentifierOffset > (nuint)strings.Length - ResolvedAssetStringsPerRecord ||
                record.ResolvedPathOffset != record.IdentifierOffset + 1 ||
                record.ExtensionOffset != record.IdentifierOffset + 2 ||
                record.AssetVersionOffset != record.IdentifierOffset + 3 ||
                record.AssetNameOffset != record.IdentifierOffset + 4)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native runtime returned an invalid resolved-asset string range.");
            }

            int field = (int)record.IdentifierOffset;
            assets[i] = new OpenUsdNativeResolvedAsset(
                assetPaths[i],
                strings[field],
                strings[field + 1],
                strings[field + 2],
                strings[field + 3],
                strings[field + 4],
                record.Resolved != 0,
                record.ContextDependent != 0,
                record.TimestampValid != 0 ? record.ModificationTime : null);
        }
        return assets;
    }

    private static string[] GetStringList(NativeStringListGetter getter)
    {
        EnsureCompatibleAbi();

        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(out list, ref view, ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    private delegate OpenUsdNativeStatus NativeStringListGetter(
        out nint list,
        ref NativeStringListView view,
        ref NativeErrorBuffer error);
}
