// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerStormSequenceProfileTests
{
    [Test]
    public async Task OpenGlProfileAdmitsExactAovLimitButNeverSelectedHdrOrLargerRasters()
    {
        var outputs = new ViewerRenderSequenceOutputOptions(true, true);
        StageRenderState state = DefaultState()
            .WithViewport(new ViewportDimensions(1024, 1024));
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state, outputs, supportsAovCapture: true)).IsNull();
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state.WithViewport(new ViewportDimensions(1024, 1025)),
            outputs, supportsAovCapture: true)).Contains("1,048,576");
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state.WithViewport(new ViewportDimensions(4097, 1)),
            outputs, supportsAovCapture: true)).Contains("4096");
        StageRenderState selected = state.WithSelection(new SelectionState([new SelectionItem("/World")]));
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            selected, outputs, supportsAovCapture: true)).Contains("selection");
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            selected, new ViewerRenderSequenceOutputOptions(true, false), supportsAovCapture: true)).IsNull();
    }

    [Test]
    public async Task NativeViewportProfileAdmitsPngWithoutClaimingAdditionalPlanes()
    {
        StageRenderState state = DefaultState();
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state, new ViewerRenderSequenceOutputOptions(false, false))).IsNull();
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state, new ViewerRenderSequenceOutputOptions(true, false))).Contains("PNG");
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state, new ViewerRenderSequenceOutputOptions(false, true))).Contains("PNG");
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state, new ViewerRenderSequenceOutputOptions(true, true, RenderHdrColorFormat.Exr))).Contains("PNG");
    }

    [Test]
    public async Task UnsupportedAppearanceAndGeometryChoicesAreNotSilentlyIgnored()
    {
        var outputs = new ViewerRenderSequenceOutputOptions(false, false);
        StageRenderState state = DefaultState();
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state.WithRenderSettings(RenderSettings.Default), outputs)).Contains("overrides");
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state.WithDisplay(new SceneDisplayState(
                RenderPurpose.Default, RenderVisibility.RespectAuthored, RenderDrawMode.SmoothShaded)),
            outputs)).Contains("purposes");
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state.WithDisplay(new SceneDisplayState(
                SceneDisplayState.Default.Purposes, RenderVisibility.RespectAuthored, RenderDrawMode.Wireframe)),
            outputs)).Contains("smooth shading");
        await Assert.That(StormNativeHostedBackendSession.GetSequenceProfileUnsupportedReason(
            state.WithViewport(new ViewportDimensions(8193, 1)), outputs)).Contains("8192");
    }

    private static StageRenderState DefaultState() => StageRenderState.Create(new StageIdentity("storm.usda"))
        .WithViewport(new ViewportDimensions(16, 16))
        .WithRenderSettings(RenderSettings.PresentationDefault);
}
