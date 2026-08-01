// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal readonly record struct ViewerCameraDescriptor(
    string Mode,
    string Payload,
    string Signature,
    string NativeSignature);

internal static class ViewerCameraEvidence
{
    internal const int PayloadSize =
        sizeof(uint) + (32 * sizeof(double)) + sizeof(uint) + sizeof(uint) + (8 * 4 * sizeof(double));

    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const int MatrixPayloadOffset = sizeof(uint);
    private const int ClipPlaneCountOffset = MatrixPayloadOffset + (32 * sizeof(double));
    private const int ClipPlanePayloadOffset = ClipPlaneCountOffset + sizeof(uint) + sizeof(uint);

    internal static CameraState CreateDeterministicExplicitCamera() =>
        new(
            new Matrix4x4(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, -5, 1),
            new Matrix4x4(
                1.5f, 0, 0, 0,
                0, 2, 0, 0,
                0, 0, -1.002002f, -1,
                0, 0, -0.2002002f, 0));

    internal static ViewerCameraDescriptor Describe(CameraState camera)
    {
        Span<byte> payload = stackalloc byte[PayloadSize];
        WritePayload(camera, payload);
        return new ViewerCameraDescriptor(
            camera.Mode.ToString(),
            Convert.ToBase64String(payload),
            Convert.ToHexString(SHA256.HashData(payload)),
            ComputeNativeSignature(camera).ToString("X16", CultureInfo.InvariantCulture));
    }

    internal static bool IsValid(
        string mode,
        string payload,
        string signature,
        string nativeSignature)
    {
        if (!Enum.TryParse(mode, ignoreCase: false, out CameraMode parsedMode) ||
            !Enum.IsDefined(parsedMode) ||
            signature.Length != 64 ||
            !signature.All(Uri.IsHexDigit) ||
            nativeSignature.Length != 16 ||
            !nativeSignature.All(Uri.IsHexDigit))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return false;
        }
        if (bytes.Length != PayloadSize)
        {
            return false;
        }

        uint clipPlaneCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(ClipPlaneCountOffset));
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != (uint)parsedMode ||
            clipPlaneCount > CameraState.MaxClipPlanes ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(
                signature,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (int offset = MatrixPayloadOffset;
             offset < ClipPlaneCountOffset;
             offset += sizeof(double))
        {
            if (!IsFiniteDouble(bytes, offset))
            {
                return false;
            }
        }
        for (int offset = ClipPlanePayloadOffset;
             offset < ClipPlanePayloadOffset + (clipPlaneCount * 4 * sizeof(double));
             offset += sizeof(double))
        {
            if (!IsFiniteDouble(bytes, offset))
            {
                return false;
            }
        }

        return ComputeNativeSignature(bytes, parsedMode)
            .ToString("X16", CultureInfo.InvariantCulture)
            .Equals(
                nativeSignature,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void WritePayload(CameraState camera, Span<byte> payload)
    {
        payload.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)camera.Mode);
        int offset = sizeof(uint);
        WriteMatrix(camera.View, payload, ref offset);
        WriteMatrix(camera.Projection, payload, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[offset..], (uint)camera.ClipPlanes.Count);
        offset += sizeof(uint) + sizeof(uint);
        foreach (Vector4 plane in camera.ClipPlanes)
        {
            WriteDouble(plane.X, payload, ref offset);
            WriteDouble(plane.Y, payload, ref offset);
            WriteDouble(plane.Z, payload, ref offset);
            WriteDouble(plane.W, payload, ref offset);
        }
    }

    private static void WriteMatrix(
        Matrix4x4 matrix,
        Span<byte> payload,
        ref int offset)
    {
        WriteDouble(matrix.M11, payload, ref offset);
        WriteDouble(matrix.M12, payload, ref offset);
        WriteDouble(matrix.M13, payload, ref offset);
        WriteDouble(matrix.M14, payload, ref offset);
        WriteDouble(matrix.M21, payload, ref offset);
        WriteDouble(matrix.M22, payload, ref offset);
        WriteDouble(matrix.M23, payload, ref offset);
        WriteDouble(matrix.M24, payload, ref offset);
        WriteDouble(matrix.M31, payload, ref offset);
        WriteDouble(matrix.M32, payload, ref offset);
        WriteDouble(matrix.M33, payload, ref offset);
        WriteDouble(matrix.M34, payload, ref offset);
        WriteDouble(matrix.M41, payload, ref offset);
        WriteDouble(matrix.M42, payload, ref offset);
        WriteDouble(matrix.M43, payload, ref offset);
        WriteDouble(matrix.M44, payload, ref offset);
    }

    private static void WriteDouble(
        double value,
        Span<byte> payload,
        ref int offset)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(
            payload[offset..],
            (ulong)BitConverter.DoubleToInt64Bits(value));
        offset += sizeof(double);
    }

    private static ulong ComputeNativeSignature(CameraState camera)
    {
        ulong hash = Append(FnvOffsetBasis, (uint)camera.Mode);

        Span<byte> payload = stackalloc byte[PayloadSize];
        WritePayload(camera, payload);
        return ComputeNativeSignature(payload, camera.Mode);
    }

    private static ulong ComputeNativeSignature(
        ReadOnlySpan<byte> payload,
        CameraMode mode)
    {
        ulong hash = Append(FnvOffsetBasis, (uint)mode);
        if (mode == CameraMode.Matrices)
        {
            for (int offset = MatrixPayloadOffset; offset < ClipPlaneCountOffset; offset += sizeof(ulong))
            {
                hash = Append(hash, BinaryPrimitives.ReadUInt64LittleEndian(payload[offset..]));
            }
        }
        uint clipPlaneCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[ClipPlaneCountOffset..]);
        if (clipPlaneCount != 0)
        {
            hash = Append(hash, clipPlaneCount);
            for (int offset = ClipPlanePayloadOffset;
                 offset < ClipPlanePayloadOffset + (clipPlaneCount * 4 * sizeof(ulong));
                 offset += sizeof(ulong))
            {
                hash = Append(hash, BinaryPrimitives.ReadUInt64LittleEndian(payload[offset..]));
            }
        }
        return hash;
    }

    private static bool IsFiniteDouble(ReadOnlySpan<byte> bytes, int offset)
    {
        double value = BitConverter.Int64BitsToDouble(
            (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]));
        return double.IsFinite(value);
    }

    private static ulong Append(ulong hash, ulong value)
    {
        unchecked
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= FnvPrime;
            }
        }
        return hash;
    }
}
