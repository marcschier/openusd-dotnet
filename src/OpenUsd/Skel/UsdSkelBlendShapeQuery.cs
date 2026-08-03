// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Skel;

/// <summary>Resolves authored blend-shape targets and inbetweens for a binding site.</summary>
public sealed class UsdSkelBlendShapeQuery
{
    private UsdSkelBlendShapeQuery(
        string primPath,
        string[] blendShapes,
        UsdSkelBlendShape[] targets)
    {
        PrimPath = primPath;
        BlendShapes = blendShapes;
        Targets = targets;
    }

    /// <summary>Gets the prim path the query was built from.</summary>
    public string PrimPath { get; }

    /// <summary>Gets blend-shape channel names.</summary>
    public IReadOnlyList<string> BlendShapes { get; }

    /// <summary>Gets target blend-shape schemas.</summary>
    public IReadOnlyList<UsdSkelBlendShape> Targets { get; }

    /// <summary>Builds a query from authored UsdSkelBindingAPI blend-shape data.</summary>
    public static UsdSkelBlendShapeQuery Create(UsdSkelBinding binding)
    {
        string[] names = binding.GetBlendShapes().ToArray();
        UsdSkelBlendShape[] targets = binding.GetBlendShapeTargets().ToArray();
        return new UsdSkelBlendShapeQuery(binding.Path, names, targets);
    }

    /// <summary>Gets all authored inbetweens for one target.</summary>
    public IReadOnlyList<UsdSkelBlendShapeInbetween> GetInbetweens(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= Targets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }
        UsdSkelBlendShape target = Targets[targetIndex];
        return target.GetInbetweenNames()
            .Select(target.GetInbetween)
            .ToArray();
    }
}
