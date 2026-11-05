#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "release-validation-checkpoint.yml"


class ReleaseValidationCheckpointTests(unittest.TestCase):
    def test_native_session_probe_syntax_job_compiles_the_checked_out_source(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        triggers, jobs = workflow.split("\njobs:\n", 1)
        self.assertTrue(
            "  native-session-probe-syntax:\n" in jobs, "Native syntax job is missing."
        )
        job = jobs.split("  native-session-probe-syntax:\n", 1)[1]
        job = re.split(r"\n  [a-z0-9-]+:\n", job, maxsplit=1)[0]

        for path in (
            "native/hdSilk/tests/hdsilk_direct_lights_session_probe.cpp",
            "native/hdSilk/tests/hdsilk_direct_lights_test_decode.h",
            "native/hdSilk/include/openusd_hdsilk.h",
            "native/openusd_dotnet/include/openusd_dotnet.h",
            "native/include/openusd_render_camera.h",
        ):
            self.assertIn(f"      - '{path}'", triggers)
        self.assertIn("ref: ${{ github.event.pull_request.head.sha }}", job)
        self.assertIn(
            "g++ -std=c++17 -Wall -Wextra -Wpedantic -Werror -fsyntax-only",
            job,
        )
        for include in (
            "native/hdSilk/tests",
            "native/hdSilk/include",
            "native/openusd_dotnet/include",
            "native/include",
        ):
            self.assertIn(f"-I{include}", job)
        self.assertIn(
            "native/hdSilk/tests/hdsilk_direct_lights_session_probe.cpp", job
        )
        self.assertNotIn("|| true", job)

    def test_linux_native_ctest_repro_uses_verified_inputs_and_preserves_each_run(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        triggers, jobs = workflow.split("\njobs:\n", 1)
        job = jobs.split("\n  linux-native-ctest-repro:\n", 1)[1]
        job = re.split(r"\n  [a-z0-9-]+:\n", job, maxsplit=1)[0]

        for path in (
            "native/hdSilk/src/mesh.cpp",
            "native/hdSilk/src/sceneState.cpp",
            "native/hdSilk/tests/CMakeLists.txt",
            "native/hdSilk/tests/geometry_sync_test_hook.cpp",
            "native/hdSilk/tests/hdsilk_direct_lights_probe.cpp",
            "native/hdSilk/tests/hdsilk_mesh_preparation_probe.cpp",
        ):
            self.assertIn(f"      - '{path}'", triggers)
        self.assertIn("ref: ${{ github.event.pull_request.head.sha }}", job)
        self.assertIn("fail-on-cache-miss: true", job)
        self.assertIn("./eng/verify-sdk-build-input.ps1 -Rid linux-x64", job)
        native_workflow = (ROOT / ".github" / "workflows" / "native.yml").read_text(
            encoding="utf-8"
        )
        native_cache = native_workflow.split("id: sdk-linux-restore", 1)[1]
        native_cache = native_cache.split("id: sdk-linux-lookup", 1)[0]
        cache_key = r"openusd-install-linux-x64-\$\{\{ hashFiles\((.*?)\)\s*\}\}"
        repro_inputs = re.search(cache_key, job, re.DOTALL)
        producer_inputs = re.search(cache_key, native_cache, re.DOTALL)
        self.assertIsNotNone(repro_inputs)
        self.assertIsNotNone(producer_inputs)
        self.assertEqual(
            re.findall(r"'([^']+)'", repro_inputs.group(1)),
            re.findall(r"'([^']+)'", producer_inputs.group(1)),
        )
        self.assertIn(
            "hdsilk_probe hdsilk_direct_lights_probe hdsilk_mesh_preparation_probe",
            job,
        )
        self.assertIn(
            "for test_name in hdsilk_geometry_non_geometry_sync hdsilk_direct_light_domes_shadows \\",
            job,
        )
        self.assertIn("hdsilk_mesh_preparation; do", job)
        self.assertIn("for attempt in 1 2", job)
        self.assertIn('rm -f -- "$build/Testing/Temporary/LastTest.log"', job)
        self.assertIn('--no-tests=error --output-on-failure', job)
        self.assertIn('--output-junit "$evidence/$name.xml"', job)
        self.assertIn('tee "$evidence/$name.txt"', job)
        self.assertIn('"$evidence/$name-LastTest.log"', job)
        self.assertIn("git rev-parse HEAD", job)
        self.assertIn('exit "$failed"', job)
        self.assertIn("path: artifacts/linux-native-ctest-repro/", job)

    def test_metal_reproducer_resolves_runner_temp_at_step_scope(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        job = workflow.split("\n  metal-volume-repro:", 1)[1]
        header, steps = job.split("\n    steps:", 1)
        run = steps.split("\n      - name: Run targeted Metal volume reproduction", 1)[1]
        run = run.split("\n      - name:", 1)[0]

        self.assertNotIn("${{ runner.", header)
        self.assertIn(
            "OPENUSD_SILK_SHADER_CACHE_DIR: ${{ runner.temp }}/openusd-volume-repro-shaders",
            run,
        )
        self.assertIn(
            "OPENUSD_TEST_WORK_ROOT: ${{ runner.temp }}/openusd-volume-repro-tests",
            run,
        )
        self.assertIn("eng/run-managed-tests.ps1", run)

    def test_metal_math_diagnostic_is_separate_from_product_validation(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        triggers, jobs = workflow.split("\njobs:\n", 1)
        job = jobs.split("\n  metal-volume-math-diagnostic:\n", 1)[1]
        job = re.split(r"\n  [a-z0-9-]+:\n", job, maxsplit=1)[0]
        for path in (
            "eng/diagnostics/metal-volume-math.metal",
            "eng/diagnostics/metal-volume-math.swift",
        ):
            self.assertIn(f"      - '{path}'", triggers)
            self.assertIn(path, job)
        self.assertIn("ref: ${{ github.event.pull_request.head.sha }}", job)
        self.assertIn("DEVELOPER_DIR: /Applications/Xcode_16.4.app/Contents/Developer", job)
        self.assertIn("metal -std=macos-metal2.4 \\", job)
        self.assertIn("metal -std=macos-metal2.4 -fno-fast-math", job)
        self.assertIn("path: artifacts/metal-volume-math/", job)
        self.assertIn("EXPECTED_SOURCE: ${{ github.event.pull_request.head.sha }}", job)
        self.assertIn('actual_source="$(git rev-parse HEAD)"', job)
        self.assertIn('[[ "$actual_source" != "$EXPECTED_SOURCE" ]]', job)
        self.assertIn('"$evidence/source-commit.txt"', job)
        self.assertIn('"$evidence/source-sha256.txt"', job)
        self.assertIn('"$evidence/binary-sha256.txt"', job)
        self.assertNotIn("eng/shaders/checked/", job)
        swift = (ROOT / "eng" / "diagnostics" / "metal-volume-math.swift").read_text(
            encoding="utf-8"
        )
        metal = (ROOT / "eng" / "diagnostics" / "metal-volume-math.metal").read_text(
            encoding="utf-8"
        )
        self.assertIn('"baselineValid"', swift)
        self.assertIn("variants.allSatisfy", swift)
        self.assertIn("width: samples.count * 2", swift)
        self.assertIn('"tintContribution"', swift)
        self.assertIn('"tintAfterTransmittance"', swift)
        self.assertIn("tint * (fieldDepth / opticalDepth)", metal)
        self.assertIn("tint * (otherDepth / opticalDepth)", metal)
        self.assertLess(
            swift.index("FileHandle.standardOutput.write(json)"),
            swift.index("variants.allSatisfy"),
        )
        product = jobs.split("\n  metal-volume-repro:\n", 1)[1]
        self.assertIn("needs: [shader-receipts, metal-link]", product)
        self.assertIn("eng/run-managed-tests.ps1", product)

    def test_metal_reproducer_records_thin_and_overlapping_cases_independently(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        triggers = workflow.split("\njobs:\n", 1)[0]
        for path in (
            "eng/metal-volume-repro-report.ps1",
            "eng/test-metal-volume-repro-report.ps1",
        ):
            self.assertIn(f"      - '{path}'", triggers)
        job = workflow.split("\n  metal-volume-repro:", 1)[1]
        run = job.split("\n      - name: Run targeted Metal volume reproduction", 1)[1]
        run = run.split("\n      - name:", 1)[0]

        for method, minimum in (
            ("LargeFiniteDensityInThinFieldRemainsVisible", 8),
            ("OverlappingHighFiniteTintsMatchTheIndependentComposite", 10),
        ):
            self.assertIn(f"Method = '{method}'; Minimum = {minimum}", run)
        self.assertIn("-MinimumExpectedTests $case.Minimum", run)
        self.assertIn("-Method $case.Method", run)
        self.assertIn('"$($case.Method).tunit-report.json"', run)
        self.assertLess(
            run.index("Move-MetalVolumeReproReport -Source $reportPath"),
            run.index("Get-Content -LiteralPath $retainedPath"),
        )
        self.assertNotIn("Remove-Item -LiteralPath $reportPath", run)
        self.assertIn("if (Test-Path -LiteralPath $reportPath -PathType Leaf) {\n                break", run)
        self.assertIn("throw ($failures -join [Environment]::NewLine)", run)
        self.assertIn("artifacts/metal-volume-repro/*.tunit-report.json", job)
        self.assertIn(
            "tests/OpenUsd.Rendering.ConformanceTests/bin/Release/net10.0/"
            "TestResults/OpenUsd.Rendering.ConformanceTests-macos-net10.0.tunit-report.json",
            job.split("\n      - name: Upload targeted Metal volume report", 1)[1],
        )

    def test_ptex_fallback_repro_requires_geometry_and_backend_cases(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        managed = workflow.split(
            "\n      - name: Run unsupported Ptex fallback geometry tests", 1
        )[1].split("\n      - name:", 1)[0]
        self.assertIn(
            "/*/*/SilkMaterialCommandTests/UnsupportedPtex*", managed
        )
        self.assertIn("-MinimumExpectedTests 5", managed)
        for case in (
            "UnsupportedPtexInputSamplesItsNonDefaultAuthoredFallback(Normal)",
            "UnsupportedPtexInputSamplesItsNonDefaultAuthoredFallback(Opacity)",
            "UnsupportedPtexNormalFallbackBuildsTangentsAndRestoresGeometry(Normal)",
            "UnsupportedPtexForbiddenInputIsRejectedAndCorrectedRetryWorks(DetailNormal)",
            "UnsupportedPtexForbiddenInputIsRejectedAndCorrectedRetryWorks(Displacement)",
        ):
            self.assertIn(f"'{case}'", managed)
        self.assertNotIn(
            "UnsupportedPtexInputSamplesItsNonDefaultAuthoredFallback(DetailNormal)",
            managed,
        )
        self.assertNotIn(
            "UnsupportedPtexNormalFallbackBuildsTangentsAndRestoresGeometry(DetailNormal)",
            managed,
        )
        self.assertIn("$report.summary.total -ne 5", managed)
        self.assertIn("$report.summary.passed -ne 5", managed)
        self.assertIn("$report.summary.skipped -ne 0", managed)
        self.assertIn("'ptex-fallback-geometry.json'", managed)

        windows = workflow.split("\n  windows-conformance-repro:", 1)[1]
        for backend in ("D3D12", "Vulkan"):
            self.assertIn(
                "UnsupportedPtexNormalUsesAuthoredFallbackUntilImageReplacementAndRestoration"
                f"({backend})",
                windows,
            )

    def test_point_shadow_checkpoint_requires_managed_and_backend_cases(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        managed = workflow.split(
            "\n      - name: Run point-shadow depth and transaction tests", 1
        )[1].split("\n      - name:", 1)[0]
        for method, count in (
            ("DistantCubeDepthSeparatesCastersWithoutFittingTheNearPlane", 1),
            ("CubeBiasPreservesThePublishedPerspectiveBias", 3),
            ("CubeFacesUseOneScaleWhenTheirFarPlanesDiffer", 1),
            ("CubeGroupRejectsFaceWhoseLinearNearDepthWouldUnderflow", 1),
            ("InvalidCubeDepthProjectionIsRejected", 4),
            ("OrthographicMapsRetainTheirPublishedDepthBias", 1),
            ("CasterInstanceWritesTheDepthScaleAndClearsOtherReservedValues", 1),
            ("CasterInstanceRejectsInvalidDepthScale", 3),
            ("CubeDisplacementRetainsTheOriginalNearPlaneAndExpandsFar", 1),
            ("PreparedCubeRendersWithCandidateDepthScaleBeforePublication", 1),
            ("InvalidCubeProjectionIsRejectedBeforeReplacingThePublishedAtlas", 1),
            ("SixFaceGroupsRetainTheirFirstSlotAcrossReplacementAndRetirement", 1),
        ):
            with self.subTest(method=method):
                self.assertIn(f"{method} = {count}", managed)
        self.assertIn("-MinimumExpectedTests $expectedCount", managed)
        self.assertIn("$report.summary.passed -ne $expectedCount", managed)
        self.assertIn("$report.summary.skipped -ne 0", managed)
        self.assertIn("$report.summary.failed -ne 0", managed)
        for output in (
            "point-depth-encoding.json",
            "point-depth-transactions.json",
            "point-depth-wire.json",
        ):
            self.assertIn(output, managed)

        windows = workflow.split("\n  windows-conformance-repro:", 1)[1]
        repro = windows.split("\n      - name: Reproduce Windows conformance cases", 1)[
            1
        ].split("\n      - name:", 1)[0]
        methods = {
            "PointReceiverBeyondFarPlaneRemainsLitWhenPerspectiveDepthRoundsToOne": ("",),
            "PointLightLayersRespectReceiverDepthAndLifecycle": (
                ", 1, False", ", 2, False", ", 4, False", ", 1, True", ", 2, True"
            ),
            "PointLightOverflowAndEqualDepthPreservePixelsAndAllowCorrection": (
                ", False", ", True"
            ),
            "PointLightConstantAndTexturedLayersComposeInEitherDepthOrder": (
                ", False", ", True"
            ),
            "PointLightOpaqueAndCutoutCastersComposeWithTransmission": ("",),
            "PointLightLayersBeyondFarPlaneDoNotTriggerOverflow": ("",),
        }
        for method, arguments in methods.items():
            with self.subTest(method=method):
                self.assertEqual(repro.count(f"Method = '{method}'"), 1)
                for backend in ("D3D12", "Vulkan"):
                    for argument in arguments:
                        self.assertEqual(
                            repro.count(f"'{method}({backend}{argument})'"), 1
                        )
        self.assertIn("-MinimumExpectedTests $case.Names.Count", repro)
        self.assertIn("$report.summary.passed -ne $case.Names.Count", repro)
        self.assertIn("$report.summary.skipped -ne 0", repro)

    def test_native_ptex_admission_gate_keeps_both_reports_and_requires_exact_cases(self):
        native = (ROOT / ".github" / "workflows" / "native.yml").read_text(
            encoding="utf-8"
        )
        triggers, _ = native.split("\npermissions:\n", 1)
        for path in (
            "eng/ptex-atlas-admission-report.ps1",
            "eng/test-ptex-atlas-admission-report.ps1",
        ):
            self.assertEqual(triggers.count(f"      - '{path}'"), 2)

        decoder = native.split(
            "\n      - name: Run Windows native Ptex decoder tests", 1
        )[1].split("\n      - name:", 1)[0]
        self.assertIn("SilkNativePtexDecoderTests/*", decoder)
        self.assertIn("if ($LASTEXITCODE -ne 0)", decoder)
        self.assertIn("A stale Ptex report already exists", decoder)
        self.assertIn("native-ptex-decoder.tunit-report.json", decoder)
        self.assertIn("OPENUSD_PARITY_RUNTIME_ROOT", decoder)

        admission = native.split(
            "\n      - name: Run Windows native Ptex atlas admission tests", 1
        )[1].split("\n      - name:", 1)[0]
        self.assertIn("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED: '1'", admission)
        self.assertIn("./eng/test-ptex-atlas-admission-report.ps1", admission)
        self.assertIn("-MinimumExpectedTests 3", admission)
        self.assertIn(
            "/*/*/SilkMaterialCommandTests/"
            "MappedPtexAtlasAdmissionRollsBackAtCpuAndGpuBoundaries",
            admission,
        )
        self.assertIn("A stale Ptex admission report already exists", admission)
        self.assertIn("if ($LASTEXITCODE -ne 0)", admission)
        self.assertIn("OPENUSD_PARITY_RUNTIME_ROOT", admission)
        self.assertIn("Assert-PtexAtlasAdmissionReport", admission)
        self.assertIn("native-ptex-atlas-admission.tunit-report.json", admission)
        self.assertLess(
            admission.index("$started ="),
            admission.index("./eng/run-native-managed-tests.ps1"),
        )
        self.assertLess(
            admission.index("./eng/run-native-managed-tests.ps1"),
            admission.index("$finished ="),
        )

        upload = native.split(
            "\n      - name: Upload native Ptex decoder test reports", 1
        )[1].split("\n      - name:", 1)[0]
        self.assertIn("if: always() && runner.os == 'Windows'", upload)
        self.assertIn(
            "tests/OpenUsd.Rendering.Tests/bin/Release/net10.0/TestResults/",
            upload,
        )

    def test_windows_primitive_transport_requires_d3d12_and_lavapipe(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        triggers, jobs = workflow.split("\njobs:\n", 1)
        job = jobs.split("\n  windows-conformance-repro:\n", 1)[1]
        job = re.split(r"\n  [a-z0-9-]+:\n", job, maxsplit=1)[0]
        for path in (
            "eng/mesa-lavapipe-test-runtime.lock.json",
            "eng/primitive-id-transport-report.ps1",
            "eng/run-windows-lavapipe-primitive-probe.ps1",
            "eng/test-primitive-id-transport-report.ps1",
            "eng/test-windows-lavapipe-primitive-probe.ps1",
            "tests/OpenUsd.Rendering.ConformanceTests/PrimitiveIdProbeVulkanDevice.cs",
        ):
            self.assertIn(f"      - '{path}'", triggers)
        self.assertIn("./eng/test-windows-lavapipe-primitive-probe.ps1", job)
        repro = job.split("\n      - name: Reproduce Windows conformance cases\n", 1)[
            1
        ].split("\n      - name:", 1)[0]
        self.assertIn("./eng/run-vulkan-conformance-tests.ps1", repro)
        self.assertNotIn("OPENUSD_PRIMITIVE_PROBE_BACKEND", triggers)
        self.assertIn(
            "$case.Class -ceq 'SilkPrimitiveIdTransportConformanceTests'", repro
        )
        self.assertIn(
            "$case.Method -ceq 'SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex'",
            repro,
        )
        self.assertIn("$env:OPENUSD_PRIMITIVE_PROBE_BACKEND = 'D3D12'", repro)
        self.assertIn("'OPENUSD_PRIMITIVE_PROBE_BACKEND', $previousPrimitiveBackend", repro)
        self.assertIn("-MinimumExpectedTests $case.Names.Count", repro)
        self.assertIn("$report.summary.passed -ne $case.Names.Count", repro)
        self.assertIn("$report.summary.failed -ne 0", repro)
        self.assertIn("$report.summary.skipped -ne 0", repro)
        for boundary in (255, 32767, 65535):
            name = (
                "SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex"
                f"(D3D12, {boundary})"
            )
            self.assertEqual(repro.count(name), 1)
            self.assertNotIn(
                "SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex"
                f"(Vulkan, {boundary})",
                repro,
            )
        lavapipe = job.split(
            "\n      - name: Require Windows Vulkan primitive transport on lavapipe\n", 1
        )[1].split("\n      - name:", 1)[0]
        self.assertIn(
            "always() && !cancelled() && steps.windows_conformance_build.outcome == 'success'",
            lavapipe,
        )
        self.assertIn("./eng/run-windows-lavapipe-primitive-probe.ps1", lavapipe)
        self.assertNotIn("continue-on-error", lavapipe)
        self.assertIn("path: artifacts/windows-lavapipe-primitive/", job)
        probe = (
            ROOT / "eng" / "run-windows-lavapipe-primitive-probe.ps1"
        ).read_text(encoding="utf-8")
        self.assertIn("OPENUSD_PRIMITIVE_PROBE_BACKEND = 'Vulkan'", probe)
        self.assertIn("OPENUSD_REQUIRE_SWIFTSHADER = '0'", probe)
        self.assertIn(
            "Assert-PrimitiveIdTransportReport -Report $report -Backend Vulkan", probe
        )
        self.assertIn("-StartedUtc $startedUtc -FinishedUtc $finishedUtc -ExactReport", probe)
        validator = (ROOT / "eng" / "primitive-id-transport-report.ps1").read_text(
            encoding="utf-8"
        )
        self.assertIn("$summary.total -ne 3", validator)
        self.assertIn("$summary.passed -ne 3", validator)
        self.assertIn("$summary.skipped -ne 0", validator)
        self.assertIn("foreach ($boundary in @(255, 32767, 65535))", validator)
        self.assertIn(
            '$expected.Add("$method($Backend, $boundary)")', validator
        )

    def test_windows_full_ci_requires_swiftshader_and_lavapipe_primitive_transport(self):
        workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text(
            encoding="utf-8"
        )
        self.assertIn("id: solution-build", workflow)
        test = workflow.split("\n      - name: Test\n", 1)[1].split(
            "\n      - name:", 1
        )[0]
        self.assertIn("$env:OPENUSD_PRIMITIVE_PROBE_BACKEND = 'D3D12'", test)
        self.assertIn("'OPENUSD_PRIMITIVE_PROBE_BACKEND', $previousPrimitiveBackend", test)
        self.assertIn("./eng/run-vulkan-conformance-tests.ps1 -Configuration Release", test)
        self.assertIn("LastWriteTimeUtc -lt $conformanceStarted", test)
        self.assertIn(
            "Assert-PrimitiveIdTransportReport -Report $report -Backend D3D12", test
        )
        self.assertIn(
            "-StartedUtc $conformanceStarted -FinishedUtc $conformanceFinished", test
        )
        self.assertIn("windows-d3d12-conformance.tunit-report.json", test)
        self.assertLess(
            test.index("$env:OPENUSD_PRIMITIVE_PROBE_BACKEND = 'D3D12'"),
            test.index("./eng/run-vulkan-conformance-tests.ps1"),
        )
        lavapipe = workflow.split(
            "\n      - name: Require Windows Vulkan primitive transport\n", 1
        )[1].split("\n      - name:", 1)[0]
        self.assertIn("always() && !cancelled() && runner.os == 'Windows'", lavapipe)
        self.assertIn("steps.solution-build.outcome == 'success'", lavapipe)
        self.assertIn("./eng/run-windows-lavapipe-primitive-probe.ps1", lavapipe)
        self.assertNotIn("continue-on-error", lavapipe)
        self.assertIn("path: artifacts/windows-lavapipe-primitive/", workflow)


if __name__ == "__main__":
    unittest.main()
