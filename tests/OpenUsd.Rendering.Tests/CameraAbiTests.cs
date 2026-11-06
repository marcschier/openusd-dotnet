// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class CameraAbiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClipPlaneAdmissionStopsAtFirstExcessPlaneAndDisposesTheEnumerator(bool invalidFirst)
    {
        int yielded = 0;
        bool disposed = false;
        IEnumerable<Vector4> planes()
        {
            try
            {
                for (int index = 0; index < CameraState.MaxClipPlanes + 2; index++)
                {
                    yielded++;
                    if (yielded > CameraState.MaxClipPlanes + 1)
                    {
                        throw new InvalidOperationException(
                            "Camera admission enumerated beyond the first excess plane.");
                    }
                    yield return invalidFirst && index == 0 ? new Vector4(float.NaN) : new Vector4(index, 1, 2, 3);
                }
            }
            finally
            {
                disposed = true;
            }
        }

        await Assert.That(() => _ = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, planes()))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(yielded).IsEqualTo(CameraState.MaxClipPlanes + 1);
        await Assert.That(disposed).IsTrue();
    }

    [Test]
    public async Task ClipPlaneSourceFailureWithinAdmissionIsPreservedAndDisposed()
    {
        var failure = new IOException("Clip source failed before the ABI bound.");
        bool disposed = false;
        IEnumerable<Vector4> planes()
        {
            try
            {
                yield return Vector4.UnitX;
                throw failure;
            }
            finally
            {
                disposed = true;
            }
        }
        IOException? observed = null;
        try
        {
            _ = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, planes());
        }
        catch (IOException exception)
        {
            observed = exception;
        }
        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(disposed).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    public async Task AdmittedClipPlanesRetainExactOwnedOrder(int count)
    {
        Vector4[] input = Enumerable.Range(0, count).Select(static index => new Vector4(index, 2, 3, -4)).ToArray();
        var camera = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, input.Select(static plane => plane));
        Array.Fill(input, Vector4.Zero);
        await Assert.That(camera.ClipPlanes.Count).IsEqualTo(count);
        for (int index = 0; index < count; index++)
        {
            await Assert.That(camera.ClipPlanes[index]).IsEqualTo(new Vector4(index, 2, 3, -4));
        }
    }

    [Test]
    public async Task NativeCameraLayoutMatchesTheSharedCAbi()
    {
        await Assert.That(Unsafe.SizeOf<NativeRenderMatrix>()).IsEqualTo(128);
        await Assert.That(Marshal.SizeOf<NativeRenderMatrix>()).IsEqualTo(128);
        await Assert.That(Unsafe.SizeOf<NativeRenderClipPlane>()).IsEqualTo(32);
        await Assert.That(Marshal.SizeOf<NativeRenderClipPlane>()).IsEqualTo(32);
        await Assert.That(OffsetOf<NativeRenderMatrix>(nameof(NativeRenderMatrix.M11)))
            .IsEqualTo(0);
        await Assert.That(OffsetOf<NativeRenderMatrix>(nameof(NativeRenderMatrix.M44)))
            .IsEqualTo(120);
        await Assert.That(OffsetOf<NativeRenderClipPlane>(nameof(NativeRenderClipPlane.X)))
            .IsEqualTo(0);
        await Assert.That(OffsetOf<NativeRenderClipPlane>(nameof(NativeRenderClipPlane.W)))
            .IsEqualTo(24);

        await Assert.That(Unsafe.SizeOf<NativeRenderCamera>()).IsEqualTo(528);
        await Assert.That(Marshal.SizeOf<NativeRenderCamera>()).IsEqualTo(528);
        await Assert.That(OffsetOf<NativeRenderCamera>(nameof(NativeRenderCamera.StructSize)))
            .IsEqualTo(0);
        await Assert.That(OffsetOf<NativeRenderCamera>(nameof(NativeRenderCamera.Mode)))
            .IsEqualTo(4);
        await Assert.That(OffsetOf<NativeRenderCamera>(nameof(NativeRenderCamera.View)))
            .IsEqualTo(8);
        await Assert.That(OffsetOf<NativeRenderCamera>(nameof(NativeRenderCamera.Projection)))
            .IsEqualTo(136);
        await Assert.That(OffsetOf<NativeRenderCamera>(nameof(NativeRenderCamera.ClipPlaneCount)))
            .IsEqualTo(264);
        await Assert.That(OffsetOf<NativeRenderCamera>(nameof(NativeRenderCamera.ClipPlane0)))
            .IsEqualTo(272);
        await Assert.That(OffsetOf<NativeRenderCamera>(nameof(NativeRenderCamera.ClipPlane7)))
            .IsEqualTo(496);
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<NativeRenderCamera>())
            .IsFalse();
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<NativeRenderMatrix>())
            .IsFalse();
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<NativeRenderClipPlane>())
            .IsFalse();
        await Assert.That(ContainsManagedArray(typeof(NativeRenderCamera))).IsFalse();
        await Assert.That(ContainsManagedArray(typeof(NativeRenderMatrix))).IsFalse();
        await Assert.That(ContainsManagedArray(typeof(NativeRenderClipPlane))).IsFalse();
    }

    [Test]
    public async Task NativeCameraConversionPreservesModeAndRowMajorValues()
    {
        var view = new Matrix4x4(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16);
        var projection = new Matrix4x4(
            17, 18, 19, 20,
            21, 22, 23, 24,
            25, 26, 27, 28,
            29, 30, 31, 32);

        var automatic = new NativeRenderCamera(default);
        var namedAutomatic = new NativeRenderCamera(CameraState.Default);
        var matrices = new NativeRenderCamera(new CameraState(view, projection));
        var explicitIdentity = new NativeRenderCamera(new CameraState(
            Matrix4x4.Identity,
            Matrix4x4.Identity));
        var clipped = new NativeRenderCamera(new CameraState(
            view,
            projection,
            [
                new Vector4(1, 2, 3, 4),
                new Vector4(-1, -2, -3, -4),
            ]));

        await Assert.That(automatic.StructSize).IsEqualTo(528u);
        await Assert.That(automatic.Mode).IsEqualTo(CameraMode.Automatic);
        await Assert.That(automatic.ClipPlaneCount).IsEqualTo(0u);
        await Assert.That(NativeBytesEqual(automatic, namedAutomatic)).IsTrue();
        await Assert.That(automatic.View.M11).IsEqualTo(0d);
        await Assert.That(automatic.View.M44).IsEqualTo(0d);
        await Assert.That(automatic.Projection.M11).IsEqualTo(0d);
        await Assert.That(automatic.Projection.M44).IsEqualTo(0d);
        await Assert.That(matrices.Mode).IsEqualTo(CameraMode.Matrices);
        await Assert.That(explicitIdentity.Mode).IsEqualTo(CameraMode.Matrices);
        await Assert.That(NativeBytesEqual(automatic, explicitIdentity)).IsFalse();
        await Assert.That(matrices.View.M11).IsEqualTo(1d);
        await Assert.That(matrices.View.M14).IsEqualTo(4d);
        await Assert.That(matrices.View.M41).IsEqualTo(13d);
        await Assert.That(matrices.View.M44).IsEqualTo(16d);
        await Assert.That(matrices.Projection.M11).IsEqualTo(17d);
        await Assert.That(matrices.Projection.M14).IsEqualTo(20d);
        await Assert.That(matrices.Projection.M41).IsEqualTo(29d);
        await Assert.That(matrices.Projection.M44).IsEqualTo(32d);
        await Assert.That(clipped.ClipPlaneCount).IsEqualTo(2u);
        await Assert.That(clipped.ClipPlane0.X).IsEqualTo(1d);
        await Assert.That(clipped.ClipPlane0.W).IsEqualTo(4d);
        await Assert.That(clipped.ClipPlane1.X).IsEqualTo(-1d);
        await Assert.That(clipped.ClipPlane1.W).IsEqualTo(-4d);
        await Assert.That(clipped.ClipPlane2.X).IsEqualTo(0d);
    }

    [Test]
    public async Task ExplicitCameraRejectsInvalidClipPlanes()
    {
        await Assert.That(
            () => _ = new CameraState(
                Matrix4x4.Identity,
                Matrix4x4.Identity,
                Enumerable.Repeat(Vector4.UnitX, CameraState.MaxClipPlanes + 1)))
            .Throws<ArgumentOutOfRangeException>();

        Vector4[] invalidValues =
        [
            new(float.NaN, 0, 0, 0),
            new(0, float.PositiveInfinity, 0, 0),
            new(0, 0, float.NegativeInfinity, 0),
        ];

        foreach (Vector4 invalidValue in invalidValues)
        {
            await Assert.That(
                () => _ = new CameraState(
                    Matrix4x4.Identity,
                    Matrix4x4.Identity,
                    [invalidValue]))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task ExplicitCameraRejectsEveryNonFiniteMatrixElement()
    {
        float[] invalidValues =
        [
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity,
        ];

        foreach (float invalidValue in invalidValues)
        {
            for (int element = 0; element < 16; element++)
            {
                Matrix4x4 invalid = Matrix4x4.Identity;
                SetElement(ref invalid, element, invalidValue);

                await Assert.That(
                    () => _ = new CameraState(invalid, Matrix4x4.Identity))
                    .Throws<ArgumentException>();
                await Assert.That(
                    () => _ = new CameraState(Matrix4x4.Identity, invalid))
                    .Throws<ArgumentException>();
            }
        }
    }

    [Test]
    public async Task PublicRenderEntryPointsExposeUnambiguousOptionalCameras()
    {
        ParameterInfo stormCamera = GetParameter(
            typeof(OpenUsdStormRenderer),
            nameof(OpenUsdStormRenderer.Render),
            4,
            typeof(int), typeof(int), typeof(uint), typeof(double), typeof(CameraState),
            typeof(ulong), typeof(ulong?), typeof(bool));
        ParameterInfo silkCamera = GetParameter(
            typeof(OpenUsdSilkSession),
            nameof(OpenUsdSilkSession.Sync),
            3,
            typeof(int), typeof(int), typeof(double), typeof(CameraState),
            typeof(RenderComplexity), typeof(RenderDrawMode));
        ParameterInfo silkProductCamera = GetParameter(
            typeof(OpenUsdSilkSession),
            nameof(OpenUsdSilkSession.Sync),
            4,
            typeof(int), typeof(int), typeof(SilkSceneIngestionOptions), typeof(double),
            typeof(CameraState), typeof(RenderComplexity), typeof(RenderDrawMode));
        ParameterInfo childRenderCamera = GetParameter(
            typeof(OpenUsdStormChildSession),
            nameof(OpenUsdStormChildSession.Render),
            1,
            typeof(double), typeof(CameraState), typeof(ulong), typeof(ulong?));
        ParameterInfo childRequestCamera = GetParameter(
            typeof(OpenUsdStormChildSession),
            nameof(OpenUsdStormChildSession.RequestFrame),
            2,
            typeof(double), typeof(ulong), typeof(CameraState), typeof(ulong?));

        await Assert.That(stormCamera.ParameterType).IsEqualTo(typeof(CameraState));
        await Assert.That(silkCamera.ParameterType).IsEqualTo(typeof(CameraState));
        await Assert.That(silkProductCamera.ParameterType).IsEqualTo(typeof(CameraState));
        await Assert.That(childRenderCamera.ParameterType).IsEqualTo(typeof(CameraState));
        await Assert.That(childRequestCamera.ParameterType).IsEqualTo(typeof(CameraState));
        await Assert.That(stormCamera.HasDefaultValue).IsTrue();
        await Assert.That(silkCamera.HasDefaultValue).IsTrue();
        await Assert.That(silkProductCamera.HasDefaultValue).IsTrue();
        await Assert.That(childRenderCamera.HasDefaultValue).IsTrue();
        await Assert.That(childRequestCamera.HasDefaultValue).IsTrue();
    }

    private static int OffsetOf<T>(string fieldName) where T : struct =>
        checked((int)Marshal.OffsetOf<T>(fieldName));

    private static bool ContainsManagedArray(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(field => field.FieldType.IsArray);

    private static bool NativeBytesEqual<T>(T left, T right)
        where T : unmanaged =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref left, 1))
            .SequenceEqual(
                MemoryMarshal.AsBytes(
                    MemoryMarshal.CreateReadOnlySpan(ref right, 1)));

    private static void SetElement(
        ref Matrix4x4 matrix,
        int element,
        float value)
    {
        switch (element)
        {
            case 0:
                matrix.M11 = value;
                break;
            case 1:
                matrix.M12 = value;
                break;
            case 2:
                matrix.M13 = value;
                break;
            case 3:
                matrix.M14 = value;
                break;
            case 4:
                matrix.M21 = value;
                break;
            case 5:
                matrix.M22 = value;
                break;
            case 6:
                matrix.M23 = value;
                break;
            case 7:
                matrix.M24 = value;
                break;
            case 8:
                matrix.M31 = value;
                break;
            case 9:
                matrix.M32 = value;
                break;
            case 10:
                matrix.M33 = value;
                break;
            case 11:
                matrix.M34 = value;
                break;
            case 12:
                matrix.M41 = value;
                break;
            case 13:
                matrix.M42 = value;
                break;
            case 14:
                matrix.M43 = value;
                break;
            case 15:
                matrix.M44 = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(element));
        }
    }

    private static ParameterInfo GetParameter(
        Type declaringType,
        string methodName,
        int parameterIndex,
        params Type[] parameterTypes)
    {
        MethodInfo method = declaringType.GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.Public, null, parameterTypes, null)
            ?? throw new InvalidOperationException(
                $"The expected {declaringType.Name}.{methodName} overload is missing.");
        return method.GetParameters()[parameterIndex];
    }
}
