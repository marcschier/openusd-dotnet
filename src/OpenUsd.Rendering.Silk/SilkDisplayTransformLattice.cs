// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// An immutable display-transform lattice: one bounded colour-managed lookup table
/// baked once and sampled on the GPU for every subsequent frame.
/// </summary>
/// <remarks>
/// <para>
/// The lattice is a cube of <see cref="Size"/> entries per axis, unrolled into a
/// single two-dimensional strip of <see cref="Size"/> tiles so it can be uploaded and
/// sampled as an ordinary RGBA8 texture on every backend. Red varies across a tile,
/// green varies down a tile, and blue selects the tile.
/// </para>
/// <para>
/// The axes are indexed through a base-2 logarithmic shaper, so a bounded table
/// covers an unbounded scene-referred range: lattice index <c>i</c> on any axis
/// corresponds to the linear value
/// <c>2^(ShaperMinimumLog2 + i / (Size - 1) * ShaperRangeLog2)</c>.
/// </para>
/// </remarks>
public sealed class SilkDisplayTransformLattice
{
    internal SilkDisplayTransformLattice(
        int size,
        float shaperMinimumLog2,
        float shaperRangeLog2,
        byte[] rgba8)
    {
        Size = size;
        ShaperMinimumLog2 = shaperMinimumLog2;
        ShaperRangeLog2 = shaperRangeLog2;
        Rgba8 = rgba8;
    }

    /// <summary>Gets the lattice edge length.</summary>
    public int Size { get; }

    /// <summary>Gets the lower shaper bound in stops.</summary>
    public float ShaperMinimumLog2 { get; }

    /// <summary>Gets the shaper interval width in stops.</summary>
    public float ShaperRangeLog2 { get; }

    /// <summary>Gets the unrolled strip width in texels.</summary>
    public uint StripWidth => checked((uint)(Size * Size));

    /// <summary>Gets the unrolled strip height in texels.</summary>
    public uint StripHeight => checked((uint)Size);

    /// <summary>Gets the tightly packed display-referred RGBA8 strip.</summary>
    public ReadOnlyMemory<byte> Rgba8 { get; }

    /// <summary>Gets the retained strip byte count.</summary>
    public long ByteCount => Rgba8.Length;
}

/// <summary>
/// Bakes a renderer-neutral display transform into a GPU-samplable lattice.
/// </summary>
/// <remarks>
/// This is the whole seam between the renderer and colour management. A renderer
/// only ever asks for a lattice; where the numbers come from -- an OpenColorIO
/// config, a fixture, a test double -- is entirely this interface's business.
/// </remarks>
public interface ISilkDisplayTransformLatticeProvider
{
    /// <summary>Bakes one lattice for the given transform.</summary>
    /// <param name="transform">The renderer-neutral display transform.</param>
    /// <returns>The immutable baked lattice.</returns>
    /// <exception cref="SilkDisplayTransformException">
    /// The configuration is unavailable or does not contain the requested transform.
    /// </exception>
    SilkDisplayTransformLattice Create(RenderDisplayTransform transform);
}

/// <summary>
/// Raised when a colour-managed display transform cannot be realized, carrying the
/// reason as a bounded status rather than a success-shaped identity result.
/// </summary>
public sealed class SilkDisplayTransformException : Exception
{
    /// <summary>Gets the longest message this exception ever carries.</summary>
    public const int MaximumMessageLength = 512;

    /// <summary>Initializes the exception with a bounded reason.</summary>
    /// <param name="status">The reason the transform is unavailable.</param>
    /// <param name="message">The bounded human-readable message.</param>
    /// <param name="innerException">The optional underlying failure.</param>
    public SilkDisplayTransformException(
        SilkDisplayTransformStatus status,
        string message,
        Exception? innerException = null)
        : base(Bound(message), innerException)
    {
        Status = status;
    }

    /// <summary>Gets the reason the transform is unavailable.</summary>
    public SilkDisplayTransformStatus Status { get; }

    private static string Bound(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Length <= MaximumMessageLength
            ? message
            : string.Concat(message.AsSpan(0, MaximumMessageLength - 1), "\u2026");
    }
}

