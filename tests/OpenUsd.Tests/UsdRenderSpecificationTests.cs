// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Render;

namespace OpenUsd.Tests;

public sealed class UsdRenderSpecificationTests
{
    [Test]
    public async Task SnapshotCopiesEveryCollectionAndCanLeaveTheStageScheduler()
    {
        string[] variableSettings = ["renderer:filter"];
        var variable = new UsdRenderVariableSpecification(
            "/Vars/Beauty", "color3f", "Ci", "raw", variableSettings);
        int[] indices = [0, 0];
        string[] productSettings = ["renderer:compression"];
        var product = new UsdRenderProductSpecification(
            "/Products/Beauty", "beauty.exr", "raster", "/Camera", 640, 480, 1,
            "expandAperture", new UsdVec2f(24, 18), new UsdVec4f(0, 0, 1, 1),
            false, true, indices, productSettings);
        UsdRenderProductSpecification[] products = [product];
        UsdRenderVariableSpecification[] variables = [variable];
        string[] purposes = ["default", "render"];
        string[] bindingPurposes = ["full"];
        string[] settings = ["renderer:samples"];
        var snapshot = new UsdRenderSpecification(
            "/Settings", products, variables, purposes, bindingPurposes, "lin_rec709", settings);

        variableSettings[0] = "changed";
        productSettings[0] = "changed";
        indices[0] = 99;
        products[0] = null!;
        variables[0] = null!;
        purposes[0] = "changed";
        bindingPurposes[0] = "changed";
        settings[0] = "changed";

        UsdStageBoundResultGuard.ThrowIfForbiddenResult(snapshot);
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(product);
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(variable);
        await Assert.That(snapshot.Products[0].RenderVariableIndices.ToArray()).IsEquivalentTo([0, 0]);
        await Assert.That(snapshot.RenderVariables[0].NamespacedSettingNames[0]).IsEqualTo("renderer:filter");
        await Assert.That(snapshot.Products[0].NamespacedSettingNames[0]).IsEqualTo("renderer:compression");
        await Assert.That(snapshot.IncludedPurposes[0]).IsEqualTo("default");
        await Assert.That(snapshot.MaterialBindingPurposes[0]).IsEqualTo("full");
        await Assert.That(snapshot.NamespacedSettingNames[0]).IsEqualTo("renderer:samples");
        object[] publishedCollections =
        [
            snapshot.Products, snapshot.RenderVariables, snapshot.IncludedPurposes,
            snapshot.MaterialBindingPurposes, snapshot.NamespacedSettingNames,
            product.RenderVariableIndices, product.NamespacedSettingNames, variable.NamespacedSettingNames
        ];
        foreach (object collection in publishedCollections)
        {
            await Assert.That(collection is System.Collections.ICollection).IsFalse()
                .Because("SyncRoot must not expose storage that can invalidate the detached specification");
        }
        await Assert.That(() => ((IList<int>)product.RenderVariableIndices)[0] = 1)
            .Throws<InvalidCastException>();
        await Assert.That(() => ((IList<string>)snapshot.IncludedPurposes)[0] = "changed")
            .Throws<InvalidCastException>();
    }
}
