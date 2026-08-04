// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Media;
using OpenUsd.Proc;
using OpenUsd.Render;
using OpenUsd.UI;
using OpenUsd.Vol;

namespace OpenUsd.Tests;

public sealed class SchemaFacadeSurfaceTests
{
    [Test]
    public async Task SchemaInteropEnumsMatchCAbiOrdinals()
    {
        await Assert.That(Ordinal(OpenUsdNativeVolSchemaKind.Volume)).IsEqualTo(0);
        await Assert.That(Ordinal(OpenUsdNativeVolSchemaKind.VolumeFieldBase)).IsEqualTo(1);
        await Assert.That(Ordinal(OpenUsdNativeVolSchemaKind.VolumeFieldAsset)).IsEqualTo(2);
        await Assert.That(Ordinal(OpenUsdNativeVolSchemaKind.FieldBase)).IsEqualTo(3);
        await Assert.That(Ordinal(OpenUsdNativeVolSchemaKind.FieldAsset)).IsEqualTo(4);
        await Assert.That(Ordinal(OpenUsdNativeVolSchemaKind.OpenVdbAsset)).IsEqualTo(5);
        await Assert.That(Ordinal(OpenUsdNativeVolSchemaKind.Field3dAsset)).IsEqualTo(6);
        await Assert.That(Ordinal(OpenUsdNativeRenderSchemaKind.SettingsBase)).IsEqualTo(0);
        await Assert.That(Ordinal(OpenUsdNativeRenderSchemaKind.Settings)).IsEqualTo(1);
        await Assert.That(Ordinal(OpenUsdNativeRenderSchemaKind.Product)).IsEqualTo(2);
        await Assert.That(Ordinal(OpenUsdNativeRenderSchemaKind.Var)).IsEqualTo(3);
        await Assert.That(Ordinal(OpenUsdNativeRenderSchemaKind.Pass)).IsEqualTo(4);
        await Assert.That(Ordinal(OpenUsdNativeMediaSchemaKind.SpatialAudio)).IsEqualTo(0);
        await Assert.That(Ordinal(OpenUsdNativeMediaSchemaKind.AssetPreviewsApi)).IsEqualTo(1);
        await Assert.That(Ordinal(OpenUsdNativeProcSchemaKind.GenerativeProcedural)).IsEqualTo(0);
        await Assert.That(Ordinal(OpenUsdNativeUiSchemaKind.Backdrop)).IsEqualTo(0);
        await Assert.That(Ordinal(OpenUsdNativeUiSchemaKind.NodeGraphNodeApi)).IsEqualTo(1);
        await Assert.That(Ordinal(OpenUsdNativeUiSchemaKind.SceneGraphPrimApi)).IsEqualTo(2);
    }

    [Test]
    public async Task DetachedPrimsAreRejectedWithoutNativeDispatch()
    {
        UsdPrim detached = default;

        await Assert.That(UsdVolVolume.TryWrap(detached, out _)).IsFalse();
        await Assert.That(UsdRenderSettings.TryWrap(detached, out _)).IsFalse();
        await Assert.That(UsdMediaSpatialAudio.TryWrap(detached, out _)).IsFalse();
        await Assert.That(UsdProcGenerativeProcedural.TryWrap(detached, out _)).IsFalse();
        await Assert.That(UsdUIBackdrop.TryWrap(detached, out _)).IsFalse();
        await Assert.That(UsdUINodeGraphNode.TryWrap(detached, out _)).IsFalse();

        await Assert.That(Capture(() => UsdVolVolume.Wrap(detached))).IsTypeOf<ArgumentException>();
        await Assert.That(Capture(() => UsdRenderSettings.Wrap(detached))).IsTypeOf<ArgumentException>();
        await Assert.That(Capture(() => UsdMediaSpatialAudio.Wrap(detached))).IsTypeOf<ArgumentException>();
        await Assert.That(Capture(() => UsdProcGenerativeProcedural.Wrap(detached))).IsTypeOf<ArgumentException>();
        await Assert.That(Capture(() => UsdUIBackdrop.Wrap(detached))).IsTypeOf<ArgumentException>();
        await Assert.That(Capture(() => UsdUINodeGraphNode.Wrap(detached))).IsTypeOf<ArgumentException>();
    }

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }
        throw new InvalidOperationException("Expected an exception.");
    }

    private static int Ordinal<T>(T value)
        where T : struct, Enum =>
        Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
}
