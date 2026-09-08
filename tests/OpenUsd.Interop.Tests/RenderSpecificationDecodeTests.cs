// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop.Tests;

public sealed class RenderSpecificationDecodeTests
{
    [Test]
    public async Task CanonicalOwnedViewDecodesAllFieldsAndRepeatedVariableIndices()
    {
        OpenUsdNativeRenderSpecification snapshot = Decode()!;
        await Assert.That(snapshot.SettingsPath).IsEqualTo("/Settings");
        await Assert.That(snapshot.RenderingColorSpace).IsEqualTo("lin_rec709");
        await Assert.That(snapshot.Products[0].Path).IsEqualTo("/Product");
        await Assert.That(snapshot.Products[0].Values.Width).IsEqualTo(640);
        await Assert.That(snapshot.Products[0].Values.ApertureHeight).IsEqualTo(18f);
        await Assert.That(snapshot.Products[0].RenderVariableIndices.Length).IsEqualTo(2);
        await Assert.That(snapshot.Products[0].RenderVariableIndices[0]).IsEqualTo(0);
        await Assert.That(snapshot.Products[0].RenderVariableIndices[1]).IsEqualTo(0);
        await Assert.That(snapshot.RenderVariables[0].SourceName).IsEqualTo("Ci");
        await Assert.That(snapshot.NamespacedSettingNames[0]).IsEqualTo("renderer:samples");
        await Assert.That(snapshot.Products[0].NamespacedSettingNames[0]).IsEqualTo("renderer:filter");
        await Assert.That(snapshot.RenderVariables[0].NamespacedSettingNames[0]).IsEqualTo("renderer:source");
    }

