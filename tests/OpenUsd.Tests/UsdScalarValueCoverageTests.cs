// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdScalarValueCoverageTests
{
    [Test]
    public async Task DefaultValueIsInvalidAndRejectsEveryAccessor()
    {
        UsdScalarValue value = default;

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Invalid);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsBooleanAndRejectsEveryOtherAccessor()
    {
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Boolean, boolValue: true));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Boolean);
        await Assert.That(value.BoolValue).IsTrue();
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsSigned64AndRejectsEveryOtherAccessor()
    {
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Signed64, int64Value: long.MinValue));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Signed64);
        await Assert.That(value.Int64Value).IsEqualTo(long.MinValue);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsNumberAndRejectsEveryOtherAccessor()
    {
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Number, doubleValue: -123.25));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Number);
        await Assert.That(value.DoubleValue).IsEqualTo(-123.25);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsTextAndPreservesItsReference()
    {
        string payload = new(['t', 'e', 'x', 't']);
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Text, textValue: payload));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Text);
        await Assert.That(value.StringValue).IsSameReferenceAs(payload);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsTokenAndPreservesItsReference()
    {
        string payload = new(['t', 'o', 'k', 'e', 'n']);
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Token, textValue: payload));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Token);
        await Assert.That(value.TokenValue).IsSameReferenceAs(payload);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsVector3AndRejectsColorAccessor()
    {
        var payload = new OpenUsdNativeVec3f(1.25f, -2.5f, 3.75f);
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Vector3, vec3fValue: payload));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Vector3);
        await Assert.That(value.Vec3fValue).IsEqualTo(new UsdVec3f(1.25f, -2.5f, 3.75f));
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsColor3AndRejectsVectorAccessor()
    {
        var payload = new OpenUsdNativeVec3f(0.1f, 0.2f, 0.3f);
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Color3, vec3fValue: payload));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Color3);
        await Assert.That(value.Color3fValue).IsEqualTo(new UsdVec3f(0.1f, 0.2f, 0.3f));
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeMapsMatrix4dAndEveryElement()
    {
        OpenUsdNativeMatrix4d payload = CreateNativeMatrix();
        UsdScalarValue value = UsdScalarValue.FromNative(
            CreateNative(OpenUsdNativeScalarKind.Matrix4d, matrix4dValue: payload));

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Matrix4d);
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                await Assert.That(value.Matrix4dValue[row, column])
                    .IsEqualTo((row * 4) + column + 1);
            }
        }
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FromNativeRejectsUnknownTag()
    {
        OpenUsdNativeScalarResult native = CreateNative((OpenUsdNativeScalarKind)int.MaxValue);

        InvalidOperationException exception = CaptureInvalidOperation(
            () => _ = UsdScalarValue.FromNative(native));

        await Assert.That(exception.Message)
            .IsEqualTo("The native scalar kind is not supported.");
    }

    [Test]
    public async Task Int32ArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        int[] payload = [1, -2, 3];
        UsdScalarValue value = UsdScalarValue.FromInt32Array(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Int32Array);
        await Assert.That(value.Int32ArrayValue).IsSameReferenceAs(payload);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task FloatArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        float[] payload = [1.25f, -2.5f, 3.75f];
        UsdScalarValue value = UsdScalarValue.FromFloatArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.FloatArray);
        await Assert.That(value.FloatArrayValue).IsSameReferenceAs(payload);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task DoubleArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        double[] payload = [1.25, -2.5, 3.75];
        UsdScalarValue value = UsdScalarValue.FromDoubleArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.DoubleArray);
        await Assert.That(value.DoubleArrayValue).IsSameReferenceAs(payload);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task Vec2fArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        UsdVec2f[] payload = [new(1, 2), new(3, 4)];
        UsdScalarValue value = UsdScalarValue.FromVec2fArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Vec2fArray);
        await Assert.That(value.Vec2fArrayValue).IsSameReferenceAs(payload);
        await Assert.That(value.Vec2fArrayValue[1]).IsEqualTo(new UsdVec2f(3, 4));
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task Vec3fArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        UsdVec3f[] payload = [new(1, 2, 3), new(4, 5, 6)];
        UsdScalarValue value = UsdScalarValue.FromVec3fArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Vec3fArray);
        await Assert.That(value.Vec3fArrayValue).IsSameReferenceAs(payload);
        await Assert.That(value.Vec3fArrayValue[1]).IsEqualTo(new UsdVec3f(4, 5, 6));
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task Color3fArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        UsdVec3f[] payload = [new(0.1f, 0.2f, 0.3f), new(0.4f, 0.5f, 0.6f)];
        UsdScalarValue value = UsdScalarValue.FromColor3fArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Color3fArray);
        await Assert.That(value.Color3fArrayValue).IsSameReferenceAs(payload);
        await Assert.That(value.Color3fArrayValue[1]).IsEqualTo(new UsdVec3f(0.4f, 0.5f, 0.6f));
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task BooleanArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        bool[] payload = [true, false, true];
        UsdScalarValue value = UsdScalarValue.FromBoolArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.BooleanArray);
        await Assert.That(value.BoolArrayValue).IsSameReferenceAs(payload);
        await Assert.That(value.BoolArrayValue[1]).IsFalse();
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task TokenArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        string[] payload = [new(['t', 'o', 'k', 'e', 'n']), new(['u', 's', 'd'])];
        UsdScalarValue value = UsdScalarValue.FromTokenArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.TokenArray);
        await Assert.That(value.TokenArrayValue).IsSameReferenceAs(payload);
        await Assert.That(value.TokenArrayValue[0]).IsSameReferenceAs(payload[0]);
        await AssertWrongKindAccessors(value);
    }

    [Test]
    public async Task StringArrayPreservesPayloadIdentityAndRejectsOtherAccessors()
    {
        string[] payload = [new(['s', 't', 'r', 'i', 'n', 'g']), new(['v', 'a', 'l', 'u', 'e'])];
        UsdScalarValue value = UsdScalarValue.FromStringArray(payload);

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.StringArray);
        await Assert.That(value.StringArrayValue).IsSameReferenceAs(payload);
        await Assert.That(value.StringArrayValue[1]).IsSameReferenceAs(payload[1]);
        await AssertWrongKindAccessors(value);
    }

    private static async Task AssertWrongKindAccessors(UsdScalarValue value)
    {
        foreach (UsdScalarKind requestedKind in Enum.GetValues<UsdScalarKind>())
        {
            if (requestedKind == UsdScalarKind.Invalid || requestedKind == value.Kind)
            {
                continue;
            }

            InvalidOperationException exception = CaptureInvalidOperation(
                () => _ = Read(value, requestedKind));
            await Assert.That(exception.Message)
                .IsEqualTo($"The scalar contains {value.Kind}, not {requestedKind}.")
                .Because($"{requestedKind} unexpectedly accepted a {value.Kind} payload.");
        }
    }

    private static object Read(UsdScalarValue value, UsdScalarKind kind) => kind switch
    {
        UsdScalarKind.Boolean => value.BoolValue,
        UsdScalarKind.Signed64 => value.Int64Value,
        UsdScalarKind.Number => value.DoubleValue,
        UsdScalarKind.Text => value.StringValue,
        UsdScalarKind.Token => value.TokenValue,
        UsdScalarKind.Vector3 => value.Vec3fValue,
        UsdScalarKind.Color3 => value.Color3fValue,
        UsdScalarKind.Matrix4d => value.Matrix4dValue,
        UsdScalarKind.Int32Array => value.Int32ArrayValue,
        UsdScalarKind.FloatArray => value.FloatArrayValue,
        UsdScalarKind.DoubleArray => value.DoubleArrayValue,
        UsdScalarKind.Vec2fArray => value.Vec2fArrayValue,
        UsdScalarKind.Vec3fArray => value.Vec3fArrayValue,
        UsdScalarKind.Color3fArray => value.Color3fArrayValue,
        UsdScalarKind.BooleanArray => value.BoolArrayValue,
        UsdScalarKind.TokenArray => value.TokenArrayValue,
        UsdScalarKind.StringArray => value.StringArrayValue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static OpenUsdNativeScalarResult CreateNative(
        OpenUsdNativeScalarKind kind,
        bool boolValue = false,
        long int64Value = 0,
        double doubleValue = 0,
        string? textValue = null,
        OpenUsdNativeVec3f vec3fValue = default,
        OpenUsdNativeMatrix4d matrix4dValue = default)
    {
        var value = new OpenUsdNativeScalarValue
        {
            KindValue = (int)kind,
            BoolValueRaw = boolValue ? 1 : 0,
            Int64ValueRaw = int64Value,
            DoubleValueRaw = doubleValue,
            Vec3fValueRaw = vec3fValue,
            Matrix4dValueRaw = matrix4dValue
        };
        return new OpenUsdNativeScalarResult(value, textValue);
    }

    private static OpenUsdNativeMatrix4d CreateNativeMatrix() => new()
    {
        M00 = 1,
        M01 = 2,
        M02 = 3,
        M03 = 4,
        M10 = 5,
        M11 = 6,
        M12 = 7,
        M13 = 8,
        M20 = 9,
        M21 = 10,
        M22 = 11,
        M23 = 12,
        M30 = 13,
        M31 = 14,
        M32 = 15,
        M33 = 16
    };

    private static InvalidOperationException CaptureInvalidOperation(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected an InvalidOperationException.");
    }
}
