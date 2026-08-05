// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Physics;

/// <summary>Runs optional PhysX simulation over authored UsdPhysics prims.</summary>
public static class UsdPhysicsSimulation
{
    /// <summary>Gets the PhysX SDK version reported by the optional native shim.</summary>
    public static string PhysxVersion => OpenUsdNativeRuntime.PhysxVersion;

    /// <summary>
    /// Saves the stage, simulates its UsdPhysics rigid bodies through PhysX, writes transforms
    /// to the root layer, and reloads the stage.
    /// </summary>
    public static void Step(UsdStage stage, float timeStep, uint stepCount = 1)
    {
        ArgumentNullException.ThrowIfNull(stage);
        stage.Save();
        OpenUsdNativeRuntime.SimulatePhysicsStageFile(stage.RootLayerIdentifier, timeStep, stepCount);
        stage.Reload();
    }
}
