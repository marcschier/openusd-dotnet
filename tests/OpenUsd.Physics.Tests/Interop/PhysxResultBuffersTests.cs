// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

public sealed class PhysxResultBuffersTests
{
    [Test]
    public async Task BuffersAreSizedFromTheBuildPageCapacities()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        using var buffers = new PhysxResultBuffers(page.Capacities);

        await Assert.That(buffers.BodyStateCapacity).IsEqualTo((int)page.Capacities.MaxBodyStates);
        await Assert.That(buffers.EventCapacity).IsEqualTo((int)page.Capacities.MaxEvents);
        await Assert.That(buffers.DiagnosticCapacity).IsEqualTo((int)page.Capacities.MaxDiagnostics);
        await Assert.That(buffers.DebugLineCapacity).IsEqualTo((int)page.Capacities.MaxDebugLines);
    }

    [Test]
    public async Task ResultPageDeclaresTheAbiAndTheExactCapacities()
    {
        using PhysxBuildPage build = PhysxPageFixture.CreatePage();
        using var buffers = new PhysxResultBuffers(build.Capacities);
        PhysxResultPage page = buffers.CreatePage();

        await Assert.That(page.AbiVersion).IsEqualTo(PhysxAbi.Version);
        await Assert.That((int)page.BodyStateCapacity).IsEqualTo(buffers.BodyStateCapacity);
        await Assert.That((int)page.EventCapacity).IsEqualTo(buffers.EventCapacity);
        await Assert.That((int)page.DiagnosticCapacity).IsEqualTo(buffers.DiagnosticCapacity);
        await Assert.That((int)page.DebugLineCapacity).IsEqualTo(buffers.DebugLineCapacity);
        await Assert.That(HasSectionPointers(in page)).IsTrue();
    }

    [Test]
    public async Task ZeroCapacityIsRepresentedByANullPointer()
    {
        using var buffers = new PhysxResultBuffers(default);
        PhysxResultPage page = buffers.CreatePage();

        await Assert.That(AllSectionPointersAreNull(in page)).IsTrue();
        await Assert.That((ulong)page.BodyStateCapacity).IsEqualTo(0UL);
    }

    [Test]
    public async Task CaptureCopiesTheRetainedPrefixAndDetachesFromTheBuffers()
    {
        using var buffers = new PhysxResultBuffers(new PhysxResultCapacities
        {
            MaxBodyStates = 4,
            MaxEvents = 4,
            MaxDiagnostics = 4,
            MaxDebugLines = 2,
            MaxQueryHits = 8
        });

        PhysxResultPage page = buffers.CreatePage();
        WriteBodyState(in page, 0, new PhysxBodyState
        {
            Id = 11,
            Pose = new PhysxTransform(new PhysxVec3f(1.0F, 2.0F, 3.0F), PhysxQuatf.Identity)
        });
        WriteBodyState(in page, 1, new PhysxBodyState { Id = 12 });
        WriteEvent(in page, 0, new PhysxEventRecord
        {
            Id0 = 11,
            Id1 = 12,
            StepIndex = 7,
            Type = (uint)PhysxEventType.ContactFound
        });
        WriteDebugLine(in page, 0, new PhysxDebugLine { Color = 0xFF00FF00 });
        page.Header = new PhysxResultHeader
        {
            Revision = PhysxPageFixture.FixtureRevision,
            StepIndex = 7,
            SimulationTime = 0.5,
            LastStepSeconds = 0.016,
            TotalStepSeconds = 0.112,
            BodyStateCount = 2,
            EventCount = 1,
            DiagnosticCount = 0,
            DebugLineCount = 1,
            State = (uint)PhysxWorldState.Ready
        };

        PhysxResultSnapshot snapshot = buffers.Capture(in page, 24.0);

        await Assert.That(snapshot.Revision).IsEqualTo(PhysxPageFixture.FixtureRevision);
        await Assert.That(snapshot.StepIndex).IsEqualTo(7UL);
        await Assert.That(snapshot.State).IsEqualTo(PhysxWorldState.Ready);
        await Assert.That(snapshot.BodyStates.Length).IsEqualTo(2);
        await Assert.That(snapshot.BodyStates[0].Id).IsEqualTo(11UL);
        await Assert.That(snapshot.BodyStates[0].Pose.Position.X).IsEqualTo(1.0F);
        await Assert.That(snapshot.DebugLines.Length).IsEqualTo(1);
        await Assert.That(snapshot.Events.Entries.Count).IsEqualTo(1);
        await Assert.That(snapshot.Events.Entries[0].Kind).IsEqualTo(UsdPhysicsEventKind.ContactBegan);
        await Assert.That(snapshot.Events.Entries[0].StepIndex).IsEqualTo(7UL);
        await Assert.That(snapshot.Overflow.IsOverflowed).IsFalse();

        WriteBodyState(in page, 0, new PhysxBodyState { Id = 99 });
        WriteEvent(in page, 0, default);

        await Assert.That(snapshot.BodyStates[0].Id).IsEqualTo(11UL);
        await Assert.That(snapshot.Events.Entries[0].Kind).IsEqualTo(UsdPhysicsEventKind.ContactBegan);
    }

    [Test]
    public async Task ReportedCountsAreClampedToTheDeclaredCapacity()
    {
        using var buffers = new PhysxResultBuffers(new PhysxResultCapacities
        {
            MaxBodyStates = 1,
            MaxEvents = 1,
            MaxDiagnostics = 1,
            MaxDebugLines = 0,
            MaxQueryHits = 0
        });

        PhysxResultPage page = buffers.CreatePage();
        WriteBodyState(in page, 0, new PhysxBodyState { Id = 5 });
        page.Header = new PhysxResultHeader
        {
            BodyStateCount = 4096,
            EventCount = 4096,
            DiagnosticCount = 4096,
            DebugLineCount = 4096
        };

        PhysxResultSnapshot snapshot = buffers.Capture(in page, 0.0);

        await Assert.That(snapshot.BodyStates.Length).IsEqualTo(1);
        await Assert.That(snapshot.DebugLines.Length).IsEqualTo(0);
        await Assert.That(snapshot.Diagnostics.Entries.Count).IsEqualTo(1);
        await Assert.That(snapshot.Diagnostics.Entries[0].Message)
            .IsEqualTo(snapshot.Diagnostics.Entries[0].Code);
    }

    [Test]
    public async Task OverflowIsReportedAsBoundedMetadataAndAsADiagnostic()
    {
        using var buffers = new PhysxResultBuffers(new PhysxResultCapacities
        {
            MaxBodyStates = 2,
            MaxEvents = 1,
            MaxDiagnostics = 2,
            MaxDebugLines = 1,
            MaxQueryHits = 1
        });

        PhysxResultPage page = buffers.CreatePage();
        page.Header = new PhysxResultHeader
        {
            BodyStateCount = 0,
            EventCount = 0,
            DiagnosticCount = 0,
            DebugLineCount = 0,
            DroppedEventCount = 17,
            DroppedDiagnosticCount = 3,
            DroppedDebugLineCount = 5,
            OverflowFlags = (uint)(PhysxOverflowFlags.Events | PhysxOverflowFlags.Diagnostics)
        };

        PhysxResultSnapshot snapshot = buffers.Capture(in page, 0.0);

        await Assert.That(snapshot.Overflow.IsOverflowed).IsTrue();
        await Assert.That(snapshot.Overflow.DroppedEvents).IsEqualTo(17u);
        await Assert.That(snapshot.Overflow.BodyStatesTruncated).IsFalse();
        await Assert.That(snapshot.Events.DroppedCount).IsEqualTo(17);
        await Assert.That(snapshot.Diagnostics.Entries.Count).IsEqualTo(1);
        await Assert.That(snapshot.Diagnostics.Entries[0].Code).IsEqualTo("OPENUSD_PHYSICS_RESULT_OVERFLOW");
        await Assert.That(snapshot.Diagnostics.Entries[0].Category).IsEqualTo(UsdPhysicsDiagnosticCategory.Step);
    }

    [Test]
    public async Task DiagnosticMessagesAreDecodedUpToTheirTerminator()
    {
        var record = default(PhysxDiagnosticRecord);
        record.Id = 42;
        record.Severity = (uint)PhysxDiagnosticSeverity.Warning;
        record.Code = (uint)PhysxDiagnosticCode.UnsupportedShape;
        byte[] message = Encoding.UTF8.GetBytes("Kegelstümpf wird nicht unterstützt");
        Span<byte> target = record.Message;
        message.CopyTo(target);

        string decoded = PhysxResultBuffers.DecodeMessage(in record.Message);

        await Assert.That(decoded).IsEqualTo("Kegelstümpf wird nicht unterstützt");
        await Assert.That(PhysxResultBuffers.MapCategory(PhysxDiagnosticCode.UnsupportedShape))
            .IsEqualTo(UsdPhysicsDiagnosticCategory.Build);
        await Assert.That(PhysxResultBuffers.MapSeverity(PhysxDiagnosticSeverity.Warning))
            .IsEqualTo(UsdPhysicsDiagnosticSeverity.Warning);
        await Assert.That(PhysxResultBuffers.MapCode(PhysxDiagnosticCode.UnsupportedShape))
            .IsEqualTo("OPENUSD_PHYSICS_UNSUPPORTED_SHAPE");
    }

    [Test]
    public async Task EmptyMessagesDecodeToAnEmptyString()
    {
        var record = default(PhysxDiagnosticRecord);

        await Assert.That(PhysxResultBuffers.DecodeMessage(in record.Message)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DisposedBuffersRejectFurtherUse()
    {
        var buffers = new PhysxResultBuffers(default);
        buffers.Dispose();

        await Assert.That(() => buffers.CreatePage()).Throws<ObjectDisposedException>();
    }

    private static unsafe bool HasSectionPointers(in PhysxResultPage page) =>
        page.BodyStates is not null &&
        page.Events is not null &&
        page.Diagnostics is not null;

    private static unsafe bool AllSectionPointersAreNull(in PhysxResultPage page) =>
        page.BodyStates is null &&
        page.Events is null &&
        page.Diagnostics is null &&
        page.DebugLines is null;

    private static unsafe void WriteBodyState(in PhysxResultPage page, int index, PhysxBodyState value) =>
        page.BodyStates[index] = value;

    private static unsafe void WriteEvent(in PhysxResultPage page, int index, PhysxEventRecord value) =>
        page.Events[index] = value;

    private static unsafe void WriteDebugLine(in PhysxResultPage page, int index, PhysxDebugLine value) =>
        page.DebugLines[index] = value;
}
