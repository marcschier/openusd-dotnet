// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using System.Runtime.InteropServices;

namespace OpenUsd.Interop.Tests;

public sealed class NativeContractTests
{
    [Test]
    public async Task InteropAssemblyHasExpectedName()
    {
        string? assemblyName = typeof(OpenUsdNativeContract).Assembly.GetName().Name;

        await Assert.That(assemblyName).IsEqualTo("OpenUsd.Interop");
    }

    [Test]
    public async Task DataAbiFourteenRequiresAllSchemaCapabilities()
    {
        uint abiVersion = OpenUsdNativeContract.AbiVersion;
        ulong requiredCapabilities = OpenUsdNativeContract.RequiredCapabilities;

        await Assert.That(abiVersion).IsEqualTo(14U);
        await Assert.That(requiredCapabilities).IsEqualTo(0x1FFFFUL);
        await Assert.That(requiredCapabilities & 0xFFFUL).IsEqualTo(0xFFFUL);
        await Assert.That(requiredCapabilities & 0x1000UL).IsEqualTo(0x1000UL);
        await Assert.That(requiredCapabilities & 0x2000UL).IsEqualTo(0x2000UL);
        await Assert.That(requiredCapabilities & 0x4000UL).IsEqualTo(0x4000UL);
        await Assert.That(requiredCapabilities & 0x8000UL).IsEqualTo(0x8000UL);

        // Inspection v2: oriented bounds, prim specifier, Ts splines and TfDebug.
        await Assert.That(requiredCapabilities & 0x10000UL).IsEqualTo(0x10000UL);
    }

