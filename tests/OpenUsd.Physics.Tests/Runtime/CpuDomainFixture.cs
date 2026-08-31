// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Authors a stage on disk and drives it through the whole physics chain: native extraction,
/// composition onto the world build page, retained native world build, and fixed stepping.
/// </summary>
/// <remarks>
/// <para>
/// A hand built page can only prove the managed mirror agrees with itself. This fixture exists so
/// the CPU domain tests assert on what an authored stage actually simulates, which is the only way
/// a regression anywhere along the chain - extractor, composer, ABI, or world - can be caught.
/// </para>
/// <para>
/// The fixture is inert when the native runtime is not staged, so the managed suite stays runnable
/// on a machine with no compiled native tree.
/// </para>
/// </remarks>
internal sealed class CpuDomainFixture : IDisposable
{
    private static readonly object Sync = new();
    private static bool _registered;

    private readonly string _directory;
    private readonly string _stagePath;
    private UsdPhysicsExtractionPage? _extraction;
    private bool _disposed;

    private CpuDomainFixture(string directory, string stagePath)
    {
        _directory = directory;
        _stagePath = stagePath;
    }

    /// <summary>
    /// Gets a value indicating whether the runner declared that the physics runtime is staged.
    /// </summary>
    /// <remarks>
    /// The native managed test runner sets this, so a run that was supposed to exercise the native
    /// chain fails loudly instead of quietly reporting a suite of skipped tests as a pass.
    /// </remarks>
    internal static bool RuntimeRequired =>
        Environment.GetEnvironmentVariable("OPENUSD_REQUIRE_NATIVE_PHYSICS") is "1" or "true" or "True";

    /// <summary>
    /// Skips the calling test when the native chain is not staged, and fails it when the runner
    /// declared that it is.
    /// </summary>
    internal static void RequireRuntime()
    {
        if (Unavailable() is not { } reason)
        {
            return;
        }

        if (RuntimeRequired)
        {
            throw new InvalidOperationException(
                "OPENUSD_REQUIRE_NATIVE_PHYSICS declares that the native physics chain is staged for " +
                $"this run, so this test must not be skipped, but {reason}");
        }

        Skip.Test($"The native physics chain is not staged: {reason}");
        throw new InvalidOperationException("Skip.Test returned unexpectedly.");
    }

    /// <summary>Describes why the native chain cannot run, or returns null when it can.</summary>
    private static string? Unavailable()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH")))
        {
            return "OPENUSD_TEST_PLUGIN_PATH names no staged OpenUSD plugin directory.";
        }

        try
        {
            _ = OpenUsdNativeRuntime.AbiVersion;
        }
        catch (DllNotFoundException exception)
        {
            return $"the OpenUSD shim could not be loaded: {exception.Message}";
        }
        catch (EntryPointNotFoundException exception)
        {
            return $"the OpenUSD shim does not export the expected ABI: {exception.Message}";
        }

        PhysxRuntimeInfo info = PhysxRuntime.Info;
        if (info.IsAvailable)
        {
            return null;
        }

