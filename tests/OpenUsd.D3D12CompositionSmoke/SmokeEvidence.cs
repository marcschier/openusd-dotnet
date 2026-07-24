// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenUsd.D3D12CompositionSmoke;

internal sealed record SmokePixelCaptureEvidence(
    string Phase,
    string Sha256,
    int Width,
    int Height,
    string BackgroundBgra,
    long NonBackgroundPixels,
    double MeanRed,
    double MeanGreen,
    double MeanBlue,
    string[] Samples);

internal sealed record SmokeLifecycleEvidence(
    long OldGenerationId,
    long NewGenerationId,
    long UpdateStartedBefore,
    long UpdateStartedAfter,
    long UpdateCompletedBefore,
    long UpdateCompletedAfter,
    long RetirementStartedBefore,
    long RetirementStartedAfter,
    long RetirementCompletedBefore,
    long RetirementCompletedAfter,
    long LastRetiredGenerationId,
    long ImportedDisposalsBefore,
    long ImportedDisposalsAfter,
    long StaleImportReuseCount);

internal sealed record SmokeTeardownEvidence(
    int ActiveGenerations,
    int ActiveFrames,
    int RetainedCopies);

internal sealed record SmokePixelEvidenceArtifact(
    int SchemaVersion,
    string CaptureApi,
    SmokePixelCaptureEvidence Initial,
    SmokePixelCaptureEvidence Edited,
    long ChangedPixels,
    double MeanAbsoluteChannelDelta,
    SmokeLifecycleEvidence Lifecycle,
    SmokeTeardownEvidence Teardown)
{
    internal const string RequiredCaptureApi =
        "PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush";

    internal static SmokePixelEvidenceArtifact ParseAndValidate(string json)
    {
        SmokePixelEvidenceArtifact artifact = JsonSerializer.Deserialize(
            json,
            SmokeEvidenceJsonContext.Default.SmokePixelEvidenceArtifact) ??
            throw new InvalidDataException("The pixel-evidence artifact was empty.");
        artifact.Validate();
        return artifact;
    }

    internal string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(
            this,
            SmokeEvidenceJsonContext.Default.SmokePixelEvidenceArtifact);
    }

    internal void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported evidence schema {SchemaVersion}.");
        }
        if (!string.Equals(CaptureApi, RequiredCaptureApi, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Pixel evidence must come from the composed Windows client capture API.");
        }
        ValidateCapture(Initial, "initial");
        ValidateCapture(Edited, "edited");
        if (Initial.Width != Edited.Width || Initial.Height != Edited.Height)
        {
            throw new InvalidDataException("Before/after captures have different dimensions.");
        }
        long minimumPixels = Math.Max(100, Initial.Width * (long)Initial.Height / 1000);
        if (Initial.NonBackgroundPixels < minimumPixels ||
            Edited.NonBackgroundPixels < minimumPixels)
        {
            throw new InvalidDataException("Composed capture did not contain enough scene pixels.");
        }
        if (string.Equals(Initial.Sha256, Edited.Sha256, StringComparison.OrdinalIgnoreCase) ||
            ChangedPixels < minimumPixels ||
            MeanAbsoluteChannelDelta < 0.1)
        {
            throw new InvalidDataException(
                "The composed capture did not change meaningfully after the display-color edit.");
        }
        if (Lifecycle.OldGenerationId <= 0 ||
            Lifecycle.NewGenerationId <= Lifecycle.OldGenerationId ||
            Lifecycle.UpdateCompletedBefore <= 0 ||
            Lifecycle.UpdateStartedAfter != Lifecycle.UpdateCompletedAfter ||
            Lifecycle.UpdateCompletedAfter <= Lifecycle.UpdateCompletedBefore ||
            Lifecycle.RetirementStartedAfter <= Lifecycle.RetirementStartedBefore ||
            Lifecycle.RetirementCompletedAfter <= Lifecycle.RetirementCompletedBefore ||
            Lifecycle.LastRetiredGenerationId != Lifecycle.OldGenerationId ||
            Lifecycle.ImportedDisposalsAfter <= Lifecycle.ImportedDisposalsBefore ||
            Lifecycle.StaleImportReuseCount != 0)
        {
            throw new InvalidDataException(
                "Measured composition lifecycle counters did not prove clean resize retirement.");
        }
        if (Teardown.ActiveGenerations != 0 ||
            Teardown.ActiveFrames != 0 ||
            Teardown.RetainedCopies != 0)
        {
            throw new InvalidDataException("Teardown retained composition resources.");
        }
    }

    private static void ValidateCapture(SmokePixelCaptureEvidence capture, string phase)
    {
        if (!string.Equals(capture.Phase, phase, StringComparison.Ordinal) ||
            capture.Width <= 0 ||
            capture.Height <= 0 ||
            capture.Sha256.Length != 64 ||
            !capture.Sha256.All(Uri.IsHexDigit) ||
            capture.BackgroundBgra.Length != 8 ||
            capture.Samples.Length < 4)
        {
            throw new InvalidDataException($"The {phase} capture metadata was invalid.");
        }
    }
}

[JsonSerializable(typeof(SmokePixelEvidenceArtifact))]
internal sealed partial class SmokeEvidenceJsonContext : JsonSerializerContext;
