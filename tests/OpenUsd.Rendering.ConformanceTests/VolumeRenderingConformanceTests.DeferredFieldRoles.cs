// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class VolumeRenderingConformanceTests
{
    [Test]
    [Arguments("temperature")]
    [Arguments("emission")]
    [Arguments("color")]
    [Arguments("velocity")]
    public async Task DeferredFieldRolesRefusePublicationBeforeRetainingDensity(string role)
    {
        PrependHdSilkNativeSearchPath();
        Directory.CreateDirectory(StageDirectory);
        string path = Path.Combine(StageDirectory, $"deferred-role-{role}-{Guid.NewGuid():N}.usda");
        string source = $$"""
            #usda 1.0
            def Xform "World"
            {
                def Volume "Volume"
                {
                    custom bool hdsilk:sampleDensity = true
                    rel field:density = </World/Volume/Density>
                    rel field:{{role}} = </World/Volume/Other>
                    def OpenVDBAsset "Density"
                    {
                        asset filePath = @missing-density.vdb@
                        token fieldName = "density"
                    }
                    def OpenVDBAsset "Other"
                    {
                        asset filePath = @missing-other.vdb@
                        token fieldName = "{{role}}"
                    }
                }
            }
            """;
        File.WriteAllText(path, source, new UTF8Encoding(false));
        try
        {
            using OpenUsdSilkSession session = CreateAvailableFieldSession(path);
            using var budget = new SilkVolumeFieldBudget(1024);
            session.ConfigureVolumeFieldBudget(budget);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                OpenUsdSilkException? error = await Assert.That(() =>
                {
                    using OpenUsdSilkPage page = session.Sync(Width, Height, 0);
                }).Throws<OpenUsdSilkException>();
                await Assert.That(error!.Message).Contains($"unsupported field role '{role}'");
                await Assert.That(error.Message).Contains("/World/Volume");
                await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0UL);
                await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0UL);
                await Assert.That(File.ReadAllText(path)).IsEqualTo(source);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
