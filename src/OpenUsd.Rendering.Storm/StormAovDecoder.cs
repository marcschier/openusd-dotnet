// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

internal static unsafe class StormAovDecoder
{
    internal const ulong MinimumManagedStorage = 8192;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static StormAovSnapshot Decode(
        in StormAovNative.View view, StormAovRequest request, bool hasDisplaySelection = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        ulong managedBytes = Validate(in view, request);
        var outputs = new StormAovOutput[(int)view.OutputCount];
        for (int index = 0; index < outputs.Length; index++)
        {
            ref readonly StormAovNative.Output output = ref view.Outputs[index];
            outputs[index] = output.Status != StormAovStatus.Ready
                ? StormAovOutput.Unavailable(in output)
                : CopyPixels(in view, in output);
        }
        var contexts = new StormAovInstanceContext[(int)view.ContextCount];
        for (int index = 0; index < contexts.Length; index++)
        {
            ref readonly StormAovNative.InstanceContext entry = ref view.Contexts[index];
            contexts[index] = new StormAovInstanceContext(
                ReadPath(in view, entry.PathOffset, entry.PathLength), entry.InstanceIndex);
        }
        var identities = new StormAovIdentity[(int)view.IdentityCount];
        for (int index = 0; index < identities.Length; index++)
        {
            ref readonly StormAovNative.Identity entry = ref view.Identities[index];
            var context = new StormAovInstanceContext[(int)entry.ContextCount];
            for (int item = 0; item < context.Length; item++)
            {
                context[item] = contexts[(int)entry.ContextOffset + item];
            }
            identities[index] = new StormAovIdentity(
                in entry,
                entry.PrimPathLength == 0 ? null : ReadPath(in view, entry.PrimPathOffset, entry.PrimPathLength),
                entry.InstancerPathLength == 0
                    ? null : ReadPath(in view, entry.InstancerPathOffset, entry.InstancerPathLength),
                context);
        }
        uint[] identityIndices = view.IdentityIndexCount == 0
            ? [] : new ReadOnlySpan<uint>(view.IdentityIndices, (int)view.IdentityIndexCount).ToArray();
        return new StormAovSnapshot(
            in view, outputs, identities, contexts, identityIndices, managedBytes, hasDisplaySelection);
    }

