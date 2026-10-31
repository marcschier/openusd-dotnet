// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerSourceLayerPublicationUnsupportedPlatformTests
{
    [Test]
    public async Task Begin_OnNonWindowsRefusesWithoutChangingFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("This test verifies the non-Windows publication guard.");
        }

        string directory = Path.Combine(
            Path.GetTempPath(), $"viewer-unsupported-source-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string sourcePath = Path.Combine(directory, "source.usda");
            string unrelatedPath = Path.Combine(directory, "unrelated.usda");
            byte[] sourceBytes = "#usda 1.0\n# original\n"u8.ToArray();
            byte[] unrelatedBytes = "#usda 1.0\n# unrelated\n"u8.ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(unrelatedPath, unrelatedBytes);

            await Assert.That(async () => await ViewerSourceLayerPublication.BeginAsync(
                sourcePath, Convert.ToHexString(SHA256.HashData(sourceBytes)), CancellationToken.None))
                .ThrowsExactly<PlatformNotSupportedException>()
                .WithMessage("Guarded source-layer publication currently requires Windows.");

            await Assert.That((await File.ReadAllBytesAsync(sourcePath)).SequenceEqual(sourceBytes)).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(unrelatedPath)).SequenceEqual(unrelatedBytes)).IsTrue();
            await Assert.That(Directory.GetFileSystemEntries(directory).Length).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
