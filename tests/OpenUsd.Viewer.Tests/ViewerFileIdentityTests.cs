// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerFileIdentityTests
{
    [Test]
    public async Task EqualSizeAndTimestampCannotHideForeignFileChanges()
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"identity-{Guid.NewGuid():N}.usda");
        try
        {
            await File.WriteAllTextAsync(path, "baseline");
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            ViewerFileIdentity before = await ViewerFileIdentity.CaptureAsync(path, 1024, CancellationToken.None);
            await File.WriteAllTextAsync(path, "changed!");
            File.SetLastWriteTimeUtc(path, stamp);

            ViewerFileIdentity after = await ViewerFileIdentity.CaptureAsync(path, 1024, CancellationToken.None);

            await Assert.That(before.Length).IsEqualTo(8L);
            await Assert.That(after.Length).IsEqualTo(8L);
            await Assert.That(before.Matches(after)).IsFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingBudgetAndCancellationStatesAreNotInterchangeable()
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"identity-{Guid.NewGuid():N}.usda");
        try
        {
            ViewerFileIdentity missing = await ViewerFileIdentity.CaptureAsync(path, 1024, CancellationToken.None);
            await Assert.That(missing.Exists).IsFalse();
            await File.WriteAllTextAsync(path, "abc");
            ViewerFileIdentity present = await ViewerFileIdentity.CaptureAsync(path, 1024, CancellationToken.None);

            await Assert.That(present.Exists).IsTrue();
            await Assert.That(present.Sha256)
                .IsEqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
            await Assert.That(missing.Matches(present)).IsFalse();
            await Assert.That(async () => await ViewerFileIdentity.CaptureAsync(path, 2, CancellationToken.None))
                .Throws<InvalidDataException>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () => await ViewerFileIdentity.CaptureAsync(path, 1024, cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