/// <summary>
/// Reports the identity of an OpenColorIO configuration, so a cached lattice can be
/// discarded when the configuration it was baked from is no longer the same one.
/// </summary>
/// <remarks>
/// A path is not an identity. A config can be edited in place, a LUT it references can
/// change underneath it, a symbolic link can be retargeted, and a context environment
/// variable can change which file a reference resolves to -- none of which change the
/// path. This seam exists so the cache asks colour management itself what the current
/// identity is instead of guessing from the file name.
/// </remarks>
public interface ISilkDisplayTransformConfigIdentityProvider
{
    /// <summary>
    /// Gets the current identity of the configuration at the given path.
    /// </summary>
    /// <param name="configPath">The absolute configuration path.</param>
    /// <returns>
    /// The identity and whether it covers every dependency. An identity that does not is
    /// not a usable cache key: something it never looked at can change without it
    /// changing, so a caller must not retain results against it.
    /// </returns>
    SilkDisplayTransformConfigIdentity GetIdentity(string configPath);
}

/// <summary>One observation of a configuration's identity.</summary>
/// <param name="Value">
/// The opaque identity, or an empty string when the configuration could not be read.
/// </param>
/// <param name="IsExhaustive">
/// Whether every reachable dependency was hashed. A partial observation can miss a
/// change, so it can never authorize a retained hit or a suppressed retry.
/// </param>
public readonly record struct SilkDisplayTransformConfigIdentity(
    string Value,
    bool IsExhaustive)
{
    /// <summary>Gets the identity of a configuration that could not be read at all.</summary>
    public static SilkDisplayTransformConfigIdentity Unavailable { get; } =
        new(string.Empty, IsExhaustive: true);
}

