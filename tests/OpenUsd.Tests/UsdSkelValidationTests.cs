// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using OpenUsd.Interop;
using OpenUsd.Skel;

namespace OpenUsd.Tests;

public sealed class UsdSkelValidationTests
{
    private static readonly string[] InvalidPrimPaths =
    [
        null!,
        "",
        "World/Skeleton",
        "/World/Skeleton.joints",
        "/"
    ];

    private static readonly string[] NativeEntryNames =
    [
        nameof(OpenUsdNativeStage.IsSkelSchema),
        nameof(OpenUsdNativeStage.DefineSkel),
        nameof(OpenUsdNativeStage.HasSkelBinding),
        nameof(OpenUsdNativeStage.ApplySkelBinding),
        nameof(OpenUsdNativeStage.SetSkelJoints),
        nameof(OpenUsdNativeStage.GetSkelJoints),
        nameof(OpenUsdNativeStage.SetSkelSkeletonMatrices),
        nameof(OpenUsdNativeStage.GetSkelSkeletonMatrices),
        nameof(OpenUsdNativeStage.SetSkelAnimationVec3),
        nameof(OpenUsdNativeStage.GetSkelAnimationVec3),
        nameof(OpenUsdNativeStage.SetSkelAnimationRotations),
        nameof(OpenUsdNativeStage.GetSkelAnimationRotations),
        nameof(OpenUsdNativeStage.SetSkelBindingTarget),
        nameof(OpenUsdNativeStage.GetSkelBindingTarget),
        nameof(OpenUsdNativeStage.ClearSkelBindingTarget),
        nameof(OpenUsdNativeStage.SetSkelGeomBindTransform),
        nameof(OpenUsdNativeStage.GetSkelGeomBindTransform),
        nameof(OpenUsdNativeStage.SetSkelJointInfluences),
        nameof(OpenUsdNativeStage.GetSkelJointInfluences),
        nameof(OpenUsdNativeStage.SetSkelSkinningMethod),
        nameof(OpenUsdNativeStage.GetSkelSkinningMethod),
        nameof(OpenUsdNativeStage.SetSkelBlendShapes),
        nameof(OpenUsdNativeStage.GetSkelBlendShapes),
        nameof(OpenUsdNativeStage.SetSkelBlendShapeTargets),
        nameof(OpenUsdNativeStage.GetSkelBlendShapeTargets),
        nameof(OpenUsdNativeStage.SetSkelBlendShapeVec3),
        nameof(OpenUsdNativeStage.GetSkelBlendShapeVec3),
        nameof(OpenUsdNativeStage.SetSkelBlendShapePointIndices),
        nameof(OpenUsdNativeStage.GetSkelBlendShapePointIndices),
        nameof(OpenUsdNativeStage.SetSkelBlendShapeInbetween),
        nameof(OpenUsdNativeStage.GetSkelBlendShapeInbetweenNames),
        nameof(OpenUsdNativeStage.GetSkelBlendShapeInbetween)
    ];

