// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Geom;
using OpenUsd.Interop;
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
                UsdPrim prim = stage.GetPrim(primPath);
                string composedType;
                try
                {
                    composedType = prim.GetAttribute(attributeName).TypeName;
                }
                catch (OpenUsdNativeException exception) when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    throw new NotSupportedException(
                        "The requested property is not a composed attribute; no edit was captured.",
                        exception);
                }
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
            "focalLength" or "horizontalAperture" or "verticalAperture" or
                "horizontalApertureOffset" or "verticalApertureOffset" or "focusDistance" or "fStop" => "float",
            "clippingRange" => "float2",
            "projection" => "token",
            "shutter:open" or "shutter:close" => "double",
            "exposure" or "exposure:iso" or "exposure:time" or
                "exposure:fStop" or "exposure:responsivity" => "float",
            "inputs:width" or "inputs:height" or "inputs:radius" or "inputs:length" or "inputs:angle" or
                "inputs:shaping:focus" or "inputs:shaping:cone:angle" or
                "inputs:shaping:cone:softness" => "float",
            "inputs:intensity" or "inputs:exposure" or "inputs:diffuse" or
                "inputs:specular" or "inputs:colorTemperature" => "float",
            "inputs:enableColorTemperature" or "inputs:normalize" => "bool",
            "inputs:color" => "color3f",
            _ => null
        };
        UsdPrim prim = stage.GetPrim(primPath);
        bool supportedSchema = expectedType is not null &&
            (attributeName is "focalLength" or "horizontalAperture" or "verticalAperture" or
                "horizontalApertureOffset" or "verticalApertureOffset" or "focusDistance" or "fStop" or
                "clippingRange" or "projection" or "shutter:open" or "shutter:close" or
                "exposure" or "exposure:iso" or "exposure:time" or "exposure:fStop" or "exposure:responsivity"
                ? UsdGeomCamera.TryWrap(prim, out _)
                : IsSupportedLightProperty(prim, attributeName));
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

    internal static bool IsSupportedLightProperty(UsdPrim prim, string attributeName) => attributeName switch
    {
        "inputs:width" or "inputs:height" => UsdLuxRectLight.TryWrap(prim, out _),
        "inputs:radius" => UsdLuxSphereLight.TryWrap(prim, out _) ||
            UsdLuxDiskLight.TryWrap(prim, out _) || UsdLuxCylinderLight.TryWrap(prim, out _),
        "inputs:length" => UsdLuxCylinderLight.TryWrap(prim, out _),
        "inputs:angle" => UsdLuxDistantLight.TryWrap(prim, out _),
        "inputs:shaping:focus" or "inputs:shaping:cone:angle" or
            "inputs:shaping:cone:softness" => HasAppliedShaping(prim),
        "inputs:intensity" or "inputs:exposure" or "inputs:diffuse" or "inputs:specular" or
            "inputs:colorTemperature" or "inputs:enableColorTemperature" or
            "inputs:normalize" or "inputs:color" => IsSupportedLight(prim),
        _ => false
    };

    private static bool HasAppliedShaping(UsdPrim prim)
    {
        if (UsdLuxRectLight.TryWrap(prim, out UsdLuxRectLight rect))
        {
            return rect.Light.HasShaping;
        }
        if (UsdLuxSphereLight.TryWrap(prim, out UsdLuxSphereLight sphere))
        {
            return sphere.Light.HasShaping;
        }
        if (UsdLuxDiskLight.TryWrap(prim, out UsdLuxDiskLight disk))
        {
            return disk.Light.HasShaping;
        }
        if (UsdLuxCylinderLight.TryWrap(prim, out UsdLuxCylinderLight cylinder))
        {
            return cylinder.Light.HasShaping;
        }
        if (UsdLuxDistantLight.TryWrap(prim, out UsdLuxDistantLight distant))
        {
            return distant.Light.HasShaping;
        }
        return UsdLuxDomeLight.TryWrap(prim, out UsdLuxDomeLight dome) && dome.Light.HasShaping;
    }

    internal static bool IsSupportedLight(UsdPrim prim) =>
        UsdLuxRectLight.TryWrap(prim, out _) ||
        UsdLuxSphereLight.TryWrap(prim, out _) ||
        UsdLuxDiskLight.TryWrap(prim, out _) ||
        UsdLuxCylinderLight.TryWrap(prim, out _) ||
        UsdLuxDistantLight.TryWrap(prim, out _) ||
        UsdLuxDomeLight.TryWrap(prim, out _);
}
