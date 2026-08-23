// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Reports the outcome of negotiating the native physics ABI.
/// </summary>
internal sealed record PhysxRuntimeInfo(
    bool IsAvailable,
    PhysxAbiInfo Abi,
    PhysxCapabilitiesInfo Capabilities,
    UsdPhysicsCapabilities ManagedCapabilities,
    UsdPhysicsDiagnostics Diagnostics)
{
    /// <summary>Creates an unavailable runtime with one explanatory diagnostic.</summary>
    internal static PhysxRuntimeInfo Unavailable(string code, string message) =>
        new(
            false,
            default,
            default,
            UsdPhysicsCapabilities.None,
            new UsdPhysicsDiagnostics([
                new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Warning,
                    UsdPhysicsDiagnosticCategory.Capability,
                    code,
                    message)
            ]));
}

/// <summary>
/// Negotiates the native physics ABI exactly once per process.
/// </summary>
/// <remarks>
/// Negotiation is exact and fails closed. The managed mirror asserts its own record sizes, then
/// compares every one of them against the sizes the runtime reports, then compares the page magic,
/// the page alignment, and every declared limit. A runtime that differs in any of these is reported
/// as unavailable with a diagnostic instead of being used with reinterpreted memory. A missing
/// library, a missing entry point, or a wrong-architecture binary is reported the same way, so a
/// caller without the native runtime installed sees an honest capability answer rather than a
/// type initialization failure.
/// </remarks>
internal static class PhysxRuntime
{
    /// <summary>Reported when the native library cannot be loaded or does not export the ABI.</summary>
    internal const string UnavailableCode = "OPENUSD_PHYSICS_BACKEND_UNAVAILABLE";

    /// <summary>Reported when the native runtime does not match this managed ABI mirror exactly.</summary>
    internal const string MismatchCode = "OPENUSD_PHYSICS_ABI_MISMATCH";

    private static readonly Lazy<PhysxRuntimeInfo> Lazy = new(Negotiate, isThreadSafe: true);

    /// <summary>Gets the negotiated runtime information.</summary>
    internal static PhysxRuntimeInfo Info => Lazy.Value;

