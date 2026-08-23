// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Physics.Baking;

namespace OpenUsd.Physics.Tests.Baking;

/// <summary>
/// Shared fixture helpers for the native-backed physics baking tests.
/// </summary>
internal static class BakeFixture
{
    private const ulong PhysicsBakeCapability = 1UL << 19;

    internal static bool HasCapability()
    {
        try
        {
            return (OpenUsdNativeRuntime.Capabilities & PhysicsBakeCapability) != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    internal static void SkipIfUnavailable()
    {
        if (!HasCapability())
        {
            Skip.Test("The native physics bake capability is not available.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }

    internal static string CreateWorkDirectory(string testName)
    {
        string? configured = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT");
        string root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "native-work")
            : configured;
        string directory =
            Path.Combine(root, "physics-bake", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Writes a stage whose root layer holds one xform and one mesh.</summary>
    internal static string WriteRootLayer(string directory, bool instanceable = false)
    {
        string path = Path.Combine(directory, "root.usda");
        string instanced = instanceable ? "\n        instanceable = true" : string.Empty;
        File.WriteAllText(
            path,
            "#usda 1.0\n" +
            "(\n" +
            "    timeCodesPerSecond = 24\n" +
            "    startTimeCode = 1\n" +
            "    endTimeCode = 3\n" +
            ")\n" +
            "\n" +
            "def Xform \"Body\"" + instanced + "\n" +
            "{\n" +
            "    def Xform \"Child\"\n" +
            "    {\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "def Mesh \"Cloth\"\n" +
            "{\n" +
            "    int[] faceVertexCounts = [3]\n" +
            "    int[] faceVertexIndices = [0, 1, 2]\n" +
            "    point3f[] points = [(0, 0, 0), (1, 0, 0), (0, 1, 0)]\n" +
            "}\n");
        return path;
    }

    /// <summary>Writes an empty file-backed destination layer next to the root layer.</summary>
    internal static string WriteDestinationLayer(string directory, string name = "bake.usda")
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "#usda 1.0\n");
        return path;
    }

    internal static UsdPhysicsObjectId BodyId { get; } =
        new(0x1001, UsdPhysicsObjectKind.RigidBody);

    internal static UsdPhysicsObjectId ClothId { get; } =
        new(0x2002, UsdPhysicsObjectKind.Deformable);

    internal static UsdPhysicsBakeBindings CreateBindings(ulong identityRevision = 7) =>
        new(
            identityRevision,
            [
                new UsdPhysicsBakeBinding(BodyId, "/Body"),
                new UsdPhysicsBakeBinding(ClothId, "/Cloth", -1, 3)
            ]);

    internal static UsdPhysicsBodyPose CreatePose(double x, double y, double z) =>
        new(
            BodyId,
            new UsdVec3d(x, y, z),
            UsdPhysicsOrientation.Identity,
            new UsdVec3d(1, 2, 3),
            new UsdVec3d(0.5, 0, 0),
            IsSleeping: false,
            IsKinematic: false);

    internal static UsdPhysicsPointSample CreateCloth(double lift, ulong topologyRevision = 3) =>
        new(
            ClothId,
            UsdPhysicsPointSampleDomain.Cloth,
            topologyRevision,
            [new UsdVec3d(0, lift, 0), new UsdVec3d(1, lift, 0), new UsdVec3d(0, 1 + lift, 0)],
            [new UsdVec3d(0, 1, 0), new UsdVec3d(0, 1, 0), new UsdVec3d(0, 1, 0)]);

    internal static UsdPhysicsResultBatch CreateBatch(
        double timeCode, double offset, ulong identityRevision = 7) =>
        new(
            identityRevision,
            timeCode,
            [CreatePose(offset, offset * 2, offset * 3)],
            [CreateCloth(offset)]);

    /// <summary>A deterministic bake source that offsets every sample by its time code.</summary>
    internal sealed class RampSource(ulong identityRevision = 7) : IUsdPhysicsBakeSource
    {
        public int RequestCount { get; private set; }

        public ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
            double timeCode, CancellationToken cancellationToken)
        {
            ++RequestCount;
            return ValueTask.FromResult<UsdPhysicsResultBatch?>(
                CreateBatch(timeCode, timeCode, identityRevision));
        }
    }
}