        // The physics runtime reports exactly why it refused, which is the difference between a
        // library that is missing and a library whose ABI does not match this managed mirror.
        string detail = string.Join(
            " ", info.Diagnostics.Entries.Select(entry => $"[{entry.Code}] {entry.Message}"));
        return detail.Length > 0
            ? $"the physics runtime is unavailable: {detail}"
            : "the physics runtime is unavailable.";
    }

    /// <summary>Creates a fixture with its own private work directory.</summary>
    internal static CpuDomainFixture Create(string testName)
    {
        EnsurePlugins();
        string root = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "native-work");
        string directory = Path.Combine(root, "physics-cpu", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new CpuDomainFixture(directory, Path.Combine(directory, "scene.usda"));
    }

    /// <summary>Writes the authored stage this fixture extracts from.</summary>
    internal void WriteStage(string usda)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(usda);
        File.WriteAllText(_stagePath, usda);
        _extraction = null;
    }

    /// <summary>Extracts the authored stage once and caches the resulting page.</summary>
    /// <remarks>
    /// The page is immutable and is safe to compose from many threads at once, which is what lets
    /// the concurrency test build several worlds from one extraction. Opening a stage is
    /// serialized instead, because the test host runs whole tests in parallel and the USD stage
    /// cache and plugin registry behind <see cref="UsdStage.Open(string)"/> are process wide.
    /// </remarks>
    internal UsdPhysicsExtractionPage Extract()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_extraction is not null)
        {
            return _extraction;
        }

        lock (Sync)
        {
            using UsdStage stage = UsdStage.Open(_stagePath);
            _extraction = UsdPhysicsStageExtractor.Extract(stage, UsdPhysicsExtractionOptions.Default);
        }

        return _extraction;
    }

    /// <summary>Extracts, composes, and builds a retained native world from the authored stage.</summary>
    internal UsdPhysicsSimulation BuildSimulation() => UsdPhysicsSimulation.Create(Extract());

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _extraction = null;

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover work directory is never worth failing a passing test over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void EnsurePlugins()
    {
        lock (Sync)
        {
            if (_registered)
            {
                return;
            }

            string? pluginPath = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(pluginPath))
            {
                throw new InvalidOperationException(
                    "The OpenUSD native runtime is not staged. Run eng/run-native-managed-tests.ps1.");
            }

            _ = OpenUsdNativeRuntime.RegisterPlugins(pluginPath);
            _registered = true;
        }
    }
}

/// <summary>
/// One built native world plus the preallocated frame the CPU domain tests step it with.
/// </summary>
internal sealed class UsdPhysicsSimulation : IDisposable
{
    private bool _disposed;

    private UsdPhysicsSimulation(
        UsdPhysicsNativeWorld world,
        UsdPhysicsWorldBuildResult build,
        UsdPhysicsCompositionReport? composition,
        UsdPhysicsFrame frame,
        double stepSeconds)
    {
        World = world;
        Build = build;
        Composition = composition;
        Frame = frame;
        StepSeconds = stepSeconds;
    }

    /// <summary>Gets the retained native world.</summary>
    internal UsdPhysicsNativeWorld World { get; }

    /// <summary>Gets the result of the build that created this world.</summary>
    internal UsdPhysicsWorldBuildResult Build { get; }

    /// <summary>Gets the report of the composition the build ran, if a stage was attached.</summary>
    internal UsdPhysicsCompositionReport? Composition { get; }

    /// <summary>Gets the reused result frame.</summary>
    internal UsdPhysicsFrame Frame { get; }

    /// <summary>Gets the fixed step in seconds.</summary>
    internal double StepSeconds { get; }

    /// <summary>Builds a world from an extraction page at a fixed sixty hertz.</summary>
    internal static UsdPhysicsSimulation Create(UsdPhysicsExtractionPage extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        var timeline = new UsdPhysicsTimeline(24, 0, 24);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, 60);
        var world = new UsdPhysicsNativeWorld { ExtractionPage = extraction };
        try
        {
            UsdPhysicsWorldBuildResult build = world.Build(
                timeline,
                step,
                UsdPhysicsSessionOptions.Default,
                CancellationToken.None);

            var frame = new UsdPhysicsFrame(
                Math.Max(build.BodyCapacity, 1),
                build.DeformationCapacity,
                build.DeformationVertexCapacity);
            return new UsdPhysicsSimulation(world, build, world.LastComposition, frame, step.Seconds);
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    /// <summary>Advances the world by whole fixed steps and returns the simulated seconds.</summary>
    internal double Step(int steps) => Step(steps, default);

    /// <summary>Advances the world by whole fixed steps, resubmitting one command batch each step.</summary>
    internal double Step(int steps, ReadOnlyMemory<PhysxCommand> commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int index = 0; index < steps; index++)
        {
            if (!World.TryStep(StepSeconds, 1, commands.Span, Frame))
            {
                throw new InvalidOperationException(
                    $"The native world refused step {index}: " +
                    string.Join(
                        "; ",
                        World.DrainDiagnostics().Entries.Select(static entry => $"{entry.Code}: {entry.Message}")));
            }
        }

        return steps * StepSeconds;
    }