    [Test]
    public async Task EveryNativeSkelEntryRejectsInvalidPrimPathsBeforeDispatch()
    {
        using var stage = new OpenUsdNativeStage(nint.Zero);
        (string Name, Action<string> Invoke)[] entries =
        [
            (nameof(stage.IsSkelSchema),
                path => _ = stage.IsSkelSchema(path, OpenUsdNativeSkelSchemaKind.Root)),
            (nameof(stage.DefineSkel),
                path => stage.DefineSkel(path, OpenUsdNativeSkelSchemaKind.Root)),
            (nameof(stage.HasSkelBinding), path => _ = stage.HasSkelBinding(path)),
            (nameof(stage.ApplySkelBinding), stage.ApplySkelBinding),
            (nameof(stage.SetSkelJoints),
                path => stage.SetSkelJoints(
                    path,
                    OpenUsdNativeSkelSchemaKind.Skeleton,
                    ["Root"])),
            (nameof(stage.GetSkelJoints),
                path => _ = stage.GetSkelJoints(
                    path,
                    OpenUsdNativeSkelSchemaKind.Skeleton)),
            (nameof(stage.SetSkelSkeletonMatrices),
                path => stage.SetSkelSkeletonMatrices(
                    path,
                    OpenUsdNativeSkelMatrixProperty.BindTransforms,
                    [default])),
            (nameof(stage.GetSkelSkeletonMatrices),
                path => _ = stage.GetSkelSkeletonMatrices(
                    path,
                    OpenUsdNativeSkelMatrixProperty.BindTransforms)),
            (nameof(stage.SetSkelAnimationVec3),
                path => stage.SetSkelAnimationVec3(
                    path,
                    OpenUsdNativeSkelAnimationVec3Property.Translations,
                    [default])),
            (nameof(stage.GetSkelAnimationVec3),
                path => _ = stage.GetSkelAnimationVec3(
                    path,
                    OpenUsdNativeSkelAnimationVec3Property.Translations)),
            (nameof(stage.SetSkelAnimationRotations),
                path => stage.SetSkelAnimationRotations(path, [default])),
            (nameof(stage.GetSkelAnimationRotations),
                path => _ = stage.GetSkelAnimationRotations(path)),
            (nameof(stage.SetSkelBindingTarget),
                path => stage.SetSkelBindingTarget(
                    path,
                    OpenUsdNativeSkelBindingRelationship.Skeleton,
                    "/World/Target")),
            (nameof(stage.GetSkelBindingTarget),
                path => _ = stage.GetSkelBindingTarget(
                    path,
                    OpenUsdNativeSkelBindingRelationship.Skeleton)),
            (nameof(stage.ClearSkelBindingTarget),
                path => stage.ClearSkelBindingTarget(
                    path,
                    OpenUsdNativeSkelBindingRelationship.Skeleton)),
            (nameof(stage.SetSkelGeomBindTransform),
                path => stage.SetSkelGeomBindTransform(path, default)),
            (nameof(stage.GetSkelGeomBindTransform),
                path => _ = stage.GetSkelGeomBindTransform(path)),
            (nameof(stage.SetSkelJointInfluences),
                path => stage.SetSkelJointInfluences(
                    path,
                    [0],
                    [1],
                    1,
                    OpenUsdNativeSkelInterpolation.Constant)),
            (nameof(stage.GetSkelJointInfluences),
                path => _ = stage.GetSkelJointInfluences(path)),
            (nameof(stage.SetSkelSkinningMethod),
                path => stage.SetSkelSkinningMethod(
                    path,
                    OpenUsdNativeSkelSkinningMethod.ClassicLinear)),
            (nameof(stage.GetSkelSkinningMethod),
                path => _ = stage.GetSkelSkinningMethod(path)),
            (nameof(stage.SetSkelBlendShapes),
                path => stage.SetSkelBlendShapes(path, ["Smile"])),
            (nameof(stage.GetSkelBlendShapes),
                path => _ = stage.GetSkelBlendShapes(path)),
            (nameof(stage.SetSkelBlendShapeTargets),
                path => stage.SetSkelBlendShapeTargets(path, ["/World/Smile"])),
            (nameof(stage.GetSkelBlendShapeTargets),
                path => _ = stage.GetSkelBlendShapeTargets(path)),
            (nameof(stage.SetSkelBlendShapeVec3),
                path => stage.SetSkelBlendShapeVec3(
                    path,
                    OpenUsdNativeSkelBlendShapeVec3Property.Offsets,
                    [default])),
            (nameof(stage.GetSkelBlendShapeVec3),
                path => _ = stage.GetSkelBlendShapeVec3(
                    path,
                    OpenUsdNativeSkelBlendShapeVec3Property.Offsets)),
            (nameof(stage.SetSkelBlendShapePointIndices),
                path => stage.SetSkelBlendShapePointIndices(path, [0])),
            (nameof(stage.GetSkelBlendShapePointIndices),
                path => _ = stage.GetSkelBlendShapePointIndices(path)),
            (nameof(stage.SetSkelBlendShapeInbetween),
                path => stage.SetSkelBlendShapeInbetween(
                    path,
                    "half",
                    0.5F,
                    [default],
                    [])),
            (nameof(stage.GetSkelBlendShapeInbetweenNames),
                path => _ = stage.GetSkelBlendShapeInbetweenNames(path)),
            (nameof(stage.GetSkelBlendShapeInbetween),
                path => _ = stage.GetSkelBlendShapeInbetween(path, "half"))
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
    public async Task NativeSkelEnumsAndTargetsAreValidatedBeforeDispatch()
    {
        using var stage = new OpenUsdNativeStage(nint.Zero);
        (string Name, Action Invoke)[] invalidEnums =
        [
            (nameof(stage.IsSkelSchema),
                () => _ = stage.IsSkelSchema(
                    "/World/Prim",
                    (OpenUsdNativeSkelSchemaKind)99)),
            (nameof(stage.DefineSkel),
                () => stage.DefineSkel(
                    "/World/Prim",
                    (OpenUsdNativeSkelSchemaKind)99)),
            (nameof(stage.SetSkelJoints),
                () => stage.SetSkelJoints(
                    "/World/Prim",
                    (OpenUsdNativeSkelSchemaKind)99,
                    ["Root"])),
            (nameof(stage.GetSkelJoints),
                () => _ = stage.GetSkelJoints(
                    "/World/Prim",
                    (OpenUsdNativeSkelSchemaKind)99)),
            (nameof(stage.SetSkelSkeletonMatrices),
                () => stage.SetSkelSkeletonMatrices(
                    "/World/Prim",
                    (OpenUsdNativeSkelMatrixProperty)99,
                    [default])),
            (nameof(stage.GetSkelSkeletonMatrices),
                () => _ = stage.GetSkelSkeletonMatrices(
                    "/World/Prim",
                    (OpenUsdNativeSkelMatrixProperty)99)),
            (nameof(stage.SetSkelAnimationVec3),
                () => stage.SetSkelAnimationVec3(
                    "/World/Prim",
                    (OpenUsdNativeSkelAnimationVec3Property)99,
                    [default])),
            (nameof(stage.GetSkelAnimationVec3),
                () => _ = stage.GetSkelAnimationVec3(
                    "/World/Prim",
                    (OpenUsdNativeSkelAnimationVec3Property)99)),
            (nameof(stage.SetSkelBindingTarget),
                () => stage.SetSkelBindingTarget(
                    "/World/Prim",
                    (OpenUsdNativeSkelBindingRelationship)99,
                    "/World/Target")),
            (nameof(stage.GetSkelBindingTarget),
                () => _ = stage.GetSkelBindingTarget(
                    "/World/Prim",
                    (OpenUsdNativeSkelBindingRelationship)99)),
            (nameof(stage.ClearSkelBindingTarget),
                () => stage.ClearSkelBindingTarget(
                    "/World/Prim",
                    (OpenUsdNativeSkelBindingRelationship)99)),
            (nameof(stage.SetSkelJointInfluences),
                () => stage.SetSkelJointInfluences(
                    "/World/Prim",
                    [0],
                    [1],
                    1,
                    (OpenUsdNativeSkelInterpolation)99))
        ];

        foreach ((string name, Action invoke) in invalidEnums)
        {
            Exception exception = Capture(invoke);
            await Assert.That(exception).IsTypeOf<ArgumentOutOfRangeException>()
                .Because(name);
        }

        foreach (string targetPath in InvalidPrimPaths)
        {
            Exception exception = Capture(() => stage.SetSkelBindingTarget(
                "/World/Prim",
                OpenUsdNativeSkelBindingRelationship.Skeleton,
                targetPath));
            await Assert.That(exception is ArgumentException).IsTrue();
            await Assert.That(exception is OpenUsdNativeException).IsFalse();
        }
    }

    [Test]
    public async Task JointTokensAreValidatedBeforePackingOrDispatch()
    {
        using var stage = new OpenUsdNativeStage(nint.Zero);
        string[][] invalidTokenLists =
        [
            [""],
            [" "],
            ["/Root"],
            ["Root.joints"],
            ["Root//Child"],
            ["Root", "Root"],
            ["Root/Child", "Root"]
        ];

        foreach (string[] joints in invalidTokenLists)
        {
            Exception exception = Capture(() => stage.SetSkelJoints(
                "/World/Skeleton",
                OpenUsdNativeSkelSchemaKind.Skeleton,
                joints));
            await Assert.That(exception is ArgumentException).IsTrue();
            await Assert.That(exception is OpenUsdNativeException).IsFalse();
        }

        Exception rootSchema = Capture(() => stage.SetSkelJoints(
            "/World/Root",
            OpenUsdNativeSkelSchemaKind.Root,
            ["Root"]));
        await Assert.That(rootSchema).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task PrimAndJointPathsUsePinnedOpenUsdUnicodeRules()
    {
        await Assert.That(
                () => OpenUsdNativeSkelValidation.ValidatePrimPath(
                    "/München/着色器"))
            .ThrowsNothing();
        await Assert.That(
                () => OpenUsdNativeSkelValidation.ValidateJointTokens(
                    ["Racine", "Racine/子・"],
                    OpenUsdNativeSkelSchemaKind.Skeleton))
            .ThrowsNothing();
        await Assert.That(
                () => OpenUsdNativeSkelValidation.ValidatePrimPath("/ͺ"))
            .Throws<ArgumentException>();
        await Assert.That(
                () => OpenUsdNativeSkelValidation.ValidateJointTokens(
                    ["Root", "Root/💥"],
                    OpenUsdNativeSkelSchemaKind.Skeleton))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task FacadeDefinitionsWrapsAndConstructorsRejectInvalidPaths()
    {
        using UsdStage stage = CreateDetachedStage();
        foreach (string path in InvalidPrimPaths)
        {
            Action[] entries =
            [
                () => _ = stage.DefineSkelRoot(path),
                () => _ = stage.DefineSkeleton(path),
                () => _ = stage.DefineAnimation(path),
                () => _ = new UsdSkelRoot(stage, path),
                () => _ = new UsdSkelSkeleton(stage, path),
                () => _ = new UsdSkelAnimation(stage, path),
                () => _ = new UsdSkelBinding(stage, path)
            ];
            foreach (Action entry in entries)
            {
                Exception exception = Capture(entry);
                await Assert.That(exception is ArgumentException).IsTrue();
                await Assert.That(exception is OpenUsdNativeException).IsFalse();
            }

            var prim = new UsdPrim(stage, path);
            await Assert.That(UsdSkelRoot.TryWrap(prim, out _)).IsFalse();
            await Assert.That(UsdSkelSkeleton.TryWrap(prim, out _)).IsFalse();
            await Assert.That(UsdSkelAnimation.TryWrap(prim, out _)).IsFalse();
            await Assert.That(UsdSkelBinding.TryWrap(prim, out _)).IsFalse();
            await Assert.That(Capture(() => UsdSkelRoot.Wrap(prim)) is ArgumentException)
                .IsTrue();
            await Assert.That(Capture(() => UsdSkelSkeleton.Wrap(prim)) is ArgumentException)
                .IsTrue();
            await Assert.That(Capture(() => UsdSkelAnimation.Wrap(prim)) is ArgumentException)
                .IsTrue();
            await Assert.That(Capture(() => UsdSkelBinding.Wrap(prim)) is ArgumentException)
                .IsTrue();
            await Assert.That(Capture(() => UsdSkelBinding.Apply(prim)) is ArgumentException)
                .IsTrue();
        }
    }

    [Test]
    public async Task NativeAndFacadeEntryInventoriesRemainComplete()
    {
        HashSet<string> stageEntries = typeof(OpenUsdNativeStage)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name.Contains("Skel", StringComparison.Ordinal))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> runtimeEntries = typeof(OpenUsdNativeRuntime)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => NativeEntryNames.Contains(method.Name, StringComparer.Ordinal))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        Type[] facadeTypes =
        [
            typeof(UsdSkelStageExtensions),
            typeof(UsdSkelRoot),
            typeof(UsdSkelSkeleton),
            typeof(UsdSkelAnimation),
            typeof(UsdSkelBlendShape),
            typeof(UsdSkelBinding)
        ];
        int facadeMethodCount = facadeTypes.Sum(type => type
            .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly)
            .Count(method => !method.IsSpecialName));

        await Assert.That(stageEntries.SetEquals(NativeEntryNames)).IsTrue();
        await Assert.That(runtimeEntries.SetEquals(NativeEntryNames)).IsTrue();
        await Assert.That(facadeMethodCount).IsEqualTo(58);
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
