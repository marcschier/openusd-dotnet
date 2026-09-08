// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Geom;
using OpenUsd.Render;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderProductRequestTests
{
    [Test]
    public async Task RequestedOutputsCannotBeReplacedAfterRequestCreation()
    {
        var request = new RenderProductRequest(CreateSpecification(), 0);
        await Assert.That(request.Outputs is System.Collections.ICollection).IsFalse()
            .Because("SyncRoot must not provide a mutable array of admitted render-variable declarations");
        await Assert.That(() => ((IList<UsdRenderVariableSpecification>)request.Outputs)[0] =
            request.Outputs[1]).Throws<InvalidCastException>();
        await Assert.That(request.Outputs[0].SourceName).IsEqualTo("color");
        await Assert.That(request.Outputs[1].SourceType).IsEqualTo("lpe");
        await Assert.That(request.Specification.RenderVariables[0]).IsSameReferenceAs(request.Outputs[0]);
    }

    [Test]
    public async Task ProductRequestPreparesTheSampledCameraAndPreservesUnimplementedOutputs()
    {
        UsdRenderSpecification specification = CreateSpecification();
        var request = new RenderProductRequest(specification, 0);
        RenderPreparedFrame frame = request.PrepareFrame(
            UsdMatrix4d.Identity,
            new UsdGeomCameraState(
                UsdGeomCameraProjection.Orthographic, -1, 1, -1, 1, 1, 11,
                0, 20, 20, 0, 0, 0, 0),
            24);

        await Assert.That(frame.TimeCode).IsEqualTo(24d);
        await Assert.That(frame.OutputDimensions).IsEqualTo(new ViewportDimensions(1, 2));
        await Assert.That(frame.DataWindowMinX).IsEqualTo(1);
        await Assert.That(frame.DataWindowMinY).IsEqualTo(0);
        await Assert.That(frame.Camera.Projection).IsEqualTo(new Matrix4x4(
            4, 0, 0, 0,
            0, 2, 0, 0,
            0, 0, -0.2f, 0,
            1, 1, -1.2f, 1));
        await Assert.That(request.Specification).IsSameReferenceAs(specification);
        await Assert.That(request.Outputs.Count).IsEqualTo(2);
        await Assert.That(request.Outputs[0].SourceName).IsEqualTo("color");
        await Assert.That(request.Outputs[1].SourceName).IsEqualTo("rendererSpecificExpression");
        await Assert.That(request.Outputs[1].SourceType).IsEqualTo("lpe");
        await Assert.That(request.Product.NamespacedSettingNames).Contains("renderer:customSetting");
        await Assert.That(frame.Request).IsSameReferenceAs(request);
    }

    [Test]
    public async Task ExplicitOverridesDoNotModifyAuthoredProductsOrOtherRequests()
    {
        UsdRenderSpecification specification = CreateSpecification();
        var original = new RenderProductRequest(specification, 0);
        var overridden = new RenderProductRequest(specification, 0, new RenderProductOverrides(
            cameraPath: "/World/ReviewCamera",
            resolution: new ViewportDimensions(200, 100),
            pixelAspectRatio: 2,
            aspectRatioConformPolicy: "cropAperture",
            dataWindowNdc: new UsdVec4f(0, 0, 1, 1)));
        RenderPreparedFrame frame = overridden.PrepareFrame(
            UsdMatrix4d.Identity,
            new UsdGeomCameraState(
                UsdGeomCameraProjection.Orthographic, -1, 1, -1, 1, 1, 11,
                0, 20, 20, 0, 0, 0, 0),
            12);

        await Assert.That(original.CameraPath).IsEqualTo("/World/Camera");
        await Assert.That(overridden.CameraPath).IsEqualTo("/World/ReviewCamera");
        await Assert.That(frame.OutputDimensions).IsEqualTo(new ViewportDimensions(200, 100));
        await Assert.That(frame.PixelAspectRatio).IsEqualTo(2d);
        await Assert.That(frame.Camera.Projection.M11).IsEqualTo(1f);
        await Assert.That(frame.Camera.Projection.M22).IsEqualTo(4f);
        await Assert.That(specification.Products[0].Width).IsEqualTo(4);
        await Assert.That(specification.Products[0].DataWindowNdc)
            .IsEqualTo(new UsdVec4f(0.375f, 0.125f, 0.625f, 0.625f));
        await Assert.That(original.Overrides.Resolution).IsNull();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(1)]
    public async Task MissingProductSelectionDoesNotBecomeTheFirstProduct(int index)
    {
        await Assert.That(() => new RenderProductRequest(CreateSpecification(), index))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public async Task NonFiniteFrameTimesCannotProduceAnApparentlyValidRequest(double time)
    {
        var request = new RenderProductRequest(CreateSpecification(), 0);
        await Assert.That(() => request.PrepareFrame(UsdMatrix4d.Identity, default, time))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task UnsupportedProductTypesRemainInspectableButCannotBecomeRasterFrames()
    {
        var request = new RenderProductRequest(CreateSpecification("pointcloud"), 0);

        await Assert.That(request.Product.ProductType).IsEqualTo("pointcloud");
        await Assert.That(() => request.PrepareFrame(UsdMatrix4d.Identity, default, 0))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task SingularCameraTransformsCannotProduceRasterFrames()
    {
        var request = new RenderProductRequest(CreateSpecification(), 0);
        await Assert.That(() => request.PrepareFrame(default, default, 0)).Throws<ArgumentException>();
    }

    [Test]
    public async Task InvalidOverridesCannotBeStoredAsAcceptedChoices()
    {
        await Assert.That(() => new RenderProductOverrides(cameraPath: "relative/camera"))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderProductOverrides(resolution: ViewportDimensions.Empty))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderProductOverrides(pixelAspectRatio: double.NaN))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderProductOverrides(aspectRatioConformPolicy: "invented"))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderProductOverrides(dataWindowNdc: new UsdVec4f(1, 0, 0, 1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static UsdRenderSpecification CreateSpecification(string productType = "raster") => new(
        "/Render/Settings",
        [
            new UsdRenderProductSpecification(
                "/Render/Product", "image.exr", productType, "/World/Camera", 4, 4, 1,
                "expandAperture", new UsdVec2f(50, 25),
                new UsdVec4f(0.375f, 0.125f, 0.625f, 0.625f),
                true, true, [0, 1], ["renderer:customSetting"])
        ],
        [
            new UsdRenderVariableSpecification("/Render/Color", "color3f", "color", "raw", []),
            new UsdRenderVariableSpecification("/Render/Custom", "float", "rendererSpecificExpression", "lpe", [])
        ],
        ["default", "render"],
        ["full", "preview"],
        "lin_rec709_scene",
        []);
}
