// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerPreparedDocumentProfileTests
{
    [Test]
    public async Task CrateSignatureRequiresSessionOnlyPortableReviewReason()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The portable source profile uses Windows file identity.");
        }
        string directory = CreateTestDirectory();
        try
        {
            string path = Path.Combine(directory, "source.usd");
            await File.WriteAllBytesAsync(path, "PXR-USDC"u8.ToArray());
            byte[] original = await File.ReadAllBytesAsync(path);

            string reason = ViewerPreparedDocument.GetSessionOnlyReason(path) ??
                throw new InvalidOperationException("Crate source was treated as eligible for portable review.");

            await Assert.That(reason).Contains("Crate sources remain available for viewing");
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(original)).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task TextUsdSignatureRemainsEligibleForVerifiedPortableReview()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The portable source profile uses Windows file identity.");
        }
        string directory = CreateTestDirectory();
        try
        {
            string path = Path.Combine(directory, "source.usd");
            const string text = "#usda 1.0\n";
            await File.WriteAllTextAsync(path, text);

            await Assert.That(ViewerPreparedDocument.GetSessionOnlyReason(path)).IsNull();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        string parent = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ?? Path.GetTempPath();
        if (!Path.IsPathFullyQualified(parent))
        {
            throw new InvalidOperationException("The portable profile test root must be an absolute path.");
        }
        string directory = Path.Combine(parent, $"viewer-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