    [Test]
    public async Task EmptyDefaultViewDecodesAsAbsent()
    {
        OpenUsdNativeRenderSpecification? result = DecodeAbsent();
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ViewAndRecordLayoutsMatchTheCAbi()
    {
        await Assert.That(Marshal.SizeOf<OpenUsdNativeRenderProductRecord>()).IsEqualTo(48 + (4 * IntPtr.Size));
        await Assert.That(Marshal.SizeOf<OpenUsdNativeRenderVariableRecord>()).IsEqualTo(2 * IntPtr.Size);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeRuntime.NativeRenderSpecificationView>())
            .IsEqualTo(16 + (17 * IntPtr.Size));
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeRenderProductRecord>("<StringOffset>k__BackingField").ToInt32())
            .IsEqualTo(48);
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeRuntime.NativeRenderSpecificationView>("Products").ToInt32())
            .IsEqualTo(16);
    }

    [Test]
    [Arguments("header-size")]
    [Arguments("version")]
    [Arguments("reserved")]
    [Arguments("presence-flag")]
    [Arguments("absent-with-data")]
    [Arguments("product-count-budget")]
    [Arguments("variable-count-budget")]
    [Arguments("index-count-overflow")]
    [Arguments("string-count-budget")]
    [Arguments("byte-count-budget")]
    [Arguments("purpose-count-budget")]
    [Arguments("setting-count-overflow")]
    [Arguments("products-null")]
    [Arguments("products-misaligned")]
    [Arguments("variables-null")]
    [Arguments("indices-null")]
    [Arguments("offsets-null")]
    [Arguments("data-null")]
    [Arguments("pointer-overflow")]
    [Arguments("product-byte-size")]
    [Arguments("variable-byte-size")]
    [Arguments("index-byte-size")]
    [Arguments("offset-byte-size")]
    [Arguments("string-offset-noncanonical")]
    [Arguments("unterminated-string")]
    [Arguments("embedded-terminator")]
    [Arguments("invalid-utf8")]
    [Arguments("invalid-settings-path")]
    [Arguments("product-string-range")]
    [Arguments("product-setting-range-overflow")]
    [Arguments("product-index-range")]
    [Arguments("product-index-range-overflow")]
    [Arguments("product-reserved")]
    [Arguments("product-bool")]
    [Arguments("zero-width")]
    [Arguments("negative-height")]
    [Arguments("zero-aspect")]
    [Arguments("nan-aspect")]
    [Arguments("infinite-aperture")]
    [Arguments("zero-aperture")]
    [Arguments("infinite-window")]
    [Arguments("empty-window")]
    [Arguments("inverted-window")]
    [Arguments("overflowing-window-extent")]
    [Arguments("variable-string-range")]
    [Arguments("variable-setting-range-overflow")]
    [Arguments("out-of-range-variable-index")]
    public async Task MalformedNativeDataNeverEscapesAsASnapshot(string corruption)
    {
        await Assert.That(() => Decode(corruption)).Throws<OpenUsdNativeException>();
    }

    private static unsafe OpenUsdNativeRenderSpecification? DecodeAbsent() =>
        OpenUsdNativeRuntime.DecodeRenderSpecification(new OpenUsdNativeRuntime.NativeRenderSpecificationView
        {
            StructSize = (uint)sizeof(OpenUsdNativeRuntime.NativeRenderSpecificationView),
            Version = 1
        });

    private static unsafe OpenUsdNativeRenderSpecification? Decode(string corruption = "")
    {
        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(
        [
            "/Settings", "lin_rec709", "default", "render", "full", "renderer:samples",
            "/Product", "image.exr", "raster", "/Camera", "expandAperture", "renderer:filter",
            "/Var", "color3f", "Ci", "raw", "renderer:source"
        ]);
        OpenUsdNativeRenderProductRecord[] products =
        [
            new(640, 480, 1, 24, 18, 0, 0, 1, 1, 1, 0, 0, 6, 1, 0, 2)
        ];
        OpenUsdNativeRenderVariableRecord[] variables = [new(12, 1)];
        uint[] indices = [0, 0];
        fixed (byte* dataPointer = data)
        fixed (nuint* offsetsPointer = offsets)
        fixed (OpenUsdNativeRenderProductRecord* productsPointer = products)
        fixed (OpenUsdNativeRenderVariableRecord* variablesPointer = variables)
        fixed (uint* indicesPointer = indices)
        {
            var view = new OpenUsdNativeRuntime.NativeRenderSpecificationView
            {
                StructSize = (uint)sizeof(OpenUsdNativeRuntime.NativeRenderSpecificationView),
                Version = 1,
                HasSettings = 1,
                Products = productsPointer,
                ProductsSize = (nuint)sizeof(OpenUsdNativeRenderProductRecord),
                ProductCount = 1,
                Variables = variablesPointer,
                VariablesSize = (nuint)sizeof(OpenUsdNativeRenderVariableRecord),
                VariableCount = 1,
                RenderVarIndices = indicesPointer,
                RenderVarIndicesSize = 2 * sizeof(uint),
                RenderVarIndexCount = 2,
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetsPointer,
                OffsetsSize = (nuint)(offsets.Length * sizeof(nuint)),
                StringCount = (nuint)offsets.Length,
                IncludedPurposeCount = 2,
                MaterialBindingPurposeCount = 1,
                NamespacedSettingCount = 1
            };
            switch (corruption)
            {
                case "header-size":
                    view.StructSize--;
                    break;
                case "version":
                    view.Version++;
                    break;
                case "reserved":
                    view.Reserved = 1;
                    break;
                case "presence-flag":
                    view.HasSettings = 2;
                    break;
                case "absent-with-data":
                    view.HasSettings = 0;
                    break;
                case "product-count-budget":
                    view.ProductCount = 1025;
                    break;
                case "variable-count-budget":
                    view.VariableCount = 4097;
                    break;
                case "index-count-overflow":
                    view.RenderVarIndexCount = nuint.MaxValue;
                    break;
                case "string-count-budget":
                    view.StringCount = 65537;
                    break;
                case "byte-count-budget":
                    view.DataSize = (8 << 20) + 1;
                    break;
                case "purpose-count-budget":
                    view.IncludedPurposeCount = 65;
                    break;
                case "setting-count-overflow":
                    view.NamespacedSettingCount = nuint.MaxValue;
                    break;
                case "products-null":
                    view.Products = null;
                    break;
                case "products-misaligned":
                    view.Products = (OpenUsdNativeRenderProductRecord*)((byte*)productsPointer + 1);
                    break;
                case "variables-null":
                    view.Variables = null;
                    break;
                case "indices-null":
                    view.RenderVarIndices = null;
                    break;
                case "offsets-null":
                    view.Offsets = null;
                    break;
                case "data-null":
                    view.Data = null;
                    break;
                case "pointer-overflow":
                    view.Data = (byte*)(nuint.MaxValue - 2);
                    break;
                case "product-byte-size":
                    view.ProductsSize--;
                    break;
                case "variable-byte-size":
                    view.VariablesSize--;
                    break;
                case "index-byte-size":
                    view.RenderVarIndicesSize--;
                    break;
                case "offset-byte-size":
                    view.OffsetsSize--;
                    break;
                case "string-offset-noncanonical":
                    offsets[1]++;
                    break;
                case "unterminated-string":
                    data[^1] = 65;
                    break;
                case "embedded-terminator":
                    data[2] = 0;
                    break;
                case "invalid-utf8":
                    data[2] = 255;
                    break;
                case "invalid-settings-path":
                    data[0] = 65;
                    break;
                case "product-string-range":
                    products[0] = products[0] with { StringOffset = 0 };
                    break;
                case "product-setting-range-overflow":
                    products[0] = products[0] with { NamespacedSettingCount = nuint.MaxValue };
                    break;
                case "product-index-range":
                    products[0] = products[0] with { RenderVarIndexOffset = 1 };
                    break;
                case "product-index-range-overflow":
                    products[0] = products[0] with { RenderVarIndexCount = nuint.MaxValue };
                    break;
                case "product-reserved":
                    products[0] = products[0] with { Reserved = 1 };
                    break;
                case "product-bool":
                    products[0] = products[0] with { DisableDepthOfField = -1 };
                    break;
                case "zero-width":
                    products[0] = products[0] with { Width = 0 };
                    break;
                case "negative-height":
                    products[0] = products[0] with { Height = -1 };
                    break;
                case "zero-aspect":
                    products[0] = products[0] with { PixelAspectRatio = 0 };
                    break;
                case "nan-aspect":
                    products[0] = products[0] with { PixelAspectRatio = float.NaN };
                    break;
                case "infinite-aperture":
                    products[0] = products[0] with { ApertureWidth = float.PositiveInfinity };
                    break;
                case "zero-aperture":
                    products[0] = products[0] with { ApertureHeight = 0 };
                    break;
                case "infinite-window":
                    products[0] = products[0] with { DataWindowMaxX = float.PositiveInfinity };
                    break;
                case "empty-window":
                    products[0] = products[0] with { DataWindowMaxX = 0 };
                    break;
                case "inverted-window":
                    products[0] = products[0] with { DataWindowMinY = 2 };
                    break;
                case "overflowing-window-extent":
                    products[0] = products[0] with
                    { DataWindowMinX = -float.MaxValue, DataWindowMaxX = float.MaxValue };
                    break;
                case "variable-string-range":
                    variables[0] = variables[0] with { StringOffset = 0 };
                    break;
                case "variable-setting-range-overflow":
                    variables[0] = variables[0] with { NamespacedSettingCount = nuint.MaxValue };
                    break;
                case "out-of-range-variable-index":
                    indices[0] = 1;
                    break;
                case "":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corruption));
            }
            return OpenUsdNativeRuntime.DecodeRenderSpecification(view);
        }
    }
}
