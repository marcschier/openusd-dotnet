// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol.Wire;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// Maps every <see cref="LiveStageUpdate"/> and value kind to and from its wire case.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is exhaustive and explicit in both directions. There is no reflection, no
/// <c>Any</c>, and no JSON: each case is a hand-written arm, so adding an authoring update kind
/// without adding its wire case is a compile-time gap rather than a silent runtime drop.
/// </para>
/// <para>
/// Decoding never trusts its input. An unset oneof, an unspecified enum, an unknown enum value, a
/// wrong-sized matrix, or a bound breach is reported as a <see cref="BridgeWireError"/>; the
/// authoring types themselves perform the authoritative grammar and payload validation, and the
/// <see cref="ArgumentException"/> they throw is converted into the same bounded error type.
/// </para>
/// </remarks>
internal static class BridgeUpdateCodec
{
    private const int Matrix4dComponentCount = 16;

    internal static StageUpdate ToWire(LiveStageUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update switch
        {
            DefinePrimUpdate value => CreateDefinePrim(value),
            RemovePrimUpdate value => new StageUpdate
            {
                RemovePrim = new RemovePrim { PrimPath = value.PrimPath }
            },
            SetAttributeUpdate value => CreateSetAttribute(value),
            ClearUpdate value => CreateClear(value),
            SetRelationshipTargetsUpdate value => CreateSetRelationshipTargets(value),
            SetReferenceUpdate value => CreateSetReference(value),
            SetPayloadUpdate value => CreateSetPayload(value),
            SetActiveUpdate value => new StageUpdate
            {
                SetActive = new SetActive { PrimPath = value.PrimPath, Active = value.Active }
            },
            SetInstanceableUpdate value => new StageUpdate
            {
                SetInstanceable = new SetInstanceable
                {
                    PrimPath = value.PrimPath,
                    Instanceable = value.Instanceable
                }
            },
            SetVariantSelectionUpdate value => CreateSetVariantSelection(value),
            SetMetadataUpdate value => CreateSetMetadata(value),
            ApiSchemaUpdate value => CreateApiSchema(value),
            SetPointInstancerOrientationsUpdate value => CreateOrientations(value),
            ReplaceBridgeOverlayUpdate => throw new BridgeProtocolException(
                BridgeWireError.Create(
                    BridgeWireErrorCode.OverlayReplacementNotAllowed,
                    "A bridge overlay replacement is expressed as a full snapshot, not as an update.")),
            _ => throw new BridgeProtocolException(
                BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownUpdateKind,
                    $"The update kind '{update.GetType().Name}' has no wire case."))
        };
    }

    internal static bool TryFromWire(
        StageUpdate? wire,
        out LiveStageUpdate? update,
        out BridgeWireError error)
    {
        update = null;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "A stage update was null.");
            return false;
        }

        try
        {
            return TryConvert(wire, out update, out error);
        }
        catch (ArgumentException exception)
        {
            // The authoring types own grammar, range, and payload validation. Their rejection is a
            // protocol violation here, not a local programming error, so it is reported rather than
            // propagated: an untrusted frame must never fault the receive loop.
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                $"The update was rejected by the authoring layer: {exception.Message}");
            update = null;
            return false;
        }
    }

    internal static AttributeValue ToWire(LiveAttributeValue value) => value.Kind switch
    {
        LiveAttributeKind.Boolean => new AttributeValue { BoolValue = value.Boolean },
        LiveAttributeKind.Int64 => new AttributeValue { Int64Value = value.Int64Value },
        LiveAttributeKind.Double => new AttributeValue { DoubleValue = value.DoubleValue },
        LiveAttributeKind.String => new AttributeValue { StringValue = value.StringValue },
        LiveAttributeKind.Token => new AttributeValue { TokenValue = value.TokenValue },
        LiveAttributeKind.Vec3f => new AttributeValue { Vec3FValue = ToWire(value.Vec3f) },
        LiveAttributeKind.Matrix4d => new AttributeValue { Matrix4DValue = ToWire(value.Matrix4d) },
        LiveAttributeKind.Int32Array => CreateInt32Array(value.Int32Array),
        LiveAttributeKind.FloatArray => CreateFloatArray(value.FloatArray),
        LiveAttributeKind.DoubleArray => CreateDoubleArray(value.DoubleArray),
        LiveAttributeKind.Vec2fArray => CreateVec2fArray(value.Vec2fArray),
        LiveAttributeKind.Vec3fArray => new AttributeValue
        {
            Vec3FArray = ToWireVec3fArray(value.Vec3fArray)
        },
        LiveAttributeKind.Color3fArray => new AttributeValue
        {
            Color3FArray = ToWireVec3fArray(value.Color3fArray)
        },
        LiveAttributeKind.BooleanArray => CreateBoolArray(value.BooleanArray),
        LiveAttributeKind.TokenArray => new AttributeValue
        {
            TokenArray = ToWireStringArray(value.TokenArray)
        },
        LiveAttributeKind.StringArray => new AttributeValue
        {
            StringArray = ToWireStringArray(value.StringArray)
        },
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownValueKind,
                $"The attribute value kind '{value.Kind}' has no wire case."))
    };

    internal static MetadataValue ToWire(LiveMetadataValue value) => value.Kind switch
    {
        LiveMetadataKind.Boolean => new MetadataValue { BoolValue = value.Boolean },
        LiveMetadataKind.Int64 => new MetadataValue { Int64Value = value.Int64Value },
        LiveMetadataKind.Double => new MetadataValue { DoubleValue = value.DoubleValue },
        LiveMetadataKind.String => new MetadataValue { StringValue = value.StringValue },
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownValueKind,
                $"The metadata value kind '{value.Kind}' has no wire case."))
    };

    private static bool TryConvert(
        StageUpdate wire,
        out LiveStageUpdate? update,
        out BridgeWireError error)
    {
        error = BridgeWireError.None;
        switch (wire.UpdateCase)
        {
            case StageUpdate.UpdateOneofCase.DefinePrim:
                update = new DefinePrimUpdate(
                    wire.DefinePrim.PrimPath,
                    wire.DefinePrim.HasTypeName ? wire.DefinePrim.TypeName : null);
                return true;
            case StageUpdate.UpdateOneofCase.RemovePrim:
                update = new RemovePrimUpdate(wire.RemovePrim.PrimPath);
                return true;
            case StageUpdate.UpdateOneofCase.SetAttribute:
                return TryConvertSetAttribute(wire.SetAttribute, out update, out error);
            case StageUpdate.UpdateOneofCase.Clear:
                return TryConvertClear(wire.Clear, out update, out error);
            case StageUpdate.UpdateOneofCase.SetRelationshipTargets:
                update = new SetRelationshipTargetsUpdate(
                    wire.SetRelationshipTargets.PrimPath,
                    wire.SetRelationshipTargets.RelationshipName,
                    [.. wire.SetRelationshipTargets.Targets]);
                return true;
            case StageUpdate.UpdateOneofCase.SetReference:
                update = new SetReferenceUpdate(
                    wire.SetReference.PrimPath,
                    wire.SetReference.HasAssetPath ? wire.SetReference.AssetPath : null,
                    wire.SetReference.HasTargetPrimPath ? wire.SetReference.TargetPrimPath : null);
                return true;
            case StageUpdate.UpdateOneofCase.SetPayload:
                update = new SetPayloadUpdate(
                    wire.SetPayload.PrimPath,
                    wire.SetPayload.HasAssetPath ? wire.SetPayload.AssetPath : null,
                    wire.SetPayload.HasTargetPrimPath ? wire.SetPayload.TargetPrimPath : null);
                return true;
            case StageUpdate.UpdateOneofCase.SetActive:
                update = new SetActiveUpdate(wire.SetActive.PrimPath, wire.SetActive.Active);
                return true;
            case StageUpdate.UpdateOneofCase.SetInstanceable:
                update = new SetInstanceableUpdate(
                    wire.SetInstanceable.PrimPath,
                    wire.SetInstanceable.Instanceable);
                return true;
            case StageUpdate.UpdateOneofCase.SetVariantSelection:
                update = new SetVariantSelectionUpdate(
                    wire.SetVariantSelection.PrimPath,
                    wire.SetVariantSelection.VariantSetName,
                    [.. wire.SetVariantSelection.KnownVariants],
                    wire.SetVariantSelection.HasSelection
                        ? wire.SetVariantSelection.Selection
                        : null);
                return true;
            case StageUpdate.UpdateOneofCase.SetMetadata:
                return TryConvertSetMetadata(wire.SetMetadata, out update, out error);
            case StageUpdate.UpdateOneofCase.ApiSchema:
                return TryConvertApiSchema(wire.ApiSchema, out update, out error);
            case StageUpdate.UpdateOneofCase.SetPointInstancerOrientations:
                return TryConvertOrientations(
                    wire.SetPointInstancerOrientations,
                    out update,
                    out error);
            case StageUpdate.UpdateOneofCase.None:
            default:
                update = null;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownUpdateKind,
                    $"The stage update case '{wire.UpdateCase}' is not supported by this version.");
                return false;
        }
    }

    private static bool TryConvertSetAttribute(
        SetAttribute wire,
        out LiveStageUpdate? update,
        out BridgeWireError error)
    {
        update = null;
        if (wire.Value is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "A set-attribute update carried no value.");
            return false;
        }
        if (!TryFromWire(wire.Value, out LiveAttributeValue value, out error))
        {
            return false;
        }

        update = new SetAttributeUpdate(
            wire.PrimPath,
            wire.AttributeName,
            value,
            wire.HasTimeCode ? wire.TimeCode : null);
        error = BridgeWireError.None;
        return true;
    }

    private static bool TryConvertClear(
        Wire.Clear wire,
        out LiveStageUpdate? update,
        out BridgeWireError error)
    {
        update = null;
        LiveClearTargetKind kind;
        switch (wire.Target)
        {
            case ClearTarget.AttributeValue:
                kind = LiveClearTargetKind.AttributeValue;
                break;
            case ClearTarget.RelationshipTargets:
                kind = LiveClearTargetKind.RelationshipTargets;
                break;
            case ClearTarget.References:
                kind = LiveClearTargetKind.References;
                break;
            case ClearTarget.Payloads:
                kind = LiveClearTargetKind.Payloads;
                break;
            case ClearTarget.Metadata:
                kind = LiveClearTargetKind.Metadata;
                break;
            case ClearTarget.Unspecified:
            default:
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The clear target '{(int)wire.Target}' is not supported by this version.");
                return false;
        }

        update = new ClearUpdate(wire.PrimPath, kind, wire.HasName ? wire.Name : null);
        error = BridgeWireError.None;
        return true;
    }

    private static bool TryConvertSetMetadata(
        SetMetadata wire,
        out LiveStageUpdate? update,
        out BridgeWireError error)
    {
        update = null;
        if (wire.Value is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "A set-metadata update carried no value.");
            return false;
        }
        if (!TryFromWire(wire.Value, out LiveMetadataValue value, out error))
        {
            return false;
        }

        update = new SetMetadataUpdate(wire.PrimPath, wire.Key, value);
        error = BridgeWireError.None;
        return true;
    }

    private static bool TryConvertApiSchema(
        Wire.ApiSchema wire,
        out LiveStageUpdate? update,
        out BridgeWireError error)
    {
        update = null;
        LiveApiSchemaOperation operation;
        switch (wire.Operation)
        {
            case ApiSchemaOperation.Apply:
                operation = LiveApiSchemaOperation.Apply;
                break;
            case ApiSchemaOperation.Remove:
                operation = LiveApiSchemaOperation.Remove;
                break;
            case ApiSchemaOperation.Unspecified:
            default:
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The API-schema operation '{(int)wire.Operation}' is not supported.");
                return false;
        }

        update = new ApiSchemaUpdate(wire.PrimPath, wire.SchemaToken, operation);
        error = BridgeWireError.None;
        return true;
    }

    private static bool TryConvertOrientations(
        SetPointInstancerOrientations wire,
        out LiveStageUpdate? update,
        out BridgeWireError error)
    {
        update = null;
        if (wire.Orientations is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "A point-instancer orientation update carried no array.");
            return false;
        }
        if (wire.Orientations.Values.Count > LiveAuthoringValidation.MaxCollectionElementCount)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.LimitExceeded,
                "A point-instancer orientation array exceeds " +
                $"{LiveAuthoringValidation.MaxCollectionElementCount} elements.");
            return false;
        }

        var orientations = new UsdQuatf[wire.Orientations.Values.Count];
        for (int index = 0; index < orientations.Length; index++)
        {
            Quatf value = wire.Orientations.Values[index];
            orientations[index] = new UsdQuatf(value.Real, value.X, value.Y, value.Z);
        }

        update = new SetPointInstancerOrientationsUpdate(wire.PrimPath, orientations);
        error = BridgeWireError.None;
        return true;
    }

    internal static bool TryFromWire(
        AttributeValue wire,
        out LiveAttributeValue value,
        out BridgeWireError error)
    {
        value = default;
        error = BridgeWireError.None;
        switch (wire.ValueCase)
        {
            case AttributeValue.ValueOneofCase.BoolValue:
                value = LiveAttributeValue.FromBoolean(wire.BoolValue);
                return true;
            case AttributeValue.ValueOneofCase.Int64Value:
                value = LiveAttributeValue.FromInt64(wire.Int64Value);
                return true;
            case AttributeValue.ValueOneofCase.DoubleValue:
                value = LiveAttributeValue.FromDouble(wire.DoubleValue);
                return true;
            case AttributeValue.ValueOneofCase.StringValue:
                value = LiveAttributeValue.FromString(wire.StringValue);
                return true;
            case AttributeValue.ValueOneofCase.TokenValue:
                value = LiveAttributeValue.FromToken(wire.TokenValue);
                return true;
            case AttributeValue.ValueOneofCase.Vec3FValue:
                value = LiveAttributeValue.FromVec3f(FromWire(wire.Vec3FValue));
                return true;
            case AttributeValue.ValueOneofCase.Matrix4DValue:
                return TryConvertMatrix(wire.Matrix4DValue, out value, out error);
            case AttributeValue.ValueOneofCase.Int32Array:
                value = LiveAttributeValue.FromInt32Array([.. wire.Int32Array.Values]);
                return true;
            case AttributeValue.ValueOneofCase.FloatArray:
                value = LiveAttributeValue.FromFloatArray([.. wire.FloatArray.Values]);
                return true;
            case AttributeValue.ValueOneofCase.DoubleArray:
                value = LiveAttributeValue.FromDoubleArray([.. wire.DoubleArray.Values]);
                return true;
            case AttributeValue.ValueOneofCase.Vec2FArray:
                value = LiveAttributeValue.FromVec2fArray(FromWire(wire.Vec2FArray));
                return true;
            case AttributeValue.ValueOneofCase.Vec3FArray:
                value = LiveAttributeValue.FromVec3fArray(FromWire(wire.Vec3FArray));
                return true;
            case AttributeValue.ValueOneofCase.Color3FArray:
                value = LiveAttributeValue.FromColor3fArray(FromWire(wire.Color3FArray));
                return true;
            case AttributeValue.ValueOneofCase.BoolArray:
                value = LiveAttributeValue.FromBooleanArray([.. wire.BoolArray.Values]);
                return true;
            case AttributeValue.ValueOneofCase.TokenArray:
                value = LiveAttributeValue.FromTokenArray([.. wire.TokenArray.Values]);
                return true;
            case AttributeValue.ValueOneofCase.StringArray:
                value = LiveAttributeValue.FromStringArray([.. wire.StringArray.Values]);
                return true;
            case AttributeValue.ValueOneofCase.None:
            default:
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownValueKind,
                    $"The attribute value case '{wire.ValueCase}' is not supported by this version.");
                return false;
        }
    }

    internal static bool TryFromWire(
        MetadataValue wire,
        out LiveMetadataValue value,
        out BridgeWireError error)
    {
        value = default;
        error = BridgeWireError.None;
        switch (wire.ValueCase)
        {
            case MetadataValue.ValueOneofCase.BoolValue:
                value = LiveMetadataValue.FromBoolean(wire.BoolValue);
                return true;
            case MetadataValue.ValueOneofCase.Int64Value:
                value = LiveMetadataValue.FromInt64(wire.Int64Value);
                return true;
            case MetadataValue.ValueOneofCase.DoubleValue:
                value = LiveMetadataValue.FromDouble(wire.DoubleValue);
                return true;
            case MetadataValue.ValueOneofCase.StringValue:
                value = LiveMetadataValue.FromString(wire.StringValue);
                return true;
            case MetadataValue.ValueOneofCase.None:
            default:
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownValueKind,
                    $"The metadata value case '{wire.ValueCase}' is not supported by this version.");
                return false;
        }
    }

    private static bool TryConvertMatrix(
        Matrix4d wire,
        out LiveAttributeValue value,
        out BridgeWireError error)
    {
        value = default;
        if (wire.Values.Count != Matrix4dComponentCount)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                $"A matrix4d carries exactly {Matrix4dComponentCount} values; " +
                $"the message carried {wire.Values.Count}.");
            return false;
        }

        value = LiveAttributeValue.FromMatrix4d(new UsdMatrix4d(
            wire.Values[0], wire.Values[1], wire.Values[2], wire.Values[3],
            wire.Values[4], wire.Values[5], wire.Values[6], wire.Values[7],
            wire.Values[8], wire.Values[9], wire.Values[10], wire.Values[11],
            wire.Values[12], wire.Values[13], wire.Values[14], wire.Values[15]));
        error = BridgeWireError.None;
        return true;
    }

    private static StageUpdate CreateDefinePrim(DefinePrimUpdate value)
    {
        var wire = new DefinePrim { PrimPath = value.PrimPath };
        if (value.TypeName is not null)
        {
            wire.TypeName = value.TypeName;
        }

        return new StageUpdate { DefinePrim = wire };
    }

    private static StageUpdate CreateSetAttribute(SetAttributeUpdate value)
    {
        var wire = new SetAttribute
        {
            PrimPath = value.PrimPath,
            AttributeName = value.AttributeName,
            Value = ToWire(value.Value)
        };
        if (value.TimeCode is double timeCode)
        {
            wire.TimeCode = timeCode;
        }

        return new StageUpdate { SetAttribute = wire };
    }

    private static StageUpdate CreateClear(ClearUpdate value)
    {
        ClearTarget target = value.TargetKind switch
        {
            LiveClearTargetKind.AttributeValue => ClearTarget.AttributeValue,
            LiveClearTargetKind.RelationshipTargets => ClearTarget.RelationshipTargets,
            LiveClearTargetKind.References => ClearTarget.References,
            LiveClearTargetKind.Payloads => ClearTarget.Payloads,
            LiveClearTargetKind.Metadata => ClearTarget.Metadata,
            _ => throw new BridgeProtocolException(
                BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The clear target kind '{value.TargetKind}' has no wire case."))
        };
        var wire = new Wire.Clear { PrimPath = value.PrimPath, Target = target };
        if (value.Name is not null)
        {
            wire.Name = value.Name;
        }

        return new StageUpdate { Clear = wire };
    }

    private static StageUpdate CreateSetRelationshipTargets(SetRelationshipTargetsUpdate value)
    {
        var wire = new SetRelationshipTargets
        {
            PrimPath = value.PrimPath,
            RelationshipName = value.RelationshipName
        };
        for (int index = 0; index < value.Targets.Count; index++)
        {
            wire.Targets.Add(value.Targets[index]);
        }

        return new StageUpdate { SetRelationshipTargets = wire };
    }

    private static StageUpdate CreateSetReference(SetReferenceUpdate value)
    {
        var wire = new SetReference { PrimPath = value.PrimPath };
        if (value.AssetPath is not null)
        {
            wire.AssetPath = value.AssetPath;
        }
        if (value.TargetPrimPath is not null)
        {
            wire.TargetPrimPath = value.TargetPrimPath;
        }

        return new StageUpdate { SetReference = wire };
    }

    private static StageUpdate CreateSetPayload(SetPayloadUpdate value)
    {
        var wire = new SetPayload { PrimPath = value.PrimPath };
        if (value.AssetPath is not null)
        {
            wire.AssetPath = value.AssetPath;
        }
        if (value.TargetPrimPath is not null)
        {
            wire.TargetPrimPath = value.TargetPrimPath;
        }

        return new StageUpdate { SetPayload = wire };
    }

    private static StageUpdate CreateSetVariantSelection(SetVariantSelectionUpdate value)
    {
        var wire = new SetVariantSelection
        {
            PrimPath = value.PrimPath,
            VariantSetName = value.VariantSetName
        };
        for (int index = 0; index < value.KnownVariants.Count; index++)
        {
            wire.KnownVariants.Add(value.KnownVariants[index]);
        }
        if (value.Selection is not null)
        {
            wire.Selection = value.Selection;
        }

        return new StageUpdate { SetVariantSelection = wire };
    }

    private static StageUpdate CreateSetMetadata(SetMetadataUpdate value) =>
        new()
        {
            SetMetadata = new SetMetadata
            {
                PrimPath = value.PrimPath,
                Key = value.Key,
                Value = ToWire(value.Value)
            }
        };

    private static StageUpdate CreateApiSchema(ApiSchemaUpdate value)
    {
        ApiSchemaOperation operation = value.Operation switch
        {
            LiveApiSchemaOperation.Apply => ApiSchemaOperation.Apply,
            LiveApiSchemaOperation.Remove => ApiSchemaOperation.Remove,
            _ => throw new BridgeProtocolException(
                BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The API-schema operation '{value.Operation}' has no wire case."))
        };

        return new StageUpdate
        {
            ApiSchema = new Wire.ApiSchema
            {
                PrimPath = value.PrimPath,
                SchemaToken = value.SchemaToken,
                Operation = operation
            }
        };
    }

    private static StageUpdate CreateOrientations(SetPointInstancerOrientationsUpdate value)
    {
        var array = new QuatfArray();
        for (int index = 0; index < value.Orientations.Count; index++)
        {
            UsdQuatf orientation = value.Orientations[index];
            array.Values.Add(new Quatf
            {
                Real = orientation.Real,
                X = orientation.X,
                Y = orientation.Y,
                Z = orientation.Z
            });
        }

        return new StageUpdate
        {
            SetPointInstancerOrientations = new SetPointInstancerOrientations
            {
                PrimPath = value.PrimPath,
                Orientations = array
            }
        };
    }

    private static AttributeValue CreateInt32Array(IReadOnlyList<int> values)
    {
        var array = new Int32Array();
        for (int index = 0; index < values.Count; index++)
        {
            array.Values.Add(values[index]);
        }

        return new AttributeValue { Int32Array = array };
    }

    private static AttributeValue CreateFloatArray(IReadOnlyList<float> values)
    {
        var array = new FloatArray();
        for (int index = 0; index < values.Count; index++)
        {
            array.Values.Add(values[index]);
        }

        return new AttributeValue { FloatArray = array };
    }

    private static AttributeValue CreateDoubleArray(IReadOnlyList<double> values)
    {
        var array = new DoubleArray();
        for (int index = 0; index < values.Count; index++)
        {
            array.Values.Add(values[index]);
        }

        return new AttributeValue { DoubleArray = array };
    }

    private static AttributeValue CreateBoolArray(IReadOnlyList<bool> values)
    {
        var array = new BoolArray();
        for (int index = 0; index < values.Count; index++)
        {
            array.Values.Add(values[index]);
        }

        return new AttributeValue { BoolArray = array };
    }

    private static AttributeValue CreateVec2fArray(IReadOnlyList<UsdVec2f> values)
    {
        var array = new Vec2fArray();
        for (int index = 0; index < values.Count; index++)
        {
            array.Values.Add(ToWire(values[index]));
        }

        return new AttributeValue { Vec2FArray = array };
    }

    private static Vec3fArray ToWireVec3fArray(IReadOnlyList<UsdVec3f> values)
    {
        var array = new Vec3fArray();
        for (int index = 0; index < values.Count; index++)
        {
            array.Values.Add(ToWire(values[index]));
        }

        return array;
    }

    private static StringArray ToWireStringArray(IReadOnlyList<string> values)
    {
        var array = new StringArray();
        for (int index = 0; index < values.Count; index++)
        {
            array.Values.Add(values[index]);
        }

        return array;
    }

    private static Vec2f ToWire(UsdVec2f value) => new() { X = value.X, Y = value.Y };

    private static Vec3f ToWire(UsdVec3f value) => new() { X = value.X, Y = value.Y, Z = value.Z };

    private static Matrix4d ToWire(UsdMatrix4d value)
    {
        var wire = new Matrix4d();
        wire.Values.Add(value.M00);
        wire.Values.Add(value.M01);
        wire.Values.Add(value.M02);
        wire.Values.Add(value.M03);
        wire.Values.Add(value.M10);
        wire.Values.Add(value.M11);
        wire.Values.Add(value.M12);
        wire.Values.Add(value.M13);
        wire.Values.Add(value.M20);
        wire.Values.Add(value.M21);
        wire.Values.Add(value.M22);
        wire.Values.Add(value.M23);
        wire.Values.Add(value.M30);
        wire.Values.Add(value.M31);
        wire.Values.Add(value.M32);
        wire.Values.Add(value.M33);
        return wire;
    }

    private static UsdVec3f FromWire(Vec3f value) => new(value.X, value.Y, value.Z);

    private static UsdVec2f[] FromWire(Vec2fArray array)
    {
        var values = new UsdVec2f[array.Values.Count];
        for (int index = 0; index < values.Length; index++)
        {
            Vec2f value = array.Values[index];
            values[index] = new UsdVec2f(value.X, value.Y);
        }

        return values;
    }

    private static UsdVec3f[] FromWire(Vec3fArray array)
    {
        var values = new UsdVec3f[array.Values.Count];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = FromWire(array.Values[index]);
        }

        return values;
    }
}
