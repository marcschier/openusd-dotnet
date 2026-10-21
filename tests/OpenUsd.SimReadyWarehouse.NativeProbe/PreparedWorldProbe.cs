// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUsd.Physics;
using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

internal static class PreparedWorldProbe
{
    internal static int Run(string path, string? dynamicPrim = null)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > (long)PhysicsExtractAbi.PageMaxBytes)
        {
            throw new ArgumentException("An existing bounded prepared page is required.", nameof(path));
        }
        byte[] bytes = File.ReadAllBytes(path);
        UsdPhysicsExtractionPage page = UsdPhysicsExtractionPage.Adopt(bytes);
        var report = new PreparedWorldReport
        {
            SourcePage = file.FullName,
            SourcePageSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            SourceFingerprint = $"{page.FingerprintHigh:X16}{page.FingerprintLow:X16}",
            DynamicPrim = dynamicPrim,
        };
        using var world = new UsdPhysicsNativeWorld { ExtractionPage = page };
        var timeline = new UsdPhysicsTimeline(page.TimeCodesPerSecond, page.StartTimeCode, page.EndTimeCode);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, 60);
        var options = new UsdPhysicsSessionOptions(maxRigidBodies: 32768, maxColliders: 65536);
        UsdPhysicsWorldBuildResult build = world.Build(timeline, step,
            options, CancellationToken.None);
        report.Diagnostics.AddRange(build.Diagnostics.Entries.Select(static item => item.Code + ": " + item.Message));
        report.BuildSucceeded = build.Succeeded;
        if (build.Succeeded)
        {
            (PhysxShapeSnapshotInfo info, PhysxShapeIdentity[] shapes) = world.InspectRigidShapes();
            report.ActualActors = info.ActorCount;
            report.ActualShapes = info.ShapeCount;
            report.UniqueConvexMeshes = info.ConvexMeshCount;
            report.UniqueTriangleMeshes = info.TriangleMeshCount;
            var expected = new HashSet<(ulong Actor, ulong Shape)>();
            for (int index = 0; index < page.ObjectCount; index++)
            {
                UsdPhysicsExtractionObject collider = page.GetObject(index);
                if (collider.Kind != UsdPhysicsExtractionObjectKind.Collider || !collider.IsEnabled)
                {
                    continue;
                }
                UsdPhysicsExtractionObject owner = collider.ParentBodyIndex >= 0
                    ? page.GetObject(collider.ParentBodyIndex) : collider;
                if (!owner.IsEnabled)
                {
                    continue;
                }
                report.ExpectedColliders++;
                ulong actor = UsdPhysicsIdentities.ForSimulatedObject(owner.Path, UsdPhysicsObjectKind.RigidBody).Value;
                for (int hull = 0; hull < Math.Max(1, collider.HullCount); hull++)
                {
                    string address = hull == 0 ? collider.Path + ".shape" :
                        collider.Path + ".shape.hull" + hull.ToString(CultureInfo.InvariantCulture);
                    ulong shape = PhysxIdentity.Compute(address, PhysxInstanceDomain.Prim, 0);
                    if (!expected.Add((actor, shape)))
                    {
                        throw new InvalidDataException("Source collider identities are not unique.");
                    }
                }
            }
            var actual = new HashSet<(ulong Actor, ulong Shape)>();
            foreach (PhysxShapeIdentity shape in shapes)
            {
                if (!actual.Add((shape.ActorId, shape.ShapeId)))
                {
                    report.DuplicateShapes++;
                }
                report.ShapeIdentities.Add(new WorldShapeIdentity(shape.ActorId, shape.ShapeId));
            }
            report.ExpectedShapes = expected.Count;
            report.MissingShapes = expected.Except(actual).Count();
            report.UnexpectedShapes = actual.Except(expected).Count();
            var frame = new UsdPhysicsFrame(Math.Max(build.BodyCapacity, 1),
                build.DeformationCapacity, build.DeformationVertexCapacity);
            report.Stepped = dynamicPrim is null
                ? world.TryStep(step.Seconds, 1, frame)
                : InspectMotion(world, page, step, frame, report, dynamicPrim);
            report.PublishedBodies = frame.BodyCount;
            world.ResetToStart();
            (_, PhysxShapeIdentity[] reset) = world.InspectRigidShapes();
            report.ResetPreservedShapes = reset.Length == shapes.Length &&
                reset.Select(static item => (item.ActorId, item.ShapeId)).ToHashSet().SetEquals(actual);
            if (dynamicPrim is not null)
            {
                if (!world.TryFetch(frame))
                {
                    throw new InvalidOperationException("The reset world did not publish its restored body.");
                }
                WorldBodySample restored = Sample(world, frame, dynamicPrim, 0, step.Seconds);
                WorldBodySample initial = report.MotionSamples[0];
                report.ResetPreservedMotion = restored.PositionDistance(initial) < 1e-5 &&
                    restored.VelocityDistance(initial) < 1e-5 &&
                    Math.Abs(restored.Qx - initial.Qx) < 1e-5 &&
                    Math.Abs(restored.Qy - initial.Qy) < 1e-5 &&
                    Math.Abs(restored.Qz - initial.Qz) < 1e-5 &&
                    Math.Abs(restored.Qw - initial.Qw) < 1e-5;
            }
            report.Complete = report.Stepped && report.ResetPreservedShapes &&
                (dynamicPrim is null || (report.MotionAccepted && report.ResetPreservedMotion)) &&
                report.MissingShapes == 0 && report.UnexpectedShapes == 0 && report.DuplicateShapes == 0 &&
                !build.Diagnostics.Entries.Any(static item =>
                    item.Severity == UsdPhysicsDiagnosticSeverity.Error ||
                    item.Code == "OPENUSD_PHYSICS_COMPOSITION_SKIPPED");
        }
        Console.WriteLine(JsonSerializer.Serialize(report, PreparedWorldJsonContext.Default.PreparedWorldReport));
        return report.Complete ? 0 : 1;
    }

    private static bool InspectMotion(
        UsdPhysicsNativeWorld world, UsdPhysicsExtractionPage page, UsdPhysicsFixedStep step,
        UsdPhysicsFrame frame, PreparedWorldReport report, string path)
    {
        if (page.UpAxis != UsdPhysicsExtractionUpAxis.Z || page.MetersPerUnit != 1 ||
            page.KilogramsPerUnit != 1 || page.TimeCodesPerSecond != 60 ||
            page.StartTimeCode != 0 || page.EndTimeCode < 240)
        {
            throw new InvalidDataException("The warehouse trajectory profile requires explicit metre/kg/Z-up, 60-Hz, 0..240 metadata.");
        }
        if (!world.TryFetch(frame))
        {
            throw new InvalidOperationException("The world did not publish the initial dynamic body.");
        }
        WorldBodySample initial = Sample(world, frame, path, 0, step.Seconds);
        report.MotionSamples.Add(initial);
        for (int index = 1; index <= 240; index++)
        {
            if (!world.TryStep(step.Seconds, 1, frame))
            {
                throw new InvalidOperationException($"The dynamic world failed at fixed step {index}.");
            }
            WorldBodySample sample = Sample(world, frame, path, index, step.Seconds);
            report.MotionSamples.Add(sample);
            report.MaximumRiseMetres = Math.Max(report.MaximumRiseMetres, sample.Z - initial.Z);
        }
        WorldBodySample final = report.MotionSamples[^1];
        report.FinalSpeedMetresPerSecond = Math.Sqrt(final.Vx * final.Vx + final.Vy * final.Vy + final.Vz * final.Vz);
        report.MotionAccepted = Math.Abs(initial.Vx) < 1e-5 && Math.Abs(initial.Vy) < 1e-5 &&
            Math.Abs(initial.Vz - 2) < 1e-5 && report.MaximumRiseMetres > 0.03 &&
            report.MaximumRiseMetres < 1 && final.Z > initial.Z - 2 &&
            report.FinalSpeedMetresPerSecond < 0.1;
        return true;
    }

    private static WorldBodySample Sample(
        UsdPhysicsNativeWorld world, UsdPhysicsFrame frame, string path, int step, double secondsPerStep)
    {
        ulong id = UsdPhysicsIdentities.ForSimulatedObject(path, UsdPhysicsObjectKind.RigidBody).Value;
        if (frame.BodyCount != 1 || frame.Bodies[0].Id.Value != id || frame.Bodies[0].IsKinematic ||
            frame.HasOverflow || frame.BodiesTruncated || frame.DeformationsTruncated ||
            frame.DeformationCount != 0 || frame.DroppedEventCount != 0)
        {
            throw new InvalidDataException("The trajectory must publish exactly the selected dynamic body without overflow.");
        }
        PhysxWorldStatusInfo status = world.InspectStatus();
        if (status.StepIndex != (ulong)step || Math.Abs(status.SimulationTime - step * secondsPerStep) > 1e-9)
        {
            throw new InvalidDataException("The actual solver clock does not match the requested fixed step.");
        }
        UsdPhysicsBodyPose body = frame.Bodies[0];
        return new WorldBodySample(step, status.SimulationTime,
            body.Position.X, body.Position.Y, body.Position.Z,
            body.LinearVelocity.X, body.LinearVelocity.Y, body.LinearVelocity.Z,
            body.Orientation.X, body.Orientation.Y, body.Orientation.Z, body.Orientation.W);
    }
}