    internal static ulong Validate(in StormAovNative.View view, StormAovRequest request)
    {
        StormAovNative.Request expected = StormAovNative.Request.Create(request);
        if (view.StructSize != Unsafe.SizeOf<StormAovNative.View>() ||
            view.Version != StormAovNative.Version ||
            view.OutputCount != request.Outputs.Count ||
            view.Width != request.Width || view.Height != request.Height ||
            view.CaptureId == 0 ||
            view.RevisionFlags != expected.RevisionFlags ||
            view.StateRevision != request.CallerStateRevision ||
            view.SceneRevision != request.CallerSceneRevision.GetValueOrDefault() ||
            BitConverter.DoubleToInt64Bits(view.TimeCode) != BitConverter.DoubleToInt64Bits(request.TimeCode) ||
            view.OwnedBytes > view.AdmittedWorkingBytes ||
            view.AdmittedWorkingBytes > request.Limits.NativeWorkingByteLimit ||
            view.RetainedScratchUpperBoundBytes > view.AdmittedWorkingBytes ||
            view.IdentityCount > request.Limits.IdentityLimit ||
            view.ContextCount > request.Limits.InstanceContextLimit ||
            view.TextByteCount > request.Limits.PathByteLimit ||
            view.IdentityIndexCount > (ulong)request.Width * (ulong)request.Height ||
            Unsafe.SizeOf<StormAovColor>() != 8 || Unsafe.SizeOf<StormAovNeye>() != 4)
        {
            throw Invalid("The native view version, limits, or render binding is invalid.");
        }
        ValidatePointer(view.Outputs, view.OutputCount, (uint)Unsafe.SizeOf<StormAovNative.Output>(), 8);
        ValidatePointer(view.PixelData, view.PixelBytes, 1, 8);
        ValidatePointer(view.Identities, view.IdentityCount, (uint)Unsafe.SizeOf<StormAovNative.Identity>(), 4);
        ValidatePointer(view.Contexts, view.ContextCount, (uint)Unsafe.SizeOf<StormAovNative.InstanceContext>(), 4);
        ValidatePointer(view.Text, view.TextByteCount, 1, 1);
        ValidatePointer(view.IdentityIndices, view.IdentityIndexCount, 4, 4);
        ValidateCamera(in view.AppliedCamera, in expected.Camera);
        ulong extent = 0;
        ulong managedBytes = MinimumManagedStorage;
        ulong minimumOwned = (ulong)Unsafe.SizeOf<StormAovNative.View>() +
            (view.OutputCount * (ulong)Unsafe.SizeOf<StormAovNative.Output>()) +
            (view.IdentityCount * (ulong)Unsafe.SizeOf<StormAovNative.Identity>()) +
            (view.ContextCount * (ulong)Unsafe.SizeOf<StormAovNative.InstanceContext>()) +
            view.TextByteCount + (view.IdentityIndexCount * sizeof(uint));
        for (int index = 0; index < request.Outputs.Count; index++)
        {
            ref readonly StormAovNative.Output output = ref view.Outputs[index];
            if (output.StructSize != Unsafe.SizeOf<StormAovNative.Output>() ||
                output.Version != StormAovNative.Version ||
                output.Kind != request.Outputs[index] || output.Reserved != 0 ||
                output.Status is not (StormAovStatus.Ready or StormAovStatus.Unsupported or StormAovStatus.Absent))
            {
                throw Invalid("An output name, version, status, or reserved field is invalid.");
            }
            if (output.Status != StormAovStatus.Ready)
            {
                if (output.Format != StormAovFormat.None || output.Width != 0 || output.Height != 0 ||
                    output.RowStrideBytes != 0 || output.Origin != StormAovOrigin.None ||
                    output.Flags != 0 || output.DepthConvention != StormAovDepthConvention.None ||
                    output.DataOffset != 0 || output.DataBytes != 0)
                {
                    throw Invalid("An unavailable output carries invented pixel metadata.");
                }
                continue;
            }
            uint bytesPerPixel = ExpectedBytes(output.Kind, output.Format);
            ulong bytes = (ulong)view.Width * view.Height * bytesPerPixel;
            if (output.Width != view.Width || output.Height != view.Height ||
                output.RowStrideBytes != view.Width * bytesPerPixel ||
                output.Origin != StormAovOrigin.TopLeft ||
                (output.Flags & ~StormAovNative.ResolvedMultisample) != 0 ||
                output.DepthConvention != (output.Kind == StormAovKind.Depth
                    ? StormAovDepthConvention.OpenGlWindow : StormAovDepthConvention.None) ||
                output.DataOffset != Align8(extent) ||
                output.DataBytes != bytes ||
                output.DataOffset > view.PixelBytes || bytes > view.PixelBytes - output.DataOffset)
            {
                throw Invalid("An output format, stride, origin, offset, or extent is invalid.");
            }
            extent = output.DataOffset + bytes;
            managedBytes = Admit(managedBytes, bytes, request.Limits.ManagedByteLimit);
        }
        if (view.PixelBytes != Align8(extent) ||
            view.OwnedBytes < minimumOwned + view.PixelBytes)
        {
            throw Invalid("The native pixel extent or ownership accounting is inconsistent.");
        }
        managedBytes = Admit(managedBytes, 0, request.Limits.ManagedByteLimit);
        managedBytes = ValidateIdentities(in view, request, managedBytes);
        for (int index = 0; index < request.Outputs.Count; index++)
        {
            ref readonly StormAovNative.Output output = ref view.Outputs[index];
            if (output.Status == StormAovStatus.Ready)
            {
                ValidatePixels(in view, in output);
            }
        }
        ValidateIdentityPixels(in view);
        return managedBytes;
    }