    [Test]
    public async Task GeometryValueLayoutsMatchTheCAbi()
    {
        await Assert.That(Marshal.SizeOf<OpenUsdNativeVec2f>()).IsEqualTo(8);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeVec3f>()).IsEqualTo(12);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeQuatf>()).IsEqualTo(16);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeMatrix4d>()).IsEqualTo(128);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeExtent3f>()).IsEqualTo(24);

        await Assert.That(Marshal.OffsetOf<OpenUsdNativeVec2f>(nameof(OpenUsdNativeVec2f.X)).ToInt32()).IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeVec2f>(nameof(OpenUsdNativeVec2f.Y)).ToInt32()).IsEqualTo(4);
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeVec3f>(nameof(OpenUsdNativeVec3f.Z)).ToInt32()).IsEqualTo(8);
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeQuatf>("<Real>k__BackingField").ToInt32()).IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeQuatf>("<Z>k__BackingField").ToInt32()).IsEqualTo(12);
        await Assert.That(
            Marshal.OffsetOf<OpenUsdNativeMatrix4d>(nameof(OpenUsdNativeMatrix4d.M33)).ToInt32())
            .IsEqualTo(120);
        await Assert.That(
            Marshal.OffsetOf<OpenUsdNativeExtent3f>(nameof(OpenUsdNativeExtent3f.Maximum)).ToInt32())
            .IsEqualTo(12);
    }

    [Test]
    public async Task VersionedStructLayoutsMatchTheCAbi()
    {
        Type runtime = typeof(OpenUsdNativeRuntime);
        Type view = runtime.GetNestedType("NativeStringListView", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NativeStringListView was not found.");
        Type payloadArcView = runtime.GetNestedType(
            "NativePayloadArcListView",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NativePayloadArcListView was not found.");
        Type error = runtime.GetNestedType("NativeErrorBuffer", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NativeErrorBuffer was not found.");
        Type metadata = runtime.Assembly.GetType("OpenUsd.Interop.OpenUsdNativeMetadataValue")
            ?? throw new InvalidOperationException("OpenUsdNativeMetadataValue was not found.");
        Type scalar = runtime.Assembly.GetType("OpenUsd.Interop.OpenUsdNativeScalarValue")
            ?? throw new InvalidOperationException("OpenUsdNativeScalarValue was not found.");
        Type bounds = runtime.Assembly.GetType("OpenUsd.Interop.OpenUsdNativeBounds3d")
            ?? throw new InvalidOperationException("OpenUsdNativeBounds3d was not found.");
        Type cameraState = runtime.Assembly.GetType(
            "OpenUsd.Interop.OpenUsdNativeCameraState")
            ?? throw new InvalidOperationException("OpenUsdNativeCameraState was not found.");
        int pointerSize = IntPtr.Size;

        await Assert.That(Marshal.SizeOf(view)).IsEqualTo(pointerSize == 8 ? 48 : 24);
        await Assert.That(Marshal.OffsetOf(view, "StructSize").ToInt32()).IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf(view, "Data").ToInt32()).IsEqualTo(pointerSize == 8 ? 8 : 4);
        await Assert.That(Marshal.OffsetOf(view, "DataSize").ToInt32()).IsEqualTo(pointerSize == 8 ? 16 : 8);
        await Assert.That(Marshal.OffsetOf(view, "Offsets").ToInt32()).IsEqualTo(pointerSize == 8 ? 24 : 12);
        await Assert.That(Marshal.OffsetOf(view, "OffsetsSize").ToInt32()).IsEqualTo(pointerSize == 8 ? 32 : 16);
        await Assert.That(Marshal.OffsetOf(view, "Count").ToInt32()).IsEqualTo(pointerSize == 8 ? 40 : 20);

        await Assert.That(Marshal.SizeOf(payloadArcView)).IsEqualTo(pointerSize == 8 ? 48 : 28);
        await Assert.That(Marshal.OffsetOf(payloadArcView, "StructSize").ToInt32()).IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf(payloadArcView, "Version").ToInt32()).IsEqualTo(4);
        await Assert.That(Marshal.OffsetOf(payloadArcView, "Data").ToInt32()).IsEqualTo(8);
        await Assert.That(Marshal.OffsetOf(payloadArcView, "DataSize").ToInt32())
            .IsEqualTo(pointerSize == 8 ? 16 : 12);
        await Assert.That(Marshal.OffsetOf(payloadArcView, "Offsets").ToInt32())
            .IsEqualTo(pointerSize == 8 ? 24 : 16);
        await Assert.That(Marshal.OffsetOf(payloadArcView, "OffsetsSize").ToInt32())
            .IsEqualTo(pointerSize == 8 ? 32 : 20);
        await Assert.That(Marshal.OffsetOf(payloadArcView, "Count").ToInt32())
            .IsEqualTo(pointerSize == 8 ? 40 : 24);

        await Assert.That(Marshal.SizeOf(error)).IsEqualTo(pointerSize * 3);
        await Assert.That(Marshal.OffsetOf(error, "Data").ToInt32()).IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf(error, "Capacity").ToInt32()).IsEqualTo(pointerSize);
        await Assert.That(Marshal.OffsetOf(error, "Required").ToInt32()).IsEqualTo(pointerSize * 2);

        await Assert.That(Marshal.SizeOf(metadata)).IsEqualTo(32);
        await Assert.That(Marshal.OffsetOf(metadata, "Int64Value").ToInt32()).IsEqualTo(16);
        await Assert.That(Marshal.OffsetOf(metadata, "DoubleValue").ToInt32()).IsEqualTo(24);
        await Assert.That(Marshal.SizeOf(scalar)).IsEqualTo(176);
        await Assert.That(Marshal.OffsetOf(scalar, "Int64ValueRaw").ToInt32()).IsEqualTo(16);
        await Assert.That(Marshal.OffsetOf(scalar, "Vec3fValueRaw").ToInt32()).IsEqualTo(32);
        await Assert.That(Marshal.OffsetOf(scalar, "Matrix4dValueRaw").ToInt32()).IsEqualTo(48);
        await Assert.That(Marshal.SizeOf(bounds)).IsEqualTo(64);
        await Assert.That(Marshal.OffsetOf(bounds, "StructSize").ToInt32()).IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf(bounds, "Version").ToInt32()).IsEqualTo(4);
        await Assert.That(Marshal.OffsetOf(bounds, "IsValid").ToInt32()).IsEqualTo(8);
        await Assert.That(Marshal.OffsetOf(bounds, "IsEmpty").ToInt32()).IsEqualTo(12);
        await Assert.That(Marshal.OffsetOf(bounds, "MinimumX").ToInt32()).IsEqualTo(16);
        await Assert.That(Marshal.OffsetOf(bounds, "MaximumX").ToInt32()).IsEqualTo(40);
        await Assert.That(Marshal.SizeOf(cameraState)).IsEqualTo(120);
        await Assert.That(Marshal.OffsetOf(cameraState, "StructSize").ToInt32())
            .IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf(cameraState, "Version").ToInt32())
            .IsEqualTo(4);
        await Assert.That(Marshal.OffsetOf(cameraState, "IsValid").ToInt32())
            .IsEqualTo(8);
        await Assert.That(Marshal.OffsetOf(cameraState, "Projection").ToInt32())
            .IsEqualTo(12);
        await Assert.That(Marshal.OffsetOf(cameraState, "WindowLeft").ToInt32())
            .IsEqualTo(16);
        await Assert.That(Marshal.OffsetOf(cameraState, "ClippingNear").ToInt32())
            .IsEqualTo(48);
        await Assert.That(Marshal.OffsetOf(cameraState, "FocalLength").ToInt32())
            .IsEqualTo(64);
        await Assert.That(Marshal.OffsetOf(
            cameraState,
            "HorizontalApertureOffset").ToInt32()).IsEqualTo(88);
        await Assert.That(Marshal.OffsetOf(cameraState, "FocusDistance").ToInt32())
            .IsEqualTo(104);
        await Assert.That(Marshal.OffsetOf(cameraState, "FStop").ToInt32())
            .IsEqualTo(112);
    }

    [Test]
    public async Task GeneratedInteropContainsCompositionEnumerationExports()
    {
        Type nativeMethods = typeof(OpenUsdNativeRuntime).GetNestedType(
            "NativeMethods",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NativeMethods was not found.");

        await Assert.That(nativeMethods.GetMethod(
            "StageGetVariantSetNames",
            BindingFlags.NonPublic | BindingFlags.Static)).IsNotNull();
        await Assert.That(nativeMethods.GetMethod(
            "StageGetComposedPayloadArcs",
            BindingFlags.NonPublic | BindingFlags.Static)).IsNotNull();
        await Assert.That(nativeMethods.GetMethod(
            "PayloadArcListRelease",
            BindingFlags.NonPublic | BindingFlags.Static)).IsNotNull();
    }

    [Test]
    public async Task GeneratedInteropContainsBulkWorldTransformExport()
    {
        Type nativeMethods = typeof(OpenUsdNativeRuntime).GetNestedType(
            "NativeMethods",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NativeMethods was not found.");
        MethodInfo method = nativeMethods.GetMethod(
            "GeomXformableGetWorldTransform",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("World-transform export was not found.");
        ParameterInfo[] parameters = method.GetParameters();

        await Assert.That(method.ReturnType).IsEqualTo(typeof(OpenUsdNativeStatus));
        await Assert.That(parameters).Count().IsEqualTo(6);
        await Assert.That(parameters[4].IsOut).IsTrue();
        await Assert.That(parameters[4].ParameterType)
            .IsEqualTo(typeof(OpenUsdNativeMatrix4d).MakeByRefType());
    }

    [Test]
    public async Task GeneratedInteropContainsBulkCameraStateExport()
    {
        Type nativeMethods = typeof(OpenUsdNativeRuntime).GetNestedType(
            "NativeMethods",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NativeMethods was not found.");
        MethodInfo method = nativeMethods.GetMethod(
            "GeomCameraGetState",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Camera-state export was not found.");
        ParameterInfo[] parameters = method.GetParameters();

        await Assert.That(method.ReturnType).IsEqualTo(typeof(OpenUsdNativeStatus));
        await Assert.That(parameters).Count().IsEqualTo(6);
        await Assert.That(parameters[4].ParameterType.Name)
            .IsEqualTo("OpenUsdNativeCameraState&");
        await Assert.That(parameters[4].IsOut).IsFalse();
    }

    [Test]
    public async Task ManagedWorldTransformContractRejectsNonFiniteNativeMatrices()
    {
        MethodInfo validator = typeof(OpenUsdNativeRuntime).GetMethod(
            "ValidateWorldTransformResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("World-transform validator was not found.");
        var finite = new OpenUsdNativeMatrix4d
        {
            M00 = 1,
            M11 = 1,
            M22 = 1,
            M33 = 1
        };
        validator.Invoke(null, [finite]);

        finite.M21 = double.PositiveInfinity;
        Exception exception;
        try
        {
            validator.Invoke(null, [finite]);
            throw new InvalidOperationException("A non-finite world transform was accepted.");
        }
        catch (TargetInvocationException invocationException)
        {
            exception = invocationException.InnerException ?? invocationException;
        }

        await Assert.That(exception).IsTypeOf<OpenUsdNativeException>();
        await Assert.That(((OpenUsdNativeException)exception).Status)
            .IsEqualTo(OpenUsdNativeStatus.NativeError);
    }

    [Test]
    public async Task ManagedCameraStateContractRejectsNonFiniteOrInvalidResults()
    {
        Type runtime = typeof(OpenUsdNativeRuntime);
        Type stateType = runtime.Assembly.GetType(
            "OpenUsd.Interop.OpenUsdNativeCameraState")
            ?? throw new InvalidOperationException("OpenUsdNativeCameraState was not found.");
        MethodInfo validator = runtime.GetMethod(
            "ValidateGeomCameraStateResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Camera-state validator was not found.");
        object state = Activator.CreateInstance(stateType)
            ?? throw new InvalidOperationException("Could not create native camera state.");
        SetField(stateType, state, "StructSize", (uint)Marshal.SizeOf(stateType));
        SetField(stateType, state, "Version", 1U);
        SetField(stateType, state, "IsValid", 1);
        SetField(stateType, state, "Projection", 0);
        SetField(stateType, state, "WindowLeft", -0.2d);
        SetField(stateType, state, "WindowRight", 0.3d);
        SetField(stateType, state, "WindowBottom", -0.1d);
        SetField(stateType, state, "WindowTop", 0.15d);
        SetField(stateType, state, "ClippingNear", 0.1d);
        SetField(stateType, state, "ClippingFar", 1000d);
        SetField(stateType, state, "FocalLength", 50d);
        SetField(stateType, state, "HorizontalAperture", 24d);
        SetField(stateType, state, "VerticalAperture", 18d);
        SetField(stateType, state, "HorizontalApertureOffset", 2d);
        SetField(stateType, state, "VerticalApertureOffset", -1d);
        SetField(stateType, state, "FocusDistance", 10d);
        SetField(stateType, state, "FStop", 2.8d);
        validator.Invoke(null, [state]);

        SetField(stateType, state, "Projection", 1);
        SetField(stateType, state, "ClippingNear", -10d);
        SetField(stateType, state, "FocalLength", 0d);
        validator.Invoke(null, [state]);

        SetField(stateType, state, "Projection", 0);
        SetField(stateType, state, "ClippingNear", 0.1d);
        Exception perspectiveZero = CaptureInvocation(validator, state);
        await Assert.That(perspectiveZero).IsTypeOf<OpenUsdNativeException>();
        await Assert.That(((OpenUsdNativeException)perspectiveZero).Status)
            .IsEqualTo(OpenUsdNativeStatus.NativeError);

        SetField(stateType, state, "Projection", 1);
        SetField(stateType, state, "ClippingNear", -10d);
        SetField(stateType, state, "FocalLength", -1d);
        Exception negativeFocal = CaptureInvocation(validator, state);
        await Assert.That(negativeFocal).IsTypeOf<OpenUsdNativeException>();
        await Assert.That(((OpenUsdNativeException)negativeFocal).Status)
            .IsEqualTo(OpenUsdNativeStatus.NativeError);

        SetField(stateType, state, "FocalLength", 0d);
        SetField(stateType, state, "WindowRight", double.PositiveInfinity);
        Exception exception = CaptureInvocation(validator, state);

        await Assert.That(exception).IsTypeOf<OpenUsdNativeException>();
        await Assert.That(((OpenUsdNativeException)exception).Status)
            .IsEqualTo(OpenUsdNativeStatus.NativeError);
    }

    [Test]
    public async Task WorldBoundsContractRejectsOverflowingFiniteExtent()
    {
        Type runtime = typeof(OpenUsdNativeRuntime);
        Type boundsType = runtime.Assembly.GetType("OpenUsd.Interop.OpenUsdNativeBounds3d")
            ?? throw new InvalidOperationException("OpenUsdNativeBounds3d was not found.");
        MethodInfo validator = runtime.GetMethod(
            "ValidateWorldBoundsResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("World-bounds validator was not found.");
        object bounds = Activator.CreateInstance(boundsType)
            ?? throw new InvalidOperationException("Could not create native bounds.");
        SetField(boundsType, bounds, "StructSize", (uint)Marshal.SizeOf(boundsType));
        SetField(boundsType, bounds, "Version", 1U);
        SetField(boundsType, bounds, "IsValid", 1);
        SetField(boundsType, bounds, "IsEmpty", 0);
        SetField(boundsType, bounds, "MinimumX", -double.MaxValue);
        SetField(boundsType, bounds, "MinimumY", 0.0);
        SetField(boundsType, bounds, "MinimumZ", 0.0);
        SetField(boundsType, bounds, "MaximumX", double.MaxValue);
        SetField(boundsType, bounds, "MaximumY", 0.0);
        SetField(boundsType, bounds, "MaximumZ", 0.0);

        Exception exception;
        try
        {
            validator.Invoke(null, [bounds]);
            throw new InvalidOperationException("Overflowing world bounds were accepted.");
        }
        catch (TargetInvocationException invocationException)
        {
            exception = invocationException.InnerException ?? invocationException;
        }

        await Assert.That(exception).IsTypeOf<OpenUsdNativeException>();
        await Assert.That(((OpenUsdNativeException)exception).Status)
            .IsEqualTo(OpenUsdNativeStatus.NativeError);
    }

    [Test]
    public async Task VersionedNativeResultHeadersRejectEveryBoundaryMutation()
    {
        Type runtime = typeof(OpenUsdNativeRuntime);
        Type cameraType = runtime.Assembly.GetType(
            "OpenUsd.Interop.OpenUsdNativeCameraState")
            ?? throw new InvalidOperationException("OpenUsdNativeCameraState was not found.");
        Type boundsType = runtime.Assembly.GetType("OpenUsd.Interop.OpenUsdNativeBounds3d")
            ?? throw new InvalidOperationException("OpenUsdNativeBounds3d was not found.");
        MethodInfo cameraValidator = runtime.GetMethod(
            "ValidateGeomCameraStateResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Camera-state validator was not found.");
        MethodInfo boundsValidator = runtime.GetMethod(
            "ValidateWorldBoundsResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("World-bounds validator was not found.");

        cameraValidator.Invoke(null, [CreateValidCameraState(cameraType)]);
        boundsValidator.Invoke(null, [CreateValidBounds(boundsType)]);

        int rejected = 0;
        uint cameraSize = checked((uint)Marshal.SizeOf(cameraType));
        foreach (uint structSize in new uint[]
                 {
                     0,
                     cameraSize - 1,
                     cameraSize + 1,
                     uint.MaxValue,
                 })
        {
            object state = CreateValidCameraState(cameraType);
            SetField(cameraType, state, "StructSize", structSize);
            RequireNativeError(
                CaptureInvocation(cameraValidator, state),
                $"camera struct size {structSize}");
            rejected++;
        }
        foreach (uint version in new uint[] { 0, 2, uint.MaxValue })
        {
            object state = CreateValidCameraState(cameraType);
            SetField(cameraType, state, "Version", version);
            RequireNativeError(
                CaptureInvocation(cameraValidator, state),
                $"camera version {version}");
            rejected++;
        }

        uint boundsSize = checked((uint)Marshal.SizeOf(boundsType));
        foreach (uint structSize in new uint[]
                 {
                     0,
                     boundsSize - 1,
                     boundsSize + 1,
                     uint.MaxValue,
                 })
        {
            object bounds = CreateValidBounds(boundsType);
            SetField(boundsType, bounds, "StructSize", structSize);
            RequireNativeError(
                CaptureInvocation(boundsValidator, bounds),
                $"bounds struct size {structSize}");
            rejected++;
        }
        foreach (uint version in new uint[] { 0, 2, uint.MaxValue })
        {
            object bounds = CreateValidBounds(boundsType);
            SetField(boundsType, bounds, "Version", version);
            RequireNativeError(
                CaptureInvocation(boundsValidator, bounds),
                $"bounds version {version}");
            rejected++;
        }

        await Assert.That(rejected).IsEqualTo(14);
    }

    [Test]
    public async Task NativeResultValidatorsRejectEveryNonFiniteFieldMutation()
    {
        Type runtime = typeof(OpenUsdNativeRuntime);
        Type cameraType = runtime.Assembly.GetType(
            "OpenUsd.Interop.OpenUsdNativeCameraState")
            ?? throw new InvalidOperationException("OpenUsdNativeCameraState was not found.");
        Type boundsType = runtime.Assembly.GetType("OpenUsd.Interop.OpenUsdNativeBounds3d")
            ?? throw new InvalidOperationException("OpenUsdNativeBounds3d was not found.");
        MethodInfo cameraValidator = runtime.GetMethod(
            "ValidateGeomCameraStateResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Camera-state validator was not found.");
        MethodInfo boundsValidator = runtime.GetMethod(
            "ValidateWorldBoundsResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("World-bounds validator was not found.");
        MethodInfo transformValidator = runtime.GetMethod(
            "ValidateWorldTransformResult",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("World-transform validator was not found.");
        string[] cameraFields =
        [
            "WindowLeft",
            "WindowRight",
            "WindowBottom",
            "WindowTop",
            "ClippingNear",
            "ClippingFar",
            "FocalLength",
            "HorizontalAperture",
            "VerticalAperture",
            "HorizontalApertureOffset",
            "VerticalApertureOffset",
            "FocusDistance",
            "FStop",
        ];
        string[] boundsFields =
        [
            "MinimumX",
            "MinimumY",
            "MinimumZ",
            "MaximumX",
            "MaximumY",
            "MaximumZ",
        ];
        string[] transformFields =
        [
            nameof(OpenUsdNativeMatrix4d.M00),
            nameof(OpenUsdNativeMatrix4d.M01),
            nameof(OpenUsdNativeMatrix4d.M02),
            nameof(OpenUsdNativeMatrix4d.M03),
            nameof(OpenUsdNativeMatrix4d.M10),
            nameof(OpenUsdNativeMatrix4d.M11),
            nameof(OpenUsdNativeMatrix4d.M12),
            nameof(OpenUsdNativeMatrix4d.M13),
            nameof(OpenUsdNativeMatrix4d.M20),
            nameof(OpenUsdNativeMatrix4d.M21),
            nameof(OpenUsdNativeMatrix4d.M22),
            nameof(OpenUsdNativeMatrix4d.M23),
            nameof(OpenUsdNativeMatrix4d.M30),
            nameof(OpenUsdNativeMatrix4d.M31),
            nameof(OpenUsdNativeMatrix4d.M32),
            nameof(OpenUsdNativeMatrix4d.M33),
        ];
        double[] nonFiniteValues =
        [
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
        ];

        int rejected = 0;
        foreach (string field in cameraFields)
        {
            foreach (double value in nonFiniteValues)
            {
                object state = CreateValidCameraState(cameraType);
                SetField(cameraType, state, field, value);
                RequireNativeError(
                    CaptureInvocation(cameraValidator, state),
                    $"camera {field}={value}");
                rejected++;
            }
        }
        foreach (string field in boundsFields)
        {
            foreach (double value in nonFiniteValues)
            {
                object bounds = CreateValidBounds(boundsType);
                SetField(boundsType, bounds, field, value);
                RequireNativeError(
                    CaptureInvocation(boundsValidator, bounds),
                    $"bounds {field}={value}");
                rejected++;
            }
        }
        foreach (string field in transformFields)
        {
            FieldInfo matrixField = typeof(OpenUsdNativeMatrix4d).GetField(field)
                ?? throw new InvalidOperationException($"{field} was not found.");
            foreach (double value in nonFiniteValues)
            {
                object matrix = CreateFiniteMatrix();
                matrixField.SetValue(matrix, value);
                RequireNativeError(
                    CaptureInvocation(transformValidator, matrix),
                    $"world transform {field}={value}");
                rejected++;
            }
        }

        await Assert.That(rejected).IsEqualTo(105);
    }

    private static object CreateValidCameraState(Type stateType)
    {
        object state = Activator.CreateInstance(stateType)
            ?? throw new InvalidOperationException("Could not create native camera state.");
        SetField(stateType, state, "StructSize", checked((uint)Marshal.SizeOf(stateType)));
        SetField(stateType, state, "Version", 1U);
        SetField(stateType, state, "IsValid", 1);
        SetField(stateType, state, "Projection", 0);
        SetField(stateType, state, "WindowLeft", -0.2d);
        SetField(stateType, state, "WindowRight", 0.3d);
        SetField(stateType, state, "WindowBottom", -0.1d);
        SetField(stateType, state, "WindowTop", 0.15d);
        SetField(stateType, state, "ClippingNear", 0.1d);
        SetField(stateType, state, "ClippingFar", 1000d);
        SetField(stateType, state, "FocalLength", 50d);
        SetField(stateType, state, "HorizontalAperture", 24d);
        SetField(stateType, state, "VerticalAperture", 18d);
        SetField(stateType, state, "HorizontalApertureOffset", 2d);
        SetField(stateType, state, "VerticalApertureOffset", -1d);
        SetField(stateType, state, "FocusDistance", 10d);
        SetField(stateType, state, "FStop", 2.8d);
        return state;
    }

    private static object CreateValidBounds(Type boundsType)
    {
        object bounds = Activator.CreateInstance(boundsType)
            ?? throw new InvalidOperationException("Could not create native bounds.");
        SetField(boundsType, bounds, "StructSize", checked((uint)Marshal.SizeOf(boundsType)));
        SetField(boundsType, bounds, "Version", 1U);
        SetField(boundsType, bounds, "IsValid", 1);
        SetField(boundsType, bounds, "IsEmpty", 0);
        SetField(boundsType, bounds, "MinimumX", -1d);
        SetField(boundsType, bounds, "MinimumY", -2d);
        SetField(boundsType, bounds, "MinimumZ", -3d);
        SetField(boundsType, bounds, "MaximumX", 4d);
        SetField(boundsType, bounds, "MaximumY", 5d);
        SetField(boundsType, bounds, "MaximumZ", 6d);
        return bounds;
    }

    private static OpenUsdNativeMatrix4d CreateFiniteMatrix() => new()
    {
        M00 = 1,
        M11 = 1,
        M22 = 1,
        M33 = 1,
    };

    private static void RequireNativeError(Exception exception, string mutation)
    {
        if (exception is not OpenUsdNativeException nativeException ||
            nativeException.Status != OpenUsdNativeStatus.NativeError)
        {
            throw new InvalidOperationException(
                $"Native result mutation '{mutation}' escaped as {exception.GetType().Name}.",
                exception);
        }
    }

    private static void SetField(Type type, object instance, string name, object value)
    {
        FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{name} was not found.");
        field.SetValue(instance, value);
    }

    private static Exception CaptureInvocation(MethodInfo method, object argument)
    {
        try
        {
            method.Invoke(null, [argument]);
            throw new InvalidOperationException("The invalid native result was accepted.");
        }
        catch (TargetInvocationException invocationException)
        {
            return invocationException.InnerException ?? invocationException;
        }
    }
}
