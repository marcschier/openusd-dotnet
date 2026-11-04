// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;
using VulkanResult = Silk.NET.Vulkan.Result;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed class SilkBackendRetirementOwnershipTests
{
    private static readonly int[] ExpectedReadbackRetirementOrder = [0, 1];

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task VulkanReadbackOwnerRetainsFailedTeardownStepBeforeReleasingItsDependentOwner(int failAt)
    {
        SilkResourceReleaseSteps steps = default;
        var attempts = new int[3];
        var releases = new int[3];
        bool refuse = true;
        void release(int step)
        {
            attempts[step]++;
            if (refuse && step == failAt)
            { throw new InvalidOperationException("Injected readback teardown refusal."); }
            releases[step]++;
        }
        await Assert.That(() => VulkanSilkPickReadbackBuffer.RetireOwnedSteps(ref steps, release))
            .Throws<InvalidOperationException>();
        await Assert.That(steps.Completed).IsEqualTo(failAt);
        for (int step = 0; step < 3; step++)
        {
            await Assert.That(attempts[step]).IsEqualTo(step <= failAt ? 1 : 0);
            await Assert.That(releases[step]).IsEqualTo(step < failAt ? 1 : 0);
        }
        refuse = false;
        VulkanSilkPickReadbackBuffer.RetireOwnedSteps(ref steps, release);
        VulkanSilkPickReadbackBuffer.RetireOwnedSteps(ref steps, release);
        await Assert.That(steps.Completed).IsEqualTo(3);
        for (int step = 0; step < 3; step++)
        {
            await Assert.That(attempts[step]).IsEqualTo(step == failAt ? 2 : 1);
            await Assert.That(releases[step]).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ZeroHandleD3D12RetainedRecordRetriesReservationFailureAfterNativeRetirement()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The managed D3D12 retained-record seam is Windows-only; native handles are zero.");
            return;
        }
        var budget = new SilkGpuStagingBudget(8);
        using var reservation = new Reservation(budget.Reserve(8)) { Refuse = true };
        var record = new D3D12RetainedResources([0, 0], 0, 0, 0, reservation);
        await Assert.That(record.Release).Throws<InvalidOperationException>();
        await Assert.That(reservation.Attempts).IsEqualTo(1);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
        reservation.Refuse = false;
        record.Release();
        record.Release();
        await Assert.That(reservation.Attempts).IsEqualTo(2);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task D3D12PickOwnerRetainsFailedAndUnvisitedUploadsAfterItsReadbackTransition()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The managed D3D12 pick owner seam is Windows-only; native handles are zero.");
            return;
        }
        var budget = new SilkGpuStagingBudget(16);
        using var first = new Reservation(budget.Reserve(8)) { Refuse = true };
        using var last = new Reservation(budget.Reserve(8));
        Retirement owner = PickUploadRetirement(first, last);
        await Assert.That(owner.Retire).Throws<InvalidOperationException>();
        await Assert.That(owner.IsReleased()).IsFalse();
        await Assert.That(owner.PendingUploads()).IsEqualTo(2);
        await Assert.That(first.Attempts).IsEqualTo(1);
        await Assert.That(last.Attempts).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(16ul);
        first.Refuse = false;
        owner.Retire();
        owner.Retire();
        await Assert.That(owner.IsReleased()).IsTrue();
        await Assert.That(owner.PendingUploads()).IsEqualTo(0);
        await Assert.That(first.Attempts).IsEqualTo(2);
        await Assert.That(last.Attempts).IsEqualTo(1);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(VulkanResult.NotReady)]
    [Arguments(VulkanResult.Timeout)]
    [Arguments(VulkanResult.ErrorOutOfHostMemory)]
    [Arguments(VulkanResult.ErrorOutOfDeviceMemory)]
    [Arguments(VulkanResult.ErrorUnknown)]
    public async Task VulkanPickOwnerRetainsReadbackTransitionAndLeasesAfterNonterminalFenceResult(VulkanResult result)
    {
        var budget = new SilkGpuBufferBudget(8);
        using var resource = new Resource(budget);
        IDisposable?[]? leases = [resource.Borrow()];
        resource.Dispose();
        bool releasing = false;
        int slotReleases = 0;
        SilkResourceReleaseSteps steps = default;
        void release(int step)
        {
            if (step == 0)
            {
                VulkanSilkPickReadbackBuffer.RequireTerminalRetirementResult(result);
                slotReleases++;
                return;
            }
            VulkanSilkPickSubmission.RetireOwnedLeases(ref leases, ref releasing);
        }
        await Assert.That(() => VulkanSilkPickSubmission.RetireOwnedSteps(ref steps, release))
            .Throws<InvalidOperationException>();
        await Assert.That(steps.Completed).IsEqualTo(0);
        await Assert.That(slotReleases).IsEqualTo(0);
        await Assert.That(resource.Attempts).IsEqualTo(0);
        await Assert.That(leases![0]).IsNotNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
        result = VulkanResult.Success;
        VulkanSilkPickSubmission.RetireOwnedSteps(ref steps, release);
        VulkanSilkPickSubmission.RetireOwnedSteps(ref steps, release);
        await Assert.That(steps.Completed).IsEqualTo(2);
        await Assert.That(slotReleases).IsEqualTo(1);
        await Assert.That(resource.Releases).IsEqualTo(1);
        await Assert.That(leases).IsNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(VulkanResult.Success)]
    [Arguments(VulkanResult.ErrorDeviceLost)]
    public async Task VulkanPickRetirementAcceptsOnlyCompletedOrDeviceLostFenceObservation(VulkanResult result)
    {
        SilkResourceReleaseSteps steps = default;
        var released = new List<int>();
        void release(int step)
        {
            if (step == 0)
            { VulkanSilkPickReadbackBuffer.RequireTerminalRetirementResult(result); }
            released.Add(step);
        }
        VulkanSilkPickSubmission.RetireOwnedSteps(ref steps, release);
        VulkanSilkPickSubmission.RetireOwnedSteps(ref steps, release);
        await Assert.That(steps.Completed).IsEqualTo(2);
        await Assert.That(released.ToArray()).IsEquivalentTo(ExpectedReadbackRetirementOrder);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, 1)]
    [Arguments(SilkGraphicsBackend.D3D12, 2)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, 2)]
    public async Task PickOwnerRetriesLeaseFailureWithoutRepeatingReadbackTransition(
        SilkGraphicsBackend backend, int failAt)
    {
        if (!RequirePlatform(backend))
        { return; }
        var budget = new SilkGpuBufferBudget(24);
        Resource[] resources = [new(budget), new(budget), new(budget)];
        IDisposable?[]? leases = resources.Select(resource => resource.Borrow()).ToArray();
        IDisposable?[] original = [.. leases];
        foreach (Resource resource in resources)
        { resource.Dispose(); }
        resources[failAt].Refuse = true;
        bool releasing = false, releasingUploads = false;
        D3D12StagingResource?[]? uploads = [];
        nint[]? descriptorHeaps = [];
        SilkResourceReleaseSteps steps = default;
        RetireSteps retire = backend == SilkGraphicsBackend.Vulkan
            ? VulkanSilkPickSubmission.RetireOwnedSteps
            : OperatingSystem.IsWindows()
                ? D3D12SilkPickSubmission.RetireOwnedSteps
                : throw new PlatformNotSupportedException();
        int slotReleases = 0;
        void release(int step)
        {
            if (step == 0)
            { slotReleases++; return; }
            if (backend == SilkGraphicsBackend.Vulkan)
            {
                VulkanSilkPickSubmission.RetireOwnedLeases(ref leases, ref releasing);
            }
            else if (OperatingSystem.IsWindows())
            {
                D3D12SilkPickSubmission.RetireOwnedResources(
                    ref leases, ref releasing, ref uploads, ref releasingUploads, ref descriptorHeaps);
            }
            else
            { throw new PlatformNotSupportedException(); }
        }
        await Assert.That(() => retire(ref steps, release)).Throws<InvalidOperationException>();
        await Assert.That(steps.Completed).IsEqualTo(1);
        await Assert.That(slotReleases).IsEqualTo(1);
        int retained = backend == SilkGraphicsBackend.Vulkan ? failAt + 1 : 3 - failAt;
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(checked((ulong)retained * 8));
        for (int index = 0; index < 3; index++)
        {
            bool visited = backend == SilkGraphicsBackend.Vulkan ? index >= failAt : index <= failAt;
            bool released = visited && index != failAt;
            await Assert.That(resources[index].Attempts).IsEqualTo(visited ? 1 : 0);
            await Assert.That(leases![index]).IsSameReferenceAs(released ? null : original[index]);
        }
        resources[failAt].Refuse = false;
        retire(ref steps, release);
        retire(ref steps, release);
        await Assert.That(steps.Completed).IsEqualTo(2);
        await Assert.That(slotReleases).IsEqualTo(1);
        await Assert.That(leases).IsNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        for (int index = 0; index < 3; index++)
        {
            await Assert.That(resources[index].Releases).IsEqualTo(1);
            await Assert.That(resources[index].Attempts).IsEqualTo(index == failAt ? 2 : 1);
        }
    }

    [Test]
    public async Task D3D12SubmissionRetainsBothLeaseAndStagingFailuresWithoutReplacingTheFirstFailure()
    {
        if (!RequirePlatform(SilkGraphicsBackend.D3D12))
        { return; }
        var buffers = new SilkGpuBufferBudget(8);
        var staging = new SilkGpuStagingBudget(8);
        using var resource = new Resource(buffers) { Refuse = true };
        using var reservation = new Reservation(staging.Reserve(8)) { Refuse = true };
        var owner = SubmissionRetirement(SilkGraphicsBackend.D3D12, [resource.Borrow()], [reservation]);
        resource.Dispose();
        AggregateException? failure = await Assert.That(owner.Retire).Throws<AggregateException>();
        await Assert.That(failure!.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(failure.InnerExceptions[0].Message).Contains("resource release refusal");
        await Assert.That(failure.InnerExceptions[1].Message).Contains("staging reservation release refusal");
        await Assert.That(owner.IsReleased()).IsFalse();
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(8ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(8ul);
        resource.Refuse = false;
        reservation.Refuse = false;
        owner.Retire();
        await Assert.That(owner.IsReleased()).IsTrue();
        await Assert.That(resource.Attempts).IsEqualTo(2);
        await Assert.That(resource.Releases).IsEqualTo(1);
        await Assert.That(reservation.Attempts).IsEqualTo(2);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(false, 3)]
    [Arguments(false, 4)]
    [Arguments(false, 5)]
    [Arguments(false, 6)]
    [Arguments(false, 7)]
    [Arguments(true, 0)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    [Arguments(true, 3)]
    [Arguments(true, 4)]
    public async Task MetalPipelineOwnerResumesFailedNativeOrProgramStepWithoutRepeatingCompletedCallbacks(
        bool compute, int failAt)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("The managed Metal owner seam is macOS-only; callbacks do not invoke Metal.");
            return;
        }
        RetireSteps retire = compute ? MetalSilkComputePipeline.RetireOwnedNativeSteps : MetalSilkGraphicsPipeline.RetireOwnedNativeSteps;
        int count = compute ? 5 : 8;
        var attempts = new int[count];
        var releases = new int[count];
        SilkResourceReleaseSteps steps = default;
        bool refuse = true;
        void release(int step)
        {
            attempts[step]++;
            if (refuse && step == failAt)
            { throw new InvalidOperationException("Injected Metal owner retirement callback failure."); }
            releases[step]++;
        }
        await Assert.That(() => retire(ref steps, release)).Throws<InvalidOperationException>();
        await Assert.That(steps.Completed).IsEqualTo(failAt);
        for (int step = 0; step < count; step++)
        {
            await Assert.That(releases[step]).IsEqualTo(step < failAt ? 1 : 0);
            await Assert.That(attempts[step]).IsEqualTo(step <= failAt ? 1 : 0);
        }
        refuse = false;
        retire(ref steps, release);
        retire(ref steps, release);
        await Assert.That(steps.Completed).IsEqualTo(count);
        for (int step = 0; step < count; step++)
        {
            await Assert.That(releases[step]).IsEqualTo(1);
            await Assert.That(attempts[step]).IsEqualTo(step == failAt ? 2 : 1);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task ShaderProgramOwnerRetainsFailedAndUnvisitedRealBaseLeases(SilkGraphicsBackend backend, bool compute)
    {
        if (!RequirePlatform(backend))
        { return; }
        var budget = new SilkGpuBufferBudget(24);
        Resource[] resources = compute ? [new(budget), new(budget)] : [new(budget), new(budget), new(budget)];
        IDisposable?[]? leases = resources.Select(resource => resource.Borrow()).ToArray();
        IDisposable?[] original = [.. leases];
        foreach (Resource resource in resources)
        { resource.Dispose(); }
        int failAt = compute ? 0 : 1;
        resources[failAt].Refuse = true;
        bool releasing = false;
        RetireProgram retire = ProgramRetirement(backend, compute);
        await Assert.That(() => retire(ref leases, ref releasing)).Throws<InvalidOperationException>();
        await Assert.That(releasing).IsFalse();
        for (int index = 0; index < resources.Length; index++)
        {
            await Assert.That(resources[index].Attempts).IsEqualTo(index <= failAt ? 1 : 0);
            await Assert.That(leases![index]).IsSameReferenceAs(index < failAt ? null : original[index]);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(checked((ulong)(resources.Length - failAt) * 8));
        resources[failAt].Refuse = false;
        retire(ref leases, ref releasing);
        retire(ref leases, ref releasing);
        await Assert.That(leases).IsNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        for (int index = 0; index < resources.Length; index++)
        {
            await Assert.That(resources[index].Releases).IsEqualTo(1);
            await Assert.That(resources[index].Attempts).IsEqualTo(index == failAt ? 2 : 1);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, 1)]
    [Arguments(SilkGraphicsBackend.D3D12, 2)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, 2)]
    [Arguments(SilkGraphicsBackend.Metal, 0)]
    [Arguments(SilkGraphicsBackend.Metal, 1)]
    [Arguments(SilkGraphicsBackend.Metal, 2)]
    public async Task ZeroHandleSubmissionOwnerRetainsFailedAndUnvisitedLeasesBeforeRetry(
        SilkGraphicsBackend backend, int failAt)
    {
        if (!RequirePlatform(backend))
        { return; }
        var buffers = new SilkGpuBufferBudget(24);
        var staging = new SilkGpuStagingBudget(16);
        Resource[] resources = [new(buffers), new(buffers), new(buffers)];
        IDisposable?[] leases = resources.Select(resource => resource.Borrow()).ToArray();
        IDisposable?[] original = [.. leases];
        var owner = SubmissionRetirement(backend, leases, [staging.Reserve(8), staging.Reserve(8)]);
        foreach (Resource resource in resources)
        { resource.Dispose(); }
        resources[failAt].Refuse = true;
        await Assert.That(owner.Retire).Throws<InvalidOperationException>();
        await Assert.That(owner.IsReleased()).IsFalse();
        for (int index = 0; index < resources.Length; index++)
        {
            await Assert.That(resources[index].Attempts).IsEqualTo(index <= failAt ? 1 : 0);
            await Assert.That(leases[index]).IsSameReferenceAs(index < failAt ? null : original[index]);
        }
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(checked((ulong)(3 - failAt) * 8));
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(backend == SilkGraphicsBackend.D3D12 ? 0ul : 16ul);
        resources[failAt].Refuse = false;
        owner.Retire();
        owner.Retire();
        await Assert.That(owner.IsReleased()).IsTrue();
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        for (int index = 0; index < resources.Length; index++)
        {
            await Assert.That(resources[index].Releases).IsEqualTo(1);
            await Assert.That(resources[index].Attempts).IsEqualTo(index == failAt ? 2 : 1);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task ZeroHandleSubmissionOwnerRetriesFailedStagingReservationWithoutLosingTheFollowingUpload(
        SilkGraphicsBackend backend)
    {
        if (!RequirePlatform(backend))
        { return; }
        var budget = new SilkGpuStagingBudget(16);
        using var first = new Reservation(budget.Reserve(8)) { Refuse = true };
        using var last = new Reservation(budget.Reserve(8));
        var owner = SubmissionRetirement(backend, [], [first, last]);
        await Assert.That(owner.Retire).Throws<InvalidOperationException>();
        await Assert.That(first.Attempts).IsEqualTo(1);
        await Assert.That(last.Attempts).IsEqualTo(0);
        await Assert.That(owner.IsReleased()).IsFalse();
        await Assert.That(owner.PendingUploads()).IsEqualTo(2);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(16ul);
        first.Refuse = false;
        owner.Retire();
        owner.Retire();
        await Assert.That(first.Attempts).IsEqualTo(2);
        await Assert.That(last.Attempts).IsEqualTo(1);
        await Assert.That(owner.IsReleased()).IsTrue();
        await Assert.That(owner.PendingUploads()).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private delegate void RetireProgram(ref IDisposable?[]? leases, ref bool releasing);
    private delegate void RetireSteps(ref SilkResourceReleaseSteps steps, Action<int> release);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static unsafe Retirement PickUploadRetirement(IDisposable first, IDisposable last)
    {
        IDisposable?[]? leases = [];
        D3D12StagingResource?[]? uploads = [new(null, first), new(null, last)];
        nint[]? descriptorHeaps = [];
        bool releasingLeases = false, releasingUploads = false;
        int slotReleases = 0;
        SilkResourceReleaseSteps steps = default;
        void release(int step)
        {
            if (step == 0)
            { slotReleases++; return; }
            D3D12SilkPickSubmission.RetireOwnedResources(
                ref leases, ref releasingLeases, ref uploads, ref releasingUploads, ref descriptorHeaps);
        }
        return new(
            () => D3D12SilkPickSubmission.RetireOwnedSteps(ref steps, release),
            () => steps.Completed == 2 && slotReleases == 1 && leases is null &&
                uploads is null && descriptorHeaps is null,
            () => uploads?.Count(upload => upload is not null) ?? 0);
    }

    private static RetireProgram ProgramRetirement(SilkGraphicsBackend backend, bool compute)
    {
        if (backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows())
        {
            return compute ? D3D12SilkComputeShaderProgram.RetireOwnedLeases : D3D12SilkGraphicsShaderProgram.RetireOwnedLeases;
        }
        if (backend == SilkGraphicsBackend.Metal && OperatingSystem.IsMacOS())
        {
            return compute ? MetalSilkComputeShaderProgram.RetireOwnedLeases : MetalSilkGraphicsShaderProgram.RetireOwnedLeases;
        }
        if (backend == SilkGraphicsBackend.Vulkan)
        {
            return compute ? VulkanSilkComputeShaderProgram.RetireOwnedLeases : VulkanSilkGraphicsShaderProgram.RetireOwnedLeases;
        }
        throw new PlatformNotSupportedException("The selected backend retirement seam is unavailable on this host.");
    }

    private static unsafe Retirement SubmissionRetirement(
        SilkGraphicsBackend backend, IDisposable?[]? leases, IDisposable[] reservations)
    {
        if (backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows())
        {
            return D3D12SubmissionRetirement(leases, reservations);
        }
        if (backend == SilkGraphicsBackend.Metal && OperatingSystem.IsMacOS())
        {
            return MetalSubmissionRetirement(leases, reservations);
        }
        if (backend == SilkGraphicsBackend.Vulkan)
        {
            bool releasing = false;
            VulkanUploadResource[]? uploads = reservations.Select(reservation => new VulkanUploadResource(default, default, reservation)).ToArray();
            VulkanDrawSubmissionResource[]? draws = [default];
            VulkanComputeSubmissionResource[]? computes = [default];
            return new(
                () => VulkanSilkGraphicsSubmission.RetireOwnedResources(
                    null, default, ref leases, ref releasing, ref uploads, ref draws, ref computes),
                () => leases is null && uploads is null && draws is null && computes is null,
                () => uploads?.Count(upload => upload.Reservation is not null) ?? 0);
        }
        throw new PlatformNotSupportedException("The selected backend retirement seam is unavailable on this host.");
    }

    [SupportedOSPlatform("windows")]
    private static unsafe Retirement D3D12SubmissionRetirement(
        IDisposable?[]? leases, IDisposable[] reservations)
    {
        bool releasing = false, releasingUploads = false;
        D3D12StagingResource?[]? uploads =
            reservations.Select(reservation => new D3D12StagingResource(null, reservation)).ToArray();
        nint[]? heaps = [0, 0];
        return new(
            () => D3D12SilkGraphicsSubmission.RetireOwnedResources(
                ref leases, ref releasing, ref uploads, ref releasingUploads, ref heaps),
            () => leases is null && uploads is null && heaps is null,
            () => uploads?.Count(upload => upload is not null) ?? 0);
    }

    [SupportedOSPlatform("macos")]
    private static Retirement MetalSubmissionRetirement(
        IDisposable?[]? leases, IDisposable[] reservations)
    {
        bool releasing = false, releasingUploads = false;
        MetalStagingResource?[]? uploads =
            reservations.Select(reservation => new MetalStagingResource(default, reservation)).ToArray();
        return new(
            () => MetalSilkGraphicsSubmission.RetireOwnedResources(
                ref leases, ref releasing, ref uploads, ref releasingUploads),
            () => leases is null && uploads is null,
            () => uploads?.Count(upload => upload is not null) ?? 0);
    }

    private static bool RequirePlatform(SilkGraphicsBackend backend)
    {
        if ((backend == SilkGraphicsBackend.D3D12 && !OperatingSystem.IsWindows()) ||
            (backend == SilkGraphicsBackend.Metal && !OperatingSystem.IsMacOS()))
        {
            Skip.Test("The managed retirement seam follows its backend's platform annotation; no GPU is used.");
            return false;
        }
        return true;
    }

    private sealed record Retirement(Action Retire, Func<bool> IsReleased, Func<int> PendingUploads);

    private sealed class Resource : SilkGraphicsResourceBase
    {
        internal Resource(SilkGpuBufferBudget budget) => OwnBufferReservation(budget.Reserve(8));
        internal bool Refuse { get; set; }
        internal int Attempts { get; private set; }
        internal int Releases { get; private set; }
        internal IDisposable Borrow() => AcquireResourceLease();
        protected override void ReleaseNative()
        {
            Attempts++;
            if (Refuse)
            { throw new InvalidOperationException("Injected real-base resource release refusal."); }
            Releases++;
        }
    }

    private sealed class Reservation(IDisposable reservation) : IDisposable
    {
        internal bool Refuse { get; set; }
        internal int Attempts { get; private set; }
        public void Dispose()
        {
            Attempts++;
            if (Refuse)
            { throw new InvalidOperationException("Injected staging reservation release refusal."); }
            reservation.Dispose();
        }
    }
}
