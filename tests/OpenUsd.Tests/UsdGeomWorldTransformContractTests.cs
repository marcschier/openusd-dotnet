// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using OpenUsd.Geom;

namespace OpenUsd.Tests;

public sealed class UsdGeomWorldTransformContractTests
{
    [Test]
    public async Task XformablePublishesDetachedDefaultAndNumericWorldTransformOverloads()
    {
        MethodInfo[] methods = typeof(UsdGeomXformable)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method => method.Name == nameof(UsdGeomXformable.GetWorldTransform))
            .OrderBy(static method => method.GetParameters().Length)
            .ToArray();

        await Assert.That(methods).Count().IsEqualTo(2);
        await Assert.That(methods[0].ReturnType).IsEqualTo(typeof(UsdMatrix4d));
        await Assert.That(methods[0].GetParameters()).IsEmpty();
        await Assert.That(methods[1].ReturnType).IsEqualTo(typeof(UsdMatrix4d));
        await Assert.That(methods[1].GetParameters()).Count().IsEqualTo(1);
        await Assert.That(methods[1].GetParameters()[0].ParameterType).IsEqualTo(typeof(double));
        await Assert.That(typeof(IUsdDetachedResult).IsAssignableFrom(typeof(UsdMatrix4d))).IsTrue();
    }

    [Test]
    public async Task NumericWorldTransformRejectsNonFiniteTimeBeforeStageAccess()
    {
        double[] invalidTimes = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];

        foreach (double timeCode in invalidTimes)
        {
            ArgumentOutOfRangeException exception = Capture(
                () => default(UsdGeomXformable).GetWorldTransform(timeCode));
            await Assert.That(exception.ParamName).IsEqualTo("timeCode");
        }
    }

    private static ArgumentOutOfRangeException Capture(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected a non-finite time-code rejection.");
    }
}
