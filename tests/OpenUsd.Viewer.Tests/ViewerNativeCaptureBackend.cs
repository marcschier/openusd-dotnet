// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Avalonia.Controls;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

internal static class ViewerNativeCaptureBackend
{
    internal static RenderBackendKind Kind =>
        Environment.GetEnvironmentVariable("OPENUSD_VIEWER_CAPTURE_RENDERER") switch
        {
            null or "" or "D3D12" => RenderBackendKind.D3D12,
            "Vulkan" => RenderBackendKind.Vulkan,
            "Metal" => RenderBackendKind.Metal,
            string value => throw new ArgumentException($"Unknown native capture renderer '{value}'.")
        };

    internal static void Require(ViewerStageSession session)
    {
        if (session.PickingBackend is not ViewerRenderBackend backend || backend.Identity.Kind != Kind)
        {
            throw new InvalidOperationException($"The {Kind} capture workflow must not use a fallback renderer.");
        }
    }

    internal static void RecordComposition(MainWindow window, ViewerStageSession session, string outputPath)
    {
        Require(session);
        RendererSwitchingViewport viewport = window.FindControl<RendererSwitchingViewport>("ViewportHost") ??
            throw new InvalidOperationException("The capture workflow has no composition viewport.");
        ViewerCompositionEvidence evidence = viewport.GetCompositionRuntimeEvidence(Kind);
        if (!evidence.CompositionHostVisible || evidence.SuccessfulImports == 0 || evidence.SuccessfulPresents == 0)
        {
            throw new InvalidOperationException(
                "The requested capture renderer has not actually imported and presented a frame.");
        }
        if (Environment.GetEnvironmentVariable("OPENUSD_VIEWER_CAPTURE_EVIDENCE_ROOT") is { } evidenceRoot)
        {
            if (!Path.IsPathFullyQualified(evidenceRoot) || !Directory.Exists(evidenceRoot))
            {
                throw new ArgumentException("Capture evidence requires an existing absolute directory.");
            }
            outputPath = Path.Combine(evidenceRoot, Path.GetFileName(outputPath));
        }
        using var stream = new FileStream(outputPath, FileMode.CreateNew);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("backend", evidence.Backend);
        writer.WriteString("deviceLuid", evidence.DeviceLuid);
        writer.WriteString("deviceUuid", evidence.DeviceUuid);
        writer.WriteString("imageHandleType", evidence.UsedImageHandleType);
        writer.WriteString("synchronization", evidence.SynchronizationKind);
        writer.WriteNumber("successfulImports", evidence.SuccessfulImports);
        writer.WriteNumber("successfulPresents", evidence.SuccessfulPresents);
        writer.WriteBoolean("visible", evidence.CompositionHostVisible);
        writer.WriteEndObject();
    }
}
