// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>Performs pure validation before a live-authoring batch can reach a scheduler.</summary>
public static class LiveAuthoringValidation
{
    /// <summary>Validates a constructed batch without native access or stage mutation.</summary>
    public static void Validate(LiveAuthoringBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Validate(
            batch.Sequence,
            batch.Updates,
            batch.CoalescingKey,
            nameof(batch));
    }

    internal static void Validate(
        long sequence,
        IReadOnlyList<LiveStageUpdate> updates,
        string? coalescingKey,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            throw new ArgumentException(
                "A live-authoring batch must contain at least one update.",
                parameterName);
        }
        if (coalescingKey is not null)
        {
            ValidateRequiredText(
                coalescingKey,
                $"{parameterName}.CoalescingKey",
                "A coalescing key");
        }

        for (int index = 0; index < updates.Count; index++)
        {
            LiveStageUpdate? update = updates[index];
            string updateName = $"{parameterName}[{index}]";
            if (update is null)
            {
                throw new ArgumentException(
                    "A live-authoring batch cannot contain null updates.",
                    updateName);
            }
            ValidateUpdate(update, updateName);
        }
    }

    private static void ValidateUpdate(LiveStageUpdate update, string parameterName)
    {
        switch (update)
        {
            case DefinePrimUpdate define:
                ValidatePrimPath(define.PrimPath, $"{parameterName}.PrimPath");
                ValidateOptionalIdentifier(define.TypeName, $"{parameterName}.TypeName");
                break;
            case RemovePrimUpdate remove:
                ValidatePrimPath(remove.PrimPath, $"{parameterName}.PrimPath");
                break;
            case SetScalarUpdate scalar:
                ValidatePrimPath(scalar.PrimPath, $"{parameterName}.PrimPath");
                ValidateNamespacedIdentifier(
                    scalar.AttributeName,
                    $"{parameterName}.AttributeName");
                if (scalar.TimeCode is { } timeCode && !double.IsFinite(timeCode))
                {
                    throw new ArgumentOutOfRangeException(
                        $"{parameterName}.TimeCode",
                        timeCode,
                        "A time code must be finite.");
                }
                ValidateScalar(scalar.Value, $"{parameterName}.Value");
                break;
            case SetRelationshipTargetsUpdate relationship:
                ValidatePrimPath(relationship.PrimPath, $"{parameterName}.PrimPath");
                ValidateNamespacedIdentifier(
                    relationship.RelationshipName,
                    $"{parameterName}.RelationshipName");
                ValidateRelationshipTargets(
                    relationship.Targets,
                    $"{parameterName}.Targets");
                break;
            case SetReferenceUpdate reference:
                ValidatePrimPath(reference.PrimPath, $"{parameterName}.PrimPath");
                ValidateRequiredText(
                    reference.AssetPath,
                    $"{parameterName}.AssetPath",
                    "An asset path");
                ValidateOptionalPrimPath(
                    reference.TargetPrimPath,
                    $"{parameterName}.TargetPrimPath");
                break;
            case SetPayloadUpdate payload:
                ValidatePrimPath(payload.PrimPath, $"{parameterName}.PrimPath");
                ValidateRequiredText(
                    payload.AssetPath,
                    $"{parameterName}.AssetPath",
                    "An asset path");
                ValidateOptionalPrimPath(
                    payload.TargetPrimPath,
                    $"{parameterName}.TargetPrimPath");
                break;
            case SetActiveUpdate active:
                ValidatePrimPath(active.PrimPath, $"{parameterName}.PrimPath");
                break;
            case SetInstanceableUpdate instanceable:
                ValidatePrimPath(instanceable.PrimPath, $"{parameterName}.PrimPath");
                break;
            case SetVariantSelectionUpdate variant:
                ValidatePrimPath(variant.PrimPath, $"{parameterName}.PrimPath");
                ValidateNamespacedIdentifier(
                    variant.VariantSetName,
                    $"{parameterName}.VariantSetName");
                ValidateVariants(variant, parameterName);
                break;
            default:
                throw new ArgumentException(
                    $"The live update type '{update.GetType().FullName}' is not supported.",
                    parameterName);
        }
    }

    private static void ValidateScalar(LiveScalarValue value, string parameterName)
    {
        if ((uint)value.Kind > (uint)LiveScalarKind.Vec3f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Kind,
                "The scalar kind is not supported.");
        }

        if (value.Kind is LiveScalarKind.String or LiveScalarKind.Token)
        {
            ValidateNoNullCharacter(value.Text, $"{parameterName}.Text", "A scalar text value");
            if (value.Kind == LiveScalarKind.Token && string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException(
                    "A token value cannot be empty.",
                    $"{parameterName}.Text");
            }
        }
    }

    private static void ValidateRelationshipTargets(
        IReadOnlyList<string>? targets,
        string parameterName)
    {
        if (targets is null)
        {
            throw new ArgumentException(
                "Relationship targets cannot be null.",
                parameterName);
        }

        for (int index = 0; index < targets.Count; index++)
        {
            ValidatePrimPath(targets[index], $"{parameterName}[{index}]");
        }
    }

    private static void ValidateVariants(
        SetVariantSelectionUpdate update,
        string parameterName)
    {
        IReadOnlyList<string>? variants = update.KnownVariants;
        string variantsName = $"{parameterName}.KnownVariants";
        if (variants is null)
        {
            throw new ArgumentException(
                "The known variant list cannot be null.",
                variantsName);
        }
        if (variants.Count == 0)
        {
            throw new ArgumentException(
                "The known variant list cannot be empty.",
                variantsName);
        }

        var known = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < variants.Count; index++)
        {
            string? variant = variants[index];
            string variantName = $"{variantsName}[{index}]";
            ValidateIdentifier(variant, variantName, "A variant name");
            if (!known.Add(variant!))
            {
                throw new ArgumentException(
                    $"The known variant list contains duplicate '{variant}'.",
                    variantName);
            }
        }

        if (update.Selection is not null)
        {
            string selectionName = $"{parameterName}.Selection";
            ValidateIdentifier(update.Selection, selectionName, "A variant selection");
            if (!known.Contains(update.Selection))
            {
                throw new ArgumentException(
                    $"The variant selection '{update.Selection}' is not in the known variant list.",
                    selectionName);
            }
        }
    }

    private static void ValidateOptionalIdentifier(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateNamespacedIdentifier(value, parameterName);
        }
    }

    private static void ValidateNamespacedIdentifier(string? value, string parameterName)
    {
        ValidateRequiredText(value, parameterName, "A namespaced identifier");
        int segmentStart = 0;
        for (int index = 0; index <= value!.Length; index++)
        {
            if (index == value.Length || value[index] == ':')
            {
                if (!IsIdentifier(value.AsSpan(segmentStart, index - segmentStart)))
                {
                    throw new ArgumentException(
                        $"'{value}' is not a valid namespaced identifier.",
                        parameterName);
                }
                segmentStart = index + 1;
            }
        }
    }

    private static void ValidateIdentifier(
        string? value,
        string parameterName,
        string description)
    {
        ValidateRequiredText(value, parameterName, description);
        if (!IsIdentifier(value.AsSpan()))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid identifier.",
                parameterName);
        }
    }

    private static bool IsIdentifier(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty ||
            (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsAsciiLetterOrDigit(current) && current != '_')
            {
                return false;
            }
        }
        return true;
    }

    private static void ValidateOptionalPrimPath(string? path, string parameterName)
    {
        if (path is not null)
        {
            ValidatePrimPath(path, parameterName);
        }
    }

    private static void ValidatePrimPath(string? path, string parameterName)
    {
        UsdPath.ValidateAbsolutePrimPath(path, parameterName);
    }

    private static void ValidateRequiredText(
        string? value,
        string parameterName,
        string description)
    {
        ValidateNoNullCharacter(value, parameterName, description);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{description} cannot be empty.",
                parameterName);
        }
    }

    private static void ValidateNoNullCharacter(
        string? value,
        string parameterName,
        string description)
    {
        if (value is null)
        {
            throw new ArgumentException(
                $"{description} cannot be null.",
                parameterName);
        }
        if (value.Contains('\0'))
        {
            throw new ArgumentException(
                $"{description} cannot contain a NUL character.",
                parameterName);
        }
    }
}