    /// <summary>Verifies that every managed record matches the size the ABI declares.</summary>
    /// <returns>An empty array when the managed mirror is self-consistent.</returns>
    internal static ImmutableArray<string> ValidateManagedLayout()
    {
        var mismatches = ImmutableArray.CreateBuilder<string>();
        Compare(mismatches, "transform", PhysxAbi.RecordSizes.Transform, Unsafe.SizeOf<PhysxTransform>());
        Compare(mismatches, "page span", PhysxAbi.RecordSizes.PageSpan, Unsafe.SizeOf<PhysxPageSpan>());
        Compare(mismatches, "vector", PhysxAbi.RecordSizes.Vec3f, Unsafe.SizeOf<PhysxVec3f>());
        Compare(
            mismatches,
            "result capacities",
            PhysxAbi.RecordSizes.ResultCapacities,
            Unsafe.SizeOf<PhysxResultCapacities>());
        Compare(
            mismatches,
            "build page header",
            PhysxAbi.RecordSizes.BuildPageHeader,
            Unsafe.SizeOf<PhysxBuildPageHeader>());
        Compare(mismatches, "identity", PhysxAbi.RecordSizes.Identity, Unsafe.SizeOf<PhysxIdentityRecord>());
        Compare(mismatches, "scene", PhysxAbi.RecordSizes.SceneDesc, Unsafe.SizeOf<PhysxSceneDesc>());
        Compare(mismatches, "material", PhysxAbi.RecordSizes.MaterialDesc, Unsafe.SizeOf<PhysxMaterialDesc>());
        Compare(mismatches, "shape", PhysxAbi.RecordSizes.ShapeDesc, Unsafe.SizeOf<PhysxShapeDesc>());
        Compare(mismatches, "actor", PhysxAbi.RecordSizes.ActorDesc, Unsafe.SizeOf<PhysxActorDesc>());
        Compare(
            mismatches,
            "actor shape reference",
            PhysxAbi.RecordSizes.ActorShapeRef,
            Unsafe.SizeOf<PhysxActorShapeRef>());
        Compare(mismatches, "joint", PhysxAbi.RecordSizes.JointDesc, Unsafe.SizeOf<PhysxJointDesc>());
        Compare(mismatches, "filter pair", PhysxAbi.RecordSizes.FilterPair, Unsafe.SizeOf<PhysxFilterPair>());
        Compare(mismatches, "command", PhysxAbi.RecordSizes.Command, Unsafe.SizeOf<PhysxCommand>());
        Compare(mismatches, "body state", PhysxAbi.RecordSizes.BodyState, Unsafe.SizeOf<PhysxBodyState>());
        Compare(mismatches, "event", PhysxAbi.RecordSizes.Event, Unsafe.SizeOf<PhysxEventRecord>());
        Compare(mismatches, "diagnostic", PhysxAbi.RecordSizes.Diagnostic, Unsafe.SizeOf<PhysxDiagnosticRecord>());
        Compare(mismatches, "debug line", PhysxAbi.RecordSizes.DebugLine, Unsafe.SizeOf<PhysxDebugLine>());
        Compare(mismatches, "result header", PhysxAbi.RecordSizes.ResultHeader, Unsafe.SizeOf<PhysxResultHeader>());
        Compare(mismatches, "query request", PhysxAbi.RecordSizes.QueryRequest, Unsafe.SizeOf<PhysxQueryRequest>());
        Compare(mismatches, "query hit", PhysxAbi.RecordSizes.QueryHit, Unsafe.SizeOf<PhysxQueryHit>());
        Compare(
            mismatches,
            "heightfield sample",
            PhysxAbi.RecordSizes.HeightfieldSample,
            Unsafe.SizeOf<PhysxHeightfieldSample>());
        Compare(
            mismatches,
            "articulation",
            PhysxAbi.RecordSizes.ArticulationDesc,
            Unsafe.SizeOf<PhysxArticulationDesc>());
        Compare(
            mismatches,
            "articulation link",
            PhysxAbi.RecordSizes.ArticulationLinkDesc,
            Unsafe.SizeOf<PhysxArticulationLinkDesc>());
        Compare(mismatches, "controller", PhysxAbi.RecordSizes.ControllerDesc, Unsafe.SizeOf<PhysxControllerDesc>());
        Compare(mismatches, "tendon", PhysxAbi.RecordSizes.TendonDesc, Unsafe.SizeOf<PhysxTendonDesc>());
        Compare(
            mismatches,
            "tendon node",
            PhysxAbi.RecordSizes.TendonNodeDesc,
            Unsafe.SizeOf<PhysxTendonNodeDesc>());
        Compare(
            mismatches,
            "mimic joint",
            PhysxAbi.RecordSizes.MimicJointDesc,
            Unsafe.SizeOf<PhysxMimicJointDesc>());
        Compare(mismatches, "vehicle", PhysxAbi.RecordSizes.VehicleDesc, Unsafe.SizeOf<PhysxVehicleDesc>());
        Compare(
            mismatches,
            "vehicle wheel",
            PhysxAbi.RecordSizes.VehicleWheelDesc,
            Unsafe.SizeOf<PhysxVehicleWheelDesc>());
        Compare(
            mismatches,
            "particle material",
            PhysxAbi.RecordSizes.ParticleMaterialDesc,
            Unsafe.SizeOf<PhysxParticleMaterialDesc>());
        Compare(
            mismatches,
            "particle system",
            PhysxAbi.RecordSizes.ParticleSystemDesc,
            Unsafe.SizeOf<PhysxParticleSystemDesc>());
        Compare(
            mismatches,
            "particle body",
            PhysxAbi.RecordSizes.ParticleBodyDesc,
            Unsafe.SizeOf<PhysxParticleBodyDesc>());
        Compare(
            mismatches,
            "deformable material",
            PhysxAbi.RecordSizes.DeformableMaterialDesc,
            Unsafe.SizeOf<PhysxDeformableMaterialDesc>());
        Compare(
            mismatches,
            "deformable",
            PhysxAbi.RecordSizes.DeformableDesc,
            Unsafe.SizeOf<PhysxDeformableDesc>());
        Compare(
            mismatches,
            "deformation state",
            PhysxAbi.RecordSizes.DeformationState,
            Unsafe.SizeOf<PhysxDeformationState>());
        return mismatches.ToImmutable();
    }