/// <summary>
/// Reports OpenColorIO's own config cache identity, which incorporates the parsed config
/// and the file-system state of every file it references through its current context.
/// </summary>
/// <remarks>
/// Reading that identity re-parses the config, so it is asked at most once per
/// <see cref="RevalidationInterval"/>. That interval is the exact, documented staleness
/// window: an edit is picked up within it, and a zero interval revalidates on every
/// lookup, which is what the tests use.
/// </remarks>
public sealed class SilkOpenColorIoConfigIdentityProvider :
    ISilkDisplayTransformConfigIdentityProvider
{
    /// <summary>Gets the default revalidation interval.</summary>
    public static TimeSpan DefaultRevalidationInterval { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the shared provider.</summary>
    public static SilkOpenColorIoConfigIdentityProvider Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, CachedIdentity> _identities =
        new(StringComparer.Ordinal);
    private readonly Func<long> _timestampProvider;

    /// <summary>Initializes a provider with the default revalidation interval.</summary>
    public SilkOpenColorIoConfigIdentityProvider()
        : this(DefaultRevalidationInterval)
    {
    }

    /// <summary>Initializes a provider with an explicit revalidation interval.</summary>
    /// <param name="revalidationInterval">
    /// How long a previously read identity may be reused. <see cref="TimeSpan.Zero"/>
    /// revalidates on every lookup.
    /// </param>
    public SilkOpenColorIoConfigIdentityProvider(TimeSpan revalidationInterval)
        : this(revalidationInterval, Stopwatch.GetTimestamp)
    {
    }

    internal SilkOpenColorIoConfigIdentityProvider(
        TimeSpan revalidationInterval,
        Func<long> timestampProvider)
    {
        if (revalidationInterval < TimeSpan.Zero ||
            revalidationInterval > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(revalidationInterval),
                revalidationInterval,
                "The revalidation interval must be between zero and one minute.");
        }
        ArgumentNullException.ThrowIfNull(timestampProvider);
        RevalidationInterval = revalidationInterval;
        _timestampProvider = timestampProvider;
    }

    /// <summary>Gets how long a previously read identity may be reused.</summary>
    public TimeSpan RevalidationInterval { get; }

    /// <summary>Gets the number of times the identity was actually read from OpenColorIO.</summary>
    public ulong Reads { get; private set; }

    /// <summary>
    /// Gets how many transitive dependency walks the native identity entry point has
    /// completed in this process, or zero when the native runtime is unavailable.
    /// </summary>
    /// <remarks>
    /// One revalidation must cost exactly one walk. Sizing a buffer with a throwaway
    /// call and then filling it ran the whole file walk twice for every observation,
    /// which this counter makes assertable rather than assumed.
    /// </remarks>
    public static ulong NativeDependencyWalks
    {
        get
        {
            try
            {
                return OpenUsdNativeRuntime.GetOcioConfigDependencyWalks();
            }
            catch (OpenUsdNativeException)
            {
                return 0;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException or
                    BadImageFormatException)
            {
                return 0;
            }
        }
    }

    /// <summary>Gets how many times the native identity entry point has been called.</summary>
    public static long NativeIdentityCalls => OpenUsdNativeRuntime.OcioConfigCacheIdCalls;

    /// <inheritdoc/>
    public SilkDisplayTransformConfigIdentity GetIdentity(string configPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);
        long now = _timestampProvider();
        long ticks = (long)(RevalidationInterval.TotalSeconds * Stopwatch.Frequency);
        lock (_gate)
        {
            if (ticks > 0 &&
                _identities.TryGetValue(configPath, out CachedIdentity cached) &&
                now - cached.Timestamp < ticks)
            {
                return cached.Identity;
            }
        }

        SilkDisplayTransformConfigIdentity identity = Read(configPath);
        lock (_gate)
        {
            _identities[configPath] = new CachedIdentity(identity, now);
            Reads++;
            // The map is keyed by absolute config path and a process only ever names a
            // handful, but it is still bounded rather than unbounded by construction.
            if (_identities.Count > 32)
            {
                _identities.Clear();
                _identities[configPath] = new CachedIdentity(identity, now);
            }
        }
        return identity;
    }

    private static SilkDisplayTransformConfigIdentity Read(string configPath)
    {
        try
        {
            // Cleared first, and under the same interlock every processor build takes:
            // OpenColorIO would otherwise answer this from the very caches that make an
            // externally edited LUT invisible, and clearing while another thread builds
            // a processor from the same config is a race the library does not arbitrate.
            (string identity, bool exhaustive) = SilkOpenColorIoInterlock.Refresh(
                () => OpenUsdNativeRuntime.GetOcioConfigCacheId(configPath));
            return new SilkDisplayTransformConfigIdentity(identity, exhaustive);
        }
        catch (OpenUsdNativeException)
        {
            // A config that cannot be parsed has no identity. Returning empty is not a
            // silent success: it is a distinct identity that never matches a lattice
            // baked from a readable config, so the cached lattice is dropped and the
            // bake is retried, which is where the failure is reported.
            return SilkDisplayTransformConfigIdentity.Unavailable;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException)
        {
            return SilkDisplayTransformConfigIdentity.Unavailable;
        }
    }

    private readonly record struct CachedIdentity(
        SilkDisplayTransformConfigIdentity Identity,
        long Timestamp);
}

/// <summary>
/// Bakes display transforms through the shared OpenColorIO CPU processor.
/// </summary>
/// <remarks>
/// <para>
/// The whole lattice is converted in exactly one bulk transition to native code: the
/// shaped lattice is packed into one RGBA32Float image, handed to the processor once,
/// and returned as one display-referred RGBA8 image. There is no per-entry and no
/// per-pixel native call anywhere on this path, and no native call at all on the
/// per-frame path once a lattice exists.
/// </para>
/// <para>
/// This is a precise subset of OpenColorIO, not arbitrary GPU shader generation. See
/// the rendering documentation for the exact exclusions.
/// </para>
/// </remarks>
public sealed class SilkOpenColorIoLatticeProvider : ISilkDisplayTransformLatticeProvider
{
    /// <summary>Gets the shared provider.</summary>
    public static SilkOpenColorIoLatticeProvider Shared { get; } = new();

    /// <inheritdoc/>
    public SilkDisplayTransformLattice Create(RenderDisplayTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!File.Exists(transform.ConfigPath))
        {
            throw new SilkDisplayTransformException(
                SilkDisplayTransformStatus.ConfigUnavailable,
                $"The OpenColorIO config '{transform.ConfigPath}' was not found, so no " +
                "colour-managed display transform was applied.");
        }

