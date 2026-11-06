// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdStageLinearUnitsNativeTests
{
    [Test]
    public async Task UnauthoredStageReportsTheUsdGeomFallbacks()
    {
        string path = await WriteStageAsync("#usda 1.0\n\ndef Xform \"World\"\n{\n}\n");
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdStageLinearUnits units = stage.GetLinearUnits();
            await Assert.That(units.MetersPerUnit).IsEqualTo(UsdStageLinearUnits.FallbackMetersPerUnit);
            await Assert.That(units.IsMetersPerUnitAuthored).IsFalse();
            await Assert.That(units.UpAxis == UsdStageUpAxis.Y || units.UpAxis == UsdStageUpAxis.Z).IsTrue();
            await Assert.That(units.IsUpAxisAuthored).IsFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task AuthoredMetersPerUnitAndUpAxisAreReportedAsAuthored()
    {
        string path = await WriteStageAsync(
            "#usda 1.0\n(\n    metersPerUnit = 1\n    upAxis = \"Z\"\n)\n\ndef Xform \"World\"\n{\n}\n");
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdStageLinearUnits units = stage.GetLinearUnits();
            await Assert.That(units).IsEqualTo(new UsdStageLinearUnits(1, true, UsdStageUpAxis.Z, true));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task EachAuthoredFlagIsIndependent()
    {
        string path = await WriteStageAsync("#usda 1.0\n(\n    metersPerUnit = 0.001\n)\n");
        string axisOnly = await WriteStageAsync("#usda 1.0\n(\n    upAxis = \"Y\"\n)\n");
        try
        {
            using UsdStage millimeters = UsdStage.Open(path);
            UsdStageLinearUnits units = millimeters.GetLinearUnits();
            await Assert.That(units.MetersPerUnit).IsEqualTo(0.001);
            await Assert.That(units.IsMetersPerUnitAuthored).IsTrue();
            await Assert.That(units.IsUpAxisAuthored).IsFalse();

            using UsdStage yUp = UsdStage.Open(axisOnly);
            UsdStageLinearUnits axis = yUp.GetLinearUnits();
            await Assert.That(axis.MetersPerUnit).IsEqualTo(UsdStageLinearUnits.FallbackMetersPerUnit);
            await Assert.That(axis.IsMetersPerUnitAuthored).IsFalse();
            await Assert.That(axis.UpAxis).IsEqualTo(UsdStageUpAxis.Y);
            await Assert.That(axis.IsUpAxisAuthored).IsTrue();
        }
        finally
        {
            File.Delete(path);
            File.Delete(axisOnly);
        }
    }

    [Test]
    public async Task UnsupportedUpAxisIsRefusedRatherThanGuessed()
    {
        string path = await WriteStageAsync("#usda 1.0\n(\n    upAxis = \"X\"\n)\n");
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            OpenUsdNativeException? failure = null;
            try
            {
                _ = stage.GetLinearUnits();
            }
            catch (OpenUsdNativeException exception)
            {
                failure = exception;
            }

            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Message).Contains("Y or Z");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task DisposedStageIsRefused()
    {
        string path = await WriteStageAsync("#usda 1.0\n");
        try
        {
            UsdStage stage = UsdStage.Open(path);
            stage.Dispose();
            await Assert.That(() => stage.GetLinearUnits()).Throws<ObjectDisposedException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteStageAsync(string contents)
    {
        string? pluginPath = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(pluginPath) || !Directory.Exists(pluginPath))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH and stage a matching native data ABI 26 runtime.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(pluginPath!);
        string root = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "native-work");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, $"linear-units-{Guid.NewGuid():N}.usda");
        await File.WriteAllTextAsync(path, contents);
        return path;
    }
}
