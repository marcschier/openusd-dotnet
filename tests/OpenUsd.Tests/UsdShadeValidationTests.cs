// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using OpenUsd.Interop;
using OpenUsd.Shade;

namespace OpenUsd.Tests;

public sealed class UsdShadeValidationTests
{
    private static readonly string[] InvalidPrimPaths =
    [
        null!,
        "",
        "World/Shader",
        "/World/Shader.outputs:surface",
        "/"
    ];

    private static readonly string[] NativeEntryNames =
    [
        nameof(OpenUsdNativeStage.IsShadeMaterial),
        nameof(OpenUsdNativeStage.IsShadeShader),
        nameof(OpenUsdNativeStage.IsShadeNodeGraph),
        nameof(OpenUsdNativeStage.DefineShadeMaterial),
        nameof(OpenUsdNativeStage.DefineShadeShader),
        nameof(OpenUsdNativeStage.DefineShadeNodeGraph),
        nameof(OpenUsdNativeStage.SetShaderSourceId),
        nameof(OpenUsdNativeStage.GetShaderSourceId),
        nameof(OpenUsdNativeStage.CreateShadeInput),
        nameof(OpenUsdNativeStage.GetShadeInputType),
        nameof(OpenUsdNativeStage.SetShadeInput),
        nameof(OpenUsdNativeStage.GetShadeInputFloat),
        nameof(OpenUsdNativeStage.GetShadeInputVec3f),
        nameof(OpenUsdNativeStage.GetShadeInputString),
        nameof(OpenUsdNativeStage.CreateShadeOutput),
        nameof(OpenUsdNativeStage.GetShadeOutputType),
        nameof(OpenUsdNativeStage.GetShadeInputNames),
        nameof(OpenUsdNativeStage.GetShadeOutputNames),
        nameof(OpenUsdNativeStage.ConnectShade),
        nameof(OpenUsdNativeStage.DisconnectShade),
        nameof(OpenUsdNativeStage.GetConnectedShadeSource),
        nameof(OpenUsdNativeStage.GetConnectedShadeSources),
        nameof(OpenUsdNativeStage.CreateMaterialSurfaceOutput),
        nameof(OpenUsdNativeStage.CreateMaterialTerminalOutput),
        nameof(OpenUsdNativeStage.BindMaterial),
        nameof(OpenUsdNativeStage.BindMaterialCollection),
        nameof(OpenUsdNativeStage.UnbindMaterial),
        nameof(OpenUsdNativeStage.GetDirectMaterialPath)
    ];

    private static readonly string[] NativeRuntimeEntryNames =
    [
        nameof(OpenUsdNativeRuntime.IsShadeMaterial),
        nameof(OpenUsdNativeRuntime.IsShadeShader),
        nameof(OpenUsdNativeRuntime.IsShadeNodeGraph),
        nameof(OpenUsdNativeRuntime.DefineShadeMaterial),
        nameof(OpenUsdNativeRuntime.DefineShadeShader),
        nameof(OpenUsdNativeRuntime.DefineShadeNodeGraph),
        nameof(OpenUsdNativeRuntime.SetShaderSourceId),
        nameof(OpenUsdNativeRuntime.GetShaderSourceId),
        nameof(OpenUsdNativeRuntime.CreateShadeInput),
        nameof(OpenUsdNativeRuntime.GetShadeInputType),
        nameof(OpenUsdNativeRuntime.SetShadeInputFloat),
        nameof(OpenUsdNativeRuntime.GetShadeInputFloat),
        nameof(OpenUsdNativeRuntime.SetShadeInputVec3f),
        nameof(OpenUsdNativeRuntime.GetShadeInputVec3f),
        nameof(OpenUsdNativeRuntime.SetShadeInputString),
        nameof(OpenUsdNativeRuntime.GetShadeInputString),
        nameof(OpenUsdNativeRuntime.CreateShadeOutput),
        nameof(OpenUsdNativeRuntime.GetShadeOutputType),
        nameof(OpenUsdNativeRuntime.GetShadeInputNames),
        nameof(OpenUsdNativeRuntime.GetShadeOutputNames),
        nameof(OpenUsdNativeRuntime.ConnectShade),
        nameof(OpenUsdNativeRuntime.DisconnectShade),
        nameof(OpenUsdNativeRuntime.GetConnectedShadeSource),
        nameof(OpenUsdNativeRuntime.GetConnectedShadeSources),
        nameof(OpenUsdNativeRuntime.CreateMaterialSurfaceOutput),
        nameof(OpenUsdNativeRuntime.CreateMaterialTerminalOutput),
        nameof(OpenUsdNativeRuntime.BindMaterial),
        nameof(OpenUsdNativeRuntime.BindMaterialCollection),
        nameof(OpenUsdNativeRuntime.UnbindMaterial),
        nameof(OpenUsdNativeRuntime.GetDirectMaterialPath)
    ];