    /// <summary>Compares the sizes a runtime reports against this managed mirror.</summary>
    /// <returns>An empty array when the runtime matches exactly.</returns>
    internal static ImmutableArray<string> CompareWithNative(in PhysxAbiInfo info)
    {
        var mismatches = ImmutableArray.CreateBuilder<string>();
        if (info.AbiVersion != PhysxAbi.Version)
        {
            mismatches.Add(Format("abi version", PhysxAbi.Version, info.AbiVersion));
        }
        if (info.PageMagic != PhysxAbi.PageMagic)
        {
            mismatches.Add(Format("page magic", PhysxAbi.PageMagic, info.PageMagic));
        }
        if (info.PageAlignment != PhysxAbi.PageAlignment)
        {
            mismatches.Add(Format("page alignment", PhysxAbi.PageAlignment, info.PageAlignment));
        }

        Compare(mismatches, "build page header", PhysxAbi.RecordSizes.BuildPageHeader, (int)info.BuildPageHeaderSize);
        Compare(mismatches, "page span", PhysxAbi.RecordSizes.PageSpan, (int)info.PageSpanSize);
        Compare(mismatches, "result capacities", PhysxAbi.RecordSizes.ResultCapacities, (int)info.CapacitiesSize);
        Compare(mismatches, "identity", PhysxAbi.RecordSizes.Identity, (int)info.IdentitySize);
        Compare(mismatches, "scene", PhysxAbi.RecordSizes.SceneDesc, (int)info.SceneDescSize);
        Compare(mismatches, "material", PhysxAbi.RecordSizes.MaterialDesc, (int)info.MaterialDescSize);
        Compare(mismatches, "shape", PhysxAbi.RecordSizes.ShapeDesc, (int)info.ShapeDescSize);
        Compare(mismatches, "actor", PhysxAbi.RecordSizes.ActorDesc, (int)info.ActorDescSize);
        Compare(mismatches, "actor shape reference", PhysxAbi.RecordSizes.ActorShapeRef, (int)info.ActorShapeRefSize);
        Compare(mismatches, "joint", PhysxAbi.RecordSizes.JointDesc, (int)info.JointDescSize);
        Compare(mismatches, "filter pair", PhysxAbi.RecordSizes.FilterPair, (int)info.FilterPairSize);
        Compare(mismatches, "command", PhysxAbi.RecordSizes.Command, (int)info.CommandSize);
        Compare(mismatches, "body state", PhysxAbi.RecordSizes.BodyState, (int)info.BodyStateSize);
        Compare(mismatches, "event", PhysxAbi.RecordSizes.Event, (int)info.EventSize);
        Compare(mismatches, "diagnostic", PhysxAbi.RecordSizes.Diagnostic, (int)info.DiagnosticSize);
        Compare(mismatches, "debug line", PhysxAbi.RecordSizes.DebugLine, (int)info.DebugLineSize);
        Compare(mismatches, "result header", PhysxAbi.RecordSizes.ResultHeader, (int)info.ResultHeaderSize);
        Compare(mismatches, "query request", PhysxAbi.RecordSizes.QueryRequest, (int)info.QueryRequestSize);
        Compare(mismatches, "query hit", PhysxAbi.RecordSizes.QueryHit, (int)info.QueryHitSize);
        Compare(
            mismatches,
            "height field sample",
            PhysxAbi.RecordSizes.HeightfieldSample,
            (int)info.HeightfieldSampleSize);
        Compare(mismatches, "articulation", PhysxAbi.RecordSizes.ArticulationDesc, (int)info.ArticulationDescSize);
        Compare(
            mismatches,
            "articulation link",
            PhysxAbi.RecordSizes.ArticulationLinkDesc,
            (int)info.ArticulationLinkDescSize);
        Compare(mismatches, "controller", PhysxAbi.RecordSizes.ControllerDesc, (int)info.ControllerDescSize);
        Compare(mismatches, "tendon", PhysxAbi.RecordSizes.TendonDesc, (int)info.TendonDescSize);
        Compare(mismatches, "tendon node", PhysxAbi.RecordSizes.TendonNodeDesc, (int)info.TendonNodeDescSize);
        Compare(mismatches, "mimic joint", PhysxAbi.RecordSizes.MimicJointDesc, (int)info.MimicJointDescSize);
        Compare(mismatches, "vehicle", PhysxAbi.RecordSizes.VehicleDesc, (int)info.VehicleDescSize);
        Compare(mismatches, "vehicle wheel", PhysxAbi.RecordSizes.VehicleWheelDesc, (int)info.VehicleWheelDescSize);
        Compare(
            mismatches,
            "particle material",
            PhysxAbi.RecordSizes.ParticleMaterialDesc,
            (int)info.ParticleMaterialDescSize);
        Compare(
            mismatches,
            "particle system",
            PhysxAbi.RecordSizes.ParticleSystemDesc,
            (int)info.ParticleSystemDescSize);
        Compare(
            mismatches,
            "particle body",
            PhysxAbi.RecordSizes.ParticleBodyDesc,
            (int)info.ParticleBodyDescSize);
        Compare(
            mismatches,
            "deformable material",
            PhysxAbi.RecordSizes.DeformableMaterialDesc,
            (int)info.DeformableMaterialDescSize);
        Compare(mismatches, "deformable", PhysxAbi.RecordSizes.DeformableDesc, (int)info.DeformableDescSize);
        Compare(
            mismatches,
            "deformation state",
            PhysxAbi.RecordSizes.DeformationState,
            (int)info.DeformationStateSize);
        return mismatches.ToImmutable();
    }

