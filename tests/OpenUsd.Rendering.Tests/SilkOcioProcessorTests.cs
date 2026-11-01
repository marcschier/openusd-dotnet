// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed partial class SilkOcioProcessorTests
{
    private static string TestConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "test-assets", "ocio-test-config.ocio");

    private static void RequireNativeRuntime()
    {
        try
        {
            _ = OpenUsdNativeRuntime.Capabilities;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }

    [Test]
    public async Task CreateProcessor_WithValidConfig_Succeeds()
    {
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
    public async Task CreateProcessor_WithBuiltinConfig_Succeeds()
    {
        RequireNativeRuntime();
        var transform = new SilkOpenColorIoDisplayTransform("ocio://default", "ACEScg");
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();

        await Assert.That(processor.Transform).IsEqualTo(transform);
    }

    [Test]
    public async Task CreateProcessor_WithNonAsciiConfigPath_Succeeds()
    {
        RequireNativeRuntime();
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
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CreateProcessor_WithArchiveConfig_Succeeds(bool nonAsciiDirectory, bool nonAsciiFile)
    {
        RequireNativeRuntime();
        string prefix = nonAsciiDirectory ? "openusd-ocio-\u8272-" : "openusd-ocio-ascii-";
        string directory = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string archivePath = Path.Combine(directory, nonAsciiFile ? "config-\u8272.ocioz" : "config.ocioz");
        try
        {
            WriteArchive(archivePath);

            var transform = new SilkOpenColorIoDisplayTransform(archivePath, "linear");
            using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
            await Assert.That(processor.Transform.ConfigPath).IsEqualTo(archivePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateProcessor_WithUnicodeArchiveRestoresWindowsCallerLocale(bool perThreadLocale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows CRT locale state is only available on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        RequireNativeRuntime();
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-ocio-locale-\u8272-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string archivePath = Path.Combine(directory, "config.ocioz");
        string invalidArchivePath = Path.Combine(directory, "invalid.ocioz");
        try
        {
            WriteArchive(archivePath);
            File.WriteAllBytes(invalidArchivePath, [0x50, 0x4b, 0x03, 0x04]);
            LocaleObservation observation = await Task.Run(
                () => OperatingSystem.IsWindows()
                    ? ObserveArchiveLocale(archivePath, invalidArchivePath, perThreadLocale)
                    : throw new PlatformNotSupportedException());

            await Assert.That(observation.BeforeMode).IsEqualTo(perThreadLocale ? 1 : 2);
            await Assert.That(observation.SuccessMode).IsEqualTo(observation.BeforeMode);
            await Assert.That(observation.SuccessLocale).IsEqualTo(observation.BeforeLocale);
            await Assert.That(observation.FailureMode).IsEqualTo(observation.BeforeMode);
            await Assert.That(observation.FailureLocale).IsEqualTo(observation.BeforeLocale);
            await Assert.That(observation.InvalidArchiveFailed).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CreateProcessor_WithInvalidNonAsciiConfig_ReportsItsPath()
    {
        RequireNativeRuntime();
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-ocio-invalid-\u8272-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string configPath = Path.Combine(directory, "config.ocio");
        File.WriteAllText(configPath, "invalid config");
        try
        {
            var transform = new SilkOpenColorIoDisplayTransform(configPath, "linear");
            OpenUsdNativeException? exception = await Assert.That(() =>
            {
                using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
            })
                .Throws<OpenUsdNativeException>();
            await Assert.That(exception!.Message).Contains(configPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CreateProcessor_WithInvalidConfigPath_ThrowsNativeException()
    {
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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
        RequireNativeRuntime();
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

    private static void WriteArchive(string path)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("config.ocio", CompressionLevel.Optimal);
        using Stream destination = entry.Open();
        using Stream source = File.OpenRead(TestConfigPath);
        source.CopyTo(destination);
    }

    [SupportedOSPlatform("windows")]
    private static LocaleObservation ObserveArchiveLocale(
        string archivePath,
        string invalidArchivePath,
        bool perThreadLocale)
    {
        const int ctype = 2;
        const int enablePerThreadLocale = 1;
        const int disablePerThreadLocale = 2;
        int previousMode = ConfigureThreadLocale(enablePerThreadLocale);
        if (previousMode == -1)
        {
            throw new InvalidOperationException("Could not enable the test thread's locale.");
        }

        string previousLocale;
        try
        {
            previousLocale = CurrentLocale();
        }
        catch
        {
            if (ConfigureThreadLocale(previousMode) == -1)
            {
                throw new InvalidOperationException("Could not restore the test thread's locale mode.");
            }
            throw;
        }

        try
        {
            if (SetLocale(ctype, "C") == nint.Zero ||
                (!perThreadLocale && ConfigureThreadLocale(disablePerThreadLocale) == -1))
            {
                throw new InvalidOperationException("Could not set the test thread's locale.");
            }

            int beforeMode = ConfigureThreadLocale(0);
            string beforeLocale = CurrentLocale();
            using (new SilkOpenColorIoDisplayTransform(archivePath, "linear").CreateProcessor())
            {
            }
            int successMode = ConfigureThreadLocale(0);
            string successLocale = CurrentLocale();

            bool invalidArchiveFailed = false;
            try
            {
                using SilkOpenColorIoProcessor processor =
                    new SilkOpenColorIoDisplayTransform(invalidArchivePath, "linear").CreateProcessor();
            }
            catch (OpenUsdNativeException)
            {
                invalidArchiveFailed = true;
            }

            return new LocaleObservation(
                beforeMode,
                beforeLocale,
                successMode,
                successLocale,
                ConfigureThreadLocale(0),
                CurrentLocale(),
                invalidArchiveFailed);
        }
        finally
        {
            RestoreTestThreadLocale(previousMode, previousLocale);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestoreTestThreadLocale(int previousMode, string previousLocale)
    {
        if (ConfigureThreadLocale(1) == -1)
        {
            throw new InvalidOperationException("Could not enable the test thread's locale.");
        }
        nint locale = SetLocale(2, previousLocale);
        int restored = ConfigureThreadLocale(previousMode);
        if (locale == nint.Zero || restored == -1)
        {
            throw new InvalidOperationException("Could not restore the test thread's locale.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string CurrentLocale() =>
        Marshal.PtrToStringUTF8(SetLocale(2, null)) ??
        throw new InvalidOperationException("Could not read the test thread's locale.");

    [LibraryImport("ucrtbase", EntryPoint = "_configthreadlocale")]
    private static partial int ConfigureThreadLocale(int mode);

    [LibraryImport("ucrtbase", EntryPoint = "setlocale", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SetLocale(int category, string? locale);

    private readonly record struct LocaleObservation(
        int BeforeMode,
        string BeforeLocale,
        int SuccessMode,
        string SuccessLocale,
        int FailureMode,
        string FailureLocale,
        bool InvalidArchiveFailed);
}