        // The processor is created and validated before a single lattice byte is
        // allocated. An unusable config, colour space, display, view, or look therefore
        // costs nothing but the failure, instead of allocating a megabyte of source and
        // destination first and discarding it.
        using SilkOpenColorIoProcessor processor = CreateProcessor(transform);

        int size = transform.LatticeSize;
        int width = size * size;
        int height = size;
        int pixelCount = width * height;
        byte[] source = new byte[checked(pixelCount * 16)];
        byte[] destination = new byte[checked(pixelCount * 4)];
        WriteShapedLattice(source, size, transform.ShaperMinimumLog2, transform.ShaperRangeLog2);

        try
        {
            // Exposure deliberately stays out of the bake. It is a per-frame control
            // applied to linear colour on the GPU immediately before the shaper, which
            // is the same exposure-then-transform order the CPU export path uses. Baking
            // it in would force a new lattice on every exposure change.
            processor.ApplyLinearFloat(source, destination, width, height, exposure: 0);
        }
        catch (OpenUsdNativeException exception)
        {
            throw new SilkDisplayTransformException(
                SilkDisplayTransformStatus.TransformUnsupported,
                DescribeUnsupported(transform, exception.Message),
                exception);
        }

        return new SilkDisplayTransformLattice(
            size,
            transform.ShaperMinimumLog2,
            transform.ShaperRangeLog2,
            destination);
    }

    private static SilkOpenColorIoProcessor CreateProcessor(RenderDisplayTransform transform)
    {
        try
        {
            return new SilkOpenColorIoProcessor(
                new SilkOpenColorIoDisplayTransform(
                    transform.ConfigPath,
                    transform.SourceColorSpace,
                    transform.Display,
                    transform.View,
                    transform.Look));
        }
        catch (OpenUsdNativeException exception)
        {
            throw new SilkDisplayTransformException(
                SilkDisplayTransformStatus.TransformUnsupported,
                DescribeUnsupported(transform, exception.Message),
                exception);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException)
        {
            throw new SilkDisplayTransformException(
                SilkDisplayTransformStatus.TransformUnsupported,
                DescribeUnsupported(
                    transform,
                    "the OpenColorIO native runtime is unavailable in this process."),
                exception);
        }
    }

    internal static void WriteShapedLattice(
        Span<byte> destination,
        int size,
        float shaperMinimumLog2,
        float shaperRangeLog2)
    {
        int width = size * size;
        int height = size;
        if (destination.Length != checked(width * height * 16))
        {
            throw new ArgumentException(
                "The lattice source buffer must hold width * height RGBA32Float pixels.",
                nameof(destination));
        }

        Span<float> axis = stackalloc float[RenderDisplayTransform.MaximumLatticeSize];
        axis = axis[..size];
        float divisor = size - 1;
        for (int index = 0; index < size; index++)
        {
            float value = MathF.Pow(2, shaperMinimumLog2 + (index / divisor * shaperRangeLog2));
            if (!float.IsFinite(value) || value <= 0)
            {
                // Unreachable for every accepted shaper bound, and asserted rather than
                // assumed: the whole point of a float lattice is that every sample the
                // contract admits is exactly representable.
                throw new ArgumentOutOfRangeException(
                    nameof(shaperMinimumLog2),
                    shaperMinimumLog2,
                    "The shaper interval produced a lattice sample that is not a finite " +
                    "positive value.");
            }
            axis[index] = value;
        }

        Span<float> channels = MemoryMarshal.Cast<byte, float>(destination);
        int channel = 0;
        for (int green = 0; green < size; green++)
        {
            for (int blue = 0; blue < size; blue++)
            {
                for (int red = 0; red < size; red++)
                {
                    channels[channel] = axis[red];
                    channels[channel + 1] = axis[green];
                    channels[channel + 2] = axis[blue];
                    channels[channel + 3] = 1f;
                    channel += 4;
                }
            }
        }
    }

    private static string DescribeUnsupported(RenderDisplayTransform transform, string reason) =>
        $"The OpenColorIO config '{transform.ConfigPath}' does not support display " +
        $"'{transform.Display ?? "<default>"}', view '{transform.View ?? "<default>"}', " +
        $"look '{transform.Look ?? "<none>"}', or source colour space " +
        $"'{transform.SourceColorSpace}': {reason}";
}

