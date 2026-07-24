// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdPathTests
{
    [Test]
    [Arguments("/World")]
    [Arguments("/World/Sensor")]
    [Arguments("/World/Sensor_1")]
    [Arguments("/_World/S1")]
    [Arguments("/a/b/c/d")]
    [Arguments("/München/着色器")]
    [Arguments("/A・")]
    [Arguments("/Café")]
    public async Task IsAbsolutePrimPathAcceptsWellFormedPaths(string path)
    {
        await Assert.That(UsdPath.IsAbsolutePrimPath(path)).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("/")]
    [Arguments("World")]
    [Arguments("World/Sensor")]
    [Arguments("/World/")]
    [Arguments("/World//Sensor")]
    [Arguments("/1World")]
    [Arguments("/World/1Sensor")]
    [Arguments("/World.Sensor")]
    [Arguments("/World/{variant=selection}")]
    [Arguments("/ͺ")]
    [Arguments("/World/💥")]
    public async Task IsAbsolutePrimPathRejectsMalformedPaths(string? path)
    {
        await Assert.That(UsdPath.IsAbsolutePrimPath(path)).IsFalse();
    }

    [Test]
    public async Task ValidateAbsolutePrimPathDoesNotThrowForValidPath()
    {
        await Assert.That(() => UsdPath.ValidateAbsolutePrimPath("/World/Sensor")).ThrowsNothing();
    }

    [Test]
    public async Task ValidateAbsolutePrimPathThrowsForInvalidPath()
    {
        await Assert.That(() => UsdPath.ValidateAbsolutePrimPath("not-a-path"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ValidateAbsolutePrimPathIncludesSuppliedParameterName()
    {
        try
        {
            UsdPath.ValidateAbsolutePrimPath("bad", "primPath");
        }
        catch (ArgumentException exception)
        {
            await Assert.That(exception.ParamName).IsEqualTo("primPath");
            return;
        }

        throw new InvalidOperationException("Expected an ArgumentException.");
    }
}
