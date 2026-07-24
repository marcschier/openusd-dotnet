// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDiagnosticsModelTests
{
    [Test]
    public async Task BufferRetainsOnlyLatestBoundedDiagnostics()
    {
        var buffer = new ViewerDiagnosticsBuffer(entryCapacity: 3, unsupportedCapacity: 2);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 5; index++)
        {
            buffer.Observe(new ViewerDiagnosticsSample(
                now.AddSeconds(index),
                "Storm",
                new ViewerBackendRuntimeIdentity("Win32", "OpenGL", "GPU"),
                "None",
                TimeSpan.FromMilliseconds(index),
                null,
                index,
                index * 10,
                0,
                (ulong)index,
                default,
                [
                    new ViewerDiagnosticEntry(
                        now.AddSeconds(index),
                        "Storm",
                        $"CODE_{index}",
                        new string((char)('a' + index), 600))
                ]));
        }

        ViewerDiagnosticsSnapshot snapshot = buffer.Snapshot();

        await Assert.That(buffer.EntryCount).IsEqualTo(3);
        await Assert.That(snapshot.Entries.Select(entry => entry.Code))
            .IsEquivalentTo(["CODE_2", "CODE_3", "CODE_4"]);
        await Assert.That(snapshot.Entries.All(entry => entry.Message.Length <= 512)).IsTrue();
        await Assert.That(snapshot.DrawCalls).IsEqualTo(4);
        await Assert.That(snapshot.StateRevision).IsEqualTo(4UL);
    }

    [Test]
    public async Task UnsupportedEntriesAreDeduplicatedAndCapped()
    {
        var buffer = new ViewerDiagnosticsBuffer(entryCapacity: 2, unsupportedCapacity: 2);

        buffer.AddUnsupported(
        [
            new ViewerUnsupportedFeature("A", "first"),
            new ViewerUnsupportedFeature("A", "duplicate"),
            new ViewerUnsupportedFeature("B", "second"),
            new ViewerUnsupportedFeature("C", "third")
        ]);
        ViewerDiagnosticsSnapshot snapshot = buffer.Snapshot();

        await Assert.That(buffer.UnsupportedCount).IsEqualTo(2);
        await Assert.That(snapshot.UnsupportedFeatures.Select(feature => feature.Code))
            .IsEquivalentTo(["B", "C"]);
    }

    [Test]
    public async Task FormattingRedactsSourceAndProfilePathsByDefaultAndStaysBounded()
    {
        string sourceRoot = Path.Combine(AppContext.BaseDirectory, "source-tree");
        string profileRoot = Path.Combine(AppContext.BaseDirectory, "profile");
        var formatter = new ViewerDiagnosticsFormatter(
            new ViewerPathRedactor(sourceRoot, profileRoot));
        var snapshot = new ViewerDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            "hdSilk / Vulkan",
            new ViewerBackendRuntimeIdentity(
                "X11",
                "Vulkan",
                $"{sourceRoot}{Path.DirectorySeparatorChar}device"),
            $"Recovered after reading {profileRoot}{Path.DirectorySeparatorChar}driver.log",
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(1),
            4,
            12,
            1,
            7,
            default,
            [
                new ViewerDiagnosticEntry(
                    DateTimeOffset.UtcNow,
                    "Vulkan",
                    "TEST",
                    string.Concat(sourceRoot, Path.DirectorySeparatorChar, new string('x', 40000)))
            ],
            [
                new ViewerUnsupportedFeature(
                    "UNSUPPORTED",
                    $"See {profileRoot}{Path.DirectorySeparatorChar}settings")
            ]);

        string redacted = formatter.Format(snapshot, includePaths: false);
        string unredacted = formatter.Format(snapshot, includePaths: true);

        await Assert.That(redacted).DoesNotContain(sourceRoot);
        await Assert.That(redacted).DoesNotContain(profileRoot);
        await Assert.That(redacted).Contains("<source-tree>");
        await Assert.That(redacted).Contains("<user-profile>");
        await Assert.That(redacted.Length)
            .IsLessThanOrEqualTo(ViewerDiagnosticsFormatter.MaximumTextLength);
        await Assert.That(unredacted).Contains(sourceRoot);
        await Assert.That(unredacted).Contains(profileRoot);
    }

    [Test]
    public async Task CadenceSamplesAtIntervalOrWhenStateChanges()
    {
        var cadence = new ViewerDiagnosticsCadence(TimeSpan.FromSeconds(1));
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        await Assert.That(cadence.ShouldSample(start, stateKey: 1, force: false)).IsTrue();
        await Assert.That(cadence.ShouldSample(start + 1, stateKey: 1, force: false)).IsFalse();
        await Assert.That(cadence.ShouldSample(start + 2, stateKey: 2, force: false)).IsTrue();
        await Assert.That(cadence.ShouldSample(start + 3, stateKey: 2, force: true)).IsTrue();
    }
}