    /// <summary>Compares the limits a runtime reports against the limits this mirror enforces.</summary>
    /// <returns>An empty array when every limit matches.</returns>
    internal static ImmutableArray<string> CompareLimits(in PhysxCapabilitiesInfo capabilities)
    {
        var mismatches = ImmutableArray.CreateBuilder<string>();
        if (capabilities.MaxScenes != PhysxAbi.MaxScenes)
        {
            mismatches.Add(Format("max scenes", PhysxAbi.MaxScenes, capabilities.MaxScenes));
        }
        if (capabilities.MaxCollisionGroups != PhysxAbi.MaxCollisionGroups)
        {
            mismatches.Add(Format(
                "max collision groups",
                PhysxAbi.MaxCollisionGroups,
                capabilities.MaxCollisionGroups));
        }
        if (capabilities.MinSimulationRateHz != PhysxAbi.MinSimulationRateHz)
        {
            mismatches.Add(Format(
                "min simulation rate",
                PhysxAbi.MinSimulationRateHz,
                capabilities.MinSimulationRateHz));
        }
        if (capabilities.MaxSimulationRateHz != PhysxAbi.MaxSimulationRateHz)
        {
            mismatches.Add(Format(
                "max simulation rate",
                PhysxAbi.MaxSimulationRateHz,
                capabilities.MaxSimulationRateHz));
        }
        if (capabilities.MaxSubsteps != PhysxAbi.MaxSubsteps)
        {
            mismatches.Add(Format("max substeps", PhysxAbi.MaxSubsteps, capabilities.MaxSubsteps));
        }
        if (capabilities.MaxResultCapacity != PhysxAbi.MaxResultCapacity)
        {
            mismatches.Add(Format("max result capacity", PhysxAbi.MaxResultCapacity, capabilities.MaxResultCapacity));
        }
        return mismatches.ToImmutable();
    }

    /// <summary>Maps native capability flags onto the public capability set.</summary>
    internal static UsdPhysicsCapabilities MapCapabilities(PhysxCapabilityFlags flags)
    {
        UsdPhysicsCapability features = UsdPhysicsCapability.None;
        if ((flags & PhysxCapabilityFlags.CpuRigidBodies) != 0)
        {
            // The retained world ABI always accepts the command batch of a step, so a runtime that
            // simulates rigid bodies also supports commands.
            features |= UsdPhysicsCapability.RigidBodies | UsdPhysicsCapability.Commands;
        }
        if ((flags & PhysxCapabilityFlags.SceneQueries) != 0)
        {
            features |= UsdPhysicsCapability.SceneQueries;
        }
        if ((flags & PhysxCapabilityFlags.Articulations) != 0)
        {
            features |= UsdPhysicsCapability.Articulations;
        }
        if ((flags & PhysxCapabilityFlags.CharacterControllers) != 0)
        {
            features |= UsdPhysicsCapability.Controllers;
        }
        if ((flags & PhysxCapabilityFlags.Vehicles) != 0)
        {
            features |= UsdPhysicsCapability.Vehicles;
        }
        if ((flags & PhysxCapabilityFlags.GpuDomains) != 0)
        {
            features |= UsdPhysicsCapability.Cuda;
        }
        // Each GPU domain is reported on its own because the runtime only
        // publishes it once a CUDA context has actually been created. A caller
        // that authors particles on a machine without a device therefore sees
        // the capability withdrawn rather than a build that silently drops
        // every particle system it declared.
        if ((flags & PhysxCapabilityFlags.ParticleSystems) != 0)
        {
            features |= UsdPhysicsCapability.Particles;
        }
        if ((flags & PhysxCapabilityFlags.SurfaceDeformables) != 0)
        {
            features |= UsdPhysicsCapability.Cloth;
        }
        if ((flags & PhysxCapabilityFlags.VolumeDeformables) != 0)
        {
            features |= UsdPhysicsCapability.Deformables;
        }
        return new UsdPhysicsCapabilities(features);
    }