/// <summary>
/// A bounded least-recently-used cache of baked display-transform lattices, keyed by the
/// transform *and* the current identity of the configuration it was baked from.
/// </summary>
/// <remarks>
/// <para>
/// Baking a lattice costs one OpenColorIO processor construction and one bulk
/// conversion, so a viewer that toggles between two views would otherwise pay for both on
/// every switch. The cache is bounded by entry count *and* by retained bytes, because the
/// lattice edge is caller-chosen; whichever bound is reached first evicts the least
/// recently used entry.
/// </para>
/// <para>
/// A path is not an identity, so every lookup revalidates the configuration identity
/// through <see cref="ISilkDisplayTransformConfigIdentityProvider"/> and an entry baked
/// from a different identity is discarded. An edited config, a changed LUT it references,
/// a retargeted link, a deleted file, and a context change that resolves a reference
/// elsewhere therefore all invalidate the cached lattice, on the CPU and, because the
/// renderer re-uploads whatever this returns, on the GPU.
/// </para>
/// <para>
/// Failures are cached too, under the same key. A transform naming a view the config does
/// not contain would otherwise reconstruct an OpenColorIO processor on every single frame;
/// instead it fails once per configuration identity and is retried as soon as the
/// configuration changes.
/// </para>
/// </remarks>
public sealed class SilkDisplayTransformLatticeCache
{
    /// <summary>Gets the default maximum number of retained lattices.</summary>
    public const int DefaultMaximumEntries = 8;

    /// <summary>Gets the default maximum retained lattice bytes.</summary>
    public const long DefaultMaximumByteSize = 16L * 1024 * 1024;

    /// <summary>Gets the maximum number of retained failures.</summary>
    public const int MaximumFailureEntries = 16;

    /// <summary>Describes the native content walk's bounds, for diagnostics.</summary>
    internal const string MaximumDependencyDescription = "256-file, 64 MiB";

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries =
        new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _order = new();
    private readonly Dictionary<string, FailureEntry> _failures = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _failureOrder = new();
    private readonly ISilkDisplayTransformLatticeProvider _provider;
    private readonly ISilkDisplayTransformConfigIdentityProvider _identities;
    private long _byteSize;
    private ulong _builds;
    private ulong _hits;
    private ulong _invalidations;
    private ulong _suppressedRetries;
    private ulong _partialIdentityRefusals;

