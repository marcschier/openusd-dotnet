// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerPropertyEditParserTests
{
    [Test]
    public async Task FocusedTransformAndLightValuesKeepTheirDeclaredNativeTypes()
    {
        await Assert.That(ViewerPropertyEditParser.TryParse(
            "double3", "(1.5, -2, 3)", out UsdLayerEditValue? transform, out _)).IsTrue();
        await Assert.That(transform!.Kind).IsEqualTo(UsdLayerEditValueKind.Vec3d);
        await Assert.That(transform.AsVec3d()).IsEqualTo(new UsdVec3d(1.5, -2, 3));
        await Assert.That(ViewerPropertyEditParser.TryParse(
            "float", "2.5", out UsdLayerEditValue? intensity, out _)).IsTrue();
        await Assert.That(intensity!.Kind).IsEqualTo(UsdLayerEditValueKind.Float);
        await Assert.That(intensity.AsFloat()).IsEqualTo(2.5f);
        await Assert.That(ViewerPropertyEditParser.TryParse(
            "float", "1e100", out _, out string error)).IsFalse();
        await Assert.That(error).IsNotEmpty();
        await Assert.That(ViewerPropertyEditParser.Supports("matrix3d")).IsFalse();
    }

    [Test]
    public async Task AssetPathsRemainTypedAuthoredPathsWithoutResolvingOrRebasingThem()
    {
        const string path = "textures/brick wall.png";
        await Assert.That(ViewerPropertyEditParser.Supports("asset")).IsTrue();
        await Assert.That(ViewerPropertyEditParser.TryParse("asset", path,
            out UsdLayerEditValue? value, out string error)).IsTrue();
        await Assert.That(error).IsEmpty();
        await Assert.That(value!.Kind).IsEqualTo(UsdLayerEditValueKind.AssetPath);
        await Assert.That(value.AsAssetPath()).IsEqualTo(new UsdLayerAssetPath(path, string.Empty, string.Empty));
        await Assert.That(ViewerPropertyEditParser.Format(value)).IsEqualTo(path);
        await Assert.That(ViewerPropertyEditParser.TryParse("asset", string.Empty, out value, out _)).IsTrue();
        await Assert.That(value!.Kind).IsEqualTo(UsdLayerEditValueKind.AssetPath);
        await Assert.That(value.AsAssetPath().AuthoredPath).IsEqualTo(string.Empty);
        await Assert.That(ViewerPropertyEditParser.TryParse("asset", "bad\0path", out _, out error)).IsFalse();
        await Assert.That(error).IsNotEmpty();
    }

    [Test]
    [Arguments("float2")]
    [Arguments("texCoord2f")]
    public async Task TwoComponentValuesPreserveFloatPrecisionAndRejectWrongArity(string typeName)
    {
        await Assert.That(ViewerPropertyEditParser.Supports(typeName)).IsTrue();
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "(0.125, 1000)",
            out UsdLayerEditValue? value, out _)).IsTrue();
        await Assert.That(value!.AsVec2f()).IsEqualTo(new UsdVec2f(0.125f, 1000));
        await Assert.That(ViewerPropertyEditParser.Format(value)).IsEqualTo("(0.125, 1000)");
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "1, 2, 3", out _, out string error)).IsFalse();
        await Assert.That(error).IsNotEmpty();
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "1e100 2", out _, out _)).IsFalse();
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "NaN 2", out _, out _)).IsFalse();
    }

    [Test]
    [Arguments("float4")]
    [Arguments("color4f")]
    [Arguments("quatf")]
    public async Task FourComponentValuesKeepOrderAndNeverNormalizeQuaternionInputs(string typeName)
    {
        await Assert.That(ViewerPropertyEditParser.Supports(typeName)).IsTrue();
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "(0.5, 0.25, -0.125, 0)",
            out UsdLayerEditValue? value, out _)).IsTrue();
        if (typeName == "quatf")
        {
            await Assert.That(value!.Kind).IsEqualTo(UsdLayerEditValueKind.Quatf);
            await Assert.That(value.AsQuatf()).IsEqualTo(new UsdQuatf(0.5f, 0.25f, -0.125f, 0));
        }
        else
        {
            await Assert.That(value!.Kind).IsEqualTo(UsdLayerEditValueKind.Vec4f);
            await Assert.That(value.AsVec4f()).IsEqualTo(new UsdVec4f(0.5f, 0.25f, -0.125f, 0));
        }
        await Assert.That(ViewerPropertyEditParser.Format(value)).IsEqualTo("(0.5, 0.25, -0.125, 0)");
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "1 2 3", out _, out _)).IsFalse();
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "1 2 3 4 5", out _, out _)).IsFalse();
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, "1e100 2 3 4", out _, out _)).IsFalse();
    }

    [Test]
    public async Task MatrixValuesUseExactRowMajorDoubleComponentsWithoutTransposingTranslation()
    {
        const string text = "((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (4.125, 5, -6, 1))";
        await Assert.That(ViewerPropertyEditParser.Supports("matrix4d")).IsTrue();
        await Assert.That(ViewerPropertyEditParser.TryParse("matrix4d", text,
            out UsdLayerEditValue? value, out _)).IsTrue();
        await Assert.That(value!.Kind).IsEqualTo(UsdLayerEditValueKind.Matrix4d);
        await Assert.That(value.AsMatrix4d()).IsEqualTo(UsdMatrix4d.CreateTranslation(4.125, 5, -6));
        await Assert.That(ViewerPropertyEditParser.Format(value)).IsEqualTo(text);
        await Assert.That(ViewerPropertyEditParser.TryParse("matrix4d",
            "[1 2 3 4; 5 6 7 8; 9 10 11 12; 13 14 15 16]", out value, out _)).IsTrue();
        await Assert.That(value!.AsMatrix4d()[2, 1]).IsEqualTo(10d);
        await Assert.That(value.AsMatrix4d()[3, 0]).IsEqualTo(13d);
        await Assert.That(ViewerPropertyEditParser.TryParse("matrix4d", "1 2 3 4", out _, out string error)).IsFalse();
        await Assert.That(error).IsNotEmpty();
        await Assert.That(ViewerPropertyEditParser.TryParse("matrix4d",
            "1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 Infinity", out _, out _)).IsFalse();
    }

    [Test]
    [Arguments("float2", "-0 0.125")]
    [Arguments("float4", "-0 0.125 0.25 0.5")]
    [Arguments("quatf", "-0 0.125 0.25 0.5")]
    [Arguments("matrix4d", "-0 0 0 0 0 1 0 0 0 0 1 0 1e100 2 3 1")]
    public async Task FormattingAndParsingPreserveExactAcceptedComponentBytes(string typeName, string text)
    {
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, text,
            out UsdLayerEditValue? original, out _)).IsTrue();
        string formatted = ViewerPropertyEditParser.Format(original!);
        await Assert.That(formatted).Contains("-0");
        await Assert.That(ViewerPropertyEditParser.TryParse(typeName, formatted,
            out UsdLayerEditValue? parsed, out string error)).IsTrue().Because(error);
        await Assert.That(parsed!.Equals(original)).IsTrue();
    }
}
