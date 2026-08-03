// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using System.Text.RegularExpressions;
using OpenUsd.Interop;

namespace OpenUsd.Native.Tests;

public sealed class NativeAbiVersionTests
{
    [Test]
    public async Task NativePlatformContractsHandleX11MacrosAndUnavailableWgl()
    {
        string repositoryRoot = FindRepositoryRoot();
        string hydraImplementation = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_hydra",
            "src",
            "openusd_hydra.cpp"));
        string hydraTests = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_hydra",
            "tests",
            "CMakeLists.txt"));
        string childTests = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_storm_child",
            "tests",
            "CMakeLists.txt"));
        string hydraProbe = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_hydra",
            "tests",
            "storm_wgl_shared_stage_probe.cpp"));
        string childProbe = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_storm_child",
            "tests",
            "storm_child_probe.cpp"));
        string macChildProbe = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_storm_child",
            "tests",
            "storm_child_probe_macos.mm"));
        string linuxChildProbe = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_storm_child",
            "tests",
            "storm_child_probe_linux.cpp"));
        string nativeWorkflow = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "native.yml"));
        string packageWorkflow = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "package.yml"));

        await Assert.That(hydraImplementation)
            .Contains("openusd_status RetainStatus() const noexcept");
        await Assert.That(hydraImplementation)
            .DoesNotContain("openusd_status Status() const noexcept");
        await Assert.That(Regex.Count(
            hydraTests,
            @"SKIP_RETURN_CODE 125",
            RegexOptions.CultureInvariant)).IsEqualTo(2);
        await Assert.That(Regex.Count(
            childTests,
            @"SKIP_RETURN_CODE 125",
            RegexOptions.CultureInvariant)).IsEqualTo(3);
        await Assert.That(hydraProbe)
            .Contains("constexpr int CapabilityUnavailableExitCode = 125;");
        await Assert.That(hydraProbe)
            .Contains("FramebufferCreationResult::Unsupported");
        await Assert.That(hydraProbe)
            .Contains("FramebufferCreationResult::Incomplete");
        await Assert.That(childProbe)
            .Contains("constexpr int CapabilityUnavailableExitCode = 125;");
        await Assert.That(childProbe)
            .Contains("\"WGL_ARB_create_context is unavailable.\"");
        await Assert.That(macChildProbe)
            .Contains("constexpr int CapabilityUnavailableExitCode = 125;");
        await Assert.That(macChildProbe)
            .Contains("\"macOS could not create the OpenGL 4.1 core pixel format.\"");
        // Linux is the third platform that can report an unusable context rather
        // than stalling in Storm initialization, so it must be able to produce the
        // capability exit code the CMake skip property matches on.
        await Assert.That(linuxChildProbe)
            .Contains("constexpr int CapabilityUnavailableExitCode = 125;");
        await Assert.That(linuxChildProbe).Contains("glXIsDirect");
        await Assert.That(nativeWorkflow).Contains("runner: macos-15");
        await Assert.That(packageWorkflow).Contains("runner: macos-15");
        await Assert.That(nativeWorkflow).DoesNotContain("macos-15-intel");
        await Assert.That(packageWorkflow).DoesNotContain("macos-15-intel");
    }

    [Test]
    public async Task SharedRenderCameraContractIsCommittedOutsideFetchedSources()
    {
        string repositoryRoot = FindRepositoryRoot();
        string headerPath = Path.Combine(
            repositoryRoot,
            "native",
            "private",
            "openusd_render_camera_internal.h");
        await Assert.That(File.Exists(headerPath)).IsTrue();

        string[] cmakePaths =
        [
            "native/openusd_hydra/CMakeLists.txt",
            "native/openusd_storm_child/CMakeLists.txt",
            "native/hdSilk/CMakeLists.txt",
        ];
        foreach (string relativePath in cmakePaths)
        {
            string cmake = await File.ReadAllTextAsync(Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            await Assert.That(cmake)
                .Contains("../private/openusd_render_camera_internal.h");
            await Assert.That(cmake)
                .DoesNotContain("../src/openusd_render_camera_internal.h");
        }

        string header = await File.ReadAllTextAsync(headerPath);
        await Assert.That(header).Contains("Automatic()");
        await Assert.That(header).Contains("Validate(");
        await Assert.That(header).Contains("AssignRowMajor(");
        await Assert.That(header).Contains("Signature(");
    }

    [Test]
    public async Task NativeContractAssemblyIsAvailable()
    {
        string? assemblyName = typeof(OpenUsdNativeContract).Assembly.GetName().Name;

        await Assert.That(assemblyName).IsEqualTo("OpenUsd.Interop");
    }

    [Test]
    public async Task OlderDataAbiIsRejected()
    {
        Exception exception = InvokeCompatibilityValidation(
            OpenUsdNativeContract.AbiVersion - 1,
            OpenUsdNativeContract.RequiredCapabilities);
        await Assert.That(exception).IsTypeOf<OpenUsdNativeException>();
    }

    [Test]
    public async Task MissingAbiCapabilityIsRejected()
    {
        Exception exception = InvokeCompatibilityValidation(
            OpenUsdNativeContract.AbiVersion,
            OpenUsdNativeContract.RequiredCapabilities & ~(1UL << 13));
        await Assert.That(exception).IsTypeOf<OpenUsdNativeException>();
    }

    [Test]
    public async Task UsdPhysicsCapabilityAndInteropAreDeclared()
    {
        string repositoryRoot = FindRepositoryRoot();
        string header = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_dotnet",
            "include",
            "openusd_dotnet.h"));
        string contract = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Interop",
            "OpenUsdNativeContract.cs"));
        string generatedInterop = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Interop",
            "OpenUsdNativeMethods.g.cs"));

        await Assert.That(header).Contains("OPENUSD_CAPABILITY_USD_PHYSICS_SCHEMA");
        await Assert.That(contract).Contains("RequiredCapabilities = 0x2FFF");
        await Assert.That(generatedInterop).Contains("PhysicsApplyApi");
        await Assert.That(generatedInterop).Contains("PhysicsSetQuatf");
    }

    [Test]
    public async Task GenericNativeStageBridgeIsNotPublished()
    {
        string repositoryRoot = FindRepositoryRoot();
        string header = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "native",
                "openusd_dotnet",
                "include",
                "openusd_dotnet.h"));
        string generatedInterop = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Interop",
                "OpenUsdNativeMethods.g.cs"));

        await Assert.That(header.Contains(
            "openusd_stage_access_invoke_native",
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(header.Contains(
            "openusd_stage_access_get_native",
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(generatedInterop.Contains(
            "StageAccessInvokeNative",
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(generatedInterop.Contains(
            "StageAccessGetNative",
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(File.Exists(
            Path.Combine(
                repositoryRoot,
                "native",
                "openusd_dotnet",
                "include",
                "openusd_dotnet_native.h"))).IsFalse();
    }

    [Test]
    public async Task StatusExportsAreGuardedAndInitializeFailureOutputs()
    {
        string repositoryRoot = FindRepositoryRoot();
        string header = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "native",
                "openusd_dotnet",
                "include",
                "openusd_dotnet.h"));
        string implementation = ReadDataAbiImplementation(repositoryRoot);

        MatchCollection declarations = Regex.Matches(
            header,
            @"OPENUSD_DOTNET_API\s+openusd_status\s+(?<name>openusd_\w+)\s*\((?<parameters>.*?)\);",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        HashSet<string> outputBearingExports = declarations
            .Cast<Match>()
            .Where(match => HasOutputParameter(match.Groups["parameters"].Value))
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        MatchCollection definitions = Regex.Matches(
            implementation,
            @"(?m)^openusd_status\s+(?<name>openusd_\w+)\s*\(",
            RegexOptions.CultureInvariant);

        await Assert.That(declarations.Count).IsEqualTo(245);
        await Assert.That(definitions.Count).IsEqualTo(246);
        await Assert.That(outputBearingExports.Count).IsEqualTo(125);
        await Assert.That(
            Regex.Count(
                implementation,
                @"// ABI_OUTPUT_INITIALIZATION",
                RegexOptions.CultureInvariant)).IsEqualTo(125);
        await Assert.That(
            Regex.Count(
                implementation,
                @"\breturn GuardStage\(stage, error",
                RegexOptions.CultureInvariant)).IsEqualTo(224);

        for (int index = 0; index < definitions.Count; index++)
        {
            Match definition = definitions[index];
            int bodyEnd = index + 1 < definitions.Count
                ? definitions[index + 1].Index
                : implementation.Length;
            string body = implementation[definition.Index..bodyEnd];
            string name = definition.Groups["name"].Value;

            await Assert.That(body.Contains(
                "// OUTER_ABI_GUARD",
                StringComparison.Ordinal)).IsTrue();
            await Assert.That(
                body.Contains("return Guard(", StringComparison.Ordinal) ||
                body.Contains("return GuardStage(", StringComparison.Ordinal) ||
                body.Contains("return GuardLayer(", StringComparison.Ordinal) ||
                name == "openusd_stage_access_end").IsTrue();
            if (outputBearingExports.Contains(name))
            {
                await Assert.That(body.Contains(
                    "// ABI_OUTPUT_INITIALIZATION",
                    StringComparison.Ordinal)).IsTrue();
            }
        }
    }

    [Test]
    public async Task WorldTransformExportUsesOneXformCacheQueryAndPublishesAfterCleanErrors()
    {
        string repositoryRoot = FindRepositoryRoot();
        string header = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "native",
                "openusd_dotnet",
                "include",
                "openusd_dotnet.h"));
        string implementation = ReadDataAbiImplementation(repositoryRoot);
        string managed = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Interop",
                "OpenUsdNativeRuntime.cs"));
        string body = ExtractExportBody(implementation, "openusd_geom_xformable_get_world_transform");
        Match managedMatch = Regex.Match(
            managed,
            @"internal static OpenUsdNativeMatrix4d GetGeomWorldTransform\(.*?" +
            @"(?=^\s*internal static void SetGeomResetXformStack\()",
            RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        await Assert.That(header).Contains(
            "#define OPENUSD_CAPABILITY_WORLD_TRANSFORM_QUERY (UINT64_C(1) << 7)");
        await Assert.That(header).Contains(
            "openusd_geom_xformable_get_world_transform(");
        await Assert.That(Regex.Count(
            body,
            @"\bGetLocalToWorldTransform\(",
            RegexOptions.CultureInvariant)).IsEqualTo(1);
        await Assert.That(body).Contains("// ABI_OUTPUT_INITIALIZATION");
        await Assert.That(body).Contains("if (!mark.IsClean())");
        await Assert.That(body).Contains(
            "const openusd_matrix4d result = FromMatrix4d(matrix);");
        await Assert.That(body).Contains("if (!IsFiniteMatrix(result))");
        await Assert.That(body.IndexOf(
            "*value = result;",
            StringComparison.Ordinal)).IsGreaterThan(body.IndexOf(
                "if (!IsFiniteMatrix(result))",
                StringComparison.Ordinal));
        await Assert.That(managedMatch.Success).IsTrue();
        await Assert.That(Regex.Count(
            managedMatch.Value,
            @"NativeMethods\.GeomXformableGetWorldTransform\(",
            RegexOptions.CultureInvariant)).IsEqualTo(1);
        await Assert.That(managedMatch.Value).Contains(
            "ValidateWorldTransformResult(value);");
        await Assert.That(managedMatch.Value).DoesNotContain("foreach");
        await Assert.That(managedMatch.Value).DoesNotContain("for (");
    }

    [Test]
    public async Task CameraStateExportUsesOneComposedCameraAndFrustumQuery()
    {
        string repositoryRoot = FindRepositoryRoot();
        string header = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "native",
            "openusd_dotnet",
            "include",
            "openusd_dotnet.h"));
        string implementation = ReadDataAbiImplementation(repositoryRoot);
        string managed = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Interop",
            "OpenUsdNativeRuntime.cs"));
        string body = ExtractExportBody(implementation, "openusd_geom_camera_get_state");
        Match managedMatch = Regex.Match(
            managed,
            @"internal static OpenUsdNativeCameraState GetGeomCameraState\(.*?" +
            @"(?=^\s*private static void SetGeomArray)",
            RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        await Assert.That(header).Contains(
            "#define OPENUSD_CAPABILITY_CAMERA_STATE_QUERY (UINT64_C(1) << 8)");
        await Assert.That(header).Contains(
            "#define OPENUSD_GEOM_CAMERA_STATE_VERSION UINT32_C(1)");
        await Assert.That(header).Contains("openusd_geom_camera_get_state(");
        await Assert.That(Regex.Count(
            body,
            @"\bschema\.GetCamera\(",
            RegexOptions.CultureInvariant)).IsEqualTo(1);
        await Assert.That(Regex.Count(
            body,
            @"\bcamera\.GetFrustum\(",
            RegexOptions.CultureInvariant)).IsEqualTo(1);
        await Assert.That(body).Contains("// ABI_OUTPUT_INITIALIZATION");
        await Assert.That(body).Contains("if (!mark.IsClean())");
        await Assert.That(body.IndexOf(
            "state->is_valid = 1;",
            StringComparison.Ordinal)).IsGreaterThan(body.IndexOf(
                "if (!finite || !valid_frustum || !valid_optics)",
                StringComparison.Ordinal));
        await Assert.That(managedMatch.Success).IsTrue();
        await Assert.That(Regex.Count(
            managedMatch.Value,
            @"NativeMethods\.GeomCameraGetState\(",
            RegexOptions.CultureInvariant)).IsEqualTo(1);
        await Assert.That(managedMatch.Value).Contains(
            "ValidateGeomCameraStateResult(state);");
    }

    [Test]
    public async Task WritableBulkGettersUseFailureOnlyBufferResetGuards()
    {
        string implementation = ReadDataAbiImplementation(FindRepositoryRoot());
        string[] expectedExports =
        [
            "openusd_stage_get_attribute_time_samples",
            "openusd_stage_get_double_array",
            "openusd_stage_get_int32_array",
            "openusd_stage_get_float_array",
            "openusd_stage_get_vec2f_array",
            "openusd_stage_get_vec3f_array",
            "openusd_geom_mesh_get_points",
            "openusd_geom_mesh_get_face_vertex_counts",
            "openusd_geom_mesh_get_face_vertex_indices",
            "openusd_geom_mesh_get_normals",
            "openusd_skel_get_skeleton_matrices",
            "openusd_skel_get_animation_vec3",
            "openusd_skel_get_animation_rotations",
            "openusd_skel_get_joint_influences",
        ];
        MatchCollection definitions = Regex.Matches(
            implementation,
            @"(?m)^openusd_status\s+(?<name>openusd_\w+)\s*\(",
            RegexOptions.CultureInvariant);
        Dictionary<string, string> bodies = definitions
            .Cast<Match>()
            .Select((definition, index) =>
            {
                int bodyEnd = index + 1 < definitions.Count
                    ? definitions[index + 1].Index
                    : implementation.Length;
                return KeyValuePair.Create(
                    definition.Groups["name"].Value,
                    implementation[definition.Index..bodyEnd]);
            })
            .ToDictionary(StringComparer.Ordinal);

        await Assert.That(
            Regex.Count(
                implementation,
                @"\breturn WithAbiWritableBuffer\(",
                RegexOptions.CultureInvariant)).IsEqualTo(13);
        await Assert.That(
            Regex.Count(
                implementation,
                @"\breturn WithAbiWritableBuffers\(",
                RegexOptions.CultureInvariant)).IsEqualTo(1);

        foreach (string export in expectedExports)
        {
            await Assert.That(bodies.ContainsKey(export)).IsTrue();
            string guard = export == "openusd_skel_get_joint_influences"
                ? "WithAbiWritableBuffers("
                : "WithAbiWritableBuffer(";
            await Assert.That(bodies[export].Contains(
                guard,
                StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task ListOwnersPublishOnlyAfterCleanNativeStatus()
    {
        string repositoryRoot = FindRepositoryRoot();
        string implementation = ReadDataAbiImplementation(repositoryRoot);
        string managed = string.Concat(
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Interop",
                "OpenUsdNativeRuntime.cs")),
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Interop",
                "OpenUsdNativeShade.cs")),
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Interop",
                "OpenUsdNativeSkel.cs")));
        MatchCollection definitions = Regex.Matches(
            implementation,
            @"(?m)^openusd_status\s+(?<name>openusd_\w+)\s*\(",
            RegexOptions.CultureInvariant);
        Dictionary<string, string> bodies = definitions
            .Cast<Match>()
            .Select((definition, index) =>
            {
                int bodyEnd = index + 1 < definitions.Count
                    ? definitions[index + 1].Index
                    : implementation.Length;
                return KeyValuePair.Create(
                    definition.Groups["name"].Value,
                    implementation[definition.Index..bodyEnd]);
            })
            .ToDictionary(StringComparer.Ordinal);
        string[] stringListExports =
        [
            "openusd_stage_get_layer_stack_identifiers",
            "openusd_stage_get_prim_paths",
            "openusd_stage_get_prim_applied_schemas",
            "openusd_stage_get_prim_child_paths",
            "openusd_stage_get_prim_attribute_names",
            "openusd_stage_get_prim_relationship_names",
            "openusd_stage_get_relationship_targets",
            "openusd_stage_get_variant_set_names",
            "openusd_stage_get_variant_names",
            "openusd_layer_get_sublayer_paths",
            "openusd_shade_get_connected_source",
            "openusd_shade_get_connected_sources",
            "openusd_skel_get_joints",
        ];

        foreach (string export in stringListExports)
        {
            await Assert.That(bodies[export].Contains(
                "GuardStringListOutput(",
                StringComparison.Ordinal)).IsTrue();
        }
        await Assert.That(bodies["openusd_stage_get_composed_payload_arcs"].Contains(
            "GuardPayloadArcListOutput(",
            StringComparison.Ordinal)).IsTrue();

        string variantNames = bodies["openusd_stage_get_variant_names"];
        await Assert.That(Regex.Count(
            variantNames,
            @"\bResetStringListOutput\(list, view\);",
            RegexOptions.CultureInvariant)).IsEqualTo(1);
        await Assert.That(variantNames.Contains("std::memcpy(&struct_size", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(variantNames.Contains("!IsAligned(view)", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(variantNames.Contains("*list =", StringComparison.Ordinal)).IsFalse();

        await Assert.That(Regex.Count(
            managed,
            @"\bThrowIfFailedAndReleaseStringList\(",
            RegexOptions.CultureInvariant)).IsEqualTo(9);
        await Assert.That(Regex.Count(
            managed,
            @"\bThrowIfFailedAndReleasePayloadArcList\(",
            RegexOptions.CultureInvariant)).IsEqualTo(2);
    }

    private static Exception InvokeCompatibilityValidation(uint version, ulong capabilities)
    {
        MethodInfo method = typeof(OpenUsdNativeRuntime).GetMethod(
            "ValidateAbiCompatibility",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ABI validation method was not found.");
        try
        {
            method.Invoke(null, [version, capabilities]);
            throw new InvalidOperationException("ABI validation unexpectedly succeeded.");
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException ?? exception;
        }
    }

    private static bool HasOutputParameter(string parameters) =>
        parameters
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(parameter =>
                parameter.Contains('*') &&
                !Regex.IsMatch(parameter, @"\bconst\b", RegexOptions.CultureInvariant) &&
                !Regex.IsMatch(parameter, @"\bcontext\b", RegexOptions.CultureInvariant) &&
                !Regex.IsMatch(
                    parameter,
                    @"openusd_error_buffer\s*\*\s*error\b",
                    RegexOptions.CultureInvariant));

    private static string ReadDataAbiImplementation(string repositoryRoot)
    {
        string sourceDirectory = Path.Combine(repositoryRoot, "native", "openusd_dotnet", "src");
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceDirectory, "*.cpp", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ExtractExportBody(string implementation, string exportName)
    {
        MatchCollection definitions = Regex.Matches(
            implementation,
            @"(?m)^openusd_status\s+(?<name>openusd_\w+)\s*\(",
            RegexOptions.CultureInvariant);
        for (int index = 0; index < definitions.Count; index++)
        {
            Match definition = definitions[index];
            if (!string.Equals(definition.Groups["name"].Value, exportName, StringComparison.Ordinal))
            {
                continue;
            }

            int bodyEnd = index + 1 < definitions.Count
                ? definitions[index + 1].Index
                : implementation.Length;
            return implementation[definition.Index..bodyEnd];
        }

        throw new InvalidOperationException($"Could not locate native export '{exportName}'.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the OpenUsd repository root.");
    }
}
