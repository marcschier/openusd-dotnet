// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

[NotInParallel]
public sealed class UsdStageSchedulerReviewTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VerifiedReviewOriginIsEstablishedOnTheSchedulerOwnerThread(bool explicitNotifications)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH and provide a matching native runtime.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
        string root = Directory.CreateTempSubdirectory("openusd-scheduled-review-").FullName;
        string path = Path.Combine(root, "source.usda");
        const string source = "#usda 1.0\ndef Xform \"World\"\n{\n    double weight = 7\n}\n";
        try
        {
            await File.WriteAllTextAsync(path, source);
            await using UsdStageScheduler scheduler = explicitNotifications
                ? UsdStageScheduler.OpenForReview(path, 1, 2)
                : UsdStageScheduler.OpenForReview(path, 1);
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await Assert.That(Path.GetFullPath(binding.SourceRootPath)).IsEqualTo(Path.GetFullPath(path));
            await Assert.That(binding.SourceFingerprint.Length).IsEqualTo(64);
            await Assert.That(binding.Dependencies.Count).IsEqualTo(1);
            await Assert.That(await scheduler.InvokeAsync(
                stage => binding.HasSamePayload(stage.CaptureReviewSourceBinding()))).IsTrue();
            await using UsdStageRetirementLease retirement = await scheduler.TryPrepareRetirementAsync(
                stage => binding.HasSamePayload(stage.CaptureReviewSourceBinding()))
                ?? throw new InvalidOperationException("The verified source origin changed.");
            await retirement.CommitAsync();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
