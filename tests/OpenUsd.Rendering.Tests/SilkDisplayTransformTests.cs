// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkDisplayTransformTests
{
    /// <summary>
    /// An absolute placeholder path. Every descriptor requires one, and the tests in this
    /// class exercise descriptor validation and caching, which never open the file.
    /// </summary>
    private static string ConfigPath { get; } = Path.Combine(
        Path.GetFullPath(AppContext.BaseDirectory),
        "display-transform-tests.ocio");

    [Test]
    public async Task Transform_RejectsEveryRelativeConfigPath()
    {
        // Lexical containment was removed rather than patched: comparing normalized
        // strings against the working directory is not a containment guarantee -- a link
        // defeats it and a working directory that changes between validation and use
        // defeats it -- so the contract is that a config is named absolutely and anything
        // else is refused outright.
        string[] relative =
        [
            "config.ocio",
            Path.Combine("nested", "config.ocio"),
            Path.Combine("..", "..", "escaped.ocio"),
            Path.Combine(".", "config.ocio"),
        ];
        foreach (string path in relative)
        {
            await Assert.That(() => new RenderDisplayTransform(path, "linear"))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task Transform_RejectsRootRelativeConfigPathOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Rooted but not fully qualified: both of these depend on ambient state (the
        // current drive, and that drive's current directory), which is exactly the
        // ambiguity an absolute-only contract exists to remove.
        await Assert.That(() => new RenderDisplayTransform("\\config.ocio", "linear"))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderDisplayTransform("C:config.ocio", "linear"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Transform_AcceptsAbsoluteConfigPath()
    {
        string absolute = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            "somewhere",
            "config.ocio");
        var transform = new RenderDisplayTransform(absolute, "linear");
        await Assert.That(transform.ConfigPath).IsEqualTo(absolute);
    }

    [Test]
    public async Task Transform_RejectsEmptyNames()
    {
        await Assert.That(() => new RenderDisplayTransform("", "linear"))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderDisplayTransform(ConfigPath, " "))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Transform_NormalizesEmptyOptionalNamesToNull()
    {
        var transform = new RenderDisplayTransform(
            ConfigPath,
            "linear",
            display: "",
            view: "",
            look: "");
        await Assert.That(transform.Display).IsNull();
        await Assert.That(transform.View).IsNull();
        await Assert.That(transform.Look).IsNull();
    }

    [Test]
    public async Task Transform_RejectsOversizedNames()
    {
        string oversized = new('a', RenderDisplayTransform.MaximumNameLength + 1);
        await Assert.That(() => new RenderDisplayTransform(ConfigPath, oversized))
            .Throws<ArgumentException>();
        await Assert.That(() => new RenderDisplayTransform(
                ConfigPath,
                "linear",
                display: oversized))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(RenderDisplayTransform.MinimumLatticeSize - 1)]
    [Arguments(RenderDisplayTransform.MaximumLatticeSize + 1)]
    [Arguments(0)]
    [Arguments(-4)]
    public async Task Transform_RejectsLatticeSizesOutsideTheSupportedRange(int size)
    {
        await Assert.That(() => new RenderDisplayTransform(
                ConfigPath,
                "linear",
                latticeSize: size))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Transform_RejectsNonFiniteAndInvertedShaperBounds()
    {
        await Assert.That(() => new RenderDisplayTransform(
                ConfigPath,
                "linear",
                shaperMinimumLog2: float.NaN))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderDisplayTransform(
                ConfigPath,
                "linear",
                shaperMaximumLog2: float.PositiveInfinity))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderDisplayTransform(
                ConfigPath,
                "linear",
                shaperMinimumLog2: 4,
                shaperMaximumLog2: 4))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderDisplayTransform(
                ConfigPath,
                "linear",
                shaperMinimumLog2: RenderDisplayTransform.MinimumShaperLog2 - 1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderDisplayTransform(
                ConfigPath,
                "linear",
                shaperMaximumLog2: RenderDisplayTransform.MaximumShaperLog2 + 1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Transform_CacheKeyDistinguishesEveryParameter()
    {
        var baseline = new RenderDisplayTransform(
            ConfigPath,
            "linear",
            "Display",
            "View",
            "Look");
        RenderDisplayTransform[] variants =
        [
            new(ConfigPath, "other", "Display", "View", "Look"),
            new(ConfigPath, "linear", "Other", "View", "Look"),
            new(ConfigPath, "linear", "Display", "Other", "Look"),
            new(ConfigPath, "linear", "Display", "View", "Other"),
            new(ConfigPath, "linear", "Display", "View", "Look", latticeSize: 32),
            new(
                ConfigPath,
                "linear",
                "Display",
                "View",
                "Look",
                shaperMinimumLog2: -10),
        ];
        foreach (RenderDisplayTransform variant in variants)
        {
            await Assert.That(variant.CacheKey).IsNotEqualTo(baseline.CacheKey);
        }
        await Assert.That(
                new RenderDisplayTransform(
                    ConfigPath,
                    "linear",
                    "Display",
                    "View",
                    "Look").CacheKey)
            .IsEqualTo(baseline.CacheKey);
    }

    [Test]
    public async Task Transform_CacheKeyIsInjectiveAcrossFieldBoundaries()
    {
        // Two transforms that a separator-joined key cannot tell apart. Colour-space,
        // display, view, and look names are free-form strings, so a name may contain
        // whatever character the join uses -- and two transforms that share a cache key
        // share a baked lattice and a cached failure, which is a wrong image rather than
        // a wasted rebake. The encoding is length-prefixed, so no field content can be
        // mistaken for a boundary.
        const char unit = '\u001f';
        var spilled = new RenderDisplayTransform(
            ConfigPath,
            "linear",
            $"Display{unit}View",
            "Look");
        var separate = new RenderDisplayTransform(
            ConfigPath,
            "linear",
            "Display",
            $"View{unit}Look");

        await Assert.That(spilled.CacheKey).IsNotEqualTo(separate.CacheKey);

        // The same collision through other plausible delimiters and control characters.
        foreach (char delimiter in new[] { '\u0000', '\u0001', '\n', '\t', '|', ':', ';' })
        {
            var left = new RenderDisplayTransform(
                ConfigPath,
                "linear",
                $"a{delimiter}b",
                "c");
            var right = new RenderDisplayTransform(
                ConfigPath,
                "linear",
                "a",
                $"b{delimiter}c");
            await Assert.That(left.CacheKey).IsNotEqualTo(right.CacheKey);
        }

        // An absent optional name is not the same request as one that happens to encode
        // to nothing, and the lattice size and shaper bounds cannot be absorbed into a
        // neighbouring field either.
        var absentLook = new RenderDisplayTransform(ConfigPath, "linear", "d", "v");
        var namedLook = new RenderDisplayTransform(ConfigPath, "linear", "d", "v", "l");
        await Assert.That(absentLook.CacheKey).IsNotEqualTo(namedLook.CacheKey);

        var digits = new RenderDisplayTransform(ConfigPath, "linear", "d", "v6", "l");
        var shifted = new RenderDisplayTransform(ConfigPath, "linear", "d", "v", "l6");
        await Assert.That(digits.CacheKey).IsNotEqualTo(shifted.CacheKey);

        // And it is still stable: the same request produces the same key.
        await Assert.That(
                new RenderDisplayTransform(
                    ConfigPath,
                    "linear",
                    $"Display{unit}View",
                    "Look").CacheKey)
            .IsEqualTo(spilled.CacheKey);
    }

    [Test]
    public async Task RenderSettings_RejectDisplayTransformWithNonIdentityOutput()
    {
        RenderSettings settings = RenderSettings.PresentationDefault with
        {
            DisplayTransform = new RenderDisplayTransform(ConfigPath, "linear"),
        };
        await Assert.That(settings.ValidateDisplayTransform)
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RenderSettings_AcceptDisplayTransformWithIdentityOutput()
    {
        RenderSettings settings = RenderSettings.Default with
        {
            DisplayTransform = new RenderDisplayTransform(ConfigPath, "linear"),
        };
        settings.ValidateDisplayTransform();
        await Assert.That(settings.DisplayTransform).IsNotNull();
        await Assert.That(RenderSettings.Default.DisplayTransform).IsNull();
    }

    [Test]
    public async Task UniformWriter_WritesTheCheckedThirtyTwoByteLayout()
    {
        byte[] bytes = new byte[SilkDisplayTransformUniformWriter.ByteSize];
        SilkDisplayTransformUniformWriter.Write(-1f, -14f, 20f, 64, false, bytes);
        float[] values = MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();

        await Assert.That(values[0]).IsEqualTo(0.5f);
        await Assert.That(values[1]).IsEqualTo(-14f);
        await Assert.That(values[2]).IsEqualTo(20f);
        await Assert.That(values[3]).IsEqualTo(64f);
        await Assert.That(values[4]).IsEqualTo(1f / (64f * 64f));
        await Assert.That(values[5]).IsEqualTo(1f / 64f);
        await Assert.That(values[6]).IsEqualTo(63f);
        await Assert.That(values[7]).IsEqualTo(0f);
    }

    [Test]
    public async Task UniformWriter_CarriesTheVerticalFlipFlag()
    {
        // The flag is the only difference between a correct and an upside-down fullscreen
        // composite on a backend whose framebuffer origin opposes clip-space Y, so it is
        // asserted at the byte it occupies rather than trusted.
        byte[] flipped = new byte[SilkDisplayTransformUniformWriter.ByteSize];
        byte[] upright = new byte[SilkDisplayTransformUniformWriter.ByteSize];
        SilkDisplayTransformUniformWriter.Write(0f, -14f, 20f, 64, true, flipped);
        SilkDisplayTransformUniformWriter.Write(0f, -14f, 20f, 64, false, upright);

        await Assert.That(MemoryMarshal.Cast<byte, float>(flipped.AsSpan())[7])
            .IsEqualTo(1f);
        await Assert.That(MemoryMarshal.Cast<byte, float>(upright.AsSpan())[7])
            .IsEqualTo(0f);
        await Assert.That(flipped.AsSpan(0, 28).SequenceEqual(upright.AsSpan(0, 28)))
            .IsTrue();
    }

    [Test]
    public async Task UniformWriter_RejectsInvalidArguments()
    {
        byte[] correct = new byte[SilkDisplayTransformUniformWriter.ByteSize];
        byte[] wrongSize = new byte[16];
        await Assert.That(() => SilkDisplayTransformUniformWriter.Write(
                0, -14, 20, 64, false, wrongSize))
            .Throws<ArgumentException>();
        await Assert.That(() => SilkDisplayTransformUniformWriter.Write(
                float.NaN, -14, 20, 64, false, correct))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkDisplayTransformUniformWriter.Write(
                0, float.NaN, 20, 64, false, correct))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkDisplayTransformUniformWriter.Write(
                0, -14, 0, 64, false, correct))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkDisplayTransformUniformWriter.Write(
                0, -14, 20, 4, false, correct))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkDisplayTransformUniformWriter.Write(
                256, -14, 20, 64, false, correct))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ShapedLattice_MapsEveryAxisThroughTheDocumentedShaper()
    {
        const int size = 8;
        const float minimum = -4;
        const float range = 8;
        byte[] source = new byte[size * size * size * 16];
        SilkOpenColorIoLatticeProvider.WriteShapedLattice(
            source,
            size,
            minimum,
            range);
        float[] channels = MemoryMarshal.Cast<byte, float>(source.AsSpan()).ToArray();

        for (int green = 0; green < size; green++)
        {
            for (int blue = 0; blue < size; blue++)
            {
                for (int red = 0; red < size; red++)
                {
                    int pixel = ((green * size) + blue) * size + red;
                    await Assert.That(channels[(pixel * 4) + 0])
                        .IsEqualTo(expected(red)).Within(1e-6f);
                    await Assert.That(channels[(pixel * 4) + 1])
                        .IsEqualTo(expected(green)).Within(1e-6f);
                    await Assert.That(channels[(pixel * 4) + 2])
                        .IsEqualTo(expected(blue)).Within(1e-6f);
                    await Assert.That(channels[(pixel * 4) + 3]).IsEqualTo(1f);
                }
            }
        }

        static float expected(int index) =>
            MathF.Pow(2, minimum + (index / (float)(size - 1) * range));
    }

    [Test]
    [Arguments(
        RenderDisplayTransform.MinimumShaperLog2,
        RenderDisplayTransform.MaximumShaperLog2)]
    [Arguments(RenderDisplayTransform.MinimumShaperLog2, -31f)]
    [Arguments(31f, RenderDisplayTransform.MaximumShaperLog2)]
    [Arguments(-14f, 6f)]
    public async Task ShapedLattice_IsFiniteAndPositiveAtEveryAcceptedShaperExtreme(
        float minimum,
        float maximum)
    {
        // The lattice source is 32-bit float precisely so that every bound the contract
        // accepts is exactly representable. A half-float source underflows to zero below
        // 2^-14 and overflows to infinity above 2^15, silently misrepresenting the samples
        // the shaper asked for -- and the native processor rejects a non-finite channel,
        // so the whole transform failed at the widest accepted bounds.
        const int size = RenderDisplayTransform.MaximumLatticeSize;
        var transform = new RenderDisplayTransform(
            ConfigPath,
            "linear",
            latticeSize: size,
            shaperMinimumLog2: minimum,
            shaperMaximumLog2: maximum);
        byte[] source = new byte[size * size * size * 16];
        SilkOpenColorIoLatticeProvider.WriteShapedLattice(
            source,
            size,
            transform.ShaperMinimumLog2,
            transform.ShaperRangeLog2);
        float[] channels = MemoryMarshal.Cast<byte, float>(source.AsSpan()).ToArray();

        bool allFinitePositive = true;
        float smallest = float.MaxValue;
        float largest = float.MinValue;
        for (int index = 0; index < channels.Length; index += 4)
        {
            for (int component = 0; component < 3; component++)
            {
                float value = channels[index + component];
                allFinitePositive &= float.IsFinite(value) && value > 0;
                smallest = MathF.Min(smallest, value);
                largest = MathF.Max(largest, value);
            }
        }

        await Assert.That(allFinitePositive).IsTrue();
        await Assert.That(smallest).IsEqualTo(MathF.Pow(2, minimum))
            .Within(MathF.Pow(2, minimum) * 1e-5f);
        await Assert.That(largest).IsEqualTo(MathF.Pow(2, maximum))
            .Within(MathF.Pow(2, maximum) * 1e-5f);
    }

    [Test]
    public async Task ShapedLattice_RejectsMismatchedBufferSize()
    {
        byte[] source = new byte[16];
        await Assert.That(() => SilkOpenColorIoLatticeProvider.WriteShapedLattice(
                source,
                8,
                -4,
                8))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task LatticeCache_ReusesEntriesAndCountsBuilds()
    {
        var provider = new CountingProvider();
        var identities = new StubIdentityProvider("identity-1");
        var cache = new SilkDisplayTransformLatticeCache(
            provider,
            identityProvider: identities);
        var transform = new RenderDisplayTransform(ConfigPath, "linear");

        SilkDisplayTransformLattice first = cache.Get(transform);
        SilkDisplayTransformLattice second = cache.Get(transform);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(provider.Creations).IsEqualTo(1);
        await Assert.That(cache.Builds).IsEqualTo(1UL);
        await Assert.That(cache.Hits).IsEqualTo(1UL);
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.ByteSize).IsEqualTo(first.ByteCount);
        await Assert.That(cache.Invalidations).IsEqualTo(0UL);
    }

    [Test]
    public async Task LatticeCache_InvalidatesWhenTheConfigIdentityChanges()
    {
        var provider = new CountingProvider();
        var identities = new StubIdentityProvider("identity-1");
        var cache = new SilkDisplayTransformLatticeCache(
            provider,
            identityProvider: identities);
        var transform = new RenderDisplayTransform(ConfigPath, "linear");

        SilkDisplayTransformLattice first = cache.Get(transform);
        identities.Identity = "identity-2";
        SilkDisplayTransformLattice second = cache.Get(transform);

        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(provider.Creations).IsEqualTo(2);
        await Assert.That(cache.Invalidations).IsEqualTo(1UL);

        // The stale entry is gone rather than merely shadowed, so nothing keeps the
        // previous configuration's bytes alive.
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.ByteSize).IsEqualTo(second.ByteCount);
    }

    [Test]
    public async Task LatticeCache_SuppressesRepeatedFailuresUntilTheConfigChanges()
    {
        var provider = new FailingCountingProvider();
        var identities = new StubIdentityProvider("identity-1");
        var cache = new SilkDisplayTransformLatticeCache(
            provider,
            identityProvider: identities);
        var transform = new RenderDisplayTransform(ConfigPath, "linear");

        for (int attempt = 0; attempt < 5; attempt++)
        {
            _ = await Assert.ThrowsAsync<SilkDisplayTransformException>(
                () => Task.Run(() => cache.Get(transform)));
        }

        // An unusable transform must not reconstruct an OpenColorIO processor on every
        // frame; it fails once per configuration identity.
        await Assert.That(provider.Attempts).IsEqualTo(1);
        await Assert.That(cache.SuppressedRetries).IsEqualTo(4UL);

        identities.Identity = "identity-2";
        _ = await Assert.ThrowsAsync<SilkDisplayTransformException>(
            () => Task.Run(() => cache.Get(transform)));
        await Assert.That(provider.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task LatticeCache_RetriesAfterAFailureWhenTheConfigBecomesUsable()
    {
        var provider = new RecoveringProvider();
        var identities = new StubIdentityProvider("broken");
        var cache = new SilkDisplayTransformLatticeCache(
            provider,
            identityProvider: identities);
        var transform = new RenderDisplayTransform(ConfigPath, "linear");

        _ = await Assert.ThrowsAsync<SilkDisplayTransformException>(
            () => Task.Run(() => cache.Get(transform)));
        provider.Succeed = true;
        identities.Identity = "repaired";
        SilkDisplayTransformLattice lattice = cache.Get(transform);

        await Assert.That(lattice).IsNotNull();
        await Assert.That(cache.Builds).IsEqualTo(1UL);
    }

    [Test]
    public async Task LatticeCache_EvictsLeastRecentlyUsedBeyondTheEntryBound()
    {
        var provider = new CountingProvider();
        var identities = new StubIdentityProvider("identity-1");
        var cache = new SilkDisplayTransformLatticeCache(
            provider,
            maximumEntries: 2,
            identityProvider: identities);
        var first = new RenderDisplayTransform(ConfigPath, "one");
        var second = new RenderDisplayTransform(ConfigPath, "two");
        var third = new RenderDisplayTransform(ConfigPath, "three");

        _ = cache.Get(first);
        _ = cache.Get(second);
        _ = cache.Get(first);
        _ = cache.Get(third);

        await Assert.That(cache.Count).IsEqualTo(2);

        // "second" was the least recently used when "third" arrived, so it is the one
        // that had to be rebuilt; "first" was still retained.
        _ = cache.Get(first);
        await Assert.That(provider.Creations).IsEqualTo(3);
        _ = cache.Get(second);
        await Assert.That(provider.Creations).IsEqualTo(4);
    }

    [Test]
    public async Task LatticeCache_EvictsBeyondTheByteBound()
    {
        var provider = new CountingProvider();
        var cache = new SilkDisplayTransformLatticeCache(
            provider,
            maximumEntries: 64,
            maximumByteSize: CountingProvider.LatticeByteSize * 2,
            identityProvider: new StubIdentityProvider("identity-1"));

        _ = cache.Get(new RenderDisplayTransform(ConfigPath, "one"));
        _ = cache.Get(new RenderDisplayTransform(ConfigPath, "two"));
        _ = cache.Get(new RenderDisplayTransform(ConfigPath, "three"));

        await Assert.That(cache.Count).IsEqualTo(2);
        await Assert.That(cache.ByteSize)
            .IsLessThanOrEqualTo(CountingProvider.LatticeByteSize * 2);
    }

    [Test]
    public async Task LatticeCache_RejectsNonPositiveBounds()
    {
        await Assert.That(() => new SilkDisplayTransformLatticeCache(
                null,
                maximumEntries: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkDisplayTransformLatticeCache(
                null,
                maximumByteSize: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task LatticeCache_PropagatesProviderFailures()
    {
        var cache = new SilkDisplayTransformLatticeCache(
            new FailingProvider(),
            identityProvider: new StubIdentityProvider("identity-1"));
        var exception = await Assert.ThrowsAsync<SilkDisplayTransformException>(
            () => Task.Run(() =>
                cache.Get(new RenderDisplayTransform(ConfigPath, "linear"))));
        await Assert.That(exception!.Status)
            .IsEqualTo(SilkDisplayTransformStatus.TransformUnsupported);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IdentityProvider_RejectsAnOutOfRangeRevalidationInterval()
    {
        await Assert.That(() => new SilkOpenColorIoConfigIdentityProvider(
                TimeSpan.FromSeconds(-1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkOpenColorIoConfigIdentityProvider(
                TimeSpan.FromHours(1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task DisplayTransformException_BoundsItsMessage()
    {
        var exception = new SilkDisplayTransformException(
            SilkDisplayTransformStatus.ConfigUnavailable,
            new string('x', SilkDisplayTransformException.MaximumMessageLength * 4));
        await Assert.That(exception.Message.Length)
            .IsEqualTo(SilkDisplayTransformException.MaximumMessageLength);
    }

    [Test]
    public async Task MissingConfig_ReportsConfigUnavailableRatherThanIdentity()
    {
        string missing = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"openusd-missing-{Guid.NewGuid():N}.ocio");
        var exception = await Assert.ThrowsAsync<SilkDisplayTransformException>(
            () => Task.Run(() => SilkOpenColorIoLatticeProvider.Shared.Create(
                new RenderDisplayTransform(missing, "linear"))));
        await Assert.That(exception!.Status)
            .IsEqualTo(SilkDisplayTransformStatus.ConfigUnavailable);
    }

    private sealed class StubIdentityProvider(string identity)
        : ISilkDisplayTransformConfigIdentityProvider
    {
        internal string Identity { get; set; } = identity;

        internal bool IsExhaustive { get; set; } = true;

        public SilkDisplayTransformConfigIdentity GetIdentity(string configPath) =>
            new(Identity, IsExhaustive);
    }

    private sealed class CountingProvider : ISilkDisplayTransformLatticeProvider
    {
        internal const int LatticeSize = 8;
        internal const long LatticeByteSize = LatticeSize * LatticeSize * LatticeSize * 4;

        internal int Creations { get; private set; }

        public SilkDisplayTransformLattice Create(RenderDisplayTransform transform)
        {
            ArgumentNullException.ThrowIfNull(transform);
            Creations++;
            return new SilkDisplayTransformLattice(
                LatticeSize,
                transform.ShaperMinimumLog2,
                transform.ShaperRangeLog2,
                new byte[LatticeByteSize]);
        }
    }

    private sealed class FailingProvider : ISilkDisplayTransformLatticeProvider
    {
        public SilkDisplayTransformLattice Create(RenderDisplayTransform transform) =>
            throw new SilkDisplayTransformException(
                SilkDisplayTransformStatus.TransformUnsupported,
                "The test provider never resolves a transform.");
    }

    private sealed class FailingCountingProvider : ISilkDisplayTransformLatticeProvider
    {
        internal int Attempts { get; private set; }

        public SilkDisplayTransformLattice Create(RenderDisplayTransform transform)
        {
            Attempts++;
            throw new SilkDisplayTransformException(
                SilkDisplayTransformStatus.TransformUnsupported,
                "The test provider never resolves a transform.");
        }
    }

    private sealed class RecoveringProvider : ISilkDisplayTransformLatticeProvider
    {
        internal bool Succeed { get; set; }

        public SilkDisplayTransformLattice Create(RenderDisplayTransform transform)
        {
            ArgumentNullException.ThrowIfNull(transform);
            if (!Succeed)
            {
                throw new SilkDisplayTransformException(
                    SilkDisplayTransformStatus.ConfigUnavailable,
                    "The test provider is not ready yet.");
            }
            return new SilkDisplayTransformLattice(
                8,
                transform.ShaperMinimumLog2,
                transform.ShaperRangeLog2,
                new byte[8 * 8 * 8 * 4]);
        }
    }
}
