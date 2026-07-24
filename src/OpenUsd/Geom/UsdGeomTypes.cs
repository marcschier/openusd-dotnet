// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Geom;

/// <summary>Identifies the focused UsdGeom schema types supported by the native facade.</summary>
internal enum UsdGeomSchemaKind
{
    Imageable = 0,
    Xformable = 1,
    Xform = 2,
    Mesh = 3,
    Camera = 4
}

/// <summary>Specifies composed imageable visibility.</summary>
public enum UsdGeomVisibility
{
    /// <summary>Inherits visibility from ancestors.</summary>
    Inherited = 0,

    /// <summary>Hides the prim and its descendants.</summary>
    Invisible = 1
}

/// <summary>Specifies an imageable purpose.</summary>
public enum UsdGeomPurpose
{
    /// <summary>The default purpose.</summary>
    Default = 0,

    /// <summary>Final render geometry.</summary>
    Render = 1,

    /// <summary>Proxy geometry.</summary>
    Proxy = 2,

    /// <summary>Guide geometry.</summary>
    Guide = 3
}

/// <summary>Selects imageable purposes included in a world-bounds query.</summary>
[Flags]
public enum UsdGeomPurposeMask : uint
{
    /// <summary>Includes no imageable purposes.</summary>
    None = 0,

    /// <summary>Includes geometry with the default purpose.</summary>
    Default = 1U << 0,

    /// <summary>Includes proxy geometry.</summary>
    Proxy = 1U << 1,

    /// <summary>Includes final render geometry.</summary>
    Render = 1U << 2,

    /// <summary>Includes guide geometry.</summary>
    Guide = 1U << 3,

    /// <summary>Includes default, proxy, render, and guide geometry.</summary>
    All = Default | Proxy | Render | Guide
}

/// <summary>Specifies how mesh normals are interpolated.</summary>
public enum UsdGeomInterpolation
{
    /// <summary>One value for the whole primitive.</summary>
    Constant = 0,

    /// <summary>One value per face.</summary>
    Uniform = 1,

    /// <summary>Values vary across the primitive.</summary>
    Varying = 2,

    /// <summary>One value per point.</summary>
    Vertex = 3,

    /// <summary>One value per face vertex.</summary>
    FaceVarying = 4
}

/// <summary>Specifies a mesh subdivision scheme.</summary>
public enum UsdGeomSubdivisionScheme
{
    /// <summary>No subdivision.</summary>
    None = 0,

    /// <summary>Catmull-Clark subdivision.</summary>
    CatmullClark = 1,

    /// <summary>Loop subdivision.</summary>
    Loop = 2,

    /// <summary>Bilinear subdivision.</summary>
    Bilinear = 3
}

/// <summary>Specifies mesh winding orientation.</summary>
public enum UsdGeomOrientation
{
    /// <summary>Right-handed winding.</summary>
    RightHanded = 0,

    /// <summary>Left-handed winding.</summary>
    LeftHanded = 1
}

/// <summary>Specifies camera projection.</summary>
public enum UsdGeomCameraProjection
{
    /// <summary>Perspective projection.</summary>
    Perspective = 0,

    /// <summary>Orthographic projection.</summary>
    Orthographic = 1
}

/// <summary>Contains minimum and maximum corners for a three-dimensional extent.</summary>
public readonly record struct UsdExtent3f(
    UsdVec3f Minimum,
    UsdVec3f Maximum) : IUsdDetachedResult;
