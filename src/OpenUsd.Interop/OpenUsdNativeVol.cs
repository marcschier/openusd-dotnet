// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

internal enum OpenUsdNativeVolSchemaKind
{
    Volume = 0,
    VolumeFieldBase = 1,
    VolumeFieldAsset = 2,
    FieldBase = 3,
    FieldAsset = 4,
    OpenVdbAsset = 5,
    Field3dAsset = 6,
}
internal enum OpenUsdNativeVolAssetProperty { FilePath = 0 }

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static bool IsVolSchema(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeVolSchemaKind schemaKind) =>
        GetSchemaInt(stage, (nint handle, out int value, ref NativeErrorBuffer error) =>
            NativeMethods.VolIsSchema(handle, primPath, (int)schemaKind, out value, ref error)) != 0;

    internal static void DefineVol(OpenUsdNativeStage stage, string primPath, OpenUsdNativeVolSchemaKind schemaKind) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.VolDefine(handle, primPath, (int)schemaKind, ref error));

    internal static string[] GetVolFieldPathPairs(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint list = 0;
        var view = new NativeStringListView { StructSize = (uint)sizeof(NativeStringListView) };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.VolGetFieldPaths(
                lease.Handle,
                primPath,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
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
    }

    internal static void SetVolFieldPath(
        OpenUsdNativeStage stage,
        string primPath,
        string fieldName,
        string targetPrimPath) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.VolSetFieldPath(handle, primPath, fieldName, targetPrimPath, ref error));

    internal static bool HasVolFieldRelationship(OpenUsdNativeStage stage, string primPath, string fieldName) =>
        GetSchemaInt(stage, (nint handle, out int value, ref NativeErrorBuffer error) =>
            NativeMethods.VolHasFieldRelationship(handle, primPath, fieldName, out value, ref error)) != 0;

    internal static void BlockVolFieldRelationship(OpenUsdNativeStage stage, string primPath, string fieldName) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.VolBlockFieldRelationship(handle, primPath, fieldName, ref error));

    internal static void SetVolAsset(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeVolAssetProperty property,
        string assetPath) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.VolSetAsset(handle, primPath, (int)property, assetPath, ref error));

    internal static string GetVolAsset(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeVolAssetProperty property)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.VolGetAsset(
                    handle,
                    primPath,
                    (int)property,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    private static void InvokeSchemaAction(OpenUsdNativeStage stage, NativeSchemaAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(lease.Handle, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static int GetSchemaInt(OpenUsdNativeStage stage, NativeSchemaIntGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, out int value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private delegate OpenUsdNativeStatus NativeSchemaAction(nint stage, ref NativeErrorBuffer error);
    private delegate OpenUsdNativeStatus NativeSchemaIntGetter(nint stage, out int value, ref NativeErrorBuffer error);

}
