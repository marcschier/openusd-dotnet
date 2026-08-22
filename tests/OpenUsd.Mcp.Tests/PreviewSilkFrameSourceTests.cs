// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class PreviewSilkFrameSourceTests
{
    [Test]
    public async Task DisposesNativeResourcesInRequiredDependencyOrder()
    {
        var order = new List<string>();
        PreviewSilkFrameSource source = CreateFrameSource(order);

        source.Dispose();
        source.Dispose();

        await Assert.That(string.Join(",", order))
            .IsEqualTo("capturer,session,device,source");
        await Assert.That(() => source.Capture(new CaptureView("view", default), 1, 1))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments("capturer")]
    [Arguments("session")]
    [Arguments("device")]
    [Arguments("source")]
    public async Task DisposalFailureRetainsOnlyFailedResourceForRetry(string failingResource)
    {
        var order = new List<string>();
        Dictionary<string, ThrowingDisposable> resources = CreateResources(order);
        resources[failingResource].FailuresRemaining = 1;
        PreviewSilkFrameSource source = CreateFrameSource(resources);

        Exception failure = CaptureException(source.Dispose);

        await Assert.That(failure).IsTypeOf<AggregateException>();
        var aggregate = (AggregateException)failure;
        await Assert.That(aggregate.InnerExceptions).Count().IsEqualTo(1);
        await Assert.That(aggregate.InnerExceptions[0].Message)
            .IsEqualTo($"{failingResource} failed");
        await Assert.That(string.Join(",", order))
            .IsEqualTo("capturer,session,device,source");
        await Assert.That(
                () => source.Capture(new CaptureView("view", default), 1, 1))
            .Throws<ObjectDisposedException>();

        source.Dispose();
        source.Dispose();

        await Assert.That(string.Join(",", order))
            .IsEqualTo($"capturer,session,device,source,{failingResource}");
        await Assert.That(resources.Values.All(static resource => resource.Disposed))
            .IsTrue();
    }

    [Test]
    public async Task DisposalAggregatesFailuresAndRetriesThemInDependencyOrder()
    {
        var order = new List<string>();
        Dictionary<string, ThrowingDisposable> resources = CreateResources(order);
        resources["capturer"].FailuresRemaining = 1;
        resources["session"].FailuresRemaining = 1;
        PreviewSilkFrameSource source = CreateFrameSource(resources);

        Exception failure = CaptureException(source.Dispose);

        await Assert.That(failure).IsTypeOf<AggregateException>();
        var aggregate = (AggregateException)failure;
        await Assert.That(aggregate.InnerExceptions.Select(static item => item.Message))
            .IsEquivalentTo(["capturer failed", "session failed"]);
        await Assert.That(string.Join(",", order))
            .IsEqualTo("capturer,session,device,source");

        source.Dispose();

        await Assert.That(string.Join(",", order))
            .IsEqualTo("capturer,session,device,source,capturer,session");
        await Assert.That(resources.Values.All(static resource => resource.Disposed))
            .IsTrue();
    }

    [Test]
    [Arguments("source", "")]
    [Arguments("device", "source")]
    [Arguments("session", "device,source")]
    [Arguments("capturer", "session,device,source")]
    [Arguments("frame-source", "capturer,session,device,source")]
    public async Task ConstructionFailureCleansCreatedResources(
        string failurePosition,
        string expectedCleanupOrder)
    {
        var order = new List<string>();
        Dictionary<string, ThrowingDisposable> resources = CreateResources(order);
        var constructionFailure = new InvalidOperationException(
            $"{failurePosition} construction failed");

        Exception failure = CaptureException(() =>
            _ = PreviewSilkFrameSourceFactory.CreateCore(
                () => createOrThrow("source"),
                () => createOrThrow("device"),
                _ => createOrThrow("session"),
                _ => createOrThrow("capturer"),
                (_, _, _, _) => failurePosition == "frame-source"
                    ? throw constructionFailure
                    : throw new InvalidOperationException("Unexpected construction completion.")));

        await Assert.That(failure).IsSameReferenceAs(constructionFailure);
        await Assert.That(string.Join(",", order)).IsEqualTo(expectedCleanupOrder);

        ThrowingDisposable createOrThrow(string position) =>
            failurePosition == position
                ? throw constructionFailure
                : resources[position];
    }

    [Test]
    public async Task ConstructionFailureAggregatesAllCleanupFailures()
    {
        var order = new List<string>();
        Dictionary<string, ThrowingDisposable> resources = CreateResources(order);
        foreach (ThrowingDisposable resource in resources.Values)
        {
            resource.FailuresRemaining = 1;
        }
        var constructionFailure = new InvalidOperationException("construction failed");

        Exception failure = CaptureException(() =>
            _ = PreviewSilkFrameSourceFactory.CreateCore(
                () => resources["source"],
                () => resources["device"],
                _ => resources["session"],
                _ => resources["capturer"],
                (_, _, _, _) => throw constructionFailure));

        await Assert.That(failure).IsTypeOf<AggregateException>();
        var aggregate = (AggregateException)failure;
        await Assert.That(aggregate.InnerExceptions).Count().IsEqualTo(5);
        await Assert.That(aggregate.InnerExceptions[0])
            .IsSameReferenceAs(constructionFailure);
        await Assert.That(
                aggregate.InnerExceptions.Skip(1).Select(static item => item.Message))
            .IsEquivalentTo(
            [
                "capturer failed",
                "session failed",
                "device failed",
                "source failed",
            ]);
        await Assert.That(string.Join(",", order))
            .IsEqualTo("capturer,session,device,source");
    }

    private static Exception CaptureException(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The operation did not throw.");
    }

    private static PreviewSilkFrameSource CreateFrameSource(List<string> order) =>
        CreateFrameSource(CreateResources(order));

    private static PreviewSilkFrameSource CreateFrameSource(
        IReadOnlyDictionary<string, ThrowingDisposable> resources) =>
        new(
            static (_, width, height) =>
                new ImageRgba8(width, height, new byte[width * height * 4]),
            resources["capturer"],
            resources["session"],
            resources["device"],
            resources["source"]);

    private static Dictionary<string, ThrowingDisposable> CreateResources(
        List<string> order) =>
        new(StringComparer.Ordinal)
        {
            ["capturer"] = new ThrowingDisposable("capturer", order),
            ["session"] = new ThrowingDisposable("session", order),
            ["device"] = new ThrowingDisposable("device", order),
            ["source"] = new ThrowingDisposable("source", order),
        };

    private sealed class ThrowingDisposable(string name, List<string> order) : IDisposable
    {
        internal bool Disposed { get; private set; }

        internal int FailuresRemaining { get; set; }

        public void Dispose()
        {
            order.Add(name);
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new IOException($"{name} failed");
            }

            Disposed = true;
        }
    }
}
