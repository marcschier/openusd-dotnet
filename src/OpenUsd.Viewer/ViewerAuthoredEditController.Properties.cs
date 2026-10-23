// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Geom;
using OpenUsd.Lux;

namespace OpenUsd.Viewer;

internal sealed record ViewerPropertyEditCapture(
    ViewerAuthoredEditCapture Capture, string TypeName, UsdLayerEditVariability Variability, bool Custom)
    : IUsdDetachedResult;

internal sealed partial class ViewerAuthoredEditController
{
    internal async Task<ViewerPropertyEditCapture> CapturePropertyAsync(
        string primPath, string attributeName, UsdLayerEditField field, double timeCode,
        CancellationToken cancellationToken = default)
    {
        if (field is not (UsdLayerEditField.Default or UsdLayerEditField.TimeSample))
        {
            throw new ArgumentException("The focused editor supports defaults and individual samples.", nameof(field));
        }
        var address = new UsdLayerEditAddress($"{primPath}.{attributeName}", field, timeCode);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before editing.");
            }
            return await _scheduler.InvokeAsync(stage =>
            {
                ViewerDocumentObservation document = ReadDocument(stage);
                string composedType = stage.GetPrim(primPath).GetAttribute(attributeName).TypeName;
                if (!ViewerPropertyEditParser.Supports(composedType))
                {
                    throw new NotSupportedException("This declared type remains read-only in the focused editor.");
                }
                UsdLayerAuthoredOpinion? declaration = null;
                foreach (UsdLayerEditingState state in document.Layers)
                {
                    if (state.Role == UsdLayerRole.Physics)
                    {
                        continue;
                    }
                    using UsdLayer local = stage.GetLocalLayer(state.Identifier);
                    UsdLayerAuthoredOpinion opinion = local.CaptureAuthored([address]).Opinions[0];
                    if (opinion.PropertyKind == UsdLayerPropertyKind.Attribute && opinion.TypeName is { Length: > 0 })
                    {
                        declaration = opinion;
                        break;
                    }
                }
                UsdLayerEditVariability variability;
                bool custom;
                if (declaration is null)
                {
                    UsdAttributePropertySnapshot schemaProperty = ReadSupportedSchemaProperty(
                        stage, primPath, attributeName, composedType);
                    variability = schemaProperty.Variability switch
                    {
                        UsdPropertyVariability.Varying => UsdLayerEditVariability.Varying,
                        UsdPropertyVariability.Uniform => UsdLayerEditVariability.Uniform,
                        _ => throw new NotSupportedException("The native declaration variability is unknown.")
                    };
                    custom = schemaProperty.IsCustom;
                }
                else if (declaration.TypeName != composedType)
                {
                    throw new NotSupportedException(
                        "The local and composed attribute types differ; refresh before editing.");
                }
                else
                {
                    variability = declaration.Variability ?? UsdLayerEditVariability.Varying;
                    custom = declaration.Custom ?? false;
                }
                if (field == UsdLayerEditField.TimeSample && variability == UsdLayerEditVariability.Uniform)
                {
                    throw new NotSupportedException("Uniform attributes cannot be edited as time samples.");
                }
                ViewerAuthoredEditCapture capture = CaptureReview(stage, [address]);
                return new ViewerPropertyEditCapture(capture, composedType, variability, custom);
            }, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static UsdAttributePropertySnapshot ReadSupportedSchemaProperty(
        UsdStage stage, string primPath, string attributeName, string composedType)
    {
        string? expectedType = attributeName switch
        {
            "focalLength" or "horizontalAperture" or "verticalAperture" => "float",
            "clippingRange" => "float2",
            "projection" => "token",
            "inputs:intensity" or "inputs:exposure" or "inputs:diffuse" or
                "inputs:specular" or "inputs:colorTemperature" => "float",
            "inputs:enableColorTemperature" or "inputs:normalize" => "bool",
            "inputs:color" => "color3f",
            _ => null
        };
        UsdPrim prim = stage.GetPrim(primPath);
        bool supportedSchema = expectedType is not null &&
            (attributeName.StartsWith("inputs:", StringComparison.Ordinal)
                ? IsSupportedLight(prim)
                : UsdGeomCamera.TryWrap(prim, out _));
        if (!supportedSchema || composedType != expectedType)
        {
            throw new NotSupportedException(
                "An exact local declaration or a supported native camera/light declaration is required. " +
                "Other referenced/schema-only attributes remain read-only; no declaration will be guessed.");
        }
        UsdAttributePropertySnapshot? property = stage.GetPrimPropertySnapshot(
                primPath, limits: UsdPropertyInspectionLimits.Viewer)
            .Properties.OfType<UsdAttributePropertySnapshot>()
            .FirstOrDefault(value => value.Name == attributeName);
        if (property is null || property.TypeName != expectedType || property.IsCustom)
        {
            throw new NotSupportedException(
                "The native schema property declaration is unavailable or incompatible; no edit was captured.");
        }
        return property;
    }

    private static bool IsSupportedLight(UsdPrim prim) =>
        UsdLuxRectLight.TryWrap(prim, out _) ||
        UsdLuxSphereLight.TryWrap(prim, out _) ||
        UsdLuxDiskLight.TryWrap(prim, out _) ||
        UsdLuxCylinderLight.TryWrap(prim, out _) ||
        UsdLuxDistantLight.TryWrap(prim, out _) ||
        UsdLuxDomeLight.TryWrap(prim, out _);
}
