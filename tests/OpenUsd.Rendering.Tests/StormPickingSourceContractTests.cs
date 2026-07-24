// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class StormPickingSourceContractTests
{
    [Test]
    public async Task NativeStormPickingContractsAreVersionedAndPointerFree()
    {
        string root = FindRepositoryRoot();
        string pickHeader = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "include",
            "openusd_render_pick.h"));
        string hydraHeader = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "include",
            "openusd_hydra.h"));
        string hydraSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "src",
            "openusd_hydra.cpp"));
        string probeCmake = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "tests",
            "CMakeLists.txt"));
        string probeSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "tests",
            "storm_wgl_shared_stage_probe.cpp"));

        await Assert.That(hydraHeader).Contains("OPENUSD_STORM_ABI_VERSION 5u");
        await Assert.That(hydraHeader).Contains("openusd_storm_get_abi_version");
        await Assert.That(hydraHeader).Contains("openusd_storm_render_v2");
        await Assert.That(hydraHeader).Contains("openusd_storm_pick");
        await Assert.That(hydraHeader).Contains("openusd_storm_set_selection");
        await Assert.That(pickHeader).Contains("OPENUSD_RENDER_PICK_REQUEST_VERSION 1u");
        await Assert.That(pickHeader).Contains("OPENUSD_RENDER_PICK_RESULT_VERSION 1u");
        await Assert.That(pickHeader).Contains(
            "static_assert(sizeof(openusd_render_pick_request) == 344)");
        await Assert.That(pickHeader).Contains(
            "static_assert(sizeof(openusd_render_pick_result) == 136)");
        await Assert.That(pickHeader).Contains(
            "OPENUSD_RENDER_PICK_RESULT_STALE_CAMERA");
        await Assert.That(pickHeader).Contains(
            "OPENUSD_RENDER_PICK_RESULT_STALE_VIEWPORT");
        await Assert.That(pickHeader).Contains(
            "OPENUSD_RENDER_PICK_RESULT_STALE_TIME");
        await Assert.That(pickHeader).Contains(
            "OPENUSD_RENDER_PICK_RESULT_STALE_CONTEXT_GENERATION");
        string resultStruct = Slice(
            pickHeader,
            "typedef struct openusd_render_pick_result",
            "} openusd_render_pick_result;");
        await Assert.That(resultStruct).DoesNotContain("*");

        await Assert.That(hydraSource).Contains("UsdImagingGLEngine::PickParams");
        await Assert.That(hydraSource).Contains("HdxPickTokens->resolveNearestToCenter");
        await Assert.That(hydraSource).Contains("GetPseudoRoot()");
        await Assert.That(hydraSource).Contains(
            "openusd_render_pick_detail::NarrowProjection");
        await Assert.That(hydraSource).Contains("WithStageAccess(renderer->stage_core");
        await Assert.That(hydraSource).Contains("engine->SetSelected");
        await Assert.That(hydraSource).Contains("engine->ClearSelected");
        await Assert.That(hydraSource).Contains("engine->AddSelected");
        await Assert.That(hydraSource).Contains("engine->SetSelectionColor");
        await Assert.That(hydraSource).Contains(
            "instances.emplace_back(path, item.instance_index)");
        await Assert.That(hydraSource).Contains("parameters.highlight = true");
        await Assert.That(hydraSource).Contains("parameters.enableLighting = false");
        await Assert.That(hydraSource).Contains("RenderedStateMismatchFlags");
        await Assert.That(probeCmake).Contains(
            "openusd_storm_wgl_shared_stage_probe_legacy");
        await Assert.That(probeCmake).Contains(
            "openusd_storm_wgl_shared_stage_probe_scene_index");
        await Assert.That(probeCmake).Contains(
            "USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX=set:0");
        await Assert.That(probeCmake).Contains(
            "USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX=set:1");
        await Assert.That(probeSource).Contains(
            "baseline_hash == selected_hash");
        await Assert.That(probeSource).Contains(
            "cleared_pixels != selection_baseline");
        await Assert.That(probeSource).Contains(
            "openusd_storm_set_selection(renderer, &selection, &error)");
    }

    [Test]
    public async Task ChildPickingIsPrioritizedOnEveryPlatformAndAbi7()
    {
        string root = FindRepositoryRoot();
        string header = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "include",
            "openusd_storm_child.h"));
        string cmake = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "CMakeLists.txt"));
        string[] sources =
        [
            "openusd_storm_child.cpp",
            "openusd_storm_child_linux.cpp",
            "openusd_storm_child_macos.mm",
        ];

        await Assert.That(header).Contains("OPENUSD_STORM_CHILD_ABI_VERSION 7u");
        await Assert.That(header).Contains("openusd_storm_child_render_v2");
        await Assert.That(header).Contains("openusd_storm_child_request_frame_v3");
        await Assert.That(header).Contains("openusd_storm_child_pick");
        await Assert.That(header).Contains("openusd_storm_child_set_selection");
        await Assert.That(cmake).Contains("VERSION 7.0.0");
        await Assert.That(cmake).Contains("SOVERSION 7");
        foreach (string sourceName in sources)
        {
            string source = await File.ReadAllTextAsync(Path.Combine(
                root,
                "native",
                "openusd_storm_child",
                "src",
                sourceName));
            await Assert.That(source).Contains("CommandKind::Pick");
            await Assert.That(source).Contains("CommandKind::Selection");
            await Assert.That(source).Contains("synchronous_commands.push_front(command)");
            await Assert.That(source).Contains("openusd_storm_child_pick");
            await Assert.That(source).Contains("openusd_storm_child_set_selection");
            await Assert.That(source).Contains("pick->Cancel");
        }
    }

    [Test]
    public async Task ManagedAndPackageValidatorsTrackPickingAbi()
    {
        string root = FindRepositoryRoot();
        string renderer = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Storm",
            "OpenUsdStormRenderer.cs"));
        string child = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Storm",
            "OpenUsdStormChildRuntime.cs"));
        string pickingInterop = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Storm",
            "StormPickingInterop.cs"));
        string pickingContract = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering",
            "PickingContracts.cs"));
        string linuxValidator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Runtime.Packaging",
            "Validate-LinuxNativePackage.ps1"));
        string macValidator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Runtime.Packaging",
            "Validate-MacOsNativePackage.ps1"));

        await Assert.That(renderer).Contains("RenderPickResult Pick(");
        await Assert.That(renderer).Contains("void SetSelection(");
        await Assert.That(child).Contains("private const uint ExpectedAbiVersion = 7");
        await Assert.That(child).Contains("RenderPickResult Pick(");
        await Assert.That(child).Contains("void SetSelection(");
        await Assert.That(pickingContract).Contains("public Vector3? WorldPosition");
        await Assert.That(pickingContract).Contains("public Vector3? WorldNormal");
        await Assert.That(pickingContract).Contains("public float? NormalizedDepth");
        await Assert.That(pickingInterop).DoesNotContain("Vector3.Zero");
        await Assert.That(pickingInterop).Contains("RenderPickResult.Miss(");
        await Assert.That(pickingInterop).Contains("RenderPickResult.Stale(");
        await Assert.That(pickingInterop).Contains("RenderPickResult.Unsupported(");
        foreach (string validator in new[] { linuxValidator, macValidator })
        {
            await Assert.That(validator).Contains("openusd_storm_child_pick");
            await Assert.That(validator).Contains("openusd_storm_child_set_selection");
            await Assert.That(validator).Contains("openusd_storm_child_render_v2");
            await Assert.That(validator).Contains("openusd_storm_child_request_frame_v3");
        }
    }

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        return value[startIndex..endIndex];
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
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
