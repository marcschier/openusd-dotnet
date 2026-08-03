// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Compilation state returned for a material shader request.</summary>
public enum SilkMaterialShaderStatus
{
    /// <summary>The checked placeholder program should be rendered while compilation is pending.</summary>
    Placeholder = 0,

    /// <summary>The requested material program is compiled and ready to bind.</summary>
    Ready = 1,

    /// <summary>Compilation failed; the checked placeholder program is still returned.</summary>
    Failed = 2
}

/// <summary>Stable content-addressed identity for one runtime material shader.</summary>
public sealed class SilkMaterialShaderKey
{
    private const string Domain = "OpenUsd.Rendering.Silk.MaterialShaderCache.v1";

    private SilkMaterialShaderKey(
        string materialHash,
        SilkShaderBinaryFormat format,
        string versionSalt,
        string cacheHash)
    {
        MaterialHash = materialHash;
        Format = format;
        VersionSalt = versionSalt;
        CacheHash = cacheHash;
    }

    /// <summary>Gets the lower-case SHA-256 hash of the canonical material network.</summary>
    public string MaterialHash { get; }

    /// <summary>Gets the backend shader binary format this entry serves.</summary>
    public SilkShaderBinaryFormat Format { get; }

    /// <summary>Gets the compiler and generator version salt included in the cache identity.</summary>
    public string VersionSalt { get; }

    /// <summary>Gets the lower-case SHA-256 cache key over material hash, backend, and salt.</summary>
    public string CacheHash { get; }

    /// <summary>Creates a cache key from canonical material-network bytes.</summary>
    public static SilkMaterialShaderKey Create(
        ReadOnlySpan<byte> materialNetwork,
        SilkShaderBinaryFormat format,
        string versionSalt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionSalt);
        ValidateFormat(format);

        string materialHash = ToLowerHex(SHA256.HashData(materialNetwork));
        string cacheHash = ComputeCacheHash(materialHash, format, versionSalt);
        return new SilkMaterialShaderKey(materialHash, format, versionSalt, cacheHash);
    }

    /// <summary>Creates a cache key from a precomputed lower-case SHA-256 material-network hash.</summary>
    public static SilkMaterialShaderKey FromMaterialHash(
        string materialHash,
        SilkShaderBinaryFormat format,
        string versionSalt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionSalt);
        ValidateFormat(format);
        if (!IsLowerSha256(materialHash))
        {
            throw new ArgumentException(
                "The material hash must be a lower-case SHA-256 hex string.",
                nameof(materialHash));
        }

        string cacheHash = ComputeCacheHash(materialHash, format, versionSalt);
        return new SilkMaterialShaderKey(materialHash, format, versionSalt, cacheHash);
    }

    internal static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        char[] rented = ArrayPool<char>.Shared.Rent(bytes.Length * 2);
        try
        {
            Span<char> chars = rented.AsSpan(0, bytes.Length * 2);
            const string hex = "0123456789abcdef";
            for (int index = 0; index < bytes.Length; index++)
            {
                byte value = bytes[index];
                chars[index * 2] = hex[value >> 4];
                chars[(index * 2) + 1] = hex[value & 0xF];
            }
            return new string(chars);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static string ComputeCacheHash(
        string materialHash,
        SilkShaderBinaryFormat format,
        string versionSalt)
    {
        byte[] input = Encoding.UTF8.GetBytes(
            string.Concat(Domain, "\n", materialHash, "\n", format.ToString(), "\n", versionSalt));
        return ToLowerHex(SHA256.HashData(input));
    }

    private static void ValidateFormat(SilkShaderBinaryFormat format)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static bool IsLowerSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }
        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>Immutable shader program generated for a runtime material.</summary>
public readonly record struct SilkMaterialShaderProgram(
    SilkShaderModuleDescriptor VertexShader,
    SilkShaderModuleDescriptor FragmentShader,
    SilkBindingLayoutDescriptor BindingLayout,
    string CacheHash)
{
    /// <summary>Validates shader modules, binding layout, and cache identity.</summary>
    public void Validate()
    {
        VertexShader.Validate();
        FragmentShader.Validate();
        BindingLayout.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(CacheHash);
        if (VertexShader.Stage != SilkShaderStage.Vertex)
        {
            throw new ArgumentException("The vertex shader must use the vertex stage.", nameof(VertexShader));
        }
        if (FragmentShader.Stage != SilkShaderStage.Fragment)
        {
            throw new ArgumentException("The fragment shader must use the fragment stage.", nameof(FragmentShader));
        }
        if (VertexShader.Format != FragmentShader.Format)
        {
            throw new ArgumentException("Both shader stages must use the same binary format.");
        }
    }
}

