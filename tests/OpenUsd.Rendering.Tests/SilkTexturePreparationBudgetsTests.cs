// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkTexturePreparationBudgetsTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task ConfigurationPreservesIndependentDomainsAndFullWidthCeilings(int domains)
    {
        SilkTexturePreparationBudgets budgets = SilkTexturePreparationBudgets.FromConfiguration(
            (domains & 1) != 0 ? "18446744073709551615" : null,
            (domains & 2) != 0 ? "1234" : null,
            (domains & 4) != 0 ? "5678" : null)!;
        await Assert.That(budgets.CpuTextureBudget?.MaximumBytes)
            .IsEqualTo((domains & 1) != 0 ? ulong.MaxValue : (ulong?)null);
        await Assert.That(budgets.GpuTextureBudget?.MaximumBytes)
            .IsEqualTo((domains & 2) != 0 ? 1234ul : (ulong?)null);
        await Assert.That(budgets.GpuStagingBudget?.MaximumBytes)
            .IsEqualTo((domains & 4) != 0 ? 5678ul : (ulong?)null);
    }

    [Test]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("-1")]
    [Arguments("+1")]
    [Arguments(" 1")]
    [Arguments("1 ")]
    [Arguments("1.5")]
    [Arguments("1,000")]
    [Arguments("0x1000")]
    [Arguments("18446744073709551616")]
    public async Task EveryConfiguredDomainRejectsMalformedOrNonPositiveCeilings(string configured)
    {
        await Assert.That(() => SilkTexturePreparationBudgets.FromConfiguration(configured, null, null))
            .Throws<ArgumentException>();
        await Assert.That(() => SilkTexturePreparationBudgets.FromConfiguration(null, configured, null))
            .Throws<ArgumentException>();
        await Assert.That(() => SilkTexturePreparationBudgets.FromConfiguration(null, null, configured))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UnconfiguredFactoryIsNullButAnExplicitEmptyPolicyIsRejected()
    {
        await Assert.That(SilkTexturePreparationBudgets.FromConfiguration(null, null, null)).IsNull();
        await Assert.That(() => new SilkTexturePreparationBudgets(null, null, null)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ConstructionRetainsTheCallersSharedPoolsWithoutReservingCredit()
    {
        var cpu = new SilkCpuTextureBudget(123);
        var textures = new SilkGpuTextureBudget(456);
        var staging = new SilkGpuStagingBudget(789);
        var budgets = new SilkTexturePreparationBudgets(cpu, textures, staging);
        await Assert.That(budgets.CpuTextureBudget).IsSameReferenceAs(cpu);
        await Assert.That(budgets.GpuTextureBudget).IsSameReferenceAs(textures);
        await Assert.That(budgets.GpuStagingBudget).IsSameReferenceAs(staging);
        await Assert.That(cpu.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(7)]
    public async Task UnsupportedDevicesCannotSilentlyIgnoreAnySelectedDomain(int domains)
    {
        var budgets = new SilkTexturePreparationBudgets(
            (domains & 1) != 0 ? new(1) : null,
            (domains & 2) != 0 ? new(1) : null,
            (domains & 4) != 0 ? new(1) : null);
        using var device = new SilkHdrColorReadbackDevice();
        await Assert.That(() => budgets.ConfigureDevice(device)).Throws<NotSupportedException>();
        await Assert.That(() => budgets.ConfigureDevice(null!)).Throws<ArgumentNullException>();
    }
}
