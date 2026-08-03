// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

internal enum OpenUsdNativeRenderSchemaKind { SettingsBase = 0, Settings = 1, Product = 2, Var = 3, Pass = 4 }
internal enum OpenUsdNativeMediaSchemaKind { SpatialAudio = 0, AssetPreviewsApi = 1 }
internal enum OpenUsdNativeMediaAssetProperty { FilePath = 0, DefaultThumbnail = 1 }
internal enum OpenUsdNativeProcSchemaKind { GenerativeProcedural = 0 }
internal enum OpenUsdNativeUiSchemaKind { Backdrop = 0, NodeGraphNodeApi = 1, SceneGraphPrimApi = 2 }

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static bool IsRenderSchema(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeRenderSchemaKind schemaKind) =>
        GetSchemaInt(stage, (nint handle, out int value, ref NativeErrorBuffer error) =>
            NativeMethods.RenderIsSchema(handle, primPath, (int)schemaKind, out value, ref error)) != 0;
    internal static void DefineRender(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeRenderSchemaKind schemaKind) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.RenderDefine(handle, primPath, (int)schemaKind, ref error));

    internal static bool IsMediaSchema(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMediaSchemaKind schemaKind) =>
        GetSchemaInt(stage, (nint handle, out int value, ref NativeErrorBuffer error) =>
            NativeMethods.MediaIsSchema(handle, primPath, (int)schemaKind, out value, ref error)) != 0;
    internal static void DefineMedia(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMediaSchemaKind schemaKind) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.MediaDefine(handle, primPath, (int)schemaKind, ref error));
    internal static void ApplyMediaApi(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMediaSchemaKind schemaKind) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.MediaApplyApi(handle, primPath, (int)schemaKind, ref error));
    internal static void SetMediaAsset(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMediaAssetProperty property,
        string assetPath) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.MediaSetAsset(handle, primPath, (int)property, assetPath, ref error));
    internal static void ClearMediaAsset(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMediaAssetProperty property) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.MediaClearAsset(handle, primPath, (int)property, ref error));
    internal static string GetMediaAsset(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMediaAssetProperty property)
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
                OpenUsdNativeStatus status = NativeMethods.MediaGetAsset(
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

    internal static bool IsProcSchema(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeProcSchemaKind schemaKind) =>
        GetSchemaInt(stage, (nint handle, out int value, ref NativeErrorBuffer error) =>
            NativeMethods.ProcIsSchema(handle, primPath, (int)schemaKind, out value, ref error)) != 0;
    internal static void DefineProc(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeProcSchemaKind schemaKind) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.ProcDefine(handle, primPath, (int)schemaKind, ref error));

    internal static bool IsUiSchema(OpenUsdNativeStage stage, string primPath, OpenUsdNativeUiSchemaKind schemaKind) =>
        GetSchemaInt(stage, (nint handle, out int value, ref NativeErrorBuffer error) =>
            NativeMethods.UiIsSchema(handle, primPath, (int)schemaKind, out value, ref error)) != 0;
    internal static void DefineUi(OpenUsdNativeStage stage, string primPath, OpenUsdNativeUiSchemaKind schemaKind) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.UiDefine(handle, primPath, (int)schemaKind, ref error));
    internal static void ApplyUiApi(OpenUsdNativeStage stage, string primPath, OpenUsdNativeUiSchemaKind schemaKind) =>
        InvokeSchemaAction(stage, (nint handle, ref NativeErrorBuffer error) =>
            NativeMethods.UiApplyApi(handle, primPath, (int)schemaKind, ref error));
}