    private static ulong ValidateIdentities(
        in StormAovNative.View view, StormAovRequest request, ulong managedBytes)
    {
        bool empty = view.IdentityCount == 0 && view.ContextCount == 0 &&
            view.TextByteCount == 0 && view.IdentityIndexCount == 0;
        if (!request.IncludeIdentities)
        {
            if (!empty || view.IdentityStatus != StormAovStatus.NotRequested)
            {
                throw Invalid("An unrequested identity table was returned.");
            }
            return managedBytes;
        }
        bool idsReady = FindReady(in view, StormAovKind.PrimId) >= 0 &&
            FindReady(in view, StormAovKind.InstanceId) >= 0;
        if (view.IdentityStatus != StormAovStatus.Ready)
        {
            if (view.IdentityStatus is not (StormAovStatus.Absent or StormAovStatus.Unsupported) ||
                !empty || idsReady)
            {
                throw Invalid("An unavailable identity table has contradictory data or status.");
            }
            return managedBytes;
        }
        if (!idsReady || view.IdentityIndexCount != (ulong)view.Width * view.Height ||
            (view.IdentityCount == 0 && (view.ContextCount != 0 || view.TextByteCount != 0)))
        {
            throw Invalid("The identity image or its required ID outputs are inconsistent.");
        }
        managedBytes = Admit(managedBytes,
            (view.IdentityIndexCount * sizeof(uint)) +
            ((ulong)view.IdentityCount * 256) + ((ulong)view.ContextCount * 128),
            request.Limits.ManagedByteLimit);
        try
        {
            _ = StrictUtf8.GetCharCount(new ReadOnlySpan<byte>(view.Text, (int)view.TextByteCount));
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("The native path payload is not strict UTF-8.");
        }
        for (uint index = 0; index < view.ContextCount; index++)
        {
            ref readonly StormAovNative.InstanceContext context = ref view.Contexts[index];
            if (context.StructSize != Unsafe.SizeOf<StormAovNative.InstanceContext>() ||
                context.Version != StormAovNative.Version ||
                context.Reserved != 0 || context.InstanceIndex < 0)
            {
                throw Invalid("An instance-context header or local index is invalid.");
            }
            managedBytes = ValidatePath(
                in view, context.PathOffset, context.PathLength,
                required: true, managedBytes, request.Limits.ManagedByteLimit);
        }
        Span<ulong> pairs = stackalloc ulong[(int)view.IdentityCount];
        Span<bool> usedContexts = stackalloc bool[(int)view.ContextCount];
        usedContexts.Clear();
        for (uint index = 0; index < view.IdentityCount; index++)
        {
            ref readonly StormAovNative.Identity identity = ref view.Identities[index];
            if (identity.StructSize != Unsafe.SizeOf<StormAovNative.Identity>() ||
                identity.Version != StormAovNative.Version ||
                identity.Status is not (1 or 2) ||
                identity.Reserved != 0 || identity.Reserved2 != 0 ||
                identity.PrimId < 0 || identity.InstanceId < -1 ||
                identity.InstanceIndex < -1 ||
                identity.ContextOffset > view.ContextCount ||
                identity.ContextCount > view.ContextCount - identity.ContextOffset)
            {
                throw Invalid("An identity header, native ID, or context range is invalid.");
            }
            pairs[(int)index] = ((ulong)(uint)identity.PrimId << 32) | (uint)identity.InstanceId;
            if (identity.Status == 2)
            {
                if (identity.PrimPathOffset != 0 || identity.PrimPathLength != 0 ||
                    identity.InstancerPathOffset != 0 || identity.InstancerPathLength != 0 ||
                    identity.ContextOffset != 0 || identity.ContextCount != 0 ||
                    identity.InstanceIndex != -1)
                {
                    throw Invalid("An unresolved identity carries invented canonical identity.");
                }
                continue;
            }
            if ((identity.InstancerPathLength == 0 && identity.ContextCount == 0)
                != (identity.InstanceIndex == -1))
            {
                throw Invalid("Decoded instance presence contradicts its ordinal and context.");
            }
            managedBytes = Admit(managedBytes, (ulong)identity.ContextCount * (uint)sizeof(nint),
                request.Limits.ManagedByteLimit);
            managedBytes = ValidatePath(
                in view, identity.PrimPathOffset, identity.PrimPathLength,
                required: true, managedBytes, request.Limits.ManagedByteLimit);
            managedBytes = ValidatePath(
                in view, identity.InstancerPathOffset, identity.InstancerPathLength,
                required: false, managedBytes, request.Limits.ManagedByteLimit);
            for (uint item = 0; item < identity.ContextCount; item++)
            {
                usedContexts[(int)(identity.ContextOffset + item)] = true;
            }
        }
        pairs.Sort();
        for (int index = 1; index < pairs.Length; index++)
        {
            if (pairs[index - 1] == pairs[index])
            {
                throw Invalid("The unique identity table repeats a native ID pair.");
            }
        }
        foreach (bool used in usedContexts)
        {
            if (!used)
            {
                throw Invalid("The native context table contains unreferenced records.");
            }
        }
        return managedBytes;
    }

