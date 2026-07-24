// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

internal static class OpenUsdNativeSkelValidation
{
    internal static void ValidatePrimPath(string? path, string paramName = "primPath")
    {
        if (!OpenUsdIdentifierValidation.IsAbsolutePrimPath(path))
        {
            throw new ArgumentException(
                $"'{path}' is not a valid absolute prim path.",
                paramName);
        }
    }

    internal static void ValidateSchemaKind(
        OpenUsdNativeSkelSchemaKind schemaKind,
        string paramName = "schemaKind")
    {
        if (schemaKind is not OpenUsdNativeSkelSchemaKind.Root and
            not OpenUsdNativeSkelSchemaKind.Skeleton and
            not OpenUsdNativeSkelSchemaKind.Animation)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    internal static void ValidateJointSchemaKind(
        OpenUsdNativeSkelSchemaKind schemaKind,
        string paramName = "schemaKind")
    {
        ValidateSchemaKind(schemaKind, paramName);
        if (schemaKind == OpenUsdNativeSkelSchemaKind.Root)
        {
            throw new ArgumentException(
                "Joints are supported only on Skeleton and Animation schemas.",
                paramName);
        }
    }

    internal static void ValidateMatrixProperty(
        OpenUsdNativeSkelMatrixProperty property,
        string paramName = "property")
    {
        if (property is not OpenUsdNativeSkelMatrixProperty.BindTransforms and
            not OpenUsdNativeSkelMatrixProperty.RestTransforms)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    internal static void ValidateAnimationVec3Property(
        OpenUsdNativeSkelAnimationVec3Property property,
        string paramName = "property")
    {
        if (property is not OpenUsdNativeSkelAnimationVec3Property.Translations and
            not OpenUsdNativeSkelAnimationVec3Property.Scales)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    internal static void ValidateBindingRelationship(
        OpenUsdNativeSkelBindingRelationship relationship,
        string paramName = "relationship")
    {
        if (relationship is not OpenUsdNativeSkelBindingRelationship.Skeleton and
            not OpenUsdNativeSkelBindingRelationship.AnimationSource)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    internal static void ValidateInterpolation(
        OpenUsdNativeSkelInterpolation interpolation,
        string paramName = "interpolation")
    {
        if (interpolation is not OpenUsdNativeSkelInterpolation.Constant and
            not OpenUsdNativeSkelInterpolation.Vertex)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    internal static void ValidateJointTokens(
        ReadOnlySpan<string> joints,
        OpenUsdNativeSkelSchemaKind schemaKind,
        string paramName = "joints")
    {
        ValidateJointSchemaKind(schemaKind);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < joints.Length; index++)
        {
            string? joint = joints[index];
            if (!OpenUsdIdentifierValidation.IsRelativePrimPath(joint))
            {
                throw new ArgumentException(
                    $"Joint token '{joint}' is not a valid relative prim path.",
                    paramName);
            }
            if (!seen.Add(joint!))
            {
                throw new ArgumentException("Joint tokens must be unique.", paramName);
            }
            if (schemaKind == OpenUsdNativeSkelSchemaKind.Skeleton)
            {
                int separator = joint!.LastIndexOf('/');
                if (separator >= 0 && !seen.Contains(joint[..separator]))
                {
                    throw new ArgumentException(
                        "Every skeleton joint parent must precede its children.",
                        paramName);
                }
            }
        }
    }

}
