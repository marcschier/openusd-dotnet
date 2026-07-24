// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Geom;

/// <summary>A validated view of a UsdGeomCamera prim.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomCamera : IUsdStageBound
{
    private const int FocalLengthProperty = 0;
    private const int HorizontalApertureProperty = 1;
    private const int VerticalApertureProperty = 2;
    private readonly UsdStage? _stage;

    internal UsdGeomCamera(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the imageable schema view.</summary>
    public UsdGeomImageable Imageable => new(Stage, Path);

    /// <summary>Gets the xformable schema view.</summary>
    public UsdGeomXformable Xformable => new(Stage, Path);

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Tries to wrap a UsdGeomCamera prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdGeomCamera value)
    {
        if (UsdGeomSchema.TryValidate(prim, UsdGeomSchemaKind.Camera, out UsdStage? stage))
        {
            value = new UsdGeomCamera(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a UsdGeomCamera prim or throws for a wrong schema.</summary>
    public static UsdGeomCamera Wrap(UsdPrim prim) => new(
        UsdGeomSchema.Validate(prim, UsdGeomSchemaKind.Camera, nameof(UsdGeomCamera)),
        prim.Path);

    /// <summary>Gets or sets projection.</summary>
    public UsdGeomCameraProjection Projection
    {
        get => (UsdGeomCameraProjection)Stage.Native.GetGeomCameraProjection(Path);
        set => Stage.Native.SetGeomCameraProjection(Path, (int)value);
    }

    /// <summary>Gets or sets focal length.</summary>
    public float FocalLength
    {
        get => Stage.Native.GetGeomCameraFloat(Path, FocalLengthProperty);
        set => Stage.Native.SetGeomCameraFloat(Path, FocalLengthProperty, value);
    }

    /// <summary>Gets or sets horizontal aperture.</summary>
    public float HorizontalAperture
    {
        get => Stage.Native.GetGeomCameraFloat(Path, HorizontalApertureProperty);
        set => Stage.Native.SetGeomCameraFloat(Path, HorizontalApertureProperty, value);
    }

    /// <summary>Gets or sets vertical aperture.</summary>
    public float VerticalAperture
    {
        get => Stage.Native.GetGeomCameraFloat(Path, VerticalApertureProperty);
        set => Stage.Native.SetGeomCameraFloat(Path, VerticalApertureProperty, value);
    }

    /// <summary>Gets or sets the near/far clipping range.</summary>
    public UsdVec2f ClippingRange
    {
        get => UsdVec2f.FromNative(Stage.Native.GetGeomCameraClippingRange(Path));
        set => Stage.Native.SetGeomCameraClippingRange(Path, value.ToNative());
    }

    /// <summary>Gets detached camera optics and frustum state at default time.</summary>
    public UsdGeomCameraState GetState() =>
        UsdGeomCameraState.FromNative(Stage.Native.GetGeomCameraState(Path));

    /// <summary>Gets detached camera optics and frustum state at a numeric time code.</summary>
    public UsdGeomCameraState GetState(double timeCode)
    {
        double validatedTimeCode = UsdGeomCameraState.ValidateTimeCode(timeCode);
        return UsdGeomCameraState.FromNative(
            Stage.Native.GetGeomCameraState(Path, validatedTimeCode));
    }

    /// <summary>Authors a camera transform at default time.</summary>
    public void SetTransform(UsdMatrix4d value) => Xformable.SetLocalTransform(value);

    /// <summary>Authors a sampled camera transform.</summary>
    public void SetTransform(UsdMatrix4d value, double timeCode) =>
        Xformable.SetLocalTransform(value, timeCode);

    /// <summary>Gets the camera transform at default time.</summary>
    public UsdMatrix4d GetTransform() => Xformable.GetLocalTransform();

    /// <summary>Gets a sampled camera transform.</summary>
    public UsdMatrix4d GetTransform(double timeCode) =>
        Xformable.GetLocalTransform(timeCode);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