    [Test]
    public async Task EveryNativeShadeEntryRejectsInvalidPrimPathsBeforeDispatch()
    {
        using var stage = new OpenUsdNativeStage(nint.Zero);
        (string Name, Action<string> Invoke)[] entries =
        [
            (nameof(stage.IsShadeMaterial), path => _ = stage.IsShadeMaterial(path)),
            (nameof(stage.IsShadeShader), path => _ = stage.IsShadeShader(path)),
            (nameof(stage.IsShadeNodeGraph), path => _ = stage.IsShadeNodeGraph(path)),
            (nameof(stage.DefineShadeMaterial), stage.DefineShadeMaterial),
            (nameof(stage.DefineShadeShader), stage.DefineShadeShader),
            (nameof(stage.DefineShadeNodeGraph), stage.DefineShadeNodeGraph),
            (nameof(stage.SetShaderSourceId), path => stage.SetShaderSourceId(path, "UsdPreviewSurface")),
            (nameof(stage.GetShaderSourceId), path => _ = stage.GetShaderSourceId(path)),
            (nameof(stage.CreateShadeInput),
                path => stage.CreateShadeInput(path, "diffuseColor", OpenUsdNativeShadeValueType.Color3f)),
            (nameof(stage.GetShadeInputType), path => _ = stage.GetShadeInputType(path, "diffuseColor")),
            (nameof(stage.SetShadeInput), path => stage.SetShadeInput(path, "roughness", 0.5F)),
            (nameof(stage.GetShadeInputFloat), path => _ = stage.GetShadeInputFloat(path, "roughness")),
            (nameof(stage.GetShadeInputVec3f),
                path => _ = stage.GetShadeInputVec3f(
                    path,
                    "diffuseColor",
                    OpenUsdNativeShadeValueType.Color3f)),
            (nameof(stage.GetShadeInputString),
                path => _ = stage.GetShadeInputString(path, "file", OpenUsdNativeShadeValueType.Asset)),
            (nameof(stage.CreateShadeOutput),
                path => stage.CreateShadeOutput(path, "out", OpenUsdNativeShadeValueType.Token)),
            (nameof(stage.GetShadeOutputType), path => _ = stage.GetShadeOutputType(path, "out")),
            (nameof(stage.GetShadeInputNames), path => _ = stage.GetShadeInputNames(path)),
            (nameof(stage.GetShadeOutputNames), path => _ = stage.GetShadeOutputNames(path)),
            (nameof(stage.ConnectShade),
                path => stage.ConnectShade(
                    path,
                    "diffuseColor",
                    OpenUsdNativeShadeAttributeType.Input,
                    "/World/Shader",
                    "out",
                    OpenUsdNativeShadeAttributeType.Output)),
            (nameof(stage.DisconnectShade),
                path => stage.DisconnectShade(path, "diffuseColor", OpenUsdNativeShadeAttributeType.Input)),
            (nameof(stage.GetConnectedShadeSource),
                path => _ = stage.GetConnectedShadeSource(path, "diffuseColor", OpenUsdNativeShadeAttributeType.Input)),
            (nameof(stage.GetConnectedShadeSources),
                path => _ = stage.GetConnectedShadeSources(
                    path,
                    "diffuseColor",
                    OpenUsdNativeShadeAttributeType.Input)),
            (nameof(stage.CreateMaterialSurfaceOutput), stage.CreateMaterialSurfaceOutput),
            (nameof(stage.CreateMaterialTerminalOutput),
                path => stage.CreateMaterialTerminalOutput(
                    path,
                    OpenUsdNativeShadeMaterialTerminal.Surface,
                    "")),
            (nameof(stage.BindMaterial), path => stage.BindMaterial(path, "/World/Material")),
            (nameof(stage.BindMaterialCollection),
                path => stage.BindMaterialCollection(
                    path,
                    "/World/CollectionOwner",
                    "members",
                    "/World/Material",
                    "",
                    OpenUsdNativeShadeBindingStrength.WeakerThanDescendants,
                    OpenUsdNativeShadeMaterialPurpose.Preview)),
            (nameof(stage.UnbindMaterial), stage.UnbindMaterial),
            (nameof(stage.GetDirectMaterialPath), path => _ = stage.GetDirectMaterialPath(path))
        ];

        foreach ((string name, Action<string> invoke) in entries)
        {
            foreach (string path in InvalidPrimPaths)
            {
                Exception exception = Capture(() => invoke(path));
                await Assert.That(exception is ArgumentException).IsTrue()
                    .Because($"{name} accepted '{path}'.");
                await Assert.That(exception is OpenUsdNativeException).IsFalse()
                    .Because($"{name} crossed the native boundary for '{path}'.");
            }
        }
    }

