// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;
using OpenUsd.Editing;
using OpenUsd.Interop;
using OpenUsd.Render;

namespace OpenUsd.Tests;

public sealed class UsdRenderSpecificationNativeTests
{
    [Test]
    public async Task NativeSpecificationComposesInheritanceForwardingAndDefaultTimeConformance()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            (
                renderSettingsPrimPath = "/Settings"
            )
            def Camera "Camera"
            {
                float horizontalAperture = 40
                float verticalAperture = 20
                float horizontalAperture.timeSamples = { 1: 80, 2: 120 }
            }
            def Scope "Forward"
            {
                rel camera = </Camera>
                rel products = [</Inherited>, </Override>]
                rel vars = [</Beauty>, </Depth>]
            }
            def RenderSettings "Settings"
            {
                rel camera = </Forward.camera>
                rel products = </Forward.products>
                uniform int2 resolution = (800, 400)
                uniform float pixelAspectRatio = 1
                uniform token aspectRatioConformPolicy = "expandAperture"
                uniform float4 dataWindowNDC = (0.125, 0.25, 0.875, 1)
                uniform bool disableMotionBlur = true
                uniform token[] includedPurposes = ["proxy", "render"]
                uniform token[] materialBindingPurposes = ["full", "preview"]
                uniform token renderingColorSpace = "lin_rec709"
            }
            def RenderProduct "Inherited"
            {
                token productName = "inherited.exr"
                rel orderedVars = </Forward.vars>
            }
            def RenderProduct "Override"
            {
                uniform int2 resolution = (400, 400)
                uniform token aspectRatioConformPolicy = "cropAperture"
                uniform float4 dataWindowNDC = (-0.25, 0, 1.25, 1)
                uniform bool disableMotionBlur = false
                uniform bool disableDepthOfField = true
                token productName = "override.exr"
                rel orderedVars = [</Depth>, </Beauty>]
            }
            def RenderVar "Beauty"
            {
                token dataType = "color3f"
                string sourceName = "Ci"
                token sourceType = "raw"
            }
            def RenderVar "Depth"
            {
                token dataType = "float"
                string sourceName = "cameraDepth"
                token sourceType = "raw"
            }
            """);
        ulong revision = stage.ChangeSerial;
        UsdRenderSpecification snapshot = stage.GetRenderSpecification()!;
        await Assert.That(stage.ChangeSerial).IsEqualTo(revision);
        await Assert.That(snapshot.SettingsPath).IsEqualTo("/Settings");
        await Assert.That(snapshot.Products.Select(product => product.Path).ToArray())
            .IsEquivalentTo(["/Inherited", "/Override"]);
        await Assert.That(snapshot.Products[0].Path).IsEqualTo("/Inherited");
        await Assert.That(snapshot.Products[1].Path).IsEqualTo("/Override");
        await Assert.That(snapshot.Products[0].Width).IsEqualTo(800);
        await Assert.That(snapshot.Products[0].Height).IsEqualTo(400);
        await Assert.That(snapshot.Products[0].CameraPath).IsEqualTo("/Camera");
        await Assert.That(snapshot.Products[0].ApertureSize).IsEqualTo(new UsdVec2f(40, 20));
        await Assert.That(snapshot.Products[0].DisableMotionBlur).IsTrue();
        await Assert.That(snapshot.Products[0].DataWindowNdc).IsEqualTo(new UsdVec4f(0.125f, 0.25f, 0.875f, 1));
        await Assert.That(snapshot.Products[1].ApertureSize).IsEqualTo(new UsdVec2f(20, 20));
        await Assert.That(snapshot.Products[1].DisableMotionBlur).IsFalse();
        await Assert.That(snapshot.Products[1].DisableDepthOfField).IsTrue();
        await Assert.That(snapshot.Products[1].DataWindowNdc).IsEqualTo(new UsdVec4f(-0.25f, 0, 1.25f, 1));
        await Assert.That(snapshot.Products[0].RenderVariableIndices.ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(snapshot.Products[1].RenderVariableIndices.ToArray()).IsEquivalentTo([1, 0]);
        await Assert.That(snapshot.Products[0].RenderVariableIndices[0]).IsEqualTo(0);
        await Assert.That(snapshot.Products[1].RenderVariableIndices[0]).IsEqualTo(1);
        await Assert.That(snapshot.RenderVariables.Count).IsEqualTo(2);
        await Assert.That(snapshot.RenderVariables[0].Path).IsEqualTo("/Beauty");
        await Assert.That(snapshot.RenderVariables[1].SourceName).IsEqualTo("cameraDepth");
        await Assert.That(snapshot.IncludedPurposes.ToArray()).IsEquivalentTo(["proxy", "render"]);
        await Assert.That(snapshot.MaterialBindingPurposes.ToArray()).IsEquivalentTo(["full", "preview"]);
        await Assert.That(snapshot.MaterialBindingPurposes[0]).IsEqualTo("full");
        await Assert.That(snapshot.MaterialBindingPurposes[1]).IsEqualTo("preview");
        await Assert.That(snapshot.RenderingColorSpace).IsEqualTo("lin_rec709");
    }

    [Test]
    public async Task MissingDefaultIsAbsentButExplicitEmptySettingsRequestsZeroOutputs()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            def RenderSettings "Settings" {}
            def Xform "Wrong" {}
            """);
        await Assert.That(stage.GetRenderSpecification()).IsNull();
        UsdRenderSpecification explicitSettings = stage.GetRenderSpecification("/Settings")!;
        await Assert.That(explicitSettings.SettingsPath).IsEqualTo("/Settings");
        await Assert.That(explicitSettings.Products.Count).IsEqualTo(0);
        await Assert.That(explicitSettings.RenderVariables.Count).IsEqualTo(0);
        await Assert.That(explicitSettings.RenderingColorSpace).IsEqualTo(string.Empty);
        await Assert.That(() => stage.GetRenderSpecification("/Missing")).Throws<OpenUsdNativeException>();
        await Assert.That(() => stage.GetRenderSpecification("/Wrong")).Throws<OpenUsdNativeException>();
        await Assert.That(() => stage.GetRenderSpecification(string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => stage.GetRenderSpecification("relative")).Throws<ArgumentException>();
        await Assert.That(() => stage.GetRenderSpecification("/Settings\0Ignored")).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("/Missing")]
    [Arguments("/Wrong")]
    [Arguments("relative")]
    [Arguments("")]
    public async Task AuthoredInvalidDefaultIsAnActionableFailure(string defaultPath)
    {
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            (
                renderSettingsPrimPath = "{{defaultPath}}"
            )
            def Xform "Wrong" {}
            """);
        await Assert.That(() => stage.GetRenderSpecification()).Throws<OpenUsdNativeException>();
    }

    [Test]
    [Arguments("products", "/Missing")]
    [Arguments("products", "/Wrong")]
    [Arguments("products", "/Wrong.missing")]
    [Arguments("camera", "/Missing")]
    [Arguments("camera", "/Wrong")]
    [Arguments("orderedVars", "/Missing")]
    [Arguments("orderedVars", "/Wrong")]
    public async Task InvalidRelatedPrimsAreNotSilentlyOmitted(string relationship, string target)
    {
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            def Camera "Camera" {}
            def Xform "Wrong" {}
            def RenderSettings "Settings"
            {
                rel camera = <{{(relationship == "camera" ? target : "/Camera")}}>
                rel products = <{{(relationship == "products" ? target : "/Product")}}>
            }
            def RenderProduct "Product"
            {
                rel orderedVars = <{{(relationship == "orderedVars" ? target : "/Var")}}>
            }
            def RenderVar "Var" {}
            """);
        await Assert.That(() => stage.GetRenderSpecification("/Settings")).Throws<OpenUsdNativeException>();
    }

    [Test]
    [Arguments("camera")]
    [Arguments("products")]
    [Arguments("orderedVars")]
    [Arguments("renderingColorSpace")]
    public async Task WrongPropertyKindsCannotBecomeAbsentSettingsOrOutputs(string property)
    {
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                {{(property == "camera" ? "token camera = \"wrong\"" : "rel camera = </Camera>")}}
                {{(property == "products" ? "token products = \"wrong\"" : "rel products = </Product>")}}
                {{(property == "renderingColorSpace" ? "rel renderingColorSpace = </Camera>" : "")}}
            }
            def RenderProduct "Product"
            {
                rel camera = </Camera>
                {{(property == "orderedVars" ? "token orderedVars = \"wrong\"" : "rel orderedVars = </Var>")}}
            }
            def RenderVar "Var" {}
            """);
        await Assert.That(() => stage.GetRenderSpecification("/Settings")).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task CyclicForwardingDoesNotBecomeAnEmptySuccessfulRequest()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            def RenderSettings "Settings"
            {
                rel products = </Forward.one>
            }
            def Scope "Forward"
            {
                rel one = </Forward.two>
                rel two = </Forward.one>
            }
            """);
        await Assert.That(() => stage.GetRenderSpecification("/Settings")).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task TargetlessForwardedProductsRemainAPresentZeroOutputSpecification()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            (
                renderSettingsPrimPath = "/Settings"
            )
            def Scope "Forward"
            {
                rel targetless
            }
            def RenderSettings "Settings"
            {
                rel products = </Forward.targetless>
            }
            """);
        UsdRenderSpecification? snapshot = stage.GetRenderSpecification();
        await Assert.That(snapshot).IsNotNull();
        await Assert.That(snapshot!.SettingsPath).IsEqualTo("/Settings");
        await Assert.That(snapshot.Products.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TargetlessForwardedProductCameraInheritsSettingsCamera()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            def Scope "Forward"
            {
                rel targetless
            }
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = </Product>
            }
            def RenderProduct "Product"
            {
                rel camera = </Forward.targetless>
            }
            """);
        UsdRenderSpecification snapshot = stage.GetRenderSpecification("/Settings")!;
        await Assert.That(snapshot.Products.Count).IsEqualTo(1);
        await Assert.That(snapshot.Products[0].CameraPath).IsEqualTo("/Camera");
    }

    [Test]
    [Arguments("uniform int2 resolution = (0, 400)")]
    [Arguments("uniform float pixelAspectRatio = -1")]
    [Arguments("uniform float4 dataWindowNDC = (1, 0, 0, 1)")]
    [Arguments("uniform token aspectRatioConformPolicy = \"unknownPolicy\"")]
    [Arguments("uniform int2 resolution = None")]
    public async Task InvalidSettingsAreRejectedBeforeNativeConformance(string opinion)
    {
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = </Product>
                {{opinion}}
            }
            def RenderProduct "Product" {}
            """);
        await Assert.That(() => stage.GetRenderSpecification("/Settings")).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task NativePixelAspectConformanceUsesTheOverridingCamera()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            def Camera "Base"
            {
                float horizontalAperture = 40
                float verticalAperture = 20
            }
            def Camera "Override"
            {
                float horizontalAperture = 36
                float verticalAperture = 24
            }
            def RenderSettings "Settings"
            {
                rel camera = </Base>
                rel products = </Product>
                uniform int2 resolution = (400, 400)
                uniform token aspectRatioConformPolicy = "adjustPixelAspectRatio"
            }
            def RenderProduct "Product"
            {
                rel camera = </Override>
            }
            """);
        UsdRenderProductSpecification product = stage.GetRenderSpecification("/Settings")!.Products[0];
        await Assert.That(product.CameraPath).IsEqualTo("/Override");
        await Assert.That(product.PixelAspectRatio).IsEqualTo(1.5f);
        await Assert.That(product.ApertureSize).IsEqualTo(new UsdVec2f(36, 24));
    }

    [Test]
    public async Task UnsupportedVariablesAndCustomSettingNamesSurviveStageDisposal()
    {
        UsdRenderSpecification snapshot;
        using (UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = </Product>
                custom int renderer:samples = 64
                custom string note = "not interpreted"
                rel custom:dependency = </Pass>
            }
            def RenderProduct "Product"
            {
                token productType = "deepRaster"
                rel orderedVars = </Unsupported>
                custom bool renderer:dither = true
            }
            def RenderVar "Unsupported"
            {
                token dataType = "color3f"
                token sourceType = "lpe"
                string sourceName = "C<RD>L"
                custom token renderer:filter = "customFilter"
            }
            def RenderPass "Pass"
            {
                string[] command = ["never-execute-a-scene-authored-command"]
            }
            """))
        {
            snapshot = stage.GetRenderSpecification("/Settings")!;
        }
        await Assert.That(snapshot.NamespacedSettingNames.ToArray())
            .IsEquivalentTo(["custom:dependency", "note", "renderer:samples"]);
        await Assert.That(snapshot.Products[0].ProductType).IsEqualTo("deepRaster");
        await Assert.That(snapshot.Products[0].NamespacedSettingNames[0]).IsEqualTo("renderer:dither");
        await Assert.That(snapshot.RenderVariables[0].SourceType).IsEqualTo("lpe");
        await Assert.That(snapshot.RenderVariables[0].SourceName).IsEqualTo("C<RD>L");
        await Assert.That(snapshot.RenderVariables[0].NamespacedSettingNames[0]).IsEqualTo("renderer:filter");
    }

    [Test]
    public async Task SharedForwardedVariablesKeepNativeDeduplicationAndFirstUseOrder()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = [</First>, </Second>]
            }
            def Scope "Forward"
            {
                rel one = [</Depth>, </Beauty>]
                rel two = </Depth>
            }
            def RenderProduct "First"
            {
                rel orderedVars = [</Forward.one>, </Forward.two>]
            }
            def RenderProduct "Second"
            {
                rel orderedVars = [</Beauty>, </Depth>]
            }
            def RenderVar "Beauty" {}
            def RenderVar "Depth" {}
            """);
        UsdRenderSpecification snapshot = stage.GetRenderSpecification("/Settings")!;
        await Assert.That(snapshot.RenderVariables.Count).IsEqualTo(2);
        await Assert.That(snapshot.RenderVariables[0].Path).IsEqualTo("/Depth");
        await Assert.That(snapshot.RenderVariables[1].Path).IsEqualTo("/Beauty");
        await Assert.That(snapshot.Products[0].RenderVariableIndices.Count).IsEqualTo(2);
        await Assert.That(snapshot.Products[0].RenderVariableIndices[0]).IsEqualTo(0);
        await Assert.That(snapshot.Products[0].RenderVariableIndices[1]).IsEqualTo(1);
        await Assert.That(snapshot.Products[1].RenderVariableIndices[0]).IsEqualTo(1);
        await Assert.That(snapshot.Products[1].RenderVariableIndices[1]).IsEqualTo(0);
    }

    [Test]
    [Arguments(1024)]
    [Arguments(1025)]
    public async Task ProductBudgetAcceptsItsLimitAndRejectsOverflowWithoutTruncating(int count)
    {
        string targets = string.Join(", ", Enumerable.Range(0, count).Select(index => $"</Product{index}>"));
        var contents = new StringBuilder(
            $$"""
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = [{{targets}}]
            }
            """);
        for (int index = 0; index < count; index++)
        {
            contents.AppendLine(CultureInfo.InvariantCulture, $"\ndef RenderProduct \"Product{index}\" {{}}");
        }
        using UsdStage stage = await OpenStageAsync(contents.ToString());
        if (count == 1024)
        {
            await Assert.That(stage.GetRenderSpecification("/Settings")!.Products.Count).IsEqualTo(1024);
            return;
        }
        OpenUsdNativeException? exception = await Assert.That(() => stage.GetRenderSpecification("/Settings"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(exception?.Message).Contains("product budget");
    }

    [Test]
    [Arguments(4096)]
    [Arguments(4097)]
    public async Task VariableBudgetAcceptsItsLimitAndRejectsOverflowWithoutOmittingVariables(int count)
    {
        var contents = new StringBuilder(
            """
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = </Product>
            }
            def RenderProduct "Product"
            {
                rel orderedVars = [
            """);
        contents.Append(string.Join(", ", Enumerable.Range(0, count).Select(index => $"</Var{index}>")));
        contents.AppendLine("]\n}");
        for (int index = 0; index < count; index++)
        {
            contents.AppendLine(CultureInfo.InvariantCulture, $"def RenderVar \"Var{index}\" {{}}");
        }
        using UsdStage stage = await OpenStageAsync(contents.ToString());
        if (count == 4096)
        {
            UsdRenderSpecification snapshot = stage.GetRenderSpecification("/Settings")!;
            await Assert.That(snapshot.RenderVariables.Count).IsEqualTo(4096);
            await Assert.That(snapshot.Products[0].RenderVariableIndices[^1]).IsEqualTo(4095);
            return;
        }
        OpenUsdNativeException? exception = await Assert.That(() => stage.GetRenderSpecification("/Settings"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(exception?.Message).Contains("unique render-variable budget");
    }

    [Test]
    [Arguments(64)]
    [Arguments(65)]
    public async Task SharedVariableIndexBudgetIsEnforcedAcrossAllProducts(int variableCount)
    {
        string products = string.Join(", ", Enumerable.Range(0, 1024).Select(index => $"</Product{index}>"));
        string variables = string.Join(", ", Enumerable.Range(0, variableCount).Select(index => $"</Var{index}>"));
        var contents = new StringBuilder(
            $$"""
            #usda 1.0
            def Camera "Camera" {}
            def Scope "Forward"
            {
                rel vars = [{{variables}}]
            }
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = [{{products}}]
            }

            """);
        for (int index = 0; index < 1024; index++)
        {
            contents.AppendLine(CultureInfo.InvariantCulture,
                $"def RenderProduct \"Product{index}\"\n{{\nrel orderedVars = </Forward.vars>\n}}");
        }
        for (int index = 0; index < variableCount; index++)
        {
            contents.AppendLine(CultureInfo.InvariantCulture, $"def RenderVar \"Var{index}\" {{}}");
        }
        using UsdStage stage = await OpenStageAsync(contents.ToString());
        if (variableCount == 64)
        {
            UsdRenderSpecification snapshot = stage.GetRenderSpecification("/Settings")!;
            await Assert.That(snapshot.Products.Sum(product => product.RenderVariableIndices.Count))
                .IsEqualTo(65536);
            await Assert.That(snapshot.Products[^1].RenderVariableIndices[^1]).IsEqualTo(63);
            return;
        }
        OpenUsdNativeException? exception = await Assert.That(() => stage.GetRenderSpecification("/Settings"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(exception?.Message).Contains("ordered render-variable index budget");
    }

    [Test]
    public async Task UnevaluatedSettingNameBudgetFailsInsteadOfHidingSettings()
    {
        var contents = new StringBuilder("#usda 1.0\ndef RenderSettings \"Settings\"\n{\n");
        for (int index = 0; index < 16385; index++)
        {
            contents.AppendLine(CultureInfo.InvariantCulture, $"custom int renderer:setting{index} = 1");
        }
        contents.AppendLine("}");
        using UsdStage stage = await OpenStageAsync(contents.ToString());
        OpenUsdNativeException? exception = await Assert.That(() => stage.GetRenderSpecification("/Settings"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(exception?.Message).Contains("setting-name budget");
    }

    [Test]
    public async Task PurposeBudgetFailsInsteadOfDroppingScenePurposes()
    {
        string purposes = string.Join(", ", Enumerable.Range(0, 65).Select(index => $"\"purpose{index}\""));
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            def RenderSettings "Settings"
            {
                uniform token[] includedPurposes = [{{purposes}}]
            }
            """);
        OpenUsdNativeException? exception = await Assert.That(() => stage.GetRenderSpecification("/Settings"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(exception?.Message).Contains("purpose-list budget");
    }

    [Test]
    public async Task StringBudgetFailsBeforeComputingAnOversizedVariable()
    {
        string sourceName = new('a', 8 << 20);
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = </Product>
            }
            def RenderProduct "Product"
            {
                rel orderedVars = </Var>
            }
            def RenderVar "Var"
            {
                string sourceName = "{{sourceName}}"
            }
            """);
        OpenUsdNativeException? exception = await Assert.That(() => stage.GetRenderSpecification("/Settings"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(exception?.Message).Contains("byte budget");
    }

    [Test]
    public async Task ReferenceTargetCompositionErrorCannotPublishItsSurvivingProduct()
    {
        using UsdStage asset = await OpenStageAsync(
            """
            #usda 1.0
            def Scope "Asset"
            {
                def Camera "Camera" {}
                def RenderProduct "Product" {}
                def RenderSettings "Settings"
                {
                    rel camera = </Asset/Camera>
                    rel products = [</Asset/Product>, </Outside>]
                }
            }
            """);
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            def Scope "Reference" (
                prepend references = @{{Path.GetFileName(asset.RootLayerIdentifier)}}@</Asset>
            ) {}
            """);
        OpenUsdNativeException? error = await Assert.That(
            () => stage.GetRenderSpecification("/Reference/Settings")).Throws<OpenUsdNativeException>();
        await Assert.That(error?.Message).Contains("target-index composition");
    }

    [Test]
    public async Task InheritedPropertyKindConflictCannotPublishItsSurvivingTarget()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            class "Base"
            {
                custom int products = 1
            }
            def Camera "Camera" {}
            def RenderProduct "Product" {}
            def RenderSettings "Settings" (
                prepend inherits = </Base>
            )
            {
                rel camera = </Camera>
                rel products = </Product>
            }
            """);
        OpenUsdNativeException? error = await Assert.That(
            () => stage.GetRenderSpecification("/Settings")).Throws<OpenUsdNativeException>();
        await Assert.That(error?.Message).Contains("property-index composition");
    }

    [Test]
    public async Task ForwardingScopeCustomResolutionIsNotARenderSettingsDefault()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            def Scope "Forward"
            {
                custom int resolution = 1
                rel targetless
            }
            def RenderSettings "Settings"
            {
                rel products = </Forward.targetless>
            }
            """);
        UsdRenderSpecification snapshot = stage.GetRenderSpecification("/Settings")!;
        await Assert.That(snapshot.SettingsPath).IsEqualTo("/Settings");
        await Assert.That(snapshot.Products.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ForwardedPrimAcquiresLaterProductRoleRequirements(bool malformedResolution)
    {
        using UsdStage stage = await OpenStageAsync(
            $$"""
            #usda 1.0
            def Camera "Camera" {}
            def RenderSettings "Settings"
            {
                rel camera = </Product.forwardCamera>
                rel products = </Product>
            }
            def RenderProduct "Product"
            {
                rel forwardCamera = </Camera>
                {{(malformedResolution ? "int resolution = 1" : "int2 resolution = (320, 160)")}}
                custom int sourceName = 7
            }
            """);
        if (malformedResolution)
        {
            OpenUsdNativeException? error = await Assert.That(
                () => stage.GetRenderSpecification("/Settings")).Throws<OpenUsdNativeException>();
            await Assert.That(error?.Message).Contains("concrete schema-typed default opinions");
        }
        else
        {
            UsdRenderSpecification snapshot = stage.GetRenderSpecification("/Settings")!;
            await Assert.That(snapshot.Products.Count).IsEqualTo(1);
            await Assert.That(snapshot.Products[0].Width).IsEqualTo(320);
            await Assert.That(snapshot.Products[0].Height).IsEqualTo(160);
            await Assert.That(snapshot.Products[0].NamespacedSettingNames).Contains("sourceName");
        }
    }

    [Test]
    public async Task OwnedReviewCameraDefaultsAreAdmittedWithoutChangingRootOpinions()
    {
        using UsdStage stage = await OpenStageAsync(
            """
            #usda 1.0
            (
                renderSettingsPrimPath = "/Settings"
            )
            def Camera "Camera"
            {
                float horizontalAperture = 20
                float verticalAperture = 20
            }
            def RenderSettings "Settings"
            {
                rel camera = </Camera>
                rel products = </Product>
                int2 resolution = (8, 4)
                token aspectRatioConformPolicy = "adjustPixelAspectRatio"
            }
            def RenderProduct "Product" {}
            """);
        using UsdLayer review = stage.GetUserReviewLayer();
        UsdRenderSpecification emptyReview = stage.GetRenderSpecification()!;
        await Assert.That(emptyReview.Products[0].ApertureSize.X).IsEqualTo(20f);
        var address = new UsdLayerEditAddress("/Camera.horizontalAperture", UsdLayerEditField.Default);
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
        UsdLayerEditResult applied = review.CompareAndApply(
            before, [UsdLayerEdit.Set(address, UsdLayerEditValue.FromFloat(40), "float")]);
        await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        UsdRenderSpecification editedReview = stage.GetRenderSpecification()!;
        await Assert.That(editedReview.Products[0].ApertureSize).IsEqualTo(new UsdVec2f(40, 20));
        await Assert.That(editedReview.Products[0].PixelAspectRatio).IsEqualTo(1f);
        using UsdLayer root = stage.GetRootLayer();
        await Assert.That(root.CaptureAuthored([address]).Opinions[0].Value.AsFloat()).IsEqualTo(20f);

        UsdLayerEditResult unsupported = review.CompareAndApply(
            applied.AfterSnapshot!,
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromFloat(float.NaN), "float")]);
        await Assert.That(unsupported.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(() => stage.GetRenderSpecification()).Throws<OpenUsdNativeException>();
        await Assert.That(root.CaptureAuthored([address]).Opinions[0].Value.AsFloat()).IsEqualTo(20f);
    }

    private static async Task<UsdStage> OpenStageAsync(string contents)
    {
        string? pluginPath = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(pluginPath))
        {
            Skip.Test(
                "Set OPENUSD_TEST_PLUGIN_PATH and stage the native runtime to execute render specification tests.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(pluginPath!);
        string root = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "native-work");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, $"render-specification-{Guid.NewGuid():N}.usda");
        await File.WriteAllTextAsync(path, contents);
        return UsdStage.Open(path);
    }
}
