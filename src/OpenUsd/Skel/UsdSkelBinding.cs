// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Skel;

/// <summary>A validated UsdSkelBindingAPI view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdSkelBinding : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdSkelBinding(UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path, nameof(path));
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Gets or sets the bind-time world transform of the geometry.</summary>
    public UsdMatrix4d GeomBindTransform
    {
        get => UsdMatrix4d.FromNative(Stage.Native.GetSkelGeomBindTransform(Path));
        set => Stage.Native.SetSkelGeomBindTransform(Path, value.ToNative());
    }

    /// <summary>Applies UsdSkelBindingAPI to a prim and returns its view.</summary>
    public static UsdSkelBinding Apply(UsdPrim prim)
    {
        UsdStage stage = UsdSkelSchema.ValidateAttachedPrim(prim);
        stage.Native.ApplySkelBinding(prim.Path);
        return new UsdSkelBinding(stage, prim.Path);
    }

    /// <summary>Tries to wrap a prim with UsdSkelBindingAPI applied.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdSkelBinding value)
    {
        value = default;
        if (!UsdSkelSchema.TryGetStage(prim, out UsdStage? stage))
        {
            return false;
        }
        try
        {
            UsdStage owningStage = stage!;
            if (!owningStage.Native.HasSkelBinding(prim.Path))
            {
                return false;
            }
            value = new UsdSkelBinding(owningStage, prim.Path);
            return true;
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            return false;
        }
    }

    /// <summary>Wraps a prim with UsdSkelBindingAPI applied.</summary>
    public static UsdSkelBinding Wrap(UsdPrim prim)
    {
        UsdStage stage = UsdSkelSchema.ValidateAttachedPrim(prim);
        if (!stage.Native.HasSkelBinding(prim.Path))
        {
            throw new ArgumentException(
                $"UsdSkelBindingAPI is not applied to prim '{prim.Path}'.",
                nameof(prim));
        }
        return new UsdSkelBinding(stage, prim.Path);
    }

    /// <summary>Authors the directly bound skeleton relationship.</summary>
    public void SetSkeleton(UsdSkelSkeleton skeleton)
    {
        UsdPath.ValidateAbsolutePrimPath(skeleton.Path, nameof(skeleton));
        UsdSkelSchema.ValidateSameStage(Stage, skeleton.Prim.OwningStage);
        Stage.Native.SetSkelBindingTarget(
            Path,
            OpenUsdNativeSkelBindingRelationship.Skeleton,
            skeleton.Path);
    }

    /// <summary>Gets the directly bound skeleton.</summary>
    public UsdSkelSkeleton GetSkeleton()
    {
        string target = Stage.Native.GetSkelBindingTarget(
            Path,
            OpenUsdNativeSkelBindingRelationship.Skeleton);
        return new UsdSkelSkeleton(Stage, target);
    }

    /// <summary>Clears the directly authored skeleton relationship.</summary>
    public void ClearSkeleton() =>
        Stage.Native.ClearSkelBindingTarget(
            Path,
            OpenUsdNativeSkelBindingRelationship.Skeleton);

    /// <summary>Authors the directly bound animation-source relationship.</summary>
    public void SetAnimationSource(UsdSkelAnimation animation)
    {
        UsdPath.ValidateAbsolutePrimPath(animation.Path, nameof(animation));
        UsdSkelSchema.ValidateSameStage(Stage, animation.Prim.OwningStage);
        Stage.Native.SetSkelBindingTarget(
            Path,
            OpenUsdNativeSkelBindingRelationship.AnimationSource,
            animation.Path);
    }

    /// <summary>Gets the directly bound animation source.</summary>
    public UsdSkelAnimation GetAnimationSource()
    {
        string target = Stage.Native.GetSkelBindingTarget(
            Path,
            OpenUsdNativeSkelBindingRelationship.AnimationSource);
        return new UsdSkelAnimation(Stage, target);
    }

    /// <summary>Clears the directly authored animation-source relationship.</summary>
    public void ClearAnimationSource() =>
        Stage.Native.ClearSkelBindingTarget(
            Path,
            OpenUsdNativeSkelBindingRelationship.AnimationSource);

    /// <summary>Authors joint indices and matching weights using one bulk native call.</summary>
    public void SetJointInfluences(
        ReadOnlySpan<int> jointIndices,
        ReadOnlySpan<float> jointWeights,
        int elementSize,
        UsdSkelInterpolation interpolation)
    {
        if (jointIndices.IsEmpty)
        {
            throw new ArgumentException(
                "Joint influences must not be empty.",
                nameof(jointIndices));
        }
        if (jointIndices.Length != jointWeights.Length)
        {
            throw new ArgumentException(
                "Joint indices and weights must have equal lengths.",
                nameof(jointWeights));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementSize);
        if (jointIndices.Length % elementSize != 0)
        {
            throw new ArgumentException(
                "The influence count must be divisible by element size.",
                nameof(elementSize));
        }
        if (interpolation is not UsdSkelInterpolation.Constant and
            not UsdSkelInterpolation.Vertex)
        {
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        }
        if (interpolation == UsdSkelInterpolation.Constant &&
            jointIndices.Length != elementSize)
        {
            throw new ArgumentException(
                "Constant interpolation requires exactly one influence tuple.",
                nameof(jointIndices));
        }
        for (int index = 0; index < jointWeights.Length; ++index)
        {
            if (!float.IsFinite(jointWeights[index]) || jointWeights[index] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(jointWeights),
                    "Joint weights must be finite and non-negative.");
            }
        }

        int jointCount = GetInheritedSkeletonJointCount();
        for (int index = 0; index < jointIndices.Length; ++index)
        {
            if (jointIndices[index] < 0 || jointIndices[index] >= jointCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(jointIndices),
                    "Every joint index must reference the bound skeleton.");
            }
        }

        Stage.Native.SetSkelJointInfluences(
            Path,
            jointIndices,
            jointWeights,
            elementSize,
            (OpenUsdNativeSkelInterpolation)interpolation);
    }

    /// <summary>Gets joint indices, weights, element size, and interpolation in one bulk call.</summary>
    public UsdSkelJointInfluences GetJointInfluences()
    {
        OpenUsdNativeSkelInfluences value = Stage.Native.GetSkelJointInfluences(Path);
        return new UsdSkelJointInfluences(
            value.JointIndices,
            value.JointWeights,
            value.ElementSize,
            (UsdSkelInterpolation)value.Interpolation);
    }

    private int GetInheritedSkeletonJointCount()
    {
        string currentPath = Path;
        while (currentPath.Length > 1)
        {
            try
            {
                string[] targets = Stage.GetPrim(currentPath)
                    .GetRelationship("skel:skeleton")
                    .GetTargets();
                if (targets.Length > 1)
                {
                    throw new InvalidOperationException(
                        "The inherited skeleton relationship must not have multiple targets.");
                }
                if (targets.Length == 1)
                {
                    UsdSkelSkeleton skeleton =
                        UsdSkelSkeleton.Wrap(Stage.GetPrim(targets[0]));
                    return skeleton.GetJoints().Length;
                }
            }
            catch (OpenUsdNativeException exception)
                when (exception.Status == OpenUsdNativeStatus.NotFound)
            {
            }

            int separator = currentPath.LastIndexOf('/');
            currentPath = separator <= 0 ? "/" : currentPath[..separator];
        }
        throw new InvalidOperationException(
            $"Prim '{Path}' has no inherited skeleton relationship.");
    }

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
