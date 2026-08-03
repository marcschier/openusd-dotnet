// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>Identifies supported concrete UsdSkel schemas.</summary>
internal enum OpenUsdNativeSkelSchemaKind
{
    /// <summary>A UsdSkelRoot.</summary>
    Root = 0,
    /// <summary>A UsdSkelSkeleton.</summary>
    Skeleton = 1,
    /// <summary>A UsdSkelAnimation.</summary>
    Animation = 2,
    /// <summary>A UsdSkelBlendShape.</summary>
    BlendShape = 3
}

/// <summary>Identifies skeleton matrix-array properties.</summary>
internal enum OpenUsdNativeSkelMatrixProperty
{
    /// <summary>World-space bind transforms.</summary>
    BindTransforms = 0,
    /// <summary>Joint-local rest transforms.</summary>
    RestTransforms = 1
}

/// <summary>Identifies vector-valued animation properties.</summary>
internal enum OpenUsdNativeSkelAnimationVec3Property
{
    /// <summary>Joint-local translations.</summary>
    Translations = 0,
    /// <summary>Joint-local scales.</summary>
    Scales = 1
}

/// <summary>Identifies focused UsdSkelBindingAPI relationships.</summary>
internal enum OpenUsdNativeSkelBindingRelationship
{
    /// <summary>The bound skeleton.</summary>
    Skeleton = 0,
    /// <summary>The bound animation source.</summary>
    AnimationSource = 1,
    /// <summary>The bound blend-shape targets.</summary>
    BlendShapeTargets = 2
}

/// <summary>Identifies supported joint-influence interpolation modes.</summary>
internal enum OpenUsdNativeSkelInterpolation
{
    /// <summary>One influence tuple for the whole primitive.</summary>
    Constant = 0,
    /// <summary>One influence tuple per point.</summary>
    Vertex = 1
}

internal enum OpenUsdNativeSkelSkinningMethod
{
    ClassicLinear = 0,
    DualQuaternion = 1
}

internal enum OpenUsdNativeSkelBlendShapeVec3Property
{
    Offsets = 0,
    NormalOffsets = 1
}

internal sealed record OpenUsdNativeSkelBlendShapeInbetween(
    float Weight,
    OpenUsdNativeVec3f[] Offsets,
    OpenUsdNativeVec3f[] NormalOffsets);