/// <summary>Result of requesting a runtime material shader.</summary>
public sealed class SilkMaterialShaderRequest
{
    internal SilkMaterialShaderRequest(
        SilkMaterialShaderStatus status,
        SilkMaterialShaderProgram program,
        Exception? compilationError)
    {
        Status = status;
        Program = program;
        CompilationError = compilationError;
    }

    /// <summary>Gets whether <see cref="Program"/> is the placeholder, real program, or failed placeholder.</summary>
    public SilkMaterialShaderStatus Status { get; }

    /// <summary>Gets the program to bind for the current frame.</summary>
    public SilkMaterialShaderProgram Program { get; }

    /// <summary>
    /// Gets the compilation failure when <see cref="Status"/> is
    /// <see cref="SilkMaterialShaderStatus.Failed"/>.
    /// </summary>
    public Exception? CompilationError { get; }

    /// <summary>Gets whether the checked placeholder program is being returned.</summary>
    public bool IsPlaceholder => Status != SilkMaterialShaderStatus.Ready;
}

/// <summary>Generates a material shader program for a runtime material key.</summary>
public interface ISilkMaterialShaderGenerator
{
    /// <summary>Compiles the material shader program.</summary>
    ValueTask<SilkMaterialShaderProgram> CompileAsync(
        SilkMaterialShaderKey key,
        CancellationToken cancellationToken);
}

/// <summary>Asynchronous content-addressed runtime material shader compiler.</summary>
public interface ISilkMaterialShaderCompiler : IDisposable
{
    /// <summary>
    /// Gets a cached program when ready, otherwise queues compilation and returns the checked placeholder.
    /// </summary>
    SilkMaterialShaderRequest GetOrQueue(SilkMaterialShaderKey key);
}

/// <summary>Options for <see cref="SilkMaterialShaderCompilerService"/>.</summary>
public sealed class SilkMaterialShaderCompilerOptions
{
    /// <summary>Gets or sets the root directory for content-addressed cache entries.</summary>
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenUsd",
        "SilkMaterialShaders");

    /// <summary>Gets or sets the maximum number of ready programs kept in memory.</summary>
    public int MaxMemoryEntries { get; set; } = 128;

    /// <summary>Gets or sets the maximum total size, in bytes, of on-disk cache files.</summary>
    public long MaxDiskBytes { get; set; } = 256L * 1024L * 1024L;

    /// <summary>Gets or sets the placeholder program used while material compilation is pending.</summary>
    public SilkMaterialShaderProgram? PlaceholderProgram { get; set; }

    /// <summary>Gets or sets the cancellation token used to stop queued compilation.</summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// No-op generator used until MaterialX backend generators are wired in.
/// </summary>
public sealed class SilkStubMaterialShaderGenerator : ISilkMaterialShaderGenerator
{
    /// <inheritdoc/>
    public ValueTask<SilkMaterialShaderProgram> CompileAsync(
        SilkMaterialShaderKey key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SilkMaterialShaderCompilerService.CreateCheckedPlaceholder(
            key.Format,
            key.CacheHash));
    }
}

/// <summary>Default runtime material shader compiler with memory and disk caches.</summary>
public sealed class SilkMaterialShaderCompilerService : ISilkMaterialShaderCompiler
{
    private const int CacheSchemaVersion = 1;
    private const string CacheExtension = ".silkshader.json";
    private readonly object _gate = new();
    private readonly ISilkMaterialShaderGenerator _generator;
    private readonly SilkMaterialShaderCompilerOptions _options;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<SilkShaderBinaryFormat, SilkMaterialShaderProgram> _placeholders = [];
    private readonly LinkedList<string> _lru = new();
    private bool _disposed;

