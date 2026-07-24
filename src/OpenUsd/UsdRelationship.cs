// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd;

/// <summary>A lightweight relationship descriptor and target wrapper.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdRelationship : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdRelationship(UsdStage stage, string primPath, string name)
    {
        _stage = stage;
        PrimPath = primPath;
        Name = name;
    }

    /// <summary>Gets the owning prim path.</summary>
    public string PrimPath { get; }

    /// <summary>Gets the property name.</summary>
    public string Name { get; }

    /// <summary>Gets the composed target paths.</summary>
    public string[] GetTargets() => Stage.Native.GetRelationshipTargets(PrimPath, Name);

    /// <summary>Replaces target paths using one bulk native call.</summary>
    public void SetTargets(ReadOnlySpan<string> targets) =>
        Stage.Native.SetRelationshipTargets(PrimPath, Name, targets);

    /// <summary>Clears authored targets.</summary>
    public void ClearTargets() => Stage.Native.ClearRelationshipTargets(PrimPath, Name);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The relationship is not attached to a stage.");
}
