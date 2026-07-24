// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class RendererSessionOwnershipTests
{
    [Test]
    public async Task StormRendererIsExplicitNonFinalizableOwnership()
    {
        Type type = typeof(OpenUsdStormRenderer);

        await Assert.That(typeof(SafeHandle).IsAssignableFrom(type)).IsFalse();
        await Assert.That(
            type.GetMethod(
                "Finalize",
                BindingFlags.Instance | BindingFlags.NonPublic)?.DeclaringType)
            .IsNotEqualTo(type);
    }

    [Test]
    public async Task StormWrongThreadChecksRunBeforeNativeCalls()
    {
        var renderer = new OpenUsdStormRenderer(
            (nint)1,
            "cached-name",
            _ => { },
            _ => { },
            () => { });

        Exception? renderError = null;
        Exception? disposeError = null;
        Exception? abandonError = null;
        Exception? detachError = null;
        Exception? pickError = null;
        Exception? selectionError = null;
        string? name = null;
        var thread = new Thread(
            () =>
            {
                name = renderer.Name;
                renderError = Capture(
                    () => renderer.Render(1, 1, 0, camera: CameraState.Default));
                pickError = Capture(() => renderer.Pick(new RenderPickRequest(
                    0,
                    0,
                    new ViewportDimensions(1, 1),
                    requestedStateRevision: 0)));
                selectionError = Capture(() => renderer.SetSelection(
                    SelectionState.Empty,
                    Vector4.One));
                disposeError = Capture(renderer.Dispose);
                abandonError = Capture(renderer.Abandon);
                detachError = Capture(renderer.ReleaseAfterDetach);
            });
        thread.Start();
        thread.Join();

        await Assert.That(name).IsEqualTo("cached-name");
        await Assert.That(renderError).IsTypeOf<InvalidOperationException>();
        await Assert.That(disposeError).IsTypeOf<InvalidOperationException>();
        await Assert.That(abandonError).IsTypeOf<InvalidOperationException>();
        await Assert.That(detachError).IsTypeOf<InvalidOperationException>();
        await Assert.That(pickError).IsTypeOf<InvalidOperationException>();
        await Assert.That(selectionError).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task StormAbandonReleasesNativeAndManagedBookkeeping()
    {
        int nativeAbandonCount = 0;
        int leaseReleaseCount = 0;
        var renderer = new OpenUsdStormRenderer(
            (nint)42,
            "cached-name",
            _ => throw new InvalidOperationException(),
            handle =>
            {
                if (handle == (nint)42)
                {
                    nativeAbandonCount++;
                }
            },
            () => leaseReleaseCount++);

        renderer.Abandon();
        renderer.Abandon();
        Exception? renderError = Capture(
            () => renderer.Render(1, 1, 0, camera: CameraState.Default));

        await Assert.That(nativeAbandonCount).IsEqualTo(1);
        await Assert.That(leaseReleaseCount).IsEqualTo(1);
        await Assert.That(renderError).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task StormDetachReleasesNativeAndManagedBookkeepingOnce()
    {
        int nativeDetachCount = 0;
        int leaseReleaseCount = 0;
        var renderer = new OpenUsdStormRenderer(
            (nint)7,
            "cached-name",
            _ => throw new InvalidOperationException(),
            _ => nativeDetachCount++,
            () => leaseReleaseCount++);

        renderer.ReleaseAfterDetach();
        renderer.ReleaseAfterDetach();
        Exception? renderError = Capture(
            () => renderer.Render(1, 1, 0, camera: CameraState.Default));

        await Assert.That(nativeDetachCount).IsEqualTo(1);
        await Assert.That(leaseReleaseCount).IsEqualTo(1);
        await Assert.That(renderError).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task StormDetachFailurePreservesLeaseForRetry()
    {
        int detachAttempts = 0;
        int leaseReleaseCount = 0;
        var renderer = new OpenUsdStormRenderer(
            (nint)8,
            "cached-name",
            _ => throw new InvalidOperationException(),
            _ =>
            {
                detachAttempts++;
                if (detachAttempts == 1)
                {
                    throw new OpenUsdStormException(
                        OpenUsd.Interop.OpenUsdNativeStatus.NativeError,
                        "stage access failed");
                }
            },
            () => leaseReleaseCount++);

        Exception? firstError = Capture(renderer.ReleaseAfterDetach);
        renderer.ReleaseAfterDetach();
        renderer.ReleaseAfterDetach();

        await Assert.That(firstError).IsTypeOf<OpenUsdStormException>();
        await Assert.That(detachAttempts).IsEqualTo(2);
        await Assert.That(leaseReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task StormDetachReleasesChildBeforeSchedulerDisposal()
    {
        bool childRegistered = true;
        var renderer = new OpenUsdStormRenderer(
            (nint)9,
            "cached-name",
            _ => throw new InvalidOperationException(),
            _ => { },
            () => childRegistered = false);

        renderer.ReleaseAfterDetach();
        Exception? schedulerError = Capture(
            () =>
            {
                if (childRegistered)
                {
                    throw new InvalidOperationException("active child");
                }
            });

        await Assert.That(schedulerError).IsNull();
    }

    [Test]
    public async Task StormNormalDisposeDoesNotUseDetachPath()
    {
        int destroyCount = 0;
        int detachCount = 0;
        int leaseReleaseCount = 0;
        var renderer = new OpenUsdStormRenderer(
            (nint)10,
            "cached-name",
            _ => destroyCount++,
            _ => detachCount++,
            () => leaseReleaseCount++);

        renderer.Dispose();
        renderer.Dispose();

        await Assert.That(destroyCount).IsEqualTo(1);
        await Assert.That(detachCount).IsEqualTo(0);
        await Assert.That(leaseReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task RapidStormDetachReleasesEveryChildExactlyOnce()
    {
        int nativeDetachCount = 0;
        int leaseReleaseCount = 0;
        for (int index = 0; index < 64; index++)
        {
            var renderer = new OpenUsdStormRenderer(
                (nint)(index + 1),
                "cached-name",
                _ => throw new InvalidOperationException(),
                _ => nativeDetachCount++,
                () => leaseReleaseCount++);
            renderer.ReleaseAfterDetach();
            renderer.ReleaseAfterDetach();
        }

        await Assert.That(nativeDetachCount).IsEqualTo(64);
        await Assert.That(leaseReleaseCount).IsEqualTo(64);
    }

    [Test]
    public async Task SilkPageCommandsRemainValidAfterPageDisposal()
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(0, 4),
            (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 16);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), 42);
        var page = (OpenUsdSilkPage)Activator.CreateInstance(
            typeof(OpenUsdSilkPage),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [SilkCommandParser.PageAbiVersion, 7ul, bytes, 1u],
            culture: null)!;

        SilkCommandEnumerator commands = page.GetEnumerator();
        page.Dispose();
        bool moved = commands.MoveNext();
        SilkCommandType type = commands.Current.Type;
        commands.Dispose();

        await Assert.That(moved).IsTrue();
        await Assert.That(type).IsEqualTo(SilkCommandType.MeshRemove);
        await Assert.That(
            typeof(OpenUsdSilkPage).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(field => typeof(SafeHandle).IsAssignableFrom(field.FieldType)))
            .IsFalse();
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