    private static ulong ValidatePath(
        in StormAovNative.View view, uint offset, uint length,
        bool required, ulong managedBytes, ulong limit)
    {
        if (offset > view.TextByteCount || length > view.TextByteCount - offset ||
            (length == 0 && (required || offset != 0)))
        {
            throw Invalid("A native UTF-8 path range or required path is invalid.");
        }
        if (length == 0)
        {
            return managedBytes;
        }
        managedBytes = Admit(managedBytes, ((ulong)length * sizeof(char)) + 64, limit);
        ReadOnlySpan<byte> path = new(view.Text + offset, (int)length);
        if (path.Length < 2 || path[0] != '/' || path[^1] == '/')
        {
            throw Invalid("The native path is not an absolute prim path below the pseudo-root.");
        }
        // SelectionPathValidation consumes strings; validate the same path
        // structure directly as UTF-8 before any managed strings are allocated.
        bool previousSlash = true;
        path = path[1..];
        while (!path.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(path, out Rune rune, out int consumed) != OperationStatus.Done ||
                Rune.IsControl(rune) || Rune.IsWhiteSpace(rune) ||
                rune.Value is '.' or '[' or ']' or '{' or '}' or '\\' or ':' or '<' or '>' or '\'' or '"' ||
                (rune.Value == '/' && previousSlash))
            {
                throw Invalid("A native path is invalid UTF-8 or not a canonical prim path.");
            }
            previousSlash = rune.Value == '/';
            path = path[consumed..];
        }
        return managedBytes;
    }

    private static int FindReady(in StormAovNative.View view, StormAovKind kind)
    {
        for (int index = 0; index < view.OutputCount; index++)
        {
            if (view.Outputs[index].Kind == kind && view.Outputs[index].Status == StormAovStatus.Ready)
            {
                return index;
            }
        }
        return -1;
    }

    private static void ValidateIdentityPixels(in StormAovNative.View view)
    {
        if (view.IdentityStatus != StormAovStatus.Ready)
        {
            return;
        }
        ref readonly StormAovNative.Output prim = ref view.Outputs[FindReady(in view, StormAovKind.PrimId)];
        ref readonly StormAovNative.Output instance = ref view.Outputs[FindReady(in view, StormAovKind.InstanceId)];
        int* primIds = (int*)((byte*)view.PixelData + prim.DataOffset);
        int* instanceIds = (int*)((byte*)view.PixelData + instance.DataOffset);
        Span<bool> used = stackalloc bool[(int)view.IdentityCount];
        used.Clear();
        for (int pixel = 0; pixel < (int)view.IdentityIndexCount; pixel++)
        {
            uint index = view.IdentityIndices[pixel];
            if (primIds[pixel] == -1)
            {
                if (index != StormAovNative.BackgroundIndex)
                {
                    throw Invalid("Background was mapped to a foreground identity.");
                }
            }
            else
            {
                if (index >= view.IdentityCount ||
                    view.Identities[index].PrimId != primIds[pixel] ||
                    view.Identities[index].InstanceId != instanceIds[pixel])
                {
                    throw Invalid("A foreground identity index disagrees with its native ID pixels.");
                }
                used[(int)index] = true;
            }
        }
        foreach (bool referenced in used)
        {
            if (!referenced)
            {
                throw Invalid("The identity table contains an unreferenced native pair.");
            }
        }
    }

    private static string ReadPath(in StormAovNative.View view, uint offset, uint length) =>
        StrictUtf8.GetString(new ReadOnlySpan<byte>(view.Text + offset, (int)length));

    private static ulong Align8(ulong value) => checked(value + 7) & ~7ul;

    private static ulong Admit(ulong used, ulong required, ulong limit)
    {
        if (used > limit || required > limit - used)
        {
            throw Invalid("The managed snapshot exceeds its admitted storage limit.");
        }
        return used + required;
    }

    private static void ValidatePointer(void* pointer, ulong count, ulong size, nuint alignment)
    {
        if ((count == 0) != (pointer == null) ||
            count > int.MaxValue || size > int.MaxValue ||
            count > (ulong)int.MaxValue / size)
        {
            throw Invalid("A native pointer/count pair is invalid.");
        }
        ulong bytes = count * size;
        if (pointer != null &&
            (((nuint)pointer % alignment) != 0 || (nuint)pointer > nuint.MaxValue - (nuint)bytes))
        {
            throw Invalid("A native address is misaligned or its range overflows.");
        }
    }

    private static uint ExpectedBytes(StormAovKind kind, StormAovFormat format) => (kind, format) switch
    {
        (StormAovKind.Color, StormAovFormat.Float16Vec4) => 8,
        (StormAovKind.Depth, StormAovFormat.Float32) => 4,
        (StormAovKind.PrimId or StormAovKind.InstanceId or StormAovKind.ElementId, StormAovFormat.Int32) => 4,
        (StormAovKind.Neye, StormAovFormat.UNorm8Vec4) => 4,
        _ => throw Invalid("The native output kind and scalar format do not agree."),
    };

    private static void ValidateCamera(in NativeRenderCamera actual, in NativeRenderCamera requested)
    {
        if (actual.StructSize != Unsafe.SizeOf<NativeRenderCamera>() ||
            actual.Mode != CameraMode.Matrices || actual.Reserved0 != 0 ||
            actual.ClipPlaneCount > CameraState.MaxClipPlanes ||
            actual.ClipPlaneCount != requested.ClipPlaneCount)
        {
            throw Invalid("The applied camera header is invalid.");
        }
        ReadOnlySpan<double> view = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.AsRef(in actual.View.M11), 16);
        ReadOnlySpan<double> projection = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.AsRef(in actual.Projection.M11), 16);
        foreach (double value in view)
        {
            if (!double.IsFinite(value))
            {
                throw Invalid("The applied view matrix is not finite.");
            }
        }
        foreach (double value in projection)
        {
            if (!double.IsFinite(value))
            {
                throw Invalid("The applied projection matrix is not finite.");
            }
        }
        if (requested.Mode == CameraMode.Matrices &&
            (!SameMatrix(in actual.View, in requested.View) ||
             !SameMatrix(in actual.Projection, in requested.Projection)))
        {
            throw Invalid("The applied matrices do not match the explicit render request.");
        }
        for (int index = 0; index < CameraState.MaxClipPlanes; index++)
        {
            NativeRenderClipPlane a = StormAovCamera.GetPlane(in actual, index);
            NativeRenderClipPlane b = StormAovCamera.GetPlane(in requested, index);
            if (!double.IsFinite(a.X) || !double.IsFinite(a.Y) ||
                !double.IsFinite(a.Z) || !double.IsFinite(a.W) ||
                a.X != b.X || a.Y != b.Y || a.Z != b.Z || a.W != b.W)
            {
                throw Invalid("Applied clipping equations differ from the render request.");
            }
        }
    }

    private static bool SameMatrix(in NativeRenderMatrix left, in NativeRenderMatrix right) =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in left), 1))
            .SequenceEqual(MemoryMarshal.AsBytes(
                MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in right), 1)));

    private static void ValidatePixels(in StormAovNative.View view, in StormAovNative.Output output)
    {
        byte* source = (byte*)view.PixelData + output.DataOffset;
        int count = checked((int)(view.Width * view.Height));
        if (output.Format == StormAovFormat.Float32)
        {
            foreach (float value in new ReadOnlySpan<float>(source, count))
            {
                if (!float.IsFinite(value) || value is < 0 or > 1)
                {
                    throw Invalid("Native window-depth pixels must be finite values in [0,1].");
                }
            }
        }
        else if (output.Format == StormAovFormat.Int32)
        {
            foreach (int value in new ReadOnlySpan<int>(source, count))
            {
                if (value < -1)
                {
                    throw Invalid("A native ID is outside the admitted signed ID range.");
                }
            }
        }
        else if (output.Format == StormAovFormat.Float16Vec4)
        {
            foreach (Half value in new ReadOnlySpan<Half>(source, checked(count * 4)))
            {
                if (!Half.IsFinite(value))
                {
                    throw Invalid("Native render color contains a non-finite half value.");
                }
            }
        }
    }

    private static StormAovOutput CopyPixels(in StormAovNative.View view, in StormAovNative.Output output)
    {
        void* source = (byte*)view.PixelData + output.DataOffset;
        int count = checked((int)(view.Width * view.Height));
        return output.Format switch
        {
            StormAovFormat.Float16Vec4 => new StormAovOutput<StormAovColor>(
                in output, new ReadOnlySpan<StormAovColor>(source, count).ToArray()),
            StormAovFormat.Float32 => new StormAovOutput<float>(
                in output, new ReadOnlySpan<float>(source, count).ToArray()),
            StormAovFormat.Int32 => new StormAovOutput<int>(
                in output, new ReadOnlySpan<int>(source, count).ToArray()),
            StormAovFormat.UNorm8Vec4 => new StormAovOutput<StormAovNeye>(
                in output, new ReadOnlySpan<StormAovNeye>(source, count).ToArray()),
            _ => throw Invalid("The admitted output representation is unavailable."),
        };
    }

    private static OpenUsdStormException Invalid(string message) =>
        new(OpenUsdNativeStatus.NativeError, message);
}
