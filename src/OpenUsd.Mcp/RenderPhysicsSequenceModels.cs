// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed class RenderPhysicsSequenceRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("scenarioIdentity"), Required, MaxLength(256)]
    [Description("Nonempty identity for the authored simulation setup; no scenario or source opinions are created.")]
    public required string ScenarioIdentity { get; init; }

    [JsonPropertyName("expectedMetersPerUnit"), Required]
    [Description(
        "Positive finite meters per workspace coordinate unit. Must match the composed workspace; " +
            "source-sublayer root metadata is not inherited.")]
    public required double ExpectedMetersPerUnit { get; init; }

    [JsonPropertyName("expectedUpAxis"), Required, MaxLength(1)]
    [Description("Expected composed workspace up axis: X, Y or Z. A mismatch refuses recording before simulation.")]
    public required string ExpectedUpAxis { get; init; }

    [JsonPropertyName("fullScenePhysics"), DefaultValue(false)]
    [Description(
        "Opt in to complete-scene CPU rigid physics: up to 32768 actors and 65536 collision shapes, with static " +
            "instances retained. Moving subtrees must be de-instanced; at most 4096 rendered mesh targets can " +
                "receive poses. Existing extraction byte/geometry ceilings still apply.")]
    public bool FullScenePhysics { get; init; }

    [JsonPropertyName("width"), Range(1, 4096), DefaultValue(1024)]
    [Description("Output width, 1-4096 pixels, subject to the existing per-frame byte quota.")]
    public int Width { get; init; } = 1024;

    [JsonPropertyName("height"), Range(1, 4096), DefaultValue(1024)]
    [Description("Output height, 1-4096 pixels, subject to the existing per-frame byte quota.")]
    public int Height { get; init; } = 1024;

    [JsonPropertyName("frameCount"), Range(1, 4096), DefaultValue(24)]
    [Description("Number of complete physics frames to record, 1-4096.")]
    public int FrameCount { get; init; } = 24;

    [JsonPropertyName("stepsPerFrame"), Range(1, 8), DefaultValue(1)]
    [Description(
        "Whole fixed simulation steps per capture, 1-8. A fresh nonlooping world starts at the authored start.")]
    public int StepsPerFrame { get; init; } = 1;

    [JsonPropertyName("cameraPath"), MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description("Optional absolute UsdGeomCamera path, sampled at the exact simulated frame times.")]
    public string? CameraPath { get; init; }

    [JsonPropertyName("includeDeviceDepth"), DefaultValue(false)]
    [Description(
        "Include normalized device-depth float32 planes; this is not metric depth or an independent coverage mask.")]
    public bool IncludeDeviceDepth { get; init; }

    [JsonPropertyName("includeHdrColor"), DefaultValue(false)]
    [Description(
        "Include pre-display RGBA binary16 framebuffer color with stored alpha and unspecified working primaries.")]
    public bool IncludeHdrColor { get; init; }
}

internal sealed record McpSimulationRecordingDto(
    [property: Description("Unique epoch of this recording.")] Guid Epoch,
    [property: Description("Host-supplied scenario identity.")] string ScenarioIdentity,
    [property: Description("Completed fixed-step index of the first frame.")] ulong FirstStep,
    [property: Description("Completed fixed-step index of the final frame.")] ulong LastStep,
    [property: Description("Actual fixed simulation step in seconds.")] double FixedStepSeconds)
{
    [Description("Explicitly checked meters per workspace coordinate unit.")]
    public double MetersPerUnit { get; init; }
    [Description("Explicitly checked workspace up axis.")]
    public string UpAxis { get; init; } = string.Empty;
    [Description("Whether the recording explicitly selected the full-scene rigid physics profile.")]
    public bool FullScenePhysics { get; init; }
}

internal sealed record McpSimulationFrameDto(
    [property: Description("Recording epoch.")] Guid Epoch,
    [property: Description("Scenario identity.")] string ScenarioIdentity,
    [property: Description("Pinned authored source revision.")] ulong SourceRevision,
    [property: Description("Exact physics publication consumed by the capture.")] ulong PublicationRevision,
    [property: Description("Completed fixed-step index.")] ulong StepIndex,
    [property: Description("Simulated seconds since the authored start.")] double SimulationSeconds,
    [property: Description("Fixed simulation step in seconds.")] double FixedStepSeconds,
    [property: Description("Binding-table revision used for this frame.")] ulong BindingRevision,
    [property: Description("Complete number of body overrides.")] int BodyCount,
    [property: Description("Complete number of deformation regions.")] int DeformationCount)
{
    [Description(
        "Optional original host-provided review identity; an attestation, not independent source verification.")]
    public string? ReviewIdentity { get; init; }

    internal static McpSimulationFrameDto From(RenderSimulationFrameIdentity identity) =>
        new(identity.Epoch, identity.ScenarioIdentity, identity.SourceRevision, identity.PublicationRevision,
            identity.StepIndex, identity.SimulationSeconds, identity.FixedStepSeconds, identity.BindingRevision,
            identity.BodyCount, identity.DeformationCount)
        {
            ReviewIdentity = identity.ReviewIdentity
        };
}
