// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Viewer;

internal static partial class ViewerStageSnapshotBuilder
{
    private static (ViewerAttributeSnapshot[] Attributes, ViewerRelationshipSnapshot[] Relationships)
        BuildProperties(UsdPrim prim, UsdPrimPropertySnapshot properties)
    {
        var attributes = new List<ViewerAttributeSnapshot>();
        var relationships = new List<ViewerRelationshipSnapshot>();
        int splineBudget = MaxReadSplinesPerInspector;
        foreach (UsdPropertySnapshot property in properties.Properties)
        {
            if (property is UsdAttributePropertySnapshot attribute)
            {
                bool splineCandidate = attribute.ResolveSource == UsdAttributeResolveSource.Spline ||
                    attribute.Value.ReasonKind == UsdPropertyPreviewReasonKind.Spline ||
                    attribute.TimeSamples.ReasonKind == UsdPropertyPreviewReasonKind.Spline;
                ViewerSplineSnapshot? spline = splineCandidate
                    ? BuildSpline(prim, attribute.Name, ref splineBudget)
                    : null;
                attributes.Add(new ViewerAttributeSnapshot(
                    attribute.Name, attribute.TypeName, attribute.HasAuthoredValueOpinion,
                    attribute.ValueState == UsdPropertyValueState.Blocked, attribute.TimeSamples.Count,
                    FormatPropertySamples(attribute.TimeSamples), FormatPropertyValue(attribute), spline, attribute));
            }
            else if (property is UsdRelationshipPropertySnapshot relationship)
            {
                relationships.Add(new ViewerRelationshipSnapshot(
                    relationship.Name, FormatPropertyTargets(relationship.Targets), relationship));
            }
            else
            {
                throw new InvalidDataException("The native property snapshot has an unknown property kind.");
            }
        }
        return (attributes.ToArray(), relationships.ToArray());
    }

    private static string FormatPropertyValue(UsdAttributePropertySnapshot attribute)
    {
        UsdPropertyValuePreview value = attribute.Value;
        if (value.Status is UsdPropertyPreviewStatus.Deferred or UsdPropertyPreviewStatus.Unsupported)
        {
            return ViewerScalarFormatter.Bound($"<{value.Status}: {value.Reason}>", 512);
        }
        if (attribute.ValueState == UsdPropertyValueState.Unset)
        {
            return "<unset>";
        }
        string text;
        if (value.IsArray)
        {
            string count = value.ElementCount?.ToString(CultureInfo.InvariantCulture) ?? "unproven";
            text = $"[{string.Join(", ", value.Elements)}] ({count} elements)";
        }
        else
        {
            text = value.Elements.Count == 0 ? string.Empty : value.Elements[0];
        }
        if (attribute.ValueState == UsdPropertyValueState.Blocked)
        {
            text = value.Elements.Count == 0 ? "<blocked>" : $"<blocked; fallback: {text}>";
        }
        if (value.Status == UsdPropertyPreviewStatus.Truncated)
        {
            string count = value.ElementCount?.ToString(CultureInfo.InvariantCulture) ?? "unproven";
            text = $"<{value.Status}: showing {value.Elements.Count} of {count}> {text}";
        }
        return ViewerScalarFormatter.Bound(text, 512);
    }

    private static string FormatPropertySamples(UsdPropertyTimeSamplePreview preview)
    {
        if (preview.Status is UsdPropertyPreviewStatus.Deferred or UsdPropertyPreviewStatus.Unsupported)
        {
            return ViewerScalarFormatter.Bound($"<{preview.Status}: {preview.Reason}>", 512);
        }
        if (preview.Count == 0)
        {
            return "<none>";
        }
        string values = string.Join(", ",
            preview.Times.Select(static value => value.ToString("G17", CultureInfo.InvariantCulture)));
        if (preview.Status != UsdPropertyPreviewStatus.Complete)
        {
            values = $"<{preview.Status}: {preview.Times.Count} shown of {preview.Count}> {values}";
        }
        return ViewerScalarFormatter.Bound(values, 512);
    }

    internal static string FormatPropertyTargets(UsdPropertyTargetPreview preview)
    {
        if (preview.Status is UsdPropertyPreviewStatus.Deferred or UsdPropertyPreviewStatus.Unsupported)
        {
            return ViewerScalarFormatter.Bound($"<{preview.Status}: {preview.Reason}>", 512);
        }
        string values = string.Join(", ", preview.Paths);
        if (preview.Status != UsdPropertyPreviewStatus.Complete)
        {
            values = $"<{preview.Status}: {preview.Paths.Count} shown of {preview.Count}> {values}";
        }
        return ViewerScalarFormatter.Bound(values, 512);
    }
}
