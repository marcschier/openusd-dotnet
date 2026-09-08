// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    private sealed class ViewerCameraBookmarkFixture(string root, bool preserve) : IDisposable
    {
        internal string Root { get; } = root;
        internal string SourcePath => Path.Combine(Root, "source.usda");
        internal string DestinationPath => Path.Combine(Root, "review.urd");

        internal static async Task<ViewerCameraBookmarkFixture> CreateAsync()
        {
            string? evidence = Environment.GetEnvironmentVariable("OPENUSD_VIEWER_BOOKMARK_EVIDENCE_ROOT");
            string parent = evidence ?? Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ??
                throw new InvalidOperationException("Use the native Viewer workflow runner for saved-view fixtures.");
            if (!Path.IsPathFullyQualified(parent) || !Directory.Exists(parent))
            {
                throw new ArgumentException("Saved-view evidence requires an existing absolute directory.");
            }
            OpenUsdNativeRuntime.RegisterPlugins(Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH") ??
                throw new InvalidOperationException("The matching native plugin path is required."));
            var fixture = new ViewerCameraBookmarkFixture(
                Path.Combine(parent, $"saved-views-{Guid.NewGuid():N}"), evidence is not null);
            Directory.CreateDirectory(fixture.Root);
            await File.WriteAllTextAsync(fixture.SourcePath, ViewerPortableReviewFixture.SourceText);
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "sublayer.usda"), ViewerPortableReviewFixture.SublayerText);
            await File.WriteAllBytesAsync(
                Path.Combine(fixture.Root, "texture.png"), ViewerPortableReviewFixture.AssetBytes);
            return fixture;
        }

        internal async Task AssertOriginalsUnchangedAsync()
        {
            await Assert.That(await File.ReadAllTextAsync(SourcePath))
                .IsEqualTo(ViewerPortableReviewFixture.SourceText);
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(Root, "sublayer.usda")))
                .IsEqualTo(ViewerPortableReviewFixture.SublayerText);
            await Assert.That((await File.ReadAllBytesAsync(Path.Combine(Root, "texture.png")))
                .SequenceEqual(ViewerPortableReviewFixture.AssetBytes)).IsTrue();
        }

        public void Dispose()
        {
            if (!preserve)
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
