// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Executable evidence for the OpenColorIO look-override contract, the float lattice
/// entry point, and OpenColorIO-derived config identity.
/// </summary>
/// <remarks>
/// These exercise the project-owned C ABI directly, so they are skipped when the native
/// runtime is not loadable in the test host rather than asserted vacuously.
/// </remarks>
// OpenColorIO's caches are process-global and these tests edit files on disk, so they
// must not overlap each other or anything else that builds a processor.
[NotInParallel]
public sealed class SilkDisplayTransformNativeTests
{
    private const float Tolerance = 1.5f / 255f;

    private static string LookConfigPath => ResolveAsset("ocio-look-override-config.ocio");

    private static string DisplayConfigPath => ResolveAsset("ocio-test-config.ocio");

    [Test]
    public async Task LookOverride_AppliesInTheLookProcessSpace()
    {
        RequireNativeOcio();

        // linear 0.5 -> look_space 0.125 -> ^2 = 0.015625 -> reference *4 = 0.0625
        //            -> display *0.5 = 0.03125
        // Applying the look in the source space instead would give 0.5^2 * 0.5 = 0.125,
        // which is four times larger and nowhere near the tolerance.
        float actual = ApplyLook(LookConfigPath, "PlainView", "OverrideLook", 0.5f);
        await Assert.That(actual).IsEqualTo(0.03125f).Within(Tolerance);
    }

    [Test]
    public async Task LookOverride_ReplacesTheViewAuthoredLook()
    {
        RequireNativeOcio();

        // The view declares its own look (a x2 scale in look_space). An override replaces
        // it, so the result must equal the plain view's overridden result exactly.
        // Composing both would multiply by two and land at 0.0625.
        float plain = ApplyLook(LookConfigPath, "PlainView", "OverrideLook", 0.5f);
        float withViewLook = ApplyLook(
            LookConfigPath,
            "ViewWithLook",
            "OverrideLook",
            0.5f);

        await Assert.That(withViewLook).IsEqualTo(plain).Within(Tolerance);
        await Assert.That(withViewLook).IsEqualTo(0.03125f).Within(Tolerance);
    }

    [Test]
    public async Task NoOverride_StillHonoursTheViewAuthoredLook()
    {
        RequireNativeOcio();

        // Without an override, the view's own look must still apply:
        // linear 0.5 -> look_space 0.125 -> x2 = 0.25 -> reference *4 = 1.0
        //            -> display *0.5 = 0.5
        float withViewLook = ApplyLook(LookConfigPath, "ViewWithLook", null, 0.5f);
        float plain = ApplyLook(LookConfigPath, "PlainView", null, 0.5f);

        await Assert.That(plain).IsEqualTo(0.25f).Within(Tolerance);
        await Assert.That(withViewLook).IsEqualTo(0.5f).Within(Tolerance);
    }

    [Test]
    public async Task FloatAndHalfEntryPointsAgreeInsideTheHalfFloatRange()
    {
        RequireNativeOcio();

        // The capture path keeps the half-float entry point and must be unchanged by the
        // lattice path's float entry point, so both are asked the same question here.
        var transform = new SilkOpenColorIoDisplayTransform(
            DisplayConfigPath,
            "linear",
            "TestDisplay",
            "TestView");
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();

        float[] values = [0.001f, 0.05f, 0.25f, 0.5f, 0.9f];
        byte[] halfSource = new byte[values.Length * 8];
        byte[] floatSource = new byte[values.Length * 16];
        Span<Half> halfChannels = MemoryMarshal.Cast<byte, Half>(halfSource.AsSpan());
        Span<float> floatChannels = MemoryMarshal.Cast<byte, float>(floatSource.AsSpan());
        for (int index = 0; index < values.Length; index++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                halfChannels[(index * 4) + channel] = (Half)values[index];
                floatChannels[(index * 4) + channel] = (float)(Half)values[index];
            }
            halfChannels[(index * 4) + 3] = (Half)1f;
            floatChannels[(index * 4) + 3] = 1f;
        }

        byte[] fromHalf = new byte[values.Length * 4];
        byte[] fromFloat = new byte[values.Length * 4];
        processor.Apply(halfSource, fromHalf, values.Length, 1, 0f);
        processor.ApplyLinearFloat(floatSource, fromFloat, values.Length, 1, 0f);