    [Test]
    public async Task NativeShadeEnumsAreValidatedBeforeDispatch()
    {
        using var stage = new OpenUsdNativeStage(nint.Zero);
        (string Name, Action Invoke)[] invalidEnums =
        [
            (nameof(stage.CreateShadeInput),
                () => stage.CreateShadeInput(
                    "/World/Shader",
                    "input",
                    (OpenUsdNativeShadeValueType)99)),
            (nameof(stage.CreateShadeOutput),
                () => stage.CreateShadeOutput(
                    "/World/Shader",
                    "out",
                    (OpenUsdNativeShadeValueType)99)),
            (nameof(stage.ConnectShade),
                () => stage.ConnectShade(
                    "/World/Shader",
                    "input",
                    (OpenUsdNativeShadeAttributeType)99,
                    "/World/Node",
                    "out",
                    OpenUsdNativeShadeAttributeType.Output)),
            (nameof(stage.CreateMaterialTerminalOutput),
                () => stage.CreateMaterialTerminalOutput(
                    "/World/Material",
                    (OpenUsdNativeShadeMaterialTerminal)99,
                    "")),
            (nameof(stage.BindMaterial),
                () => stage.BindMaterial(
                    "/World/Mesh",
                    "/World/Material",
                    (OpenUsdNativeShadeBindingStrength)99,
                    OpenUsdNativeShadeMaterialPurpose.All)),
            (nameof(stage.BindMaterialCollection),
                () => stage.BindMaterialCollection(
                    "/World/Mesh",
                    "/World/CollectionOwner",
                    "members",
                    "/World/Material",
                    "",
                    OpenUsdNativeShadeBindingStrength.WeakerThanDescendants,
                    (OpenUsdNativeShadeMaterialPurpose)99))
        ];

        foreach ((string name, Action invoke) in invalidEnums)
        {
            Exception exception = Capture(invoke);
            await Assert.That(exception).IsTypeOf<ArgumentOutOfRangeException>()
                .Because(name);
        }
    }

    [Test]
    public async Task FacadeDefinitionsAndWrappersRejectInvalidPaths()
    {
        using UsdStage stage = CreateDetachedStage();
        foreach (string path in InvalidPrimPaths)
        {
            Action[] entries =
            [
                () => _ = stage.DefineMaterial(path),
                () => _ = stage.DefineShader(path),
                () => _ = stage.DefineNodeGraph(path)
            ];
            foreach (Action entry in entries)
            {
                Exception exception = Capture(entry);
                await Assert.That(exception is ArgumentException).IsTrue();
                await Assert.That(exception is OpenUsdNativeException).IsFalse();
            }

            var prim = new UsdPrim(stage, path);
            await Assert.That(UsdShadeMaterial.TryWrap(prim, out _)).IsFalse();
            await Assert.That(UsdShadeShader.TryWrap(prim, out _)).IsFalse();
            await Assert.That(UsdShadeNodeGraph.TryWrap(prim, out _)).IsFalse();
            await Assert.That(Capture(() => UsdShadeMaterial.Wrap(prim)) is ArgumentException)
                .IsTrue();
            await Assert.That(Capture(() => UsdShadeShader.Wrap(prim)) is ArgumentException)
                .IsTrue();
            await Assert.That(Capture(() => UsdShadeNodeGraph.Wrap(prim)) is ArgumentException)
                .IsTrue();
        }
    }

    [Test]
    public async Task NativeShadeEntryInventoryRemainsComplete()
    {
        HashSet<string> stageEntries = typeof(OpenUsdNativeStage)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name.Contains("Shade", StringComparison.Ordinal) ||
                method.Name.Contains("Material", StringComparison.Ordinal))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> runtimeEntries = typeof(OpenUsdNativeRuntime)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => NativeRuntimeEntryNames.Contains(method.Name, StringComparer.Ordinal))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        await Assert.That(stageEntries.SetEquals(NativeEntryNames)).IsTrue();
        await Assert.That(runtimeEntries.SetEquals(NativeRuntimeEntryNames)).IsTrue();
    }

    private static UsdStage CreateDetachedStage()
    {
        ConstructorInfo constructor = typeof(UsdStage).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(OpenUsdNativeStage)],
            modifiers: null)
            ?? throw new InvalidOperationException("UsdStage native constructor was not found.");
        return (UsdStage)constructor.Invoke([new OpenUsdNativeStage(nint.Zero)]);
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

        throw new InvalidOperationException("Expected managed validation to reject the value.");
    }
}
