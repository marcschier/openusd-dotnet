// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Viewer;

internal static class ViewerPropertyInspectionFormatter
{
    private const int MaximumDetailLength = 8192;
    private const string OmittedDetails = "\nFurther inspection details are omitted from this view.";

    internal static string Time(double? timeCode) => timeCode is { } time
        ? $"Time {time.ToString("G17", CultureInfo.InvariantCulture)} snapshot (not live)"
        : "Default time (not timeline)";

    internal static string Authored(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "Unproven"
    };

    internal static string Count(ulong? count) =>
        count?.ToString(CultureInfo.InvariantCulture) ?? "unproven";

    internal static string? AssetWarning(UsdPropertyValuePreview? preview)
    {
        if (preview is null)
        {
            return null;
        }
        int missing = 0;
        string firstPath = string.Empty;
        foreach (UsdPropertyAssetPath asset in preview.Assets)
        {
            if (asset.IsMissing == true)
            {
                missing++;
                if (missing == 1)
                {
                    firstPath = asset.AuthoredPath;
                }
            }
        }
        return missing switch
        {
            0 => null,
            1 => $"Missing asset: {ViewerScalarFormatter.Bound(firstPath, 256)}. See details for path resolution.",
            _ => $"Missing assets in preview: {missing.ToString(CultureInfo.InvariantCulture)}. See details for paths."
        };
    }

    internal static string State(ViewerAttributeSnapshot attribute) =>
        attribute.Inspection is { } inspection
            ? $"State: {inspection.ValueState}; source: {inspection.ResolveSource}; " +
                $"authored opinion: {Authored(inspection.HasAuthoredValueOpinion)}"
            : $"authored opinion: {Authored(attribute.HasAuthoredValue)}; blocked: {attribute.IsBlocked}";

    internal static string AttributeDetails(UsdAttributePropertySnapshot attribute)
    {
        var text = new StringBuilder();
        Append(text, "Declaration authored", Authored(attribute.IsAuthored));
        Append(text, "Variability", attribute.Variability.ToString());
        string absentSource = attribute.ResolveSource == UsdAttributeResolveSource.Fallback &&
            attribute.ValueState != UsdPropertyValueState.Blocked
                ? "<schema fallback; no authored value layer>"
                : attribute.ResolveSource == UsdAttributeResolveSource.None
                    ? "<no composed value source>"
                    : "<unproven>";
        Append(text, "Winning value layer", attribute.ValueSource?.LayerIdentifier ?? absentSource);
        Append(text, "Winning value spec", attribute.ValueSource?.SpecPath ?? absentSource);
        Append(text, "Native value preview",
            $"{attribute.Value.Status}; {Count(attribute.Value.ElementCount)} element(s)");
        if (attribute.Value.Reason.Length != 0)
        {
            Append(text, "Value preview reason", attribute.Value.Reason);
        }
        Append(text, "Sample preview",
            $"{attribute.TimeSamples.Status}; {Count(attribute.TimeSamples.Count)} sample(s)");
        if (attribute.TimeSamples.Reason.Length != 0)
        {
            Append(text, "Sample preview reason", attribute.TimeSamples.Reason);
        }
        Append(text, "Connections (direct)",
            Empty(ViewerStageSnapshotBuilder.FormatPropertyTargets(attribute.Connections), "<none>"));
        AppendTargetSource(text, attribute.Connections, "Connection");
        for (int index = 0; index < attribute.Value.Assets.Count; index++)
        {
            UsdPropertyAssetPath asset = attribute.Value.Assets[index];
            if (attribute.Value.IsArray &&
                !Append(text, "Asset element", index.ToString(CultureInfo.InvariantCulture)))
            {
                break;
            }
            string status = asset.IsMissing switch
            {
                true => "Missing",
                false => "Resolved",
                null => "Unproven / not applicable"
            };
            if (!Append(text, "Asset status", status) ||
                !Append(text, "Authored asset", Empty(asset.AuthoredPath, "<empty>")) ||
                !Append(text, "Evaluated asset", Empty(asset.EvaluatedPath, "<not evaluated>")) ||
                !Append(text, "Resolved asset", Empty(asset.ResolvedPath, "<unresolved>")) ||
                !Append(text, "Asset anchor", Empty(asset.AnchorLayerIdentifier, "<unproven / not applicable>")))
            {
                break;
            }
        }
        return text.ToString();
    }

    internal static string RelationshipDetails(UsdRelationshipPropertySnapshot relationship)
    {
        var text = new StringBuilder();
        Append(text, "Declaration authored", Authored(relationship.IsAuthored));
        Append(text, "Native target preview",
            $"{relationship.Targets.Status}; {Count(relationship.Targets.Count)} target(s); no forwarding");
        if (relationship.Targets.Reason.Length != 0)
        {
            Append(text, "Target preview reason", relationship.Targets.Reason);
        }
        AppendTargetSource(text, relationship.Targets, "Target");
        return text.ToString();
    }

    private static void AppendTargetSource(StringBuilder text, UsdPropertyTargetPreview targets, string name)
    {
        if (targets.Source is { } source)
        {
            Append(text, $"{name} source layer", source.LayerIdentifier);
            Append(text, $"{name} source spec", source.SpecPath);
        }
        else if (targets.Count != 0)
        {
            Append(text, $"{name} source", "<no singular proven source>");
        }
    }

    private static bool Append(StringBuilder text, string name, string value)
    {
        string bounded = ViewerScalarFormatter.Bound(value, 512);
        int required = (text.Length == 0 ? 0 : 1) + name.Length + 2 + bounded.Length;
        if (text.Length + required > MaximumDetailLength - OmittedDetails.Length)
        {
            text.Append(OmittedDetails);
            return false;
        }
        if (text.Length != 0)
        {
            text.Append('\n');
        }
        text.Append(name).Append(": ").Append(bounded);
        return true;
    }

    private static string Empty(string value, string replacement) => value.Length == 0 ? replacement : value;
}