        await Assert.That(fromFloat.SequenceEqual(fromHalf)).IsTrue();
    }

    [Test]
    public async Task FloatEntryPoint_RejectsNonFiniteAndMismatchedBuffers()
    {
        RequireNativeOcio();
        var transform = new SilkOpenColorIoDisplayTransform(DisplayConfigPath, "linear");
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();

        byte[] source = new byte[16];
        MemoryMarshal.Cast<byte, float>(source.AsSpan())[0] = float.PositiveInfinity;
        byte[] destination = new byte[4];
        OpenUsdNativeException? nonFinite =
            await Assert.ThrowsAsync<OpenUsdNativeException>(
                () => Task.Run(() =>
                    processor.ApplyLinearFloat(source, destination, 1, 1, 0f)));
        await Assert.That(nonFinite!.Status)
            .IsEqualTo(OpenUsdNativeStatus.InvalidArgument);

        byte[] shortSource = new byte[8];
        OpenUsdNativeException? mismatched =
            await Assert.ThrowsAsync<OpenUsdNativeException>(
                () => Task.Run(() =>
                    processor.ApplyLinearFloat(shortSource, destination, 1, 1, 0f)));
        await Assert.That(mismatched!.Status)
            .IsEqualTo(OpenUsdNativeStatus.InvalidArgument);
    }

    [Test]
    public async Task ConfigIdentity_ChangesWhenTheConfigIsEditedDeletedOrRetargeted()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string configPath = Path.Combine(root, "config.ocio");
        try
        {
            File.Copy(DisplayConfigPath, configPath);
            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);

            string first = provider.GetIdentity(configPath).Value;
            await Assert.That(first).IsNotEmpty();
            await Assert.That(provider.GetIdentity(configPath).Value).IsEqualTo(first);

            // An edit that changes the parsed config must change the identity, which is
            // exactly what a path-keyed cache could never notice.
            string edited = File.ReadAllText(configPath)
                .Replace("gamma: 2.4", "gamma: 2.2", StringComparison.Ordinal);
            File.WriteAllText(configPath, edited);
            string second = provider.GetIdentity(configPath).Value;
            await Assert.That(second).IsNotEqualTo(first);

            // Retargeting the same path at a different config -- the observable effect of
            // a symbolic-link change -- must also change it.
            File.Copy(LookConfigPath, configPath, overwrite: true);
            string third = provider.GetIdentity(configPath).Value;
            await Assert.That(third).IsNotEqualTo(second);

            // Deleting it leaves no identity at all, which never matches a lattice baked
            // from a readable config.
            File.Delete(configPath);
            await Assert.That(provider.GetIdentity(configPath).Value).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_RevalidationIntervalBoundsHowOftenItIsRead()
    {
        RequireNativeOcio();
        long timestamp = 0;
        var provider = new SilkOpenColorIoConfigIdentityProvider(
            TimeSpan.FromMilliseconds(250),
            () => timestamp);

        _ = provider.GetIdentity(DisplayConfigPath);
        _ = provider.GetIdentity(DisplayConfigPath);
        _ = provider.GetIdentity(DisplayConfigPath);
        await Assert.That(provider.Reads).IsEqualTo(1UL);

        timestamp += (long)(0.3 * System.Diagnostics.Stopwatch.Frequency);
        _ = provider.GetIdentity(DisplayConfigPath);
        await Assert.That(provider.Reads).IsEqualTo(2UL);
    }

    [Test]
    public async Task LatticeCache_InvalidatesOnARealConfigEdit()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string configPath = Path.Combine(root, "config.ocio");
        try
        {
            File.Copy(DisplayConfigPath, configPath);
            var cache = new SilkDisplayTransformLatticeCache(
                identityProvider: new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero));
            var transform = new RenderDisplayTransform(
                configPath,
                "linear",
                "TestDisplay",
                "TestView",
                latticeSize: 16);

            SilkDisplayTransformLattice first = cache.Get(transform);
            await Assert.That(cache.Get(transform)).IsSameReferenceAs(first);
            await Assert.That(cache.Builds).IsEqualTo(1UL);

            File.WriteAllText(
                configPath,
                File.ReadAllText(configPath)
                    .Replace("gamma: 2.4", "gamma: 1.8", StringComparison.Ordinal));
            SilkDisplayTransformLattice second = cache.Get(transform);

            await Assert.That(cache.Builds).IsEqualTo(2UL);
            await Assert.That(cache.Invalidations).IsEqualTo(1UL);

            // The rebaked lattice is genuinely different colour, not merely a new object.
            await Assert.That(second.Rgba8.Span.SequenceEqual(first.Rgba8.Span)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ExternalLutEditDeleteAndRestoreInvalidateIdentityAndOutput()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-lut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string configPath = Path.Combine(root, "config.ocio");
        string lutPath = Path.Combine(root, "scale.spi1d");
        try
        {
            File.WriteAllText(configPath, ExternalLutConfig);
            File.WriteAllText(lutPath, CreateScaleLut(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            string firstIdentity = provider.GetIdentity(configPath).Value;
            await Assert.That(firstIdentity).IsNotEmpty();
            float first = ApplyExternalLut(configPath, 1f);

            // Only the referenced LUT changes; the config file itself is untouched. Its
            // mtime, its size, and its text are all identical, so nothing short of asking
            // OpenColorIO -- with its own caches cleared first -- can notice this.
            File.WriteAllText(lutPath, CreateScaleLut(0.25f));
            string secondIdentity = provider.GetIdentity(configPath).Value;
            float second = ApplyExternalLut(configPath, 1f);

            await Assert.That(secondIdentity).IsNotEqualTo(firstIdentity);
            await Assert.That(second).IsLessThan(first - 0.05f);

            // Deleting the referenced LUT makes the config unusable, which must be
            // reported rather than answered from a cache.
            File.Delete(lutPath);
            string deletedIdentity = provider.GetIdentity(configPath).Value;
            await Assert.That(deletedIdentity).IsNotEqualTo(secondIdentity);
            _ = await Assert.ThrowsAsync<SilkDisplayTransformException>(
                () => Task.Run(() => SilkOpenColorIoLatticeProvider.Shared.Create(
                    new RenderDisplayTransform(
                        configPath,
                        "linear",
                        "LutDisplay",
                        "LutView",
                        latticeSize: 16))));

            // Restoring the original LUT restores the original colour. The identity is
            // deliberately not required to return to its original value: it folds in
            // OpenColorIO's own cache identity, which includes file metadata, so a
            // restored file that is byte-identical but newly created is still a change.
            // Erring towards one extra rebake is the safe direction; the direction that
            // matters is that it never compares equal to the broken state.
            File.WriteAllText(lutPath, CreateScaleLut(0.5f));
            string restoredIdentity = provider.GetIdentity(configPath).Value;
            float restored = ApplyExternalLut(configPath, 1f);

            await Assert.That(restoredIdentity).IsNotEqualTo(deletedIdentity);
            await Assert.That(restoredIdentity).IsNotEqualTo(secondIdentity);
            await Assert.That(restored).IsEqualTo(first).Within(Tolerance);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task LatticeCache_RebakesWhenAReferencedLutChanges()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-lut-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string configPath = Path.Combine(root, "config.ocio");
        string lutPath = Path.Combine(root, "scale.spi1d");
        try
        {
            File.WriteAllText(configPath, ExternalLutConfig);
            File.WriteAllText(lutPath, CreateScaleLut(0.5f));

            var cache = new SilkDisplayTransformLatticeCache(
                identityProvider: new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero));
            var transform = new RenderDisplayTransform(
                configPath,
                "linear",
                "LutDisplay",
                "LutView",
                latticeSize: 16);

            SilkDisplayTransformLattice first = cache.Get(transform);
            await Assert.That(cache.Get(transform)).IsSameReferenceAs(first);

            File.WriteAllText(lutPath, CreateScaleLut(0.25f));
            SilkDisplayTransformLattice second = cache.Get(transform);

            await Assert.That(cache.Builds).IsEqualTo(2UL);
            await Assert.That(cache.Invalidations).IsEqualTo(1UL);
            await Assert.That(second).IsNotSameReferenceAs(first);
            await Assert.That(second.Rgba8.Span.SequenceEqual(first.Rgba8.Span)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// A config whose display colour space is defined entirely by an external 1D LUT, so
    /// editing that file changes the rendered colour without touching the config.
    /// </summary>
    private const string ExternalLutConfig = """
        ocio_profile_version: 2.0

        environment:
          {}

        search_path: ""

        roles:
          default: linear
          scene_linear: linear

        displays:
          LutDisplay:
            - !<View> {name: LutView, colorspace: lut_display}

        active_displays: [LutDisplay]
        active_views: [LutView]

        colorspaces:
          - !<ColorSpace>
            name: linear
            family: ""
            bitdepth: 32f
            isdata: false
            allocation: uniform

          - !<ColorSpace>
            name: lut_display
            family: ""
            bitdepth: 32f
            isdata: false
            allocation: uniform
            from_scene_reference: !<FileTransform> {src: scale.spi1d, interpolation: linear}
        """;

    [Test]
    public async Task ConfigIdentity_FollowsNestedCtfReferences()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-nested-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            string outerPath = Path.Combine(root, "outer.ctf");
            string innerPath = Path.Combine(root, "inner.ctf");
            File.WriteAllText(configPath, NestedCtfConfig);
            File.WriteAllText(outerPath, OuterReferenceCtf);
            File.WriteAllText(innerPath, CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.Value).IsNotEmpty();
            await Assert.That(first.IsExhaustive).IsTrue();
            float firstColor = ApplyNestedCtf(configPath, 1f);

            // Only the *second* level changes: the config names outer.ctf and outer.ctf
            // names inner.ctf. A digest that stopped at the files the config itself
            // mentions would return the same identity here while the image changed.
            File.WriteAllText(innerPath, CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            float secondColor = ApplyNestedCtf(configPath, 1f);

            await Assert.That(second.IsExhaustive).IsTrue();
            await Assert.That(second.Value).IsNotEqualTo(first.Value);
            await Assert.That(secondColor).IsLessThan(firstColor - 0.05f);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Arguments("plain", "<Reference inBitDepth=\"32f\" outBitDepth=\"32f\" path=\"inner.ctf\"/>")]
    [Arguments(
        "whitespace",
        "<Reference\n        inBitDepth = '32f'\n\n   path\t=\t'inner.ctf'\n  />")]
    [Arguments("case", "<REFERENCE inBitDepth='32f' PATH='inner.ctf'/>")]
    [Arguments(
        "entities",
        "<Reference path=\"&#105;nner&#x2E;ctf\" inBitDepth=\"32f\"/>")]
    [Arguments(
        "unsupported-basepath",
        "<Reference basePath=\"nowhere\" path=\"inner.ctf\" inBitDepth=\"32f\"/>")]
    [Arguments(
        "decoy-comment",
        "<!-- <Reference path=\"absent.ctf\"/> -->\n" +
        "<Description>path=\"absent.ctf\"</Description>\n" +
        "<![CDATA[ <Reference path=\"absent.ctf\"/> ]]>\n" +
        "<ReferenceList xpath=\"absent.ctf\"/>\n" +
        "<Reference path=\"inner.ctf\" inBitDepth=\"32f\"/>")]
    [Arguments(
        "gt-in-value",
        "<Reference alias=\"a &gt; b\" path=\"inner.ctf\" inBitDepth=\"32f\"/>")]
    public async Task ConfigIdentity_ParsesReferenceMarkupRatherThanScanningForIt(
        string variant,
        string referenceMarkup)
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-xml-{variant}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(configPath, NestedCtfConfig);
            File.WriteAllText(
                Path.Combine(root, "outer.ctf"),
                WrapProcessList("outer", referenceMarkup));
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();

            // Editing the nested file must move the identity. A scanner that missed this
            // spelling -- because of whitespace around '=', a different case, an entity
            // in the value, a '>' inside another attribute, or a decoy in a comment --
            // would return the same digest for a file whose contents changed.
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            await Assert.That(second.Value).IsNotEqualTo(first.Value);

            // And an unrelated file that only a decoy named must not be a dependency:
            // adding it changes nothing.
            File.WriteAllText(Path.Combine(root, "absent.ctf"), CreateScaleCtf(0.9f));
            SilkDisplayTransformConfigIdentity third = provider.GetIdentity(configPath);
            await Assert.That(third.Value).IsEqualTo(second.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_ResolvesNestedReferencesThroughTheContextSearchPath()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-searchpath-{Guid.NewGuid():N}");
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");

            // Two search directories, in order. outer.ctf lives only in the *second*
            // one, and names "inner.ctf" with no directory at all. There is an inner.ctf
            // in both, so resolution order decides which file is read: through the
            // config's unchanged context OpenColorIO finds the one in the first
            // directory. Prepending outer.ctf's own directory would find the sibling
            // decoy in the second directory instead, and this test would fail on both
            // the rendered value and the identity.
            File.WriteAllText(
                configPath,
                NestedCtfConfig.Replace(
                    "search_path: \"\"",
                    "search_path:\n  - first\n  - second",
                    StringComparison.Ordinal));
            File.WriteAllText(
                Path.Combine(second, "outer.ctf"),
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>"));
            File.WriteAllText(Path.Combine(first, "inner.ctf"), CreateScaleCtf(0.5f));
            File.WriteAllText(Path.Combine(second, "inner.ctf"), CreateScaleCtf(0.95f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity original = provider.GetIdentity(configPath);
            await Assert.That(original.IsExhaustive).IsTrue();
            float firstColor = ApplyNestedCtf(configPath, 1f);

            // The sibling decoy is never opened by OpenColorIO, so editing it must move
            // neither the image nor the identity.
            File.WriteAllText(Path.Combine(second, "inner.ctf"), CreateScaleCtf(0.05f));
            SilkDisplayTransformConfigIdentity decoyed = provider.GetIdentity(configPath);
            float decoyedColor = ApplyNestedCtf(configPath, 1f);
            await Assert.That(decoyedColor).IsEqualTo(firstColor).Within(Tolerance);
            await Assert.That(decoyed.Value).IsEqualTo(original.Value);

            // The file the search path actually selects moves both.
            File.WriteAllText(Path.Combine(first, "inner.ctf"), CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity moved = provider.GetIdentity(configPath);
            float movedColor = ApplyNestedCtf(configPath, 1f);
            await Assert.That(movedColor).IsLessThan(firstColor - 0.05f);
            await Assert.That(moved.Value).IsNotEqualTo(original.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_SurvivesAReferenceCycleAndStillReachesTheLeaf()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-cycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(configPath, NestedCtfConfig);

            // outer.ctf -> mid.ctf -> outer.ctf, and mid.ctf also names a leaf. Every
            // name resolves through the config's own context, which is the only way
            // OpenColorIO would resolve them. The cycle must terminate through the
            // canonical visited set rather than walking forever, and the leaf must still
            // be part of the identity.
            File.WriteAllText(
                Path.Combine(root, "outer.ctf"),
                WrapProcessList(
                    "outer",
                    "<Reference path=\"mid.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>"));
            File.WriteAllText(
                Path.Combine(root, "mid.ctf"),
                WrapProcessList(
                    "mid",
                    "<Reference path=\"outer.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>" +
                    "<Reference path=\"leaf.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>"));
            File.WriteAllText(Path.Combine(root, "leaf.ctf"), CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();
            await Assert.That(first.Value).IsNotEmpty();

            File.WriteAllText(Path.Combine(root, "leaf.ctf"), CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            await Assert.That(second.Value).IsNotEqualTo(first.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_FollowsTheSameNamedFileOpenColorIoActuallyOpens()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-samename-{Guid.NewGuid():N}");
        string luts = Path.Combine(root, "luts");
        Directory.CreateDirectory(luts);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(
                configPath,
                NestedCtfConfig.Replace(
                    "search_path: \"\"",
                    "search_path: luts",
                    StringComparison.Ordinal));

            // outer.ctf lives on the search path and names "inner.ctf" with no directory.
            // There are two files with that name: one beside outer.ctf on the search
            // path, and a decoy in the config's own directory. OpenColorIO resolves the
            // reference through the unchanged config context, so it opens the search-path
            // one. A walk that prepended the referencing document's directory would
            // happen to agree here, but one that resolved against the config directory --
            // or that preferred a sibling -- would hash the decoy and stop tracking the
            // image.
            File.WriteAllText(
                Path.Combine(luts, "outer.ctf"),
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>"));
            File.WriteAllText(Path.Combine(luts, "inner.ctf"), CreateScaleCtf(0.5f));
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.9f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            float firstColor = ApplyNestedCtf(configPath, 1f);
            await Assert.That(first.IsExhaustive).IsTrue();

            // Editing the decoy changes nothing OpenColorIO reads, so it must change
            // neither the rendered value nor the identity.
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.1f));
            SilkDisplayTransformConfigIdentity decoyed = provider.GetIdentity(configPath);
            float decoyedColor = ApplyNestedCtf(configPath, 1f);
            await Assert.That(decoyedColor).IsEqualTo(firstColor).Within(Tolerance);
            await Assert.That(decoyed.Value).IsEqualTo(first.Value);

            // Editing the file OpenColorIO does open changes both.
            File.WriteAllText(Path.Combine(luts, "inner.ctf"), CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity moved = provider.GetIdentity(configPath);
            float movedColor = ApplyNestedCtf(configPath, 1f);
            await Assert.That(movedColor).IsLessThan(firstColor - 0.05f);
            await Assert.That(moved.Value).IsNotEqualTo(first.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Arguments(
        "doctype-internal-entity",
        "<!DOCTYPE ProcessList [<!ENTITY leaf \"inner.ctf\">]>\n" +
        "<ProcessList version=\"1.3\" id=\"outer\">\n" +
        "<Reference path=\"&leaf;\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>\n" +
        "</ProcessList>\n")]
    [Arguments(
        "unknown-entity",
        "<ProcessList version=\"1.3\" id=\"outer\">\n" +
        "<Reference path=\"&leaf;\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>\n" +
        "</ProcessList>\n")]
    [Arguments(
        "namespace-prefixed",
        "<ctf:ProcessList xmlns:ctf=\"urn:ctf\" version=\"1.3\" id=\"outer\">\n" +
        "<ctf:Reference path=\"inner.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>\n" +
        "</ctf:ProcessList>\n")]
    [Arguments(
        "unbalanced",
        "<ProcessList version=\"1.3\" id=\"outer\">\n" +
        "<Reference path=\"inner.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>\n")]
    [Arguments(
        "mismatched-end-tag",
        "<ProcessList version=\"1.3\" id=\"outer\">\n" +
        "<Info>text</Description>\n" +
        "</ProcessList>\n")]
    [Arguments("not-a-process-list", "<Something version=\"1.3\"/>\n")]
    public async Task ConfigIdentity_RefusesRatherThanGuessingAtUnsupportedMarkup(
        string variant,
        string document)
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-refuse-{variant}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(configPath, NestedCtfConfig);
            File.WriteAllText(
                Path.Combine(root, "outer.ctf"),
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document);
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity identity = provider.GetIdentity(configPath);

            // Constructs this reader will not interpret must never be reported as an
            // exhaustive view of the file's dependencies. Guessing produces a stale
            // image; refusing produces a named diagnostic.
            await Assert.That(identity.IsExhaustive).IsFalse();

            var cache = new SilkDisplayTransformLatticeCache(
                identityProvider: new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero));
            SilkDisplayTransformException refused =
                Assert.Throws<SilkDisplayTransformException>(() => cache.Get(
                    new RenderDisplayTransform(
                        configPath,
                        "linear",
                        "CtfDisplay",
                        "CtfView",
                        latticeSize: 16)));
            await Assert.That(refused.Status)
                .IsEqualTo(SilkDisplayTransformStatus.TransformUnsupported);
            await Assert.That(refused.Message).IsNotEmpty();
            await Assert.That(cache.Builds).IsEqualTo(0UL);
            await Assert.That(cache.PartialIdentityRefusals).IsEqualTo(1UL);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_IgnoresAReferenceThatIsNotAProcessListChild()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-decoy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(configPath, NestedCtfConfig);

            // The nested <Reference> inside <Info> is metadata, not an op: OpenColorIO
            // never opens it, so neither may the walk. Only the direct child counts.
            File.WriteAllText(
                Path.Combine(root, "outer.ctf"),
                WrapProcessList(
                    "outer",
                    "<Info><Reference path=\"decoy.ctf\"/></Info>\n" +
                    "<Reference path=\"inner.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>"));
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.5f));
            File.WriteAllText(Path.Combine(root, "decoy.ctf"), CreateScaleCtf(0.9f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();

            File.WriteAllText(Path.Combine(root, "decoy.ctf"), CreateScaleCtf(0.1f));
            await Assert.That(provider.GetIdentity(configPath).Value).IsEqualTo(first.Value);

            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.25f));
            await Assert.That(provider.GetIdentity(configPath).Value)
                .IsNotEqualTo(first.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_DetectsAProcessListRegardlessOfItsFileName()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-extension-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // The config names "outer.lut". OpenColorIO falls back to trying its readers
            // when an extension does not identify a format, so this really is loaded as
            // a process list and really does reference inner.ctf. A walk that decided
            // "referencing format" from the extension called the identity exhaustive
            // while ignoring the reference entirely.
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(
                configPath,
                NestedCtfConfig.Replace("outer.ctf", "outer.lut", StringComparison.Ordinal));
            File.WriteAllText(
                Path.Combine(root, "outer.lut"),
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>"));
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();
            float firstColor = ApplyNestedCtf(configPath, 1f);

            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            float secondColor = ApplyNestedCtf(configPath, 1f);

            await Assert.That(second.Value).IsNotEqualTo(first.Value);
            await Assert.That(secondColor).IsLessThan(firstColor - 0.05f);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_FollowsAProcessListReachedThroughASymbolicLink()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-alias-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(configPath, NestedCtfConfig);

            // The config names "outer.ctf". That name is a symbolic link to a document
            // stored under a name that identifies no format at all, so the alias and its
            // target disagree about what the file is. Only content detection reaches the
            // reference through it, and only canonicalization collapses the two names to
            // one dependency.
            string target = Path.Combine(root, "process-list.data");
            File.WriteAllText(
                target,
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner.ctf\" inBitDepth=\"32f\" outBitDepth=\"32f\"/>"));
            string alias = Path.Combine(root, "outer.ctf");
            RequireSymbolicLink(target, alias);
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();
            float firstColor = ApplyNestedCtf(configPath, 1f);

            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            float secondColor = ApplyNestedCtf(configPath, 1f);

            await Assert.That(second.Value).IsNotEqualTo(first.Value);
            await Assert.That(secondColor).IsLessThan(firstColor - 0.05f);

            // Editing through the target changes the identity too: the alias and its
            // target are one file, canonicalized to one entry.
            File.WriteAllText(
                target,
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner.ctf\" inBitDepth=\"32f\" " +
                    "outBitDepth=\"32f\"/><!-- edited -->"));
            await Assert.That(provider.GetIdentity(configPath).Value)
                .IsNotEqualTo(second.Value);

            WriteAliasEvidence("executed", "symlink", "alias resolved and followed");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Creates the symbolic link the alias test needs, or skips only where the platform
    /// genuinely refuses.
    /// </summary>
    /// <remarks>
    /// Substituting a copy would keep the test green while proving nothing about aliases,
    /// so it is never done. Windows requires a privilege an ordinary developer session
    /// does not have, and that is the one place a skip is honest; Linux grants unprivileged
    /// symbolic links, so a failure there is a real failure and the promotion gate
    /// requires this test to have executed.
    /// </remarks>
    private static void RequireSymbolicLink(string target, string alias)
    {
        try
        {
            _ = File.CreateSymbolicLink(alias, target);
        }
        catch (Exception exception) when (
            !OperatingSystem.IsLinux() &&
            exception is IOException or UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            WriteAliasEvidence("skipped", "none", exception.Message);
            Skip.Test(
                "This host does not permit creating a symbolic link: " +
                exception.Message);
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }

    /// <summary>
    /// Records whether the alias evidence was actually produced, so a promotion gate can
    /// require execution rather than accepting a skip.
    /// </summary>
    private static void WriteAliasEvidence(string status, string mechanism, string reason)
    {
        string path = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            "ocio-alias-evidence.txt");
        File.WriteAllText(
            path,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"status={status}\nmechanism={mechanism}\nos={Environment.OSVersion.Platform}\nreason={reason}\n"));
    }

    [Test]
    public async Task ConfigIdentity_NormalizesLiteralWhitespaceInAReferencePath()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-attrws-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(configPath, NestedCtfConfig);

            // The reference is written across a line break. XML attribute-value
            // normalization turns that line feed into a single space, so the name
            // OpenColorIO opens is "inner two.ctf" -- with a space, not a newline. A
            // reader that skipped normalization looked for a file whose name contained a
            // line feed, found nothing, and reported an identity that tracked no file.
            File.WriteAllText(
                Path.Combine(root, "outer.ctf"),
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner\ntwo.ctf\" inBitDepth=\"32f\" " +
                    "outBitDepth=\"32f\"/>"));
            string spaced = Path.Combine(root, "inner two.ctf");
            File.WriteAllText(spaced, CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();
            float firstColor = ApplyNestedCtf(configPath, 1f);

            File.WriteAllText(spaced, CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            float secondColor = ApplyNestedCtf(configPath, 1f);

            await Assert.That(secondColor).IsLessThan(firstColor - 0.05f);
            await Assert.That(second.Value).IsNotEqualTo(first.Value);

            // A tab is normalized the same way, and so is a carriage return pair.
            File.WriteAllText(
                Path.Combine(root, "outer.ctf"),
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner\ttwo.ctf\" inBitDepth=\"32f\" " +
                    "outBitDepth=\"32f\"/>"));
            SilkDisplayTransformConfigIdentity tabbed = provider.GetIdentity(configPath);
            await Assert.That(tabbed.IsExhaustive).IsTrue();
            File.WriteAllText(spaced, CreateScaleCtf(0.125f));
            await Assert.That(provider.GetIdentity(configPath).Value)
                .IsNotEqualTo(tabbed.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_ReadsReferencesUnderNonAsciiDirectories()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-\u00fcml\u00e4ut-\u65e5\u672c-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            File.WriteAllText(configPath, NestedCtfConfig);
            File.WriteAllText(
                Path.Combine(root, "outer.ctf"),
                WrapProcessList(
                    "outer",
                    "<Reference path=\"inner.ctf\" inBitDepth=\"32f\"/>"));
            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.5f));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();

            File.WriteAllText(Path.Combine(root, "inner.ctf"), CreateScaleCtf(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            await Assert.That(second.Value).IsNotEqualTo(first.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ConfigIdentity_CostsExactlyOneDependencyWalkPerRevalidation()
    {
        RequireNativeOcio();
        var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);

        // Warm the path once so first-use costs are not attributed to the measurement.
        _ = provider.GetIdentity(DisplayConfigPath);

        ulong walksBefore = SilkOpenColorIoConfigIdentityProvider.NativeDependencyWalks;
        long callsBefore = SilkOpenColorIoConfigIdentityProvider.NativeIdentityCalls;
        await Assert.That(walksBefore).IsGreaterThan(0UL);

        const int revalidations = 5;
        for (int index = 0; index < revalidations; index++)
        {
            _ = provider.GetIdentity(DisplayConfigPath);
        }

        // The digest is a fixed-format string, so sizing a buffer with a throwaway call
        // is never necessary -- and doing it walked and hashed every referenced file
        // twice for every single revalidation.
        await Assert.That(
            SilkOpenColorIoConfigIdentityProvider.NativeIdentityCalls - callsBefore)
            .IsEqualTo((long)revalidations);
        await Assert.That(
            SilkOpenColorIoConfigIdentityProvider.NativeDependencyWalks - walksBefore)
            .IsEqualTo((ulong)revalidations);
    }

    [Test]
    public async Task ConfigIdentity_CachedLookupsCostNoWalkAtAll()
    {
        RequireNativeOcio();
        long timestamp = 0;
        var provider = new SilkOpenColorIoConfigIdentityProvider(
            TimeSpan.FromMilliseconds(250),
            () => timestamp);
        _ = provider.GetIdentity(DisplayConfigPath);

        ulong walksBefore = SilkOpenColorIoConfigIdentityProvider.NativeDependencyWalks;
        for (int index = 0; index < 8; index++)
        {
            _ = provider.GetIdentity(DisplayConfigPath);
        }

        await Assert.That(SilkOpenColorIoConfigIdentityProvider.NativeDependencyWalks)
            .IsEqualTo(walksBefore);
    }

    private static string WrapProcessList(string id, string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!-- a leading comment -->\n" +
        $"<ProcessList version=\"1.3\" id=\"{id}\">\n" +
        body +
        "\n</ProcessList>\n";

    [Test]
    public async Task ConfigIdentity_DeduplicatesAliasedPathsBeforeTheFileLimit()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            string lutPath = Path.Combine(root, "scale.spi1d");
            File.WriteAllText(lutPath, CreateScaleLut(0.5f));

            // Three hundred colour spaces, every one of them naming the same LUT through
            // a different spelling. Deduplicating by canonical path before the 256-file
            // bound is what keeps this exhaustive; counting references would exhaust the
            // bound and silently downgrade the identity to partial.
            File.WriteAllText(configPath, CreateAliasedLutConfig(300));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity first = provider.GetIdentity(configPath);
            await Assert.That(first.IsExhaustive).IsTrue();
            await Assert.That(first.Value).IsNotEmpty();

            File.WriteAllText(lutPath, CreateScaleLut(0.25f));
            SilkDisplayTransformConfigIdentity second = provider.GetIdentity(configPath);
            await Assert.That(second.IsExhaustive).IsTrue();
            await Assert.That(second.Value).IsNotEqualTo(first.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task LatticeCache_RefusesAConfigWhoseDependenciesExceedTheWalk()
    {
        RequireNativeOcio();
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"ocio-partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.ocio");
            const int lutCount = 300;
            for (int index = 0; index < lutCount; index++)
            {
                File.WriteAllText(
                    Path.Combine(root, $"scale{index}.spi1d"),
                    CreateScaleLut(0.5f));
            }
            File.WriteAllText(configPath, CreateDistinctLutConfig(lutCount));

            var provider = new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero);
            SilkDisplayTransformConfigIdentity identity = provider.GetIdentity(configPath);

            // More distinct files than the bounded walk will hash. The identity is
            // honest about that rather than pretending to be complete.
            await Assert.That(identity.IsExhaustive).IsFalse();

            var cache = new SilkDisplayTransformLatticeCache(
                identityProvider: new SilkOpenColorIoConfigIdentityProvider(TimeSpan.Zero));
            var transform = new RenderDisplayTransform(
                configPath,
                "linear",
                "LutDisplay",
                "LutView0",
                latticeSize: 16);

            // The decisive part: a partial identity must not authorize a retained hit,
            // and must not be answered with a success-shaped lattice either. Editing a
            // file the walk never reached would otherwise be invisible forever.
            SilkDisplayTransformException refused =
                Assert.Throws<SilkDisplayTransformException>(() => cache.Get(transform));
            await Assert.That(refused.Status)
                .IsEqualTo(SilkDisplayTransformStatus.TransformUnsupported);
            _ = Assert.Throws<SilkDisplayTransformException>(() => cache.Get(transform));

            await Assert.That(cache.PartialIdentityRefusals).IsEqualTo(2UL);
            await Assert.That(cache.Builds).IsEqualTo(0UL);
            await Assert.That(cache.Hits).IsEqualTo(0UL);

            // And it is not a negative cache entry either: a refusal that suppressed
            // retries would make the config permanently unusable even after it shrank.
            await Assert.That(cache.SuppressedRetries).IsEqualTo(0UL);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateAliasedLutConfig(int aliasCount)
    {
        var builder = new System.Text.StringBuilder();
        _ = builder.Append(AliasedConfigHeader);
        for (int index = 0; index < aliasCount; index++)
        {
            string spelling = index switch
            {
                _ when index % 3 == 0 => "scale.spi1d",
                _ when index % 3 == 1 => "./scale.spi1d",
                _ => "./././scale.spi1d",
            };
            _ = builder.Append(System.Globalization.CultureInfo.InvariantCulture, $$"""

  - !<ColorSpace>
    name: lut_display{{index}}
    bitdepth: 32f
    isdata: false
    allocation: uniform
    from_scene_reference: !<FileTransform> {src: {{spelling}}, interpolation: linear}
""");
        }

        return builder.ToString();
    }

    private static string CreateDistinctLutConfig(int lutCount)
    {
        var builder = new System.Text.StringBuilder();
        _ = builder.Append("""
ocio_profile_version: 2.0

search_path: ""

roles:
  default: linear
  scene_linear: linear

displays:
  LutDisplay:
""");
        for (int index = 0; index < lutCount; index++)
        {
            _ = builder.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"\n    - !<View> {{name: LutView{index}, colorspace: lut_display{index}}}");
        }

        _ = builder.Append("""


active_displays: [LutDisplay]

colorspaces:
  - !<ColorSpace>
    name: linear
    bitdepth: 32f
    isdata: false
    allocation: uniform
""");
        for (int index = 0; index < lutCount; index++)
        {
            _ = builder.Append(System.Globalization.CultureInfo.InvariantCulture, $$"""

  - !<ColorSpace>
    name: lut_display{{index}}
    bitdepth: 32f
    isdata: false
    allocation: uniform
    from_scene_reference: !<FileTransform> {src: scale{{index}}.spi1d, interpolation: linear}
""");
        }

        return builder.ToString();
    }

    private const string AliasedConfigHeader = """
ocio_profile_version: 2.0

search_path: ""

roles:
  default: linear
  scene_linear: linear

displays:
  LutDisplay:
    - !<View> {name: LutView, colorspace: lut_display0}

active_displays: [LutDisplay]

colorspaces:
  - !<ColorSpace>
    name: linear
    bitdepth: 32f
    isdata: false
    allocation: uniform
""";

    private const string NestedCtfConfig = """
        ocio_profile_version: 2.0

        search_path: ""

        roles:
          default: linear
          scene_linear: linear

        displays:
          CtfDisplay:
            - !<View> {name: CtfView, colorspace: ctf_display}

        active_displays: [CtfDisplay]
        active_views: [CtfView]

        colorspaces:
          - !<ColorSpace>
            name: linear
            bitdepth: 32f
            isdata: false
            allocation: uniform

          - !<ColorSpace>
            name: ctf_display
            bitdepth: 32f
            isdata: false
            allocation: uniform
            from_scene_reference: !<FileTransform> {src: outer.ctf}
        """;

    private const string OuterReferenceCtf = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ProcessList version="1.3" id="outer">
            <Reference inBitDepth="32f" outBitDepth="32f" path="inner.ctf"/>
        </ProcessList>
        """;

    private static string CreateScaleCtf(float scale) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ProcessList version="1.3" id="inner">
                <LUT1D inBitDepth="32f" outBitDepth="32f">
                    <Array dim="2 1">
            0.0
            {scale:F6}
                    </Array>
                </LUT1D>
            </ProcessList>
            """);

    private static float ApplyNestedCtf(string configPath, float value)
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            configPath,
            "linear",
            "CtfDisplay",
            "CtfView");
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
        byte[] source = new byte[16];
        Span<float> channels = MemoryMarshal.Cast<byte, float>(source.AsSpan());
        channels[0] = value;
        channels[1] = value;
        channels[2] = value;
        channels[3] = 1f;
        byte[] destination = new byte[4];
        processor.ApplyLinearFloat(source, destination, 1, 1, 0f);
        return destination[0] / 255f;
    }

    private static string CreateScaleLut(float scale) =>
        "Version 1\nFrom 0.000000 1.000000\nLength 2\nComponents 1\n{\n0.000000\n" +
        scale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) +
        "\n}\n";

    private static float ApplyExternalLut(string configPath, float value)
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            configPath,
            "linear",
            "LutDisplay",
            "LutView");
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
        byte[] source = new byte[16];
        Span<float> channels = MemoryMarshal.Cast<byte, float>(source.AsSpan());
        channels[0] = value;
        channels[1] = value;
        channels[2] = value;
        channels[3] = 1f;
        byte[] destination = new byte[4];
        processor.ApplyLinearFloat(source, destination, 1, 1, 0f);
        return destination[0] / 255f;
    }

    private static float ApplyLook(
        string configPath,
        string view,
        string? look,
        float value)
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            configPath,
            "linear",
            "LookDisplay",
            view,
            look);
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
        byte[] source = new byte[16];
        Span<float> channels = MemoryMarshal.Cast<byte, float>(source.AsSpan());
        channels[0] = value;
        channels[1] = value;
        channels[2] = value;
        channels[3] = 1f;
        byte[] destination = new byte[4];
        processor.ApplyLinearFloat(source, destination, 1, 1, 0f);
        return destination[0] / 255f;
    }

    private static string ResolveAsset(string fileName)
    {
        string root = FindRepositoryRoot() ??
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "test-assets", fileName);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }

    private static void RequireNativeOcio()
    {
        if (!File.Exists(LookConfigPath) || !File.Exists(DisplayConfigPath))
        {
            Skip.Test("The OpenColorIO test configs are unavailable.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        try
        {
            var transform = new SilkOpenColorIoDisplayTransform(DisplayConfigPath, "linear");
            using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }
}