    /// <summary>Initializes the compiler service.</summary>
    public SilkMaterialShaderCompilerService(
        ISilkMaterialShaderGenerator generator,
        SilkMaterialShaderCompilerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
        _options = options ?? new SilkMaterialShaderCompilerOptions();
        if (_options.MaxMemoryEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The memory shader cache must hold at least one entry.");
        }
        if (_options.MaxDiskBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The disk shader cache must hold at least one byte.");
        }
        Directory.CreateDirectory(_options.CacheDirectory);
    }

    /// <summary>Creates the checked mesh shader placeholder for one backend format.</summary>
    public static SilkMaterialShaderProgram CreateCheckedPlaceholder(
        SilkShaderBinaryFormat format,
        string cacheHash)
    {
        var program = new SilkMaterialShaderProgram(
            SilkCheckedShaderAssets.LoadMeshVertex(format),
            SilkCheckedShaderAssets.LoadMeshFragment(format),
            SilkBindingLayoutDescriptor.SceneParameters,
            cacheHash);
        program.Validate();
        return program;
    }

    /// <inheritdoc/>
    public SilkMaterialShaderRequest GetOrQueue(SilkMaterialShaderKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        CacheEntry? entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key.CacheHash, out entry))
            {
                TouchMemoryEntry(key.CacheHash, entry);
                return entry.CreateRequest(GetPlaceholder(key));
            }
        }

        if (TryLoadDisk(key, out SilkMaterialShaderProgram diskProgram))
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_entries.TryGetValue(key.CacheHash, out entry))
                {
                    entry = CacheEntry.Ready(diskProgram);
                    _entries.Add(key.CacheHash, entry);
                }
                TouchMemoryEntry(key.CacheHash, entry);
                TrimMemoryCache();
                return entry.CreateRequest(GetPlaceholder(key));
            }
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(key.CacheHash, out entry))
            {
                entry = CacheEntry.Compiling();
                _entries.Add(key.CacheHash, entry);
                QueueCompile(key, entry);
            }
            TouchMemoryEntry(key.CacheHash, entry);
            return entry.CreateRequest(GetPlaceholder(key));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _entries.Clear();
            _lru.Clear();
        }
    }

    private SilkMaterialShaderProgram GetPlaceholder(SilkMaterialShaderKey key)
    {
        if (_options.PlaceholderProgram is SilkMaterialShaderProgram configured)
        {
            return configured;
        }
        if (!_placeholders.TryGetValue(key.Format, out SilkMaterialShaderProgram placeholder))
        {
            placeholder = CreateCheckedPlaceholder(key.Format, key.CacheHash);
            _placeholders.Add(key.Format, placeholder);
        }
        return placeholder with { CacheHash = key.CacheHash };
    }

    private void QueueCompile(SilkMaterialShaderKey key, CacheEntry entry)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                SilkMaterialShaderProgram program =
                    await _generator.CompileAsync(key, _options.CancellationToken).ConfigureAwait(false);
                program.Validate();
                if (!string.Equals(program.CacheHash, key.CacheHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Generated material shader cache hash did not match the request.");
                }
                await WriteDiskAsync(key, program, _options.CancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    entry.SetReady(program);
                    TouchMemoryEntry(key.CacheHash, entry);
                    TrimMemoryCache();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        entry.SetFailed(ex);
                    }
                }
            }
        }, _options.CancellationToken);
    }

    private void TouchMemoryEntry(string cacheHash, CacheEntry entry)
    {
        if (entry.LruNode is not null)
        {
            _lru.Remove(entry.LruNode);
        }
        entry.LruNode = _lru.AddFirst(cacheHash);
    }

    private void TrimMemoryCache()
    {
        while (_entries.Count > _options.MaxMemoryEntries && _lru.Last is not null)
        {
            string cacheHash = _lru.Last.Value;
            CacheEntry entry = _entries[cacheHash];
            if (entry.Status == SilkMaterialShaderStatus.Placeholder)
            {
                break;
            }
            _lru.RemoveLast();
            _entries.Remove(cacheHash);
        }
    }

    private string GetCachePath(SilkMaterialShaderKey key)
    {
        string shard = key.CacheHash[..2];
        return Path.Combine(
            _options.CacheDirectory,
            "v1",
            key.Format.ToString(),
            shard,
            key.CacheHash + CacheExtension);
    }

    private bool TryLoadDisk(SilkMaterialShaderKey key, out SilkMaterialShaderProgram program)
    {
        string path = GetCachePath(key);
        program = default;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            CacheFile? cache = JsonSerializer.Deserialize<CacheFile>(
                File.ReadAllBytes(path),
                CacheJsonContext.Default.CacheFile);
            if (cache is null || !cache.Matches(key))
            {
                DeleteBadCache(path);
                return false;
            }
            program = cache.ToProgram();
            program.Validate();
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException)
        {
            DeleteBadCache(path);
            return false;
        }
    }

    private async Task WriteDiskAsync(
        SilkMaterialShaderKey key,
        SilkMaterialShaderProgram program,
        CancellationToken cancellationToken)
    {
        string path = GetCachePath(key);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(
            directory,
            string.Concat(key.CacheHash, ".", Guid.NewGuid().ToString("N"), ".tmp"));
        CacheFile cache = CacheFile.FromProgram(key, program);
        await using (FileStream stream = new(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                cache,
                CacheJsonContext.Default.CacheFile,
                cancellationToken).ConfigureAwait(false);
        }
        File.Move(tempPath, path, true);
        EvictDiskCache();
    }

    private void EvictDiskCache()
    {
        DirectoryInfo root = new(_options.CacheDirectory);
        if (!root.Exists)
        {
            return;
        }

        FileInfo[] files = root.GetFiles("*" + CacheExtension, SearchOption.AllDirectories);
        long total = 0;
        foreach (FileInfo file in files)
        {
            total += file.Length;
        }
        if (total <= _options.MaxDiskBytes)
        {
            return;
        }

        Array.Sort(files, static (left, right) =>
            left.LastAccessTimeUtc.CompareTo(right.LastAccessTimeUtc));
        foreach (FileInfo file in files)
        {
            if (total <= _options.MaxDiskBytes)
            {
                break;
            }
            try
            {
                total -= file.Length;
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void DeleteBadCache(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class CacheEntry
    {
        private SilkMaterialShaderProgram _program;
        private Exception? _error;

        private CacheEntry(SilkMaterialShaderStatus status, SilkMaterialShaderProgram program)
        {
            Status = status;
            _program = program;
        }

        public SilkMaterialShaderStatus Status { get; private set; }

        public LinkedListNode<string>? LruNode { get; set; }

        public static CacheEntry Compiling() => new(SilkMaterialShaderStatus.Placeholder, default);

        public static CacheEntry Ready(SilkMaterialShaderProgram program) =>
            new(SilkMaterialShaderStatus.Ready, program);

        public void SetReady(SilkMaterialShaderProgram program)
        {
            _program = program;
            _error = null;
            Status = SilkMaterialShaderStatus.Ready;
        }

        public void SetFailed(Exception error)
        {
            _error = error;
            Status = SilkMaterialShaderStatus.Failed;
        }

        public SilkMaterialShaderRequest CreateRequest(SilkMaterialShaderProgram placeholder) =>
            Status == SilkMaterialShaderStatus.Ready
                ? new SilkMaterialShaderRequest(Status, _program, null)
                : new SilkMaterialShaderRequest(Status, placeholder, _error);
    }

    internal sealed class CacheFile
    {
        public int SchemaVersion { get; set; }

        public string CacheHash { get; set; } = string.Empty;

        public string MaterialHash { get; set; } = string.Empty;

        public SilkShaderBinaryFormat Format { get; set; }

        public string VersionSaltHash { get; set; } = string.Empty;

        public ShaderModuleFile VertexShader { get; set; } = new();

        public ShaderModuleFile FragmentShader { get; set; } = new();

        public BindingLayoutFile BindingLayout { get; set; } = new();

        public static CacheFile FromProgram(
            SilkMaterialShaderKey key,
            SilkMaterialShaderProgram program) =>
            new()
            {
                SchemaVersion = CacheSchemaVersion,
                CacheHash = key.CacheHash,
                MaterialHash = key.MaterialHash,
                Format = key.Format,
                VersionSaltHash = SilkMaterialShaderKey.ToLowerHex(
                    SHA256.HashData(Encoding.UTF8.GetBytes(key.VersionSalt))),
                VertexShader = ShaderModuleFile.FromDescriptor(program.VertexShader),
                FragmentShader = ShaderModuleFile.FromDescriptor(program.FragmentShader),
                BindingLayout = BindingLayoutFile.FromDescriptor(program.BindingLayout)
            };

        public bool Matches(SilkMaterialShaderKey key)
        {
            if (SchemaVersion != CacheSchemaVersion ||
                !string.Equals(CacheHash, key.CacheHash, StringComparison.Ordinal) ||
                !string.Equals(MaterialHash, key.MaterialHash, StringComparison.Ordinal) ||
                Format != key.Format ||
                !string.Equals(
                    VersionSaltHash,
                    SilkMaterialShaderKey.ToLowerHex(SHA256.HashData(Encoding.UTF8.GetBytes(key.VersionSalt))),
                    StringComparison.Ordinal))
            {
                return false;
            }

            return VertexShader.IsHashValid() && FragmentShader.IsHashValid();
        }

        public SilkMaterialShaderProgram ToProgram() =>
            new(
                VertexShader.ToDescriptor(),
                FragmentShader.ToDescriptor(),
                BindingLayout.ToDescriptor(),
                CacheHash);
    }

    internal sealed class ShaderModuleFile
    {
        public SilkShaderStage Stage { get; set; }

        public SilkShaderBinaryFormat Format { get; set; }

        public string EntryPoint { get; set; } = string.Empty;

        public byte[] Code { get; set; } = [];

        public string Sha256 { get; set; } = string.Empty;

        public static ShaderModuleFile FromDescriptor(SilkShaderModuleDescriptor descriptor)
        {
            byte[] code = descriptor.Code.ToArray();
            return new ShaderModuleFile
            {
                Stage = descriptor.Stage,
                Format = descriptor.Format,
                EntryPoint = descriptor.EntryPoint,
                Code = code,
                Sha256 = SilkMaterialShaderKey.ToLowerHex(SHA256.HashData(code))
            };
        }

        public bool IsHashValid() =>
            string.Equals(
                Sha256,
                SilkMaterialShaderKey.ToLowerHex(SHA256.HashData(Code)),
                StringComparison.Ordinal);

        public SilkShaderModuleDescriptor ToDescriptor() =>
            new(Stage, Format, EntryPoint, Code);
    }

    internal sealed class BindingLayoutFile
    {
        public uint Set { get; set; }

        public uint Binding { get; set; }

        public uint UniformByteSize { get; set; }

        public SilkShaderStageVisibility Visibility { get; set; }

        public BindingSlotFile[] MaterialSlots { get; set; } = [];

        public static BindingLayoutFile FromDescriptor(SilkBindingLayoutDescriptor descriptor)
        {
            IReadOnlyList<SilkBindingSlot> slots = descriptor.MaterialSlots;
            var materialSlots = new BindingSlotFile[slots.Count];
            for (int index = 0; index < materialSlots.Length; index++)
            {
                materialSlots[index] = BindingSlotFile.FromSlot(slots[index]);
            }
            return new BindingLayoutFile
            {
                Set = descriptor.Set,
                Binding = descriptor.Binding,
                UniformByteSize = descriptor.UniformByteSize,
                Visibility = descriptor.Visibility,
                MaterialSlots = materialSlots
            };
        }

        public SilkBindingLayoutDescriptor ToDescriptor()
        {
            var slots = new SilkBindingSlot[MaterialSlots.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                slots[index] = MaterialSlots[index].ToSlot();
            }
            return new SilkBindingLayoutDescriptor(Set, Binding, UniformByteSize, Visibility)
            {
                MaterialSlots = slots
            };
        }
    }

    internal sealed class BindingSlotFile
    {
        public uint Set { get; set; }

        public uint Binding { get; set; }

        public SilkBindingKind Kind { get; set; }

        public uint UniformByteSize { get; set; }

        public SilkShaderStageVisibility Visibility { get; set; }

        public static BindingSlotFile FromSlot(SilkBindingSlot slot) =>
            new()
            {
                Set = slot.Set,
                Binding = slot.Binding,
                Kind = slot.Kind,
                UniformByteSize = slot.UniformByteSize,
                Visibility = slot.Visibility
            };

        public SilkBindingSlot ToSlot() =>
            new(Set, Binding, Kind, UniformByteSize, Visibility);
    }
}

[JsonSerializable(typeof(SilkMaterialShaderCompilerService.CacheFile))]
internal sealed partial class CacheJsonContext : JsonSerializerContext
{
}