    /// <summary>Builds one vehicle input command for a composed vehicle prim.</summary>
    internal static PhysxCommand VehicleInput(
        string vehiclePrimPath,
        float throttle,
        float brake,
        float steer,
        float handBrake = 0.0F,
        uint gear = 0)
    {
        ArgumentNullException.ThrowIfNull(vehiclePrimPath);
        ulong id = PhysxIdentity.Compute(
            vehiclePrimPath + ".vehicle", PhysxInstanceDomain.Prim, 0);

        return new PhysxCommand
        {
            TargetId = id,
            Type = (uint)PhysxCommandType.VehicleInput,
            Flags = 0,
            Vector = new PhysxVec3f(throttle, brake, steer),
            Point = new PhysxVec3f(handBrake, 0.0F, gear)
        };
    }

    /// <summary>Builds one move command for a composed character controller prim.</summary>
    internal static PhysxCommand ControllerMove(string controllerPrimPath, float x, float y, float z)
    {
        ArgumentNullException.ThrowIfNull(controllerPrimPath);
        ulong id = PhysxIdentity.Compute(
            controllerPrimPath + ".controller", PhysxInstanceDomain.Prim, 0);

        return new PhysxCommand
        {
            TargetId = id,
            Type = (uint)PhysxCommandType.MoveController,
            Flags = 0,
            Vector = new PhysxVec3f(x, y, z),
            Point = default
        };
    }

    /// <summary>Finds the published pose of an authored prim, or throws with the paths that were published.</summary>
    internal UsdPhysicsBodyPose RequirePose(string primPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UsdPhysicsObjectId expected = UsdPhysicsIdentities.FromPrimPath(primPath, UsdPhysicsObjectKind.RigidBody);
        foreach (UsdPhysicsBodyPose pose in Frame.Bodies)
        {
            if (pose.Id.Value == expected.Value)
            {
                return pose;
            }
        }

        throw new InvalidOperationException(
            $"The world published no body for '{primPath}'. It published {Frame.BodyCount} body/bodies: " +
            string.Join(", ", Frame.Bodies.ToArray().Select(static pose => pose.Id.ToString())));
    }

    /// <summary>Gets a value indicating whether the world published a body for an authored prim.</summary>
    internal bool HasPose(string primPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UsdPhysicsObjectId expected = UsdPhysicsIdentities.FromPrimPath(primPath, UsdPhysicsObjectKind.RigidBody);
        foreach (UsdPhysicsBodyPose pose in Frame.Bodies)
        {
            if (pose.Id.Value == expected.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the published deformation window of an authored prim, if it has one.</summary>
    internal UsdPhysicsDeformation? FindDeformation(string primPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(primPath);
        ulong expected = PhysxIdentity.Compute(primPath, PhysxInstanceDomain.Prim, 0);
        foreach (UsdPhysicsDeformation deformation in Frame.Deformations)
        {
            if (deformation.Id.Value == expected)
            {
                return deformation;
            }
        }

        return null;
    }

    /// <summary>Copies one published deformation window out of the reused frame.</summary>
    internal UsdVec3d[] CaptureDeformation(in UsdPhysicsDeformation deformation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Frame.DeformationVertices
            .Slice(deformation.VertexOffset, deformation.VertexCount)
            .ToArray();
    }

    /// <summary>Sums the squared distance between two captured vertex windows.</summary>
    internal static double Displacement(UsdVec3d[] before, UsdVec3d[] after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        int count = Math.Min(before.Length, after.Length);
        double total = 0.0;
        for (int index = 0; index < count; index++)
        {
            double dx = after[index].X - before[index].X;
            double dy = after[index].Y - before[index].Y;
            double dz = after[index].Z - before[index].Z;
            total += (dx * dx) + (dy * dy) + (dz * dz);
        }

        return total;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        World.Dispose();
    }
}
