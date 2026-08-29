// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkOcioProcessorTests
{
    private static string TestConfigPath =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "test-assets", "ocio-test-config.ocio"));

    [Test]
    public async Task CreateProcessor_WithValidConfig_Succeeds()
    {
        await Assert.That(File.Exists(TestConfigPath)).IsTrue();
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear",
            "TestDisplay",
            "TestView");
        using var processor = transform.CreateProcessor();
        await Assert.That(processor).IsNotNull();
        await Assert.That(processor.Transform).IsEqualTo(transform);
    }

    [Test]
    public async Task CreateProcessor_WithDefaults_Succeeds()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();
        await Assert.That(processor).IsNotNull();
        await Assert.That(processor.Transform.Display).IsNull();
        await Assert.That(processor.Transform.View).IsNull();
        await Assert.That(processor.Transform.Looks).IsNull();
    }

    [Test]
    public async Task CreateProcessor_WithNonAsciiConfigPath_Succeeds()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"openusd-ocio-\u8272-{Guid.NewGuid():N}");
        string configPath = Path.Combine(directory, "config.ocio");
        Directory.CreateDirectory(directory);
        File.Copy(TestConfigPath, configPath);
        try
        {
            var transform = new SilkOpenColorIoDisplayTransform(configPath, "linear");
            using var processor = transform.CreateProcessor();
            await Assert.That(processor.Transform.ConfigPath).IsEqualTo(configPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CreateProcessor_WithInvalidConfigPath_ThrowsNativeException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            "nonexistent/path.ocio",
            "linear");
        var exception = await Assert.ThrowsAsync<OpenUsdNativeException>(
            () => Task.Run(() => transform.CreateProcessor()));
        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task CreateProcessor_WithInvalidColorSpace_ThrowsNativeException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "nonexistent_color_space");
        var exception = await Assert.ThrowsAsync<OpenUsdNativeException>(
            () => Task.Run(() => transform.CreateProcessor()));
        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task CreateProcessor_WithInvalidDisplay_ThrowsNativeException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear",
            "NonexistentDisplay",
            "TestView");
        var exception = await Assert.ThrowsAsync<OpenUsdNativeException>(
            () => Task.Run(() => transform.CreateProcessor()));
        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task CreateProcessor_WithInvalidView_ThrowsNativeException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear",
            "TestDisplay",
            "NonexistentView");
        var exception = await Assert.ThrowsAsync<OpenUsdNativeException>(
            () => Task.Run(() => transform.CreateProcessor()));
        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task Apply_WithBlackPixel_ProducesBlackOutput()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();

        byte[] source = new byte[8]; // 1 pixel: 4 x Half(0)
        byte[] destination = new byte[4];

        processor.Apply(source, destination, 1, 1, 0f);

        await Assert.That(destination[0]).IsEqualTo((byte)0);
        await Assert.That(destination[1]).IsEqualTo((byte)0);
        await Assert.That(destination[2]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task Apply_AlphaIsPreserved()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();

        // 1 pixel: RGBA with zero RGB, alpha = 1.0 (half = 0x3C00)
        byte[] source = new byte[8];
        source[6] = 0x00; // alpha low byte
        source[7] = 0x3C; // alpha high byte = Half(1.0)
        byte[] destination = new byte[4];

        processor.Apply(source, destination, 1, 1, 0f);

        await Assert.That(destination[3]).IsEqualTo((byte)255);
    }

    [Test]
    public async Task Apply_ExposureScalesRgbBeforeOcio()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();

        // 1 pixel: R = 0.5 (Half 0x3800), G = B = 0, A = 1.0 (0x3C00)
        byte[] source = new byte[8];
        source[0] = 0x00;
        source[1] = 0x38; // R = Half(0.5)
        source[6] = 0x00;
        source[7] = 0x3C; // A = 1.0
        byte[] destNoExposure = new byte[4];
        byte[] destWithExposure = new byte[4];

        processor.Apply(source, destNoExposure, 1, 1, 0f);
        processor.Apply(source, destWithExposure, 1, 1, 1f);

        // With exposure=1 (2^1=2), the red channel value should be larger
        await Assert.That(destWithExposure[0]).IsGreaterThan(destNoExposure[0]);
    }

    [Test]
    public async Task Apply_SourceDataUnchanged()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();

        byte[] source = new byte[8];
        source[0] = 0x00;
        source[1] = 0x38; // R = Half(0.5)
        source[6] = 0x00;
        source[7] = 0x3C; // A = 1.0
        byte[] originalSource = (byte[])source.Clone();
        byte[] destination = new byte[4];

        processor.Apply(source, destination, 1, 1, 0f);

        await Assert.That(source).IsEquivalentTo(originalSource);
    }

    [Test]
    public async Task Apply_WithInvalidSizes_ThrowsException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();

        byte[] sourceTooSmall = new byte[4]; // Needs 8 for 1 pixel
        byte[] destination = new byte[4];

        await Assert.ThrowsAsync<OpenUsdNativeException>(
            () => Task.Run(() => processor.Apply(sourceTooSmall, destination, 1, 1, 0f)));
    }

    [Test]
    public async Task Apply_WithNonFiniteExposure_ThrowsException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();

        byte[] source = new byte[8];
        byte[] destination = new byte[4];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.Run(() => processor.Apply(source, destination, 1, 1, float.PositiveInfinity)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.Run(() => processor.Apply(source, destination, 1, 1, float.NaN)));
    }

    [Test]
    public async Task Apply_WithOverflowingExposure_ThrowsException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();
        byte[] source = new byte[8];
        byte[] destination = new byte[4];

        var exception = await Assert.ThrowsAsync<OpenUsdNativeException>(
            () => Task.Run(() => processor.Apply(source, destination, 1, 1, 128f)));
        await Assert.That(exception!.Status).IsEqualTo(OpenUsdNativeStatus.InvalidArgument);
    }

    [Test]
    public async Task Dispose_ThenApply_ThrowsObjectDisposed()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        var processor = transform.CreateProcessor();
        processor.Dispose();

        byte[] source = new byte[8];
        byte[] destination = new byte[4];

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => Task.Run(() => processor.Apply(source, destination, 1, 1, 0f)));
    }

    [Test]
    public async Task DoubleDispose_DoesNotThrow()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        var processor = transform.CreateProcessor();
        processor.Dispose();
        processor.Dispose(); // Should not throw
        await Assert.That(processor.Transform.ConfigPath).IsEqualTo(TestConfigPath);
    }

    [Test]
    public async Task DisplayTransform_NullConfigPath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(new SilkOpenColorIoDisplayTransform(null!, "linear")));
    }

    [Test]
    public async Task DisplayTransform_EmptyConfigPath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(new SilkOpenColorIoDisplayTransform("", "linear")));
    }

    [Test]
    public async Task DisplayTransform_NullSourceColorSpace_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(new SilkOpenColorIoDisplayTransform("path.ocio", null!)));
    }

    [Test]
    public async Task DisplayTransform_EmptyDisplayNormalizesToNull()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            "path.ocio",
            "linear",
            display: "",
            view: "",
            looks: "");
        await Assert.That(transform.Display).IsNull();
        await Assert.That(transform.View).IsNull();
        await Assert.That(transform.Looks).IsNull();
    }

    [Test]
    public async Task DisplayTransform_PropertiesRoundTrip()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            "path.ocio",
            "linear",
            "ACES",
            "sRGB",
            "TestLook");
        await Assert.That(transform.ConfigPath).IsEqualTo("path.ocio");
        await Assert.That(transform.SourceColorSpace).IsEqualTo("linear");
        await Assert.That(transform.Display).IsEqualTo("ACES");
        await Assert.That(transform.View).IsEqualTo("sRGB");
        await Assert.That(transform.Looks).IsEqualTo("TestLook");
    }

    [Test]
    public async Task CreateProcessor_WithLooks_Succeeds()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear",
            "TestDisplay",
            "TestView",
            "TestLook");
        using var processor = transform.CreateProcessor();
        await Assert.That(processor).IsNotNull();
    }

    [Test]
    public async Task OcioSettings_RequireIdentityOutputTransform()
    {
        var settings = new RenderSettings(
            1, true, true, System.Numerics.Vector4.Zero, true, true,
            RenderComplexity.Low,
            RenderOutputTransform.Reinhard,
            -6f);

        await Assert.That(() => SilkFrameCapture.ValidateOcioSettings(settings))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Apply_WithNonFiniteSourceChannel_ThrowsException()
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            TestConfigPath,
            "linear");
        using var processor = transform.CreateProcessor();
        byte[] source = new byte[8];
        source[0] = 0x00;
        source[1] = 0x7C;
        byte[] destination = new byte[4];

        var exception = await Assert.ThrowsAsync<OpenUsdNativeException>(
            () => Task.Run(() => processor.Apply(source, destination, 1, 1, 0f)));
        await Assert.That(exception!.Status).IsEqualTo(OpenUsdNativeStatus.InvalidArgument);
    }
}
