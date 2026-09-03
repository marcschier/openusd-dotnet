// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkMaterialShaderCompilerTests
{
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
