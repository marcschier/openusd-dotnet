// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkMaterialShaderCompilerTests
{
    private const string CacheDirectoryVariable = "OPENUSD_SILK_SHADER_CACHE_DIR";

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [NotInParallel]
    public async Task UnsetOrEmptyCacheDirectoryKeepsTheExistingCacheAndDxcLocations(
        string? configured)
    {
        string? previous = Environment.GetEnvironmentVariable(CacheDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, configured);
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenUsd",
                "SilkMaterialShaders");

            var options = new SilkMaterialShaderCompilerOptions();

            await Assert.That(options.CacheDirectory).IsEqualTo(expected);
            await Assert.That(SilkD3D12GeneratedMaterialCompiler.GetDxcWorkDirectory())
                .IsEqualTo(Path.Combine(expected, "dxc"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, previous);
        }
    }

    [Test]
    [NotInParallel]
    public async Task ConfiguredCacheDirectoryControlsTheDefaultCacheAndDxcScratch()
    {
        string? previous = Environment.GetEnvironmentVariable(CacheDirectoryVariable);
        string directory = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"openusd-material-shader-cache-{Guid.NewGuid():N}"));
        try
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, directory);
            var options = new SilkMaterialShaderCompilerOptions();
            await Assert.That(options.CacheDirectory).IsEqualTo(directory);
            await Assert.That(SilkD3D12GeneratedMaterialCompiler.GetDxcWorkDirectory())
                .IsEqualTo(Path.Combine(directory, "dxc"));

            SilkMaterialShaderKey key = CreateKey("graph:configured-cache", "stub-v1");
            using (var compiler = new SilkMaterialShaderCompilerService(
                new SilkStubMaterialShaderGenerator(),
                options))
            {
                _ = compiler.GetOrQueue(key);
                _ = await WaitForReadyAsync(compiler, key);
            }
            await Assert.That(Directory.GetFiles(
                directory,
                "*.silkshader.json",
                SearchOption.AllDirectories).Length).IsEqualTo(1);

            string explicitDirectory = Path.Combine(directory, "explicit");
            var explicitOptions = new SilkMaterialShaderCompilerOptions
            {
                CacheDirectory = explicitDirectory
            };
            await Assert.That(explicitOptions.CacheDirectory).IsEqualTo(explicitDirectory);
            await Assert.That(SilkD3D12GeneratedMaterialCompiler.GetDxcWorkDirectory())
                .IsEqualTo(Path.Combine(directory, "dxc"));

            SilkMaterialShaderKey explicitKey = CreateKey("graph:explicit-cache", "stub-v1");
            using (var compiler = new SilkMaterialShaderCompilerService(
                new SilkStubMaterialShaderGenerator(),
                explicitOptions))
            {
                _ = compiler.GetOrQueue(explicitKey);
                _ = await WaitForReadyAsync(compiler, explicitKey);
            }
            await Assert.That(Directory.GetFiles(
                explicitDirectory,
                "*.silkshader.json",
                SearchOption.AllDirectories).Length).IsEqualTo(1);
            await Assert.That(Directory.GetFiles(
                directory,
                "*.silkshader.json",
                SearchOption.AllDirectories).Length).IsEqualTo(2);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task ConfiguredCacheDirectoryContainsSuccessfulDxcScratch()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Generated D3D12 shader compilation requires Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string? previous = Environment.GetEnvironmentVariable(CacheDirectoryVariable);
        string directory = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"openusd-material-dxc-{Guid.NewGuid():N}"));
        try
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, directory);
            var generator = new SilkProjectedMaterialShaderGenerator();
            SilkMaterialShaderKey key = CreateKey(
                "materialx:configured-dxc",
                "generated-dxc-v1",
                SilkShaderBinaryFormat.Dxil);
            // The generated unlit path emits a constant-output fragment with no frame buffer.
            byte[] fragment = SilkCheckedShaderAssets
                .LoadSelectionMaskFragment(SilkShaderBinaryFormat.SpirV)
                .Code
                .ToArray();
            generator.RegisterGenerated(key, fragment);

            SilkMaterialShaderProgram program;
            try
            {
                program = await generator.CompileAsync(key, CancellationToken.None);
            }
            catch (InvalidDataException exception)
            {
                string diagnosticDirectory = Path.Combine(directory, "dxc");
                string[] diagnostics = Directory.Exists(diagnosticDirectory)
                    ? Directory.GetFiles(diagnosticDirectory, "*.err")
                    : [];
                string detail = diagnostics.Length == 1
                    ? await File.ReadAllTextAsync(diagnostics[0])
                    : "No unique DXC diagnostic file was produced.";
                throw new InvalidDataException(exception.Message + Environment.NewLine + detail, exception);
            }

            byte[] compiled = program.FragmentShader.Code.ToArray();
            await Assert.That(program.FragmentShader.Format).IsEqualTo(SilkShaderBinaryFormat.Dxil);
            await Assert.That(compiled.Length).IsGreaterThanOrEqualTo(4);
            await Assert.That(Encoding.ASCII.GetString(compiled, 0, 4)).IsEqualTo("DXBC");
            string scratch = Path.Combine(directory, "dxc");
            await Assert.That(Directory.Exists(scratch)).IsTrue();
            await Assert.That(Directory.GetFiles(scratch)).IsEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    [Arguments("relative-cache")]
    [Arguments("C:relative-cache")]
    [Arguments("\\rooted-without-volume")]
    [Arguments("   ")]
    [NotInParallel]
    public async Task NonAbsoluteCacheDirectoryIsRejected(string configured)
    {
        string? previous = Environment.GetEnvironmentVariable(CacheDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, configured);

            InvalidOperationException exception = (await Assert.That(
                    () => new SilkMaterialShaderCompilerOptions())
                .Throws<InvalidOperationException>())!;

            await Assert.That(exception.Message).Contains(CacheDirectoryVariable);
            await Assert.That(
                    () => SilkD3D12GeneratedMaterialCompiler.GetDxcWorkDirectory())
                .Throws<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CacheDirectoryVariable, previous);
        }
    }

    [Test]
    public async Task MissingMaterialShaderReturnsPlaceholderThenPublishesRealProgram()
    {
        using var generator = new BlockingGenerator();
        using var compiler = CreateCompiler(generator);
        SilkMaterialShaderKey key = CreateKey("graph:a", "stub-v1");

        SilkMaterialShaderRequest pending = compiler.GetOrQueue(key);

        await Assert.That(pending.Status).IsEqualTo(SilkMaterialShaderStatus.Placeholder);
        await Assert.That(pending.IsPlaceholder).IsTrue();
        generator.Release();
        SilkMaterialShaderRequest ready = await WaitForReadyAsync(compiler, key);

        await Assert.That(ready.Status).IsEqualTo(SilkMaterialShaderStatus.Ready);
        await Assert.That(ready.Program.CacheHash).IsEqualTo(key.CacheHash);
        await Assert.That(generator.CompileCount).IsEqualTo(1);
    }

    [Test]
    public async Task DiskCacheHitAvoidsGeneratorAndReturnsReadyImmediately()
    {
        string directory = CreateCacheDirectory();
        SilkMaterialShaderKey key = CreateKey("graph:disk", "stub-v1");
        using (var first = new SilkMaterialShaderCompilerService(
            new SilkStubMaterialShaderGenerator(),
            CreateOptions(directory)))
        {
            _ = first.GetOrQueue(key);
            _ = await WaitForReadyAsync(first, key);
        }
        using var second = new SilkMaterialShaderCompilerService(
            new ThrowingGenerator(),
            CreateOptions(directory));

        SilkMaterialShaderRequest cached = second.GetOrQueue(key);

        await Assert.That(cached.Status).IsEqualTo(SilkMaterialShaderStatus.Ready);
        await Assert.That(cached.Program.CacheHash).IsEqualTo(key.CacheHash);
    }

    [Test]
    public async Task CorruptDiskCacheEntryIsRejectedAndRecompiled()
    {
        string directory = CreateCacheDirectory();
        SilkMaterialShaderKey key = CreateKey("graph:corrupt", "stub-v1");
        using (var first = new SilkMaterialShaderCompilerService(
            new SilkStubMaterialShaderGenerator(),
            CreateOptions(directory)))
        {
            _ = first.GetOrQueue(key);
            _ = await WaitForReadyAsync(first, key);
        }
        string cacheFile = Directory.GetFiles(directory, "*.silkshader.json", SearchOption.AllDirectories)[0];
        await File.WriteAllTextAsync(cacheFile, "{ \"SchemaVersion\": 1, \"CacheHash\": \"bad\" }");
        var generator = new CountingGenerator();
        using var second = new SilkMaterialShaderCompilerService(generator, CreateOptions(directory));

        SilkMaterialShaderRequest pending = second.GetOrQueue(key);
        SilkMaterialShaderRequest ready = await WaitForReadyAsync(second, key);

        await Assert.That(pending.Status).IsEqualTo(SilkMaterialShaderStatus.Placeholder);
        await Assert.That(ready.Status).IsEqualTo(SilkMaterialShaderStatus.Ready);
        await Assert.That(generator.CompileCount).IsEqualTo(1);
    }

    [Test]
    public async Task VersionSaltChangeSelectsDifferentCacheEntry()
    {
        string directory = CreateCacheDirectory();
        SilkMaterialShaderKey firstKey = CreateKey("graph:salt", "stub-v1");
        SilkMaterialShaderKey secondKey = CreateKey("graph:salt", "stub-v2");
        using (var first = new SilkMaterialShaderCompilerService(
            new SilkStubMaterialShaderGenerator(),
            CreateOptions(directory)))
        {
            _ = first.GetOrQueue(firstKey);
            _ = await WaitForReadyAsync(first, firstKey);
        }
        var generator = new CountingGenerator();
        using var second = new SilkMaterialShaderCompilerService(generator, CreateOptions(directory));

        SilkMaterialShaderRequest pending = second.GetOrQueue(secondKey);
        SilkMaterialShaderRequest ready = await WaitForReadyAsync(second, secondKey);

        await Assert.That(firstKey.CacheHash).IsNotEqualTo(secondKey.CacheHash);
        await Assert.That(pending.Status).IsEqualTo(SilkMaterialShaderStatus.Placeholder);
        await Assert.That(ready.Status).IsEqualTo(SilkMaterialShaderStatus.Ready);
        await Assert.That(generator.CompileCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProjectedMaterialGeneratorRequiresRegisteredMaterialHash()
    {
        var generator = new SilkProjectedMaterialShaderGenerator();
        SilkMaterialShaderKey key = CreateKey("materialx:missing", "projected-v1");
        using var compiler = new SilkMaterialShaderCompilerService(generator, CreateOptions(CreateCacheDirectory()));

        SilkMaterialShaderRequest pending = compiler.GetOrQueue(key);
        SilkMaterialShaderRequest failed = await WaitForTerminalAsync(compiler, key);

        await Assert.That(pending.Status).IsEqualTo(SilkMaterialShaderStatus.Placeholder);
        await Assert.That(failed.Status).IsEqualTo(SilkMaterialShaderStatus.Failed);
        await Assert.That(failed.CompilationError).IsTypeOf<InvalidDataException>();
    }

    [Test]
    public async Task RendererReportsOneDiagnosticPerFailedRuntimeShaderAndClearsItOnSuccess()
    {
        var failingGenerator = new SilkProjectedMaterialShaderGenerator();
        SilkMaterialShaderKey key = CreateKey("materialx:diagnosed-failure", "projected-v1");
        using var failing = new SilkMaterialShaderCompilerService(
            failingGenerator, CreateOptions(CreateCacheDirectory()));
        SilkMaterialShaderRequest pending = failing.GetOrQueue(key);
        SilkMaterialShaderRequest failed = await WaitForTerminalAsync(failing, key);
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);

        renderer.ObserveMaterialShaderRequest("/World/Materials/Broken", key, pending);
        await Assert.That(renderer.MaterialShaderDiagnostics).IsEmpty();

        renderer.ObserveMaterialShaderRequest("/World/Materials/Broken", key, failed);
        renderer.ObserveMaterialShaderRequest("/World/Materials/Broken", key, failed);
        IReadOnlyList<RenderDiagnostic> reported = renderer.MaterialShaderDiagnostics;
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0].Severity).IsEqualTo(RenderDiagnosticSeverity.Warning);
        await Assert.That(reported[0].Code)
            .IsEqualTo(SilkRenderDiagnosticCodes.MaterialShaderCompilationFailed);
        await Assert.That(reported[0].Message).Contains("/World/Materials/Broken");
        await Assert.That(reported[0].Message).Contains(key.CacheHash);
        await Assert.That(reported[0].Message).Contains(failed.CompilationError!.Message);

        var readyGenerator = new CountingGenerator();
        using var ready = new SilkMaterialShaderCompilerService(readyGenerator, CreateOptions(CreateCacheDirectory()));
        _ = ready.GetOrQueue(key);
        SilkMaterialShaderRequest compiled = await WaitForReadyAsync(ready, key);
        renderer.ObserveMaterialShaderRequest("/World/Materials/Broken", key, compiled);
        await Assert.That(renderer.MaterialShaderDiagnostics).IsEmpty();
    }

    [Test]
    public async Task RendererBoundsFailedRuntimeShaderDiagnostics()
    {
        var generator = new SilkProjectedMaterialShaderGenerator();
        SilkMaterialShaderKey firstKey = CreateKey("materialx:bounded-failure", "projected-v1");
        using var compiler = new SilkMaterialShaderCompilerService(generator, CreateOptions(CreateCacheDirectory()));
        _ = compiler.GetOrQueue(firstKey);
        SilkMaterialShaderRequest failed = await WaitForTerminalAsync(compiler, firstKey);
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);

        for (int index = 0; index < SilkMeshRenderer.MaterialShaderDiagnosticCapacity; index++)
        {
            renderer.ObserveMaterialShaderRequest(
                $"/World/Materials/M{index}", CreateKey($"materialx:bounded-{index}", "projected-v1"), failed);
        }
        await Assert.That(renderer.MaterialShaderDiagnostics.Count)
            .IsEqualTo(SilkMeshRenderer.MaterialShaderDiagnosticCapacity);
        await Assert.That(renderer.MaterialShaderDiagnostics.Any(static diagnostic =>
            diagnostic.Code == SilkRenderDiagnosticCodes.CapacityExceeded)).IsFalse();

        renderer.ObserveMaterialShaderRequest(
            "/World/Materials/Overflow", CreateKey("materialx:bounded-overflow", "projected-v1"), failed);
        IReadOnlyList<RenderDiagnostic> reported = renderer.MaterialShaderDiagnostics;
        await Assert.That(reported.Count).IsEqualTo(SilkMeshRenderer.MaterialShaderDiagnosticCapacity + 1);
        await Assert.That(reported[^1].Code).IsEqualTo(SilkRenderDiagnosticCodes.CapacityExceeded);
        await Assert.That(reported.Any(static diagnostic => diagnostic.Message.Contains(
            "/World/Materials/Overflow", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ProjectedMaterialGeneratorPublishesCheckedPermutationThroughRuntimeCache()
    {
        var generator = new SilkProjectedMaterialShaderGenerator();
        SilkMaterialShaderKey key = CreateKey("materialx:constant-standard-surface", "projected-v1");
        generator.Register(key, SilkShaderFeatures.None);
        using var compiler = new SilkMaterialShaderCompilerService(generator, CreateOptions(CreateCacheDirectory()));

        SilkMaterialShaderRequest pending = compiler.GetOrQueue(key);
        SilkMaterialShaderRequest ready = await WaitForReadyAsync(compiler, key);

        await Assert.That(pending.Status).IsEqualTo(SilkMaterialShaderStatus.Placeholder);
        await Assert.That(ready.Status).IsEqualTo(SilkMaterialShaderStatus.Ready);
        await Assert.That(ready.Program.CacheHash).IsEqualTo(key.CacheHash);
        await Assert.That(ready.Program.BindingLayout.MaterialSlots.Count)
            .IsEqualTo(SilkBindingLayoutDescriptor.SceneParameters.MaterialSlots.Count);
    }

    [Test]
    public async Task GeneratedMaterialGeneratorPublishesRegisteredFragmentSpirV()
    {
        var generator = new SilkProjectedMaterialShaderGenerator();
        SilkMaterialShaderKey key = CreateKey("materialx:generated-unlit", "generated-v1");
        byte[] fragment = SilkCheckedShaderAssets
            .LoadMeshFragment(SilkShaderBinaryFormat.SpirV)
            .Code
            .ToArray();
        generator.RegisterGenerated(key, fragment);
        using var compiler = new SilkMaterialShaderCompilerService(generator, CreateOptions(CreateCacheDirectory()));

        SilkMaterialShaderRequest pending = compiler.GetOrQueue(key);
        SilkMaterialShaderRequest ready = await WaitForReadyAsync(compiler, key);

        await Assert.That(pending.Status).IsEqualTo(SilkMaterialShaderStatus.Placeholder);
        await Assert.That(ready.Status).IsEqualTo(SilkMaterialShaderStatus.Ready);
        await Assert.That(ready.Program.FragmentShader.Code.ToArray()).IsEquivalentTo(fragment);
        await Assert.That(ready.Program.BindingLayout.MaterialSlots.Count)
            .IsEqualTo(SilkBindingLayoutDescriptor.SceneParameters.MaterialSlots.Count);
    }

    [Test]
    public async Task GeneratedMaterialGeneratorPublishesRegisteredFragmentMsl()
    {
        if (!SilkCheckedShaderAssets.HasPinnedMetalLibrary)
        {
            return;
        }

        var generator = new SilkProjectedMaterialShaderGenerator();
        SilkMaterialShaderKey key = CreateKey(
            "materialx:generated-unlit-metal",
            "generated-metal-v1",
            SilkShaderBinaryFormat.MetalLibrary);
        byte[] fragment = Encoding.UTF8.GetBytes(
            "#include <metal_stdlib>\nfragment float4 main() { return float4(1); }\n");
        generator.RegisterGenerated(key, fragment);
        using var compiler = new SilkMaterialShaderCompilerService(generator, CreateOptions(CreateCacheDirectory()));

        SilkMaterialShaderRequest pending = compiler.GetOrQueue(key);
        SilkMaterialShaderRequest ready = await WaitForReadyAsync(compiler, key);

        await Assert.That(pending.Status).IsEqualTo(SilkMaterialShaderStatus.Placeholder);
        await Assert.That(ready.Status).IsEqualTo(SilkMaterialShaderStatus.Ready);
        await Assert.That(ready.Program.FragmentShader.Code.ToArray()).IsEquivalentTo(fragment);
        await Assert.That(ready.Program.FragmentShader.EntryPoint).IsEqualTo("main");
        await Assert.That(ready.Program.FragmentShader.Format).IsEqualTo(SilkShaderBinaryFormat.MetalLibrary);
    }

    private static SilkMaterialShaderCompilerService CreateCompiler(ISilkMaterialShaderGenerator generator) =>
        new(generator, CreateOptions(CreateCacheDirectory()));

    private static SilkMaterialShaderCompilerOptions CreateOptions(string directory) =>
        new()
        {
            CacheDirectory = directory,
            MaxDiskBytes = 1024 * 1024,
            MaxMemoryEntries = 4
        };

    private static SilkMaterialShaderKey CreateKey(
        string graph,
        string salt,
        SilkShaderBinaryFormat format = SilkShaderBinaryFormat.SpirV) =>
        SilkMaterialShaderKey.Create(Encoding.UTF8.GetBytes(graph), format, salt);

    private static string CreateCacheDirectory()
    {
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "material-shader-cache",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<SilkMaterialShaderRequest> WaitForReadyAsync(
        SilkMaterialShaderCompilerService compiler,
        SilkMaterialShaderKey key)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            SilkMaterialShaderRequest request = compiler.GetOrQueue(key);
            if (request.Status == SilkMaterialShaderStatus.Ready)
            {
                return request;
            }
            if (request.Status == SilkMaterialShaderStatus.Failed)
            {
                throw request.CompilationError ?? new InvalidOperationException("Material shader compilation failed.");
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("Timed out waiting for material shader compilation.");
    }

    private static async Task<SilkMaterialShaderRequest> WaitForTerminalAsync(
        SilkMaterialShaderCompilerService compiler,
        SilkMaterialShaderKey key)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            SilkMaterialShaderRequest request = compiler.GetOrQueue(key);
            if (request.Status is SilkMaterialShaderStatus.Ready or SilkMaterialShaderStatus.Failed)
            {
                return request;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("Timed out waiting for material shader compilation.");
    }

    private sealed class CountingGenerator : ISilkMaterialShaderGenerator
    {
        public int CompileCount { get; private set; }

        public ValueTask<SilkMaterialShaderProgram> CompileAsync(
            SilkMaterialShaderKey key,
            CancellationToken cancellationToken)
        {
            CompileCount++;
            return new SilkStubMaterialShaderGenerator().CompileAsync(key, cancellationToken);
        }
    }

    private sealed class BlockingGenerator : ISilkMaterialShaderGenerator, IDisposable
    {
        private readonly ManualResetEventSlim _released = new();

        public int CompileCount { get; private set; }

        public ValueTask<SilkMaterialShaderProgram> CompileAsync(
            SilkMaterialShaderKey key,
            CancellationToken cancellationToken)
        {
            CompileCount++;
            _released.Wait(cancellationToken);
            return new SilkStubMaterialShaderGenerator().CompileAsync(key, cancellationToken);
        }

        public void Release() => _released.Set();

        public void Dispose() => _released.Dispose();
    }

    private sealed class ThrowingGenerator : ISilkMaterialShaderGenerator
    {
        public ValueTask<SilkMaterialShaderProgram> CompileAsync(
            SilkMaterialShaderKey key,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The disk cache should have been used.");
    }
}
