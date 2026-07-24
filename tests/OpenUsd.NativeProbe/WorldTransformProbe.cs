// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class WorldTransformProbe
{
    internal static void Run(string directory)
    {
        string stagePath = Path.Combine(directory, "managed-world-transforms.usda");
        File.Delete(stagePath);

        try
        {
            using (UsdStage stage = UsdStage.Create(stagePath))
            {
                UsdGeomXform world = stage.DefineXform("/World");
                UsdGeomXform parent = stage.DefineXform("/World/Parent");
                UsdGeomXform child = stage.DefineXform("/World/Parent/Child");
                UsdGeomXform reset = stage.DefineXform("/World/Parent/Reset");
                UsdGeomXform inactive = stage.DefineXform("/World/Inactive");
                UsdGeomXform nonFinite = stage.DefineXform("/World/NonFinite");

                world.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(10, 0, 0));
                world.Xformable.SetLocalTransform(
                    UsdMatrix4d.CreateTranslation(20, 0, 0),
                    timeCode: 10);
                parent.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(0, 2, 0));
                parent.Xformable.SetLocalTransform(
                    UsdMatrix4d.CreateTranslation(0, 4, 0),
                    timeCode: 10);
                child.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(0, 0, 3));
                child.Xformable.SetLocalTransform(
                    UsdMatrix4d.CreateTranslation(0, 0, 6),
                    timeCode: 10);
                reset.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(7, 11, 13));
                reset.Xformable.SetResetXformStack(true);

                UsdMatrix4d detached = child.Xformable.GetWorldTransform();
                Require(
                    detached.TryInvert(out UsdMatrix4d inverse) &&
                    detached.GetInverse() == inverse &&
                    inverse.TransformPoint(
                        detached.TransformPoint(new UsdVec3d(2, 3, 5))) ==
                        new UsdVec3d(2, 3, 5),
                    "Managed world-transform inversion failed.");
                Require(
                    detached.ExtractTranslation() == new UsdVec3d(10, 2, 3),
                    "Default world transform did not include the complete parent stack.");
                Require(
                    child.Xformable.GetWorldTransform(10).ExtractTranslation() ==
                        new UsdVec3d(20, 4, 6),
                    "Sampled world transform did not evaluate at numeric time.");
                Require(
                    reset.Xformable.GetWorldTransform().ExtractTranslation() ==
                        new UsdVec3d(7, 11, 13),
                    "Reset-xform-stack did not stop inherited transforms.");

                world.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(100, 0, 0));
                Require(
                    detached.ExtractTranslation() == new UsdVec3d(10, 2, 3) &&
                    child.Xformable.GetWorldTransform().ExtractTranslation() ==
                        new UsdVec3d(100, 2, 3),
                    "Returned world transforms were not detached values.");

                inactive.Prim.SetActive(false);
                Require(
                    RejectsNotFound(() => inactive.Xformable.GetWorldTransform()),
                    "Inactive xformables did not use the documented NotFound policy.");
                Require(
                    RejectsNonFinite(child.Xformable, double.NaN) &&
                    RejectsNonFinite(child.Xformable, double.PositiveInfinity) &&
                    RejectsNonFinite(child.Xformable, double.NegativeInfinity),
                    "Managed world transforms did not reject non-finite time before P/Invoke.");
                nonFinite.Xformable.SetLocalTransform(new UsdMatrix4d(
                    double.NaN, 0, 0, 0,
                    0, 1, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1));
                Require(
                    RejectsNativeError(() => nonFinite.Xformable.GetWorldTransform()),
                    "Authored non-finite world transforms were published.");
                stage.RemovePrim("/World/NonFinite");
                stage.Save();
            }

            using UsdStage reopened = UsdStage.Open(stagePath);
            UsdGeomXformable childXformable =
                UsdGeomXform.Wrap(reopened.GetPrim("/World/Parent/Child")).Xformable;
            UsdGeomXformable resetXformable =
                UsdGeomXform.Wrap(reopened.GetPrim("/World/Parent/Reset")).Xformable;
            Require(
                childXformable.GetWorldTransform().ExtractTranslation() ==
                    new UsdVec3d(100, 2, 3) &&
                childXformable.GetWorldTransform(10).ExtractTranslation() ==
                    new UsdVec3d(20, 4, 6) &&
                resetXformable.GetWorldTransform().ExtractTranslation() ==
                    new UsdVec3d(7, 11, 13),
                "Saved managed world transforms did not round-trip.");
        }
        finally
        {
            File.Delete(stagePath);
        }
    }

    private static bool RejectsNotFound(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            return true;
        }
    }

    private static bool RejectsNonFinite(UsdGeomXformable xformable, double timeCode)
    {
        try
        {
            _ = xformable.GetWorldTransform(timeCode);
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static bool RejectsNativeError(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NativeError)
        {
            return true;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
