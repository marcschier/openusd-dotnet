// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using OpenUsd.Geom;
using OpenUsd.Lux;
using OpenUsd.Shade;
using OpenUsd.Skel;

namespace OpenUsd.Tests;

public sealed class UsdStageBoundResultGuardTests
{
    [Test]
    public async Task AllPublicStageWrappersImplementMarker()
    {
        Type[] stageBoundTypes =
        [
            typeof(UsdStage),
            typeof(UsdLayer),
            typeof(UsdPrim),
            typeof(UsdAttribute),
            typeof(UsdRelationship),
            typeof(UsdStageRenderSource),
            typeof(UsdStageRenderLease),
            typeof(UsdGeomCamera),
            typeof(UsdGeomBasisCurves),
            typeof(UsdGeomCapsule),
            typeof(UsdGeomCone),
            typeof(UsdGeomCube),
            typeof(UsdGeomCylinder),
            typeof(UsdGeomHermiteCurves),
            typeof(UsdGeomImageable),
            typeof(UsdGeomMesh),
            typeof(UsdGeomModelAPI),
            typeof(UsdGeomNurbsCurves),
            typeof(UsdGeomNurbsPatch),
            typeof(UsdGeomPlane),
            typeof(UsdGeomPoints),
            typeof(UsdGeomPointInstancer),
            typeof(UsdGeomPrimvar),
            typeof(UsdGeomPrimvarsAPI),
            typeof(UsdGeomSphere),
            typeof(UsdGeomSubset),
            typeof(UsdGeomTetMesh),
            typeof(UsdGeomXform),
            typeof(UsdGeomXformable),
            typeof(UsdPreviewSurface),
            typeof(UsdShadeConnectable),
            typeof(UsdShadeInput),
            typeof(UsdShadeMaterial),
            typeof(UsdShadeNodeGraph),
            typeof(UsdShadeOutput),
            typeof(UsdShadeShader),
            typeof(UsdUvTexture),
            typeof(UsdLuxCylinderLight),
            typeof(UsdLuxDiskLight),
            typeof(UsdLuxDistantLight),
            typeof(UsdLuxDomeLight),
            typeof(UsdLuxLight),
            typeof(UsdLuxRectLight),
            typeof(UsdLuxShaping),
            typeof(UsdLuxSphereLight),
            typeof(UsdSkelAnimation),
            typeof(UsdSkelBinding),
            typeof(UsdSkelBlendShape),
            typeof(UsdSkelRoot),
            typeof(UsdSkelSkeleton)
        ];

        foreach (Type type in stageBoundTypes)
        {
            await Assert.That(typeof(IUsdStageBound).IsAssignableFrom(type))
                .IsTrue()
                .Because(type.FullName ?? type.Name);
        }
    }

    [Test]
    [Arguments(typeof(object))]
    [Arguments(typeof(IUsdDetachedResult))]
    [Arguments(typeof(AbstractDetachedResult))]
    [Arguments(typeof(CustomClass))]
    [Arguments(typeof(CustomStruct))]
    [Arguments(typeof(Box<UsdStage>))]
    [Arguments(typeof(ContainsPrim))]
    [Arguments(typeof(IEnumerable<int>))]
    [Arguments(typeof(IReadOnlyList<int>))]
    [Arguments(typeof(CustomList))]
    [Arguments(typeof(CustomDictionary))]
    [Arguments(typeof(CustomTuple))]
    [Arguments(typeof(KeyValuePair<string, int>))]
    [Arguments(typeof(Task))]
    [Arguments(typeof(Task<string>))]
    [Arguments(typeof(ValueTask))]
    [Arguments(typeof(ValueTask<string>))]
    [Arguments(typeof(UsdStage))]
    [Arguments(typeof(List<object>))]
    [Arguments(typeof(Dictionary<string, object>))]
    public async Task UnknownStageBoundLazyAndAsyncShapesAreRejected(Type resultType)
    {
        UsdStageBoundResultException exception = Capture(
            () => UsdStageBoundResultGuard.ThrowIfForbiddenType(resultType));

        await Assert.That(exception.Code)
            .IsEqualTo(UsdStageBoundResultException.ErrorCode);
        await Assert.That(exception.Message)
            .IsEqualTo(UsdStageBoundResultException.ErrorMessage);
    }

    [Test]
    public async Task TrustedDetachedShapesAreAllowed()
    {
        Type[] allowedTypes =
        [
            typeof(bool),
            typeof(long),
            typeof(double),
            typeof(decimal),
            typeof(string),
            typeof(DayOfWeek),
            typeof(UsdVec3f),
            typeof(UsdMatrix4d),
            typeof(UsdBounds3d),
            typeof(UsdGeomCameraState),
            typeof(UsdShadeConnection),
            typeof(UsdSkelJointInfluences),
            typeof(DetachedResult),
            typeof(DetachedStruct),
            typeof(int?),
            typeof(int[]),
            typeof(List<UsdVec3f>),
            typeof(Dictionary<string, DetachedResult>),
            typeof(Tuple<string, int>),
            typeof((string Name, UsdVec3f Value))
        ];

        foreach (Type type in allowedTypes)
        {
            UsdStageBoundResultGuard.ThrowIfForbiddenType(type);
        }

        object[] allowedResults =
        [
            "detached",
            42L,
            new DetachedResult("value", 7),
            new DetachedStruct(9),
            new[] { new UsdVec3f(1, 2, 3) },
            new List<int> { 1, 2, 3 },
            new Dictionary<string, int> { ["value"] = 4 },
            (Name: "value", Count: 5)
        ];

        foreach (object result in allowedResults)
        {
            UsdStageBoundResultGuard.ThrowIfForbiddenResult(result);
        }

        await Assert.That(allowedResults).Count().IsEqualTo(8);
    }

    [Test]
    public async Task RuntimeValidationRejectsCustomContainersWithoutInvokingThem()
    {
        object[] results =
        [
            new Box<UsdPrim>(default),
            new ContainsPrim(default),
            ThrowIfEnumerated(),
            new CustomList(),
            new CustomDictionary(),
            new CustomTuple()
        ];

        foreach (object result in results)
        {
            _ = Capture(() => UsdStageBoundResultGuard.ThrowIfForbiddenResult(result));
        }

        await Assert.That(results).Count().IsEqualTo(6);
    }

    private static UsdStageBoundResultException Capture(Action action)
    {
        try
        {
            action();
        }
        catch (UsdStageBoundResultException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected a scheduler result-contract rejection.");
    }

    private static IEnumerable<int> ThrowIfEnumerated()
    {
        throw new InvalidOperationException("Lazy sequences must not be enumerated.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public sealed record DetachedResult(string Name, int Count) : IUsdDetachedResult;

    public readonly record struct DetachedStruct(int Value) : IUsdDetachedResult;

    public abstract class AbstractDetachedResult : IUsdDetachedResult;

    public sealed class CustomClass;

    public readonly struct CustomStruct;

    public sealed record Box<T>(T Value);

    public readonly record struct ContainsPrim(UsdPrim Prim);

    public sealed class CustomList : List<int>;

    public sealed class CustomDictionary : Dictionary<string, int>;

    public sealed class CustomTuple : ITuple
    {
        public int Length => throw new InvalidOperationException("Custom tuples must not be read.");

        public object? this[int index] =>
            throw new InvalidOperationException("Custom tuples must not be read.");
    }
}