/// <summary>Contains one bulk joint-influence result.</summary>
internal sealed record OpenUsdNativeSkelInfluences(
    int[] JointIndices,
    float[] JointWeights,
    int ElementSize,
    OpenUsdNativeSkelInterpolation Interpolation);

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static bool IsSkelSchema(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelSchemaKind schemaKind)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateSchemaKind(schemaKind);
        return GetSkelInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.SkelIsSchema(
                    handle,
                    primPath,
                    (int)schemaKind,
                    out value,
                    ref error)) != 0;
    }

    internal static void DefineSkel(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelSchemaKind schemaKind)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateSchemaKind(schemaKind);
        InvokeSkelAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.SkelDefine(handle, primPath, (int)schemaKind, ref error));
    }

    internal static bool HasSkelBinding(OpenUsdNativeStage stage, string primPath)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        return GetStagePrimBool(stage, primPath, NativeMethods.SkelHasBinding);
    }

    internal static void ApplySkelBinding(OpenUsdNativeStage stage, string primPath)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        InvokeStagePrimAction(stage, primPath, NativeMethods.SkelApplyBinding);
    }

    internal static void SetSkelJoints(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelSchemaKind schemaKind,
        ReadOnlySpan<string> joints)
    {
        ArgumentNullException.ThrowIfNull(stage);
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateJointTokens(joints, schemaKind);
        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(joints);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* dataPointer = data)
        fixed (nuint* offsetsPointer = offsets)
        fixed (byte* errorPointer = errorBytes)
        {
            var view = new NativeStringListView
            {
                StructSize = (uint)sizeof(NativeStringListView),
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetsPointer,
                OffsetsSize = checked((nuint)offsets.Length * (nuint)sizeof(nuint)),
                Count = (nuint)offsets.Length
            };
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelSetJoints(
                lease.Handle,
                primPath,
                (int)schemaKind,
                ref view,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string[] GetSkelJoints(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelSchemaKind schemaKind)
    {
        ArgumentNullException.ThrowIfNull(stage);
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateJointSchemaKind(schemaKind);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelGetJoints(
                lease.Handle,
                primPath,
                (int)schemaKind,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }
        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    internal static void SetSkelSkeletonMatrices(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelMatrixProperty property,
        ReadOnlySpan<OpenUsdNativeMatrix4d> values)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateMatrixProperty(property);
        SetSkelArray(
            stage,
            values,
            (nint handle, OpenUsdNativeMatrix4d* pointer, nuint count, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetSkeletonMatrices(
                    handle,
                    primPath,
                    (int)property,
                    pointer,
                    count,
                    ref error));
    }

    internal static OpenUsdNativeMatrix4d[] GetSkelSkeletonMatrices(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelMatrixProperty property)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateMatrixProperty(property);
        return GetSkelArray<OpenUsdNativeMatrix4d>(
            stage,
            (nint handle, OpenUsdNativeMatrix4d* pointer, nuint capacity, out nuint required,
                ref NativeErrorBuffer error) =>
                NativeMethods.SkelGetSkeletonMatrices(
                    handle,
                    primPath,
                    (int)property,
                    pointer,
                    capacity,
                    out required,
                    ref error),
            "skeleton matrix");
    }

    internal static void SetSkelAnimationVec3(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelAnimationVec3Property property,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        double? timeCode)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateAnimationVec3Property(property);
        SetSkelArray(
            stage,
            values,
            (nint handle, OpenUsdNativeVec3f* pointer, nuint count, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetAnimationVec3(
                    handle,
                    primPath,
                    (int)property,
                    pointer,
                    count,
                    timeCode.HasValue ? 1 : 0,
                    timeCode.GetValueOrDefault(),
                    ref error));
    }

    internal static OpenUsdNativeVec3f[] GetSkelAnimationVec3(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelAnimationVec3Property property,
        double? timeCode)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateAnimationVec3Property(property);
        return GetSkelArray<OpenUsdNativeVec3f>(
            stage,
            (nint handle, OpenUsdNativeVec3f* pointer, nuint capacity, out nuint required,
                ref NativeErrorBuffer error) =>
                NativeMethods.SkelGetAnimationVec3(
                    handle,
                    primPath,
                    (int)property,
                    timeCode.HasValue ? 1 : 0,
                    timeCode.GetValueOrDefault(),
                    pointer,
                    capacity,
                    out required,
                    ref error),
            "skeleton animation vector");
    }

    internal static void SetSkelAnimationRotations(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<OpenUsdNativeQuatf> values,
        double? timeCode)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        SetSkelArray(
            stage,
            values,
            (nint handle, OpenUsdNativeQuatf* pointer, nuint count, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetAnimationRotations(
                    handle,
                    primPath,
                    pointer,
                    count,
                    timeCode.HasValue ? 1 : 0,
                    timeCode.GetValueOrDefault(),
                    ref error));
    }

    internal static OpenUsdNativeQuatf[] GetSkelAnimationRotations(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        return GetSkelArray<OpenUsdNativeQuatf>(
            stage,
            (nint handle, OpenUsdNativeQuatf* pointer, nuint capacity, out nuint required,
                ref NativeErrorBuffer error) =>
                NativeMethods.SkelGetAnimationRotations(
                    handle,
                    primPath,
                    timeCode.HasValue ? 1 : 0,
                    timeCode.GetValueOrDefault(),
                    pointer,
                    capacity,
                    out required,
                    ref error),
            "skeleton animation rotation");
    }

    internal static void SetSkelBindingTarget(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelBindingRelationship relationship,
        string targetPrimPath)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateBindingRelationship(relationship);
        OpenUsdNativeSkelValidation.ValidatePrimPath(targetPrimPath, nameof(targetPrimPath));
        InvokeSkelAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetBindingTarget(
                    handle,
                    primPath,
                    (int)relationship,
                    targetPrimPath,
                    ref error));
    }

    internal static string GetSkelBindingTarget(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelBindingRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(stage);
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateBindingRelationship(relationship);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.SkelGetBindingTarget(
                    handle,
                    primPath,
                    (int)relationship,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static void ClearSkelBindingTarget(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelBindingRelationship relationship)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        OpenUsdNativeSkelValidation.ValidateBindingRelationship(relationship);
        InvokeSkelAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.SkelClearBindingTarget(
                    handle,
                    primPath,
                    (int)relationship,
                    ref error));
    }

    internal static void SetSkelGeomBindTransform(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMatrix4d value)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        InvokeSkelAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetGeomBindTransform(handle, primPath, ref value, ref error));
    }

    internal static OpenUsdNativeMatrix4d GetSkelGeomBindTransform(
        OpenUsdNativeStage stage,
        string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelGetGeomBindTransform(
                lease.Handle,
                primPath,
                out OpenUsdNativeMatrix4d value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static void SetSkelJointInfluences(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<int> jointIndices,
        ReadOnlySpan<float> jointWeights,
        int elementSize,
        OpenUsdNativeSkelInterpolation interpolation)
    {
        ArgumentNullException.ThrowIfNull(stage);
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        if (jointIndices.IsEmpty)
        {
            throw new ArgumentException(
                "Joint influences must not be empty.",
                nameof(jointIndices));
        }
        if (jointIndices.Length != jointWeights.Length)
        {
            throw new ArgumentException(
                "Joint indices and weights must have equal lengths.",
                nameof(jointWeights));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementSize);
        if (jointIndices.Length % elementSize != 0)
        {
            throw new ArgumentException(
                "The influence count must be divisible by element size.",
                nameof(elementSize));
        }
        OpenUsdNativeSkelValidation.ValidateInterpolation(interpolation);
        if (interpolation == OpenUsdNativeSkelInterpolation.Constant &&
            jointIndices.Length != elementSize)
        {
            throw new ArgumentException(
                "Constant interpolation requires exactly one influence tuple.",
                nameof(jointIndices));
        }
        for (int index = 0; index < jointIndices.Length; ++index)
        {
            if (jointIndices[index] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(jointIndices),
                    "Joint indices must be non-negative.");
            }
            if (!float.IsFinite(jointWeights[index]) || jointWeights[index] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(jointWeights),
                    "Joint weights must be finite and non-negative.");
            }
        }
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (int* indicesPointer = jointIndices)
        fixed (float* weightsPointer = jointWeights)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelSetJointInfluences(
                lease.Handle,
                primPath,
                indicesPointer,
                (nuint)jointIndices.Length,
                weightsPointer,
                (nuint)jointWeights.Length,
                elementSize,
                (int)interpolation,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeSkelInfluences GetSkelJointInfluences(
        OpenUsdNativeStage stage,
        string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nuint indexRequired;
        nuint weightRequired;
        int elementSize;
        int interpolation;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelGetJointInfluences(
                lease.Handle,
                primPath,
                null,
                0,
                out indexRequired,
                null,
                0,
                out weightRequired,
                out elementSize,
                out interpolation,
                ref error);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
        }
        if (indexRequired == 0 || indexRequired != weightRequired ||
            indexRequired > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native skeleton influence shape is invalid.");
        }

        int[] indices = GC.AllocateUninitializedArray<int>((int)indexRequired);
        float[] weights = GC.AllocateUninitializedArray<float>((int)weightRequired);
        fixed (int* indicesPointer = indices)
        fixed (float* weightsPointer = weights)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelGetJointInfluences(
                lease.Handle,
                primPath,
                indicesPointer,
                indexRequired,
                out nuint indicesWritten,
                weightsPointer,
                weightRequired,
                out nuint weightsWritten,
                out elementSize,
                out interpolation,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (indicesWritten != indexRequired || weightsWritten != weightRequired)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native skeleton influence shape changed during the bulk read.");
            }
        }
        var interpolationValue = (OpenUsdNativeSkelInterpolation)interpolation;
        try
        {
            OpenUsdNativeSkelValidation.ValidateInterpolation(interpolationValue);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"The native skeleton interpolation value is invalid: {exception.Message}");
        }
        return new OpenUsdNativeSkelInfluences(
            indices,
            weights,
            elementSize,
            interpolationValue);
    }

    internal static void SetSkelSkinningMethod(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelSkinningMethod method)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        InvokeSkelAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetSkinningMethod(handle, primPath, (int)method, ref error));
    }

    internal static OpenUsdNativeSkelSkinningMethod GetSkelSkinningMethod(
        OpenUsdNativeStage stage,
        string primPath)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        return (OpenUsdNativeSkelSkinningMethod)GetSkelInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.SkelGetSkinningMethod(handle, primPath, out value, ref error));
    }

    internal static void SetSkelBlendShapes(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<string> names)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        SetSkelStringList(
            stage,
            names,
            (nint handle, ref NativeStringListView view, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetBlendShapes(handle, primPath, ref view, ref error));
    }

    internal static string[] GetSkelBlendShapes(OpenUsdNativeStage stage, string primPath) =>
        GetSkelStringList(stage, primPath, NativeMethods.SkelGetBlendShapes);

    internal static void SetSkelBlendShapeTargets(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<string> targets)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        foreach (string target in targets)
        {
            OpenUsdNativeSkelValidation.ValidatePrimPath(target, nameof(targets));
        }
        SetSkelStringList(
            stage,
            targets,
            (nint handle, ref NativeStringListView view, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetBlendShapeTargets(handle, primPath, ref view, ref error));
    }

    internal static string[] GetSkelBlendShapeTargets(OpenUsdNativeStage stage, string primPath) =>
        GetSkelStringList(stage, primPath, NativeMethods.SkelGetBlendShapeTargets);

    internal static void SetSkelBlendShapeVec3(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelBlendShapeVec3Property property,
        ReadOnlySpan<OpenUsdNativeVec3f> values)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        SetSkelArray(
            stage,
            values,
            (nint handle, OpenUsdNativeVec3f* pointer, nuint count, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetBlendShapeVec3(
                    handle,
                    primPath,
                    (int)property,
                    pointer,
                    count,
                    ref error));
    }

    internal static OpenUsdNativeVec3f[] GetSkelBlendShapeVec3(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeSkelBlendShapeVec3Property property)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        return GetSkelArray<OpenUsdNativeVec3f>(
            stage,
            (nint handle, OpenUsdNativeVec3f* pointer, nuint capacity, out nuint required,
                ref NativeErrorBuffer error) =>
                NativeMethods.SkelGetBlendShapeVec3(
                    handle,
                    primPath,
                    (int)property,
                    pointer,
                    capacity,
                    out required,
                    ref error),
            "blend-shape vector");
    }

    internal static void SetSkelBlendShapePointIndices(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<int> values)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        SetSkelArray(
            stage,
            values,
            (nint handle, int* pointer, nuint count, ref NativeErrorBuffer error) =>
                NativeMethods.SkelSetBlendShapePointIndices(
                    handle,
                    primPath,
                    pointer,
                    count,
                    ref error));
    }

    internal static int[] GetSkelBlendShapePointIndices(
        OpenUsdNativeStage stage,
        string primPath)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        return GetSkelArray<int>(
            stage,
            (nint handle, int* pointer, nuint capacity, out nuint required,
                ref NativeErrorBuffer error) =>
                NativeMethods.SkelGetBlendShapePointIndices(
                    handle,
                    primPath,
                    pointer,
                    capacity,
                    out required,
                    ref error),
            "blend-shape point index");
    }

    internal static void SetSkelBlendShapeInbetween(
        OpenUsdNativeStage stage,
        string primPath,
        string name,
        float weight,
        ReadOnlySpan<OpenUsdNativeVec3f> offsets,
        ReadOnlySpan<OpenUsdNativeVec3f> normalOffsets)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!float.IsFinite(weight))
        {
            throw new ArgumentOutOfRangeException(nameof(weight));
        }
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (OpenUsdNativeVec3f* offsetsPointer = offsets)
        fixed (OpenUsdNativeVec3f* normalsPointer = normalOffsets)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelSetBlendShapeInbetween(
                lease.Handle,
                primPath,
                name,
                weight,
                offsetsPointer,
                (nuint)offsets.Length,
                normalsPointer,
                (nuint)normalOffsets.Length,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string[] GetSkelBlendShapeInbetweenNames(
        OpenUsdNativeStage stage,
        string primPath) =>
        GetSkelStringList(stage, primPath, NativeMethods.SkelGetBlendShapeInbetweenNames);

    internal static OpenUsdNativeSkelBlendShapeInbetween GetSkelBlendShapeInbetween(
        OpenUsdNativeStage stage,
        string primPath,
        string name)
    {
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        float weight;
        nuint offsetsRequired;
        nuint normalsRequired;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelGetBlendShapeInbetween(
                lease.Handle,
                primPath,
                name,
                out weight,
                null,
                0,
                out offsetsRequired,
                null,
                0,
                out normalsRequired,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
        if (offsetsRequired > int.MaxValue || normalsRequired > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native inbetween array is too large for a managed array.");
        }
        OpenUsdNativeVec3f[] offsets = GC.AllocateUninitializedArray<OpenUsdNativeVec3f>(
            (int)offsetsRequired);
        OpenUsdNativeVec3f[] normals = GC.AllocateUninitializedArray<OpenUsdNativeVec3f>(
            (int)normalsRequired);
        fixed (OpenUsdNativeVec3f* offsetsPointer = offsets)
        fixed (OpenUsdNativeVec3f* normalsPointer = normals)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SkelGetBlendShapeInbetween(
                lease.Handle,
                primPath,
                name,
                out weight,
                offsetsPointer,
                offsetsRequired,
                out offsetsRequired,
                normalsPointer,
                normalsRequired,
                out normalsRequired,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
        return new OpenUsdNativeSkelBlendShapeInbetween(weight, offsets, normals);
    }

    private static void InvokeSkelAction(OpenUsdNativeStage stage, NativeSkelAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(lease.Handle, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void SetSkelStringList(
            OpenUsdNativeStage stage,
            ReadOnlySpan<string> values,
            NativeSkelStringListSetter setter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(values);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* dataPointer = data)
        fixed (nuint* offsetsPointer = offsets)
        fixed (byte* errorPointer = errorBytes)
        {
            var view = new NativeStringListView
            {
                StructSize = (uint)sizeof(NativeStringListView),
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetsPointer,
                OffsetsSize = checked((nuint)offsets.Length * (nuint)sizeof(nuint)),
                Count = (nuint)offsets.Length
            };
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = setter(lease.Handle, ref view, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static string[] GetSkelStringList(
        OpenUsdNativeStage stage,
        string primPath,
        NativeSkelStringListGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        OpenUsdNativeSkelValidation.ValidatePrimPath(primPath);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, primPath, out list, ref view, ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }
        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    private static int GetSkelInt(OpenUsdNativeStage stage, NativeSkelIntGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, out int value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static void SetSkelArray<T>(
        OpenUsdNativeStage stage,
        ReadOnlySpan<T> values,
        NativeSkelArraySetter<T> setter)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (T* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = setter(
                lease.Handle,
                valuesPointer,
                (nuint)values.Length,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static T[] GetSkelArray<T>(
        OpenUsdNativeStage stage,
        NativeSkelArrayGetter<T> getter,
        string label)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nuint required;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                null,
                0,
                out required,
                ref error);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
            if (required == 0)
            {
                return [];
            }
        }
        if (required > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"The native {label} array is too large for a managed array.");
        }
        T[] values = GC.AllocateUninitializedArray<T>((int)required);
        fixed (T* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                valuesPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    $"The native {label} array changed during the bulk read.");
            }
        }
        return values;
    }

    private delegate OpenUsdNativeStatus NativeSkelAction(
        nint stage,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeSkelIntGetter(
        nint stage,
        out int value,
        ref NativeErrorBuffer error);

    private unsafe delegate OpenUsdNativeStatus NativeSkelArraySetter<T>(
        nint stage,
        T* values,
        nuint count,
        ref NativeErrorBuffer error)
        where T : unmanaged;

    private unsafe delegate OpenUsdNativeStatus NativeSkelArrayGetter<T>(
        nint stage,
        T* values,
        nuint capacity,
        out nuint required,
        ref NativeErrorBuffer error)
        where T : unmanaged;

    private delegate OpenUsdNativeStatus NativeSkelStringListSetter(
        nint stage,
        ref NativeStringListView view,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeSkelStringListGetter(
        nint stage,
        string primPath,
        out nint list,
        ref NativeStringListView view,
        ref NativeErrorBuffer error);
}