    /// <summary>Initializes a bounded cache over a lattice provider.</summary>
    /// <param name="provider">The provider that bakes a missing lattice.</param>
    /// <param name="maximumEntries">The maximum retained lattice count.</param>
    /// <param name="maximumByteSize">The maximum retained lattice bytes.</param>
    /// <param name="identityProvider">
    /// The configuration identity source, or <see langword="null"/> for the shared
    /// OpenColorIO one.
    /// </param>
    public SilkDisplayTransformLatticeCache(
        ISilkDisplayTransformLatticeProvider? provider = null,
        int maximumEntries = DefaultMaximumEntries,
        long maximumByteSize = DefaultMaximumByteSize,
        ISilkDisplayTransformConfigIdentityProvider? identityProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumByteSize);
        _provider = provider ?? SilkOpenColorIoLatticeProvider.Shared;
        _identities = identityProvider ?? SilkOpenColorIoConfigIdentityProvider.Shared;
        MaximumEntries = maximumEntries;
        MaximumByteSize = maximumByteSize;
    }

    /// <summary>Gets the maximum retained lattice count.</summary>
    public int MaximumEntries { get; }

    /// <summary>Gets the maximum retained lattice bytes.</summary>
    public long MaximumByteSize { get; }

    /// <summary>Gets the number of lattices this cache retains.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Gets the retained lattice bytes.</summary>
    public long ByteSize
    {
        get
        {
            lock (_gate)
            {
                return _byteSize;
            }
        }
    }

    /// <summary>Gets the number of lattices this cache baked.</summary>
    public ulong Builds
    {
        get
        {
            lock (_gate)
            {
                return _builds;
            }
        }
    }

    /// <summary>Gets the number of times a retained lattice was reused.</summary>
    public ulong Hits
    {
        get
        {
            lock (_gate)
            {
                return _hits;
            }
        }
    }

    /// <summary>
    /// Gets the number of times a retained lattice was discarded because the
    /// configuration it was baked from changed.
    /// </summary>
    public ulong Invalidations
    {
        get
        {
            lock (_gate)
            {
                return _invalidations;
            }
        }
    }

    /// <summary>
    /// Gets the number of bake attempts skipped because the same transform had already
    /// failed against the same configuration identity.
    /// </summary>
    public ulong SuppressedRetries
    {
        get
        {
            lock (_gate)
            {
                return _suppressedRetries;
            }
        }
    }

    /// <summary>
    /// Gets the number of lookups refused because the configuration's identity could not
    /// be observed exhaustively, and so could not safely key a cache.
    /// </summary>
    public ulong PartialIdentityRefusals
    {
        get
        {
            lock (_gate)
            {
                return _partialIdentityRefusals;
            }
        }
    }

    /// <summary>Gets a retained lattice, baking and retaining it when absent.</summary>
    /// <param name="transform">The renderer-neutral display transform.</param>
    /// <returns>The baked lattice.</returns>
    /// <exception cref="SilkDisplayTransformException">
    /// The configuration is unavailable or does not contain the requested transform.
    /// </exception>
    public SilkDisplayTransformLattice Get(RenderDisplayTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        SilkDisplayTransformConfigIdentity identity =
            _identities.GetIdentity(transform.ConfigPath);
        if (!identity.IsExhaustive)
        {
            // A partial identity cannot be a cache key: something the bounded walk never
            // looked at can change without the identity changing, so a retained lattice
            // could outlive the configuration that produced it and a suppressed failure
            // could outlive its cause. Rather than cache against an identity that cannot
            // detect its own invalidation -- or rebake a megabyte lattice every single
            // frame -- the transform is refused, with the reason named.
            lock (_gate)
            {
                DropEntriesFor(transform.ConfigPath);
                _partialIdentityRefusals++;
            }
            throw new SilkDisplayTransformException(
                SilkDisplayTransformStatus.TransformUnsupported,
                $"The OpenColorIO config '{transform.ConfigPath}' reaches more " +
                $"dependencies than the bounded {MaximumDependencyDescription} " +
                "content walk can hash, so a change to it could not be detected " +
                "reliably and the transform was refused rather than cached against an " +
                "identity that cannot invalidate itself.");
        }

        string key = string.Concat(transform.CacheKey, "\u001e", identity.Value);
        lock (_gate)
        {
            DropStaleEntries(transform.ConfigPath, identity.Value);
            if (_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? existing))
            {
                _order.Remove(existing);
                _order.AddFirst(existing);
                _hits++;
                return existing.Value.Lattice;
            }
            if (_failures.TryGetValue(key, out FailureEntry failure))
            {
                _suppressedRetries++;
                throw new SilkDisplayTransformException(
                    failure.Status,
                    failure.Message);
            }
        }

        // Baking runs outside the lock so one slow config parse cannot stall every
        // other renderer sharing this cache. A concurrent duplicate bake is possible
        // and harmless: the loser's lattice is discarded, never published twice.
        SilkDisplayTransformLattice lattice;
        try
        {
            lattice = _provider.Create(transform);
        }
        catch (SilkDisplayTransformException exception)
        {
            lock (_gate)
            {
                RecordFailure(key, transform.ConfigPath, identity.Value, exception);
            }
            throw;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? raced))
            {
                _order.Remove(raced);
                _order.AddFirst(raced);
                _hits++;
                return raced.Value.Lattice;
            }

            LinkedListNode<CacheEntry> node = _order.AddFirst(
                new CacheEntry(key, transform.ConfigPath, identity.Value, lattice));
            _entries.Add(key, node);
            _byteSize += lattice.ByteCount;
            _builds++;
            Trim();
            return lattice;
        }
    }

    private void DropEntriesFor(string configPath)
    {
        LinkedListNode<CacheEntry>? node = _order.First;
        while (node is not null)
        {
            LinkedListNode<CacheEntry>? next = node.Next;
            if (string.Equals(node.Value.ConfigPath, configPath, StringComparison.Ordinal))
            {
                _order.Remove(node);
                _entries.Remove(node.Value.Key);
                _byteSize -= node.Value.Lattice.ByteCount;
                _invalidations++;
            }
            node = next;
        }

        LinkedListNode<string>? failure = _failureOrder.First;
        while (failure is not null)
        {
            LinkedListNode<string>? next = failure.Next;
            if (_failures.TryGetValue(failure.Value, out FailureEntry entry) &&
                string.Equals(entry.ConfigPath, configPath, StringComparison.Ordinal))
            {
                _failureOrder.Remove(failure);
                _failures.Remove(failure.Value);
            }
            failure = next;
        }
    }

    /// <summary>Drops every retained lattice and every retained failure.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
            _failures.Clear();
            _failureOrder.Clear();
            _byteSize = 0;
        }
    }

    private void DropStaleEntries(string configPath, string currentIdentity)
    {
        // Everything remembered for this config path under a different identity is gone:
        // the config it described is no longer the config on disk. Entries for the same
        // config under the *current* identity stay, because several transforms can name
        // one config -- two views of the same show, say -- and all of them are still
        // valid.
        LinkedListNode<CacheEntry>? node = _order.First;
        while (node is not null)
        {
            LinkedListNode<CacheEntry>? next = node.Next;
            if (string.Equals(node.Value.ConfigPath, configPath, StringComparison.Ordinal) &&
                !string.Equals(
                    node.Value.Identity,
                    currentIdentity,
                    StringComparison.Ordinal))
            {
                _order.Remove(node);
                _entries.Remove(node.Value.Key);
                _byteSize -= node.Value.Lattice.ByteCount;
                _invalidations++;
            }
            node = next;
        }

        LinkedListNode<string>? failure = _failureOrder.First;
        while (failure is not null)
        {
            LinkedListNode<string>? next = failure.Next;
            if (_failures.TryGetValue(failure.Value, out FailureEntry entry) &&
                string.Equals(entry.ConfigPath, configPath, StringComparison.Ordinal) &&
                !string.Equals(entry.Identity, currentIdentity, StringComparison.Ordinal))
            {
                _failureOrder.Remove(failure);
                _failures.Remove(failure.Value);
            }
            failure = next;
        }
    }

    private void RecordFailure(
        string key,
        string configPath,
        string identity,
        SilkDisplayTransformException exception)
    {
        if (_failures.ContainsKey(key))
        {
            return;
        }
        _failures[key] = new FailureEntry(
            configPath,
            identity,
            exception.Status,
            exception.Message);
        _failureOrder.AddFirst(key);
        while (_failureOrder.Count > MaximumFailureEntries)
        {
            LinkedListNode<string>? last = _failureOrder.Last;
            if (last is null)
            {
                return;
            }
            _failureOrder.RemoveLast();
            _failures.Remove(last.Value);
        }
    }

    private void Trim()
    {
        while (_order.Count > 1 &&
            (_entries.Count > MaximumEntries || _byteSize > MaximumByteSize))
        {
            LinkedListNode<CacheEntry>? last = _order.Last;
            if (last is null)
            {
                return;
            }
            _order.RemoveLast();
            _entries.Remove(last.Value.Key);
            _byteSize -= last.Value.Lattice.ByteCount;
        }
    }

    private readonly record struct CacheEntry(
        string Key,
        string ConfigPath,
        string Identity,
        SilkDisplayTransformLattice Lattice);

    private readonly record struct FailureEntry(
        string ConfigPath,
        string Identity,
        SilkDisplayTransformStatus Status,
        string Message);
}