internal sealed class PreparedWorldReport
{
    public string Phase { get; } = "after-physics-world";
    public string Scope => DynamicPrim is null
        ? "Actual rigid collider construction, identity reconciliation and reset; not dynamic-scene or rendering acceptance."
        : "Complete collider world with one real dynamic prop, actual fixed-step trajectory and reset; not rendering or scenario-removal acceptance.";
    public int MaxRigidBodies { get; } = 32768;
    public int MaxColliders { get; } = 65536;
    public string SourcePage { get; init; } = string.Empty;
    public string SourcePageSha256 { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public bool BuildSucceeded { get; set; }
    public bool Complete { get; set; }
    public uint ActualActors { get; set; }
    public uint ActualShapes { get; set; }
    public uint UniqueConvexMeshes { get; set; }
    public uint UniqueTriangleMeshes { get; set; }
    public int ExpectedColliders { get; set; }
    public int ExpectedShapes { get; set; }
    public int MissingShapes { get; set; }
    public int UnexpectedShapes { get; set; }
    public int DuplicateShapes { get; set; }
    public bool Stepped { get; set; }
    public int PublishedBodies { get; set; }
    public bool ResetPreservedShapes { get; set; }
    public string? DynamicPrim { get; init; }
    public bool MotionAccepted { get; set; }
    public bool ResetPreservedMotion { get; set; }
    public double MaximumRiseMetres { get; set; }
    public double FinalSpeedMetresPerSecond { get; set; }
    public List<WorldBodySample> MotionSamples { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public List<WorldShapeIdentity> ShapeIdentities { get; } = [];
}

internal readonly record struct WorldShapeIdentity(ulong Actor, ulong Shape);

internal readonly record struct WorldBodySample(
    int Step, double Seconds, double X, double Y, double Z,
    double Vx, double Vy, double Vz, double Qx, double Qy, double Qz, double Qw)
{
    public double PositionDistance(WorldBodySample other) =>
        Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z));

    public double VelocityDistance(WorldBodySample other) =>
        Math.Sqrt((Vx - other.Vx) * (Vx - other.Vx) + (Vy - other.Vy) * (Vy - other.Vy) + (Vz - other.Vz) * (Vz - other.Vz));
}

[JsonSerializable(typeof(PreparedWorldReport))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PreparedWorldJsonContext : JsonSerializerContext;
