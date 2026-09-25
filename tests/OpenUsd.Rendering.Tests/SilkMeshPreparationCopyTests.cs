// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

[NotInParallel]
public sealed class SilkMeshPreparationCopyTests
{
    [Test]
    public async Task FailedNativeSyncReleasesAnyReturnedPageWithoutAcknowledgement()
    {
        FakeCopy.Reset();
        OpenUsdSilkException? failure = await Assert.That(() =>
            OpenUsdSilkRuntime.ThrowIfSyncFailed<FakeCopy>(
                OpenUsd.Interop.OpenUsdNativeStatus.NativeError, 9, "native transfer refused\0"u8, 24, 24))
            .Throws<OpenUsdSilkException>();
        await Assert.That(failure!.Message).IsEqualTo("native transfer refused");
        await Assert.That(FakeCopy.ReleasedPage).IsEqualTo((nint)9);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
        await Assert.That(FakeCopy.Copies).IsEqualTo(0);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(0);
    }

    [Test]
    public async Task FailedManagedCopyRejectsUnacknowledgedPageAndReleasesNativeOwnership()
    {
        FakeCopy.Reset();
        OutOfMemoryException error = AllocationFailure("injected managed copy failure");
        FakeCopy.CopyError = error;
        Exception? observed = null;
        try
        {
            using OpenUsdSilkPage page = Copy(16);
        }
        catch (OutOfMemoryException exception)
        {
            observed = exception;
        }
        await Assert.That(observed).IsSameReferenceAs(error);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(0);
        await Assert.That(FakeCopy.ReleasedPage).IsEqualTo((nint)9);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task AcknowledgementFailureDoesNotExposeManagedPageAndReleasesNativeOwnership()
    {
        FakeCopy.Reset();
        var failure = new InvalidOperationException("acknowledgement");
        FakeCopy.AcknowledgementError = failure;
        Exception? observed = null;
        int pagesBefore = SilkManagedDiagnostics.LivePages;
        try
        {
            using OpenUsdSilkPage page = Copy(16);
        }
        catch (InvalidOperationException exception)
        {
            observed = exception;
        }
        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(SilkManagedDiagnostics.LivePages).IsEqualTo(pagesBefore);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(1);
        await Assert.That(FakeCopy.AcknowledgedPage).IsEqualTo((nint)9);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task SuccessfulCopyKeepsAccountingAndAcknowledgesBeforeRelease()
    {
        FakeCopy.Reset();
        int pagesBefore = SilkManagedDiagnostics.LivePages;
        using OpenUsdSilkPage page = Copy(16);
        await Assert.That(page.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(16ul);
        await Assert.That(page.MeshPreparationUsage.ReservedBytes).IsEqualTo(8ul);
        await Assert.That(page.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(12ul);
        await Assert.That(page.Revision).IsEqualTo(5ul);
        await Assert.That(FakeCopy.AcknowledgedPage).IsEqualTo((nint)9);
        await Assert.That(FakeCopy.PagesAtAcknowledgement).IsEqualTo(pagesBefore + 1);
        await Assert.That(string.Join(",", FakeCopy.Order)).IsEqualTo("copy,acknowledge,release");
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task InvalidNativeUsageIsRefusedWithoutCopyOrAcknowledgement()
    {
        FakeCopy.Reset();
        await Assert.That(() => Copy(15)).Throws<OpenUsdSilkException>();
        await Assert.That(FakeCopy.Copies).IsEqualTo(0);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(0);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task OversizedNativePageIsRefusedBeforeManagedCopyOrAcknowledgement()
    {
        FakeCopy.Reset();
        await Assert.That(() => Copy(16, pageLimit: 3, nativeByteLength: 4)).Throws<OpenUsdSilkException>();
        await Assert.That(FakeCopy.Copies).IsEqualTo(0);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(0);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    [Arguments(4)]
    [Arguments(5)]
    public async Task ExactOrLargerPageLimitPreservesByteLengthAndCopyOwnership(int pageLimit)
    {
        FakeCopy.Reset();
        FakeCopy.CopyResult = [1, 2, 3, 4];
        using OpenUsdSilkPage page = Copy(16, pageLimit, nativeByteLength: 4);
        await Assert.That(page.ByteLength).IsEqualTo(4);
        await Assert.That(FakeCopy.Copies).IsEqualTo(1);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(1);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task TruncatedManagedCopyCannotAcknowledgeTheNativePage()
    {
        FakeCopy.Reset();
        FakeCopy.CopyResult = [1, 2, 3];
        await Assert.That(() => Copy(16, pageLimit: 4, nativeByteLength: 4)).Throws<OpenUsdSilkException>();
        await Assert.That(FakeCopy.Copies).IsEqualTo(1);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(0);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task PageSnapshotReadFailureReleasesWithoutCopyOrAcknowledgement()
    {
        FakeCopy.Reset();
        var failure = new InvalidOperationException("snapshot read failed");
        FakeCopy.UsageError = failure;
        Exception? observed = null;
        try
        {
            using OpenUsdSilkPage page = Copy(16, readPageUsage: true);
        }
        catch (InvalidOperationException error)
        {
            observed = error;
        }
        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(FakeCopy.Copies).IsEqualTo(0);
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(0);
        await Assert.That(FakeCopy.ReleasedPage).IsEqualTo((nint)9);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task SuccessfulPageSnapshotKeepsItsActualLimitsInCaptureDiagnostics()
    {
        FakeCopy.Reset();
        using OpenUsdSilkPage page = Copy(16, pageLimit: 64, readPageUsage: true);
        var original = new RenderDiagnostic(RenderDiagnosticSeverity.Warning, "original", "retained");
        RenderDiagnosticsState diagnostics = page.WithPreparationDiagnostics(new([original]));
        await Assert.That(diagnostics.Entries.Count).IsEqualTo(2);
        await Assert.That(diagnostics.Entries[0]).IsSameReferenceAs(original);
        await Assert.That(diagnostics.Entries[1].Code).IsEqualTo("HDSILK_PREPARATION_ADMISSION");
        await Assert.That(diagnostics.Entries[1].Severity).IsEqualTo(RenderDiagnosticSeverity.Information);
        await Assert.That(diagnostics.Entries[1].Message).Contains("limit=16");
        await Assert.That(page.MeshPreparationUsage!.ReservedBytes).IsEqualTo(8ul);
        await Assert.That(page.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(12ul);
        await Assert.That(diagnostics.Entries[1].Message).Contains("ceiling=64");
        await Assert.That(diagnostics.Entries[1].Message).Contains("Not total process memory");
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(1);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task PreparationDiagnosticsStayStableAcrossFrameUsageChanges()
    {
        using var first = new OpenUsdSilkPage(24, 1, [1, 2], 0, new(16, 8, 12), 64);
        using var next = new OpenUsdSilkPage(24, 2, [1, 2, 3, 4], 0, new(16, 16, 16), 64);
        await Assert.That(first.WithPreparationDiagnostics(RenderDiagnosticsState.Empty)
            .Equals(next.WithPreparationDiagnostics(RenderDiagnosticsState.Empty))).IsTrue();
        await Assert.That(first.MeshPreparationUsage!.ReservedBytes).IsEqualTo(8ul);
        await Assert.That(next.MeshPreparationUsage!.ReservedBytes).IsEqualTo(16ul);
        await Assert.That(first.ByteLength).IsEqualTo(2);
        await Assert.That(next.ByteLength).IsEqualTo(4);
    }

    [Test]
    public async Task AdmissionDiagnosticsDoNotOverflowTheBoundedJobDiagnosticCount()
    {
        using var page = new OpenUsdSilkPage(24, 1, [], 0, new(100, 40, 60), 200);
        RenderDiagnostic[] existing = Enumerable.Range(0, 128).Select(index =>
            new RenderDiagnostic(RenderDiagnosticSeverity.Warning, "material-" + index, "diagnostic")).ToArray();
        RenderDiagnosticsState result = page.WithPreparationDiagnostics(new(existing));
        await Assert.That(result.Entries.Count).IsEqualTo(128);
        await Assert.That(result.Entries[^1].Code).IsEqualTo("HDSILK_PREPARATION_ADMISSION");
        await Assert.That(result.Entries[^2].Code).IsEqualTo("HDSILK_ADMISSION_DIAGNOSTICS_TRUNCATED");
        await Assert.That(result.Entries.Take(126).SequenceEqual(existing.Take(126))).IsTrue();
    }

    [Test]
    public async Task UnboundedCopyFailureAlsoRejectsWithoutAcknowledgement()
    {
        FakeCopy.Reset();
        OutOfMemoryException error = AllocationFailure("legacy copy");
        FakeCopy.CopyError = error;
        await Assert.That(() => Copy(null)).Throws<OutOfMemoryException>();
        await Assert.That(FakeCopy.Acknowledgements).IsEqualTo(0);
        await Assert.That(FakeCopy.Releases).IsEqualTo(1);
    }

    private static OpenUsdSilkPage Copy(
        ulong? maximum, int? pageLimit = null, int nativeByteLength = 0, bool readPageUsage = false)
    {
        var view = new OpenUsdSilkRuntime.NativePageView
        {
            AbiVersion = 24,
            Revision = 5,
            Data = nativeByteLength == 0 ? 0 : 1,
            DataSize = checked((nuint)nativeByteLength)
        };
        var usage = new OpenUsdSilkRuntime.NativeMeshPreparationUsage
        {
            Version = 1,
            MaximumReservedBytes = 16,
            ReservedBytes = 8,
            PeakReservedBytes = 12
        };
        return OpenUsdSilkRuntime.CopyPreparedPage<FakeCopy>(
            9, in view, in usage, maximum, pageLimit, readPageUsage);
    }

    [SuppressMessage("Usage", "CA2201", Justification = "Fault injection avoids exhausting shared-host memory.")]
    private static OutOfMemoryException AllocationFailure(string message) => new(message);

    private readonly struct FakeCopy : OpenUsdSilkRuntime.IPreparedPageCopy
    {
        public static OpenUsdSilkRuntime.NativeMeshPreparationUsage ReadUsage(nint page) => UsageError is { } error
            ? throw error
            : new() { Version = 1, MaximumReservedBytes = 16, ReservedBytes = 8, PeakReservedBytes = 12 };

        internal static Exception? UsageError { get; set; }
        internal static Exception? CopyError { get; set; }
        internal static Exception? AcknowledgementError { get; set; }
        internal static byte[] CopyResult { get; set; } = [];
        internal static nint AcknowledgedPage { get; private set; }
        internal static int Acknowledgements { get; private set; }
        internal static int PagesAtAcknowledgement { get; private set; }
        internal static List<string> Order { get; } = [];
        internal static nint ReleasedPage { get; private set; }
        internal static int Releases { get; private set; }
        internal static int Copies { get; private set; }

        internal static void Reset()
        {
            CopyError = null;
            UsageError = null;
            AcknowledgementError = null;
            CopyResult = [];
            AcknowledgedPage = 0;
            Acknowledgements = 0;
            PagesAtAcknowledgement = 0;
            Order.Clear();
            ReleasedPage = 0;
            Releases = 0;
            Copies = 0;
        }

        public static byte[] Copy(in OpenUsdSilkRuntime.NativePageView view)
        {
            Copies++;
            Order.Add("copy");
            if (CopyError is { } error)
            {
                throw error;
            }
            return CopyResult;
        }

        public static void Acknowledge(nint page)
        {
            Acknowledgements++;
            PagesAtAcknowledgement = SilkManagedDiagnostics.LivePages;
            AcknowledgedPage = page;
            Order.Add("acknowledge");
            if (AcknowledgementError is { } error)
            {
                throw error;
            }
        }

        public static void Release(nint page)
        {
            ReleasedPage = page;
            Releases++;
            Order.Add("release");
        }
    }
}
