// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

#pragma warning disable CS1591

/// <summary>UsdPhysics axis tokens used by joint schemas.</summary>
public enum UsdPhysicsAxis
{
    /// <summary>The X axis.</summary>
    X,
    /// <summary>The Y axis.</summary>
    Y,
    /// <summary>The Z axis.</summary>
    Z
}

/// <summary>UsdPhysics mesh-collision approximation tokens.</summary>
public enum UsdPhysicsMeshCollisionApproximation
{
    /// <summary>Use the mesh directly.</summary>
    None,
    /// <summary>Use convex decomposition.</summary>
    ConvexDecomposition,
    /// <summary>Use a convex hull.</summary>
    ConvexHull,
    /// <summary>Use a bounding sphere.</summary>
    BoundingSphere,
    /// <summary>Use a bounding cube.</summary>
    BoundingCube,
    /// <summary>Use mesh simplification.</summary>
    MeshSimplification
}

/// <summary>UsdPhysics drive type tokens.</summary>
public enum UsdPhysicsDriveType
{
    /// <summary>The drive applies force.</summary>
    Force,
    /// <summary>The drive applies acceleration.</summary>
    Acceleration
}

/// <summary>Common multiple-apply UsdPhysics degrees of freedom.</summary>
public static class UsdPhysicsTokens
{
    /// <summary>Translation along X.</summary>
    public const string TransX = "transX";
    /// <summary>Translation along Y.</summary>
    public const string TransY = "transY";
    /// <summary>Translation along Z.</summary>
    public const string TransZ = "transZ";
    /// <summary>Rotation around X.</summary>
    public const string RotX = "rotX";
    /// <summary>Rotation around Y.</summary>
    public const string RotY = "rotY";
    /// <summary>Rotation around Z.</summary>
    public const string RotZ = "rotZ";
    /// <summary>Linear prismatic-joint drive.</summary>
    public const string Linear = "linear";
    /// <summary>Angular revolute-joint drive.</summary>
    public const string Angular = "angular";
    /// <summary>Generic distance limit.</summary>
    public const string Distance = "distance";

    internal static string ToToken(UsdPhysicsAxis axis) => axis switch
    {
        UsdPhysicsAxis.X => "X",
        UsdPhysicsAxis.Y => "Y",
        UsdPhysicsAxis.Z => "Z",
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };

    internal static UsdPhysicsAxis ToAxis(string token) => token switch
    {
        "X" => UsdPhysicsAxis.X,
        "Y" => UsdPhysicsAxis.Y,
        "Z" => UsdPhysicsAxis.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(token))
    };

    internal static string ToToken(UsdPhysicsMeshCollisionApproximation value) => value switch
    {
        UsdPhysicsMeshCollisionApproximation.None => "none",
        UsdPhysicsMeshCollisionApproximation.ConvexDecomposition => "convexDecomposition",
        UsdPhysicsMeshCollisionApproximation.ConvexHull => "convexHull",
        UsdPhysicsMeshCollisionApproximation.BoundingSphere => "boundingSphere",
        UsdPhysicsMeshCollisionApproximation.BoundingCube => "boundingCube",
        UsdPhysicsMeshCollisionApproximation.MeshSimplification => "meshSimplification",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static UsdPhysicsMeshCollisionApproximation ToApproximation(string token) => token switch
    {
        "none" => UsdPhysicsMeshCollisionApproximation.None,
        "convexDecomposition" => UsdPhysicsMeshCollisionApproximation.ConvexDecomposition,
        "convexHull" => UsdPhysicsMeshCollisionApproximation.ConvexHull,
        "boundingSphere" => UsdPhysicsMeshCollisionApproximation.BoundingSphere,
        "boundingCube" => UsdPhysicsMeshCollisionApproximation.BoundingCube,
        "meshSimplification" => UsdPhysicsMeshCollisionApproximation.MeshSimplification,
        _ => throw new ArgumentOutOfRangeException(nameof(token))
    };

    internal static string ToToken(UsdPhysicsDriveType value) => value switch
    {
        UsdPhysicsDriveType.Force => "force",
        UsdPhysicsDriveType.Acceleration => "acceleration",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static UsdPhysicsDriveType ToDriveType(string token) => token switch
    {
        "force" => UsdPhysicsDriveType.Force,
        "acceleration" => UsdPhysicsDriveType.Acceleration,
        _ => throw new ArgumentOutOfRangeException(nameof(token))
    };
}