    private static unsafe PhysxRuntimeInfo Negotiate()
    {
        ImmutableArray<string> layout = ValidateManagedLayout();
        if (layout.Length != 0)
        {
            return PhysxRuntimeInfo.Unavailable(
                MismatchCode,
                "The managed physics ABI mirror is inconsistent: " + string.Join("; ", layout) + ".");
        }

        byte* buffer = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(buffer, PhysxErrorScope.DefaultCapacity);
        var abi = new PhysxAbiInfo { StructSize = (uint)Unsafe.SizeOf<PhysxAbiInfo>() };
        PhysxStatus status;
        try
        {
            status = PhysxNativeMethods.WorldGetAbi(ref abi, ref error);
        }
        catch (DllNotFoundException exception)
        {
            return PhysxRuntimeInfo.Unavailable(UnavailableCode, Unavailable(exception));
        }
        catch (EntryPointNotFoundException exception)
        {
            return PhysxRuntimeInfo.Unavailable(UnavailableCode, Unavailable(exception));
        }
        catch (BadImageFormatException exception)
        {
            return PhysxRuntimeInfo.Unavailable(UnavailableCode, Unavailable(exception));
        }

        if (status != PhysxStatus.Ok)
        {
            return PhysxRuntimeInfo.Unavailable(
                MismatchCode,
                "The native physics runtime rejected ABI negotiation: " +
                    PhysxErrorScope.Describe(status, in error));
        }

        ImmutableArray<string> mismatches = CompareWithNative(in abi);
        if (mismatches.Length != 0)
        {
            return PhysxRuntimeInfo.Unavailable(
                MismatchCode,
                "The native physics runtime does not match this ABI mirror: " +
                    string.Join("; ", mismatches) + ".");
        }

        var capabilities = new PhysxCapabilitiesInfo { StructSize = (uint)Unsafe.SizeOf<PhysxCapabilitiesInfo>() };
        status = PhysxNativeMethods.WorldGetCapabilities(PhysxAbi.Version, ref capabilities, ref error);
        if (status != PhysxStatus.Ok)
        {
            return PhysxRuntimeInfo.Unavailable(
                MismatchCode,
                "The native physics runtime rejected capability negotiation: " +
                    PhysxErrorScope.Describe(status, in error));
        }

        ImmutableArray<string> limits = CompareLimits(in capabilities);
        if (limits.Length != 0)
        {
            return PhysxRuntimeInfo.Unavailable(
                MismatchCode,
                "The native physics runtime declares different limits: " + string.Join("; ", limits) + ".");
        }

        return new PhysxRuntimeInfo(
            true,
            abi,
            capabilities,
            MapCapabilities((PhysxCapabilityFlags)capabilities.Flags),
            UsdPhysicsDiagnostics.Empty);
    }

    private static string Unavailable(Exception exception) =>
        "The native physics runtime '" + PhysxAbi.LibraryName + "' is not available: " + exception.Message;

    private static void Compare(ImmutableArray<string>.Builder mismatches, string name, int expected, int actual)
    {
        if (expected != actual)
        {
            mismatches.Add(Format(name, (ulong)expected, (ulong)actual));
        }
    }

    private static string Format(string name, ulong expected, ulong actual) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} expected {expected} but the runtime reported {actual}");
}
