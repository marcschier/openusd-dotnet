// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Editing;

internal static class UsdLayerEditCodec
{
    internal static byte[] EncodeAddresses(ReadOnlySpan<UsdLayerEditAddress> addresses)
    {
        ValidateAddressCount(addresses.Length);
        var writer = new UsdEditWriter();
        writer.Header(1);
        writer.U32((uint)addresses.Length);
        var unique = new HashSet<UsdLayerEditAddress>();
        foreach (UsdLayerEditAddress address in addresses)
        {
            if (!unique.Add(address))
            {
                throw new ArgumentException("Duplicate canonical editing address.", nameof(addresses));
            }
            writer.Address(address);
        }
        return writer.Written.ToArray();
    }

    internal static byte[] EncodeEdits(UsdLayerAuthoredSnapshot expected, ReadOnlySpan<UsdLayerEdit> edits)
    {
        if (edits.Length != expected.Addresses.Count)
        {
            throw new ArgumentException(
                "Edits must match the expected address count and order exactly.", nameof(edits));
        }
        var writer = new UsdEditWriter();
        writer.Header(3);
        writer.U32((uint)edits.Length);
        for (int index = 0; index < edits.Length; index++)
        {
            UsdLayerEdit edit = edits[index];
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.Address != expected.Addresses[index])
            {
                throw new ArgumentException("Edits must match the expected address order exactly.", nameof(edits));
            }
            writer.Address(edit.Address);
            writer.U32((uint)edit.Operation);
            writer.Text(edit.CreationTypeName);
            writer.U32((uint)edit.CreationVariability);
            writer.U32(edit.CreationCustom ? 1u : 0u);
            writer.Bytes(edit.Value.Payload);
        }
        return writer.Written.ToArray();
    }

    internal static UsdLayerEditingState DecodeState(ReadOnlySpan<byte> bytes)
    {
        var reader = new UsdEditReader(bytes);
        reader.Header(5);
        (UsdLayerIdentity identity, ulong revision) = reader.Identity();
        uint role = reader.U32();
        uint flags = reader.U32();
        if (role > 4 || (flags & ~63u) != 0)
        {
            throw UsdEditingValidation.InvalidPacket("invalid layer role or flags");
        }
        string identifier = reader.Text();
        if (identifier.Length == 0)
        {
            throw UsdEditingValidation.InvalidPacket("empty layer identifier");
        }
        var result = new UsdLayerEditingState(
            identity, revision, (UsdLayerRole)role, flags, identifier, reader.Text(), reader.Text(), reader.Text());
        reader.End();
        return result;
    }

    internal static UsdLayerAuthoredSnapshot DecodeSnapshot(ReadOnlySpan<byte> bytes)
    {
        var reader = new UsdEditReader(bytes);
        reader.Header(2);
        (UsdLayerIdentity identity, ulong revision) = reader.Identity();
        int count = reader.Count(UsdEditingValidation.MaximumAddresses, 36);
        if (count == 0)
        {
            throw UsdEditingValidation.InvalidPacket("an authored snapshot needs at least one address");
        }
        var opinions = new UsdLayerAuthoredOpinion[count];
        var unique = new HashSet<UsdLayerEditAddress>();
        var declarations = new Dictionary<string, UsdLayerAuthoredOpinion>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            UsdLayerEditAddress address = reader.Address();
            uint propertyKind = reader.U32();
            if (!unique.Add(address) || propertyKind > 2)
            {
                throw UsdEditingValidation.InvalidPacket("duplicate address or invalid property kind");
            }
            UsdLayerEditValue typeName = reader.Value();
            UsdLayerEditValue variability = reader.Value();
            UsdLayerEditValue custom = reader.Value();
            UsdLayerEditValue value = reader.Value();
            ValidateDeclarationValue(typeName, UsdLayerEditValueKind.Token);
            ValidateDeclarationValue(variability, UsdLayerEditValueKind.Variability);
            ValidateDeclarationValue(custom, UsdLayerEditValueKind.Boolean);
            if (propertyKind == 0)
            {
                if (typeName.Kind != UsdLayerEditValueKind.Absent ||
                    variability.Kind != UsdLayerEditValueKind.Absent ||
                    custom.Kind != UsdLayerEditValueKind.Absent ||
                    value.Kind != UsdLayerEditValueKind.Absent)
                {
                    throw UsdEditingValidation.InvalidPacket("absent spec carries authored fields");
                }
            }
            else if ((propertyKind == 2) != (address.Field == UsdLayerEditField.RelationshipTargets) ||
                (address.Field >= UsdLayerEditField.AttributeConnections &&
                    value.Kind is not (UsdLayerEditValueKind.Absent or UsdLayerEditValueKind.PathList)))
            {
                throw UsdEditingValidation.InvalidPacket("property kind or value does not match the addressed field");
            }
            var opinion = new UsdLayerAuthoredOpinion(
                address, (UsdLayerPropertyKind)propertyKind,
                typeName.Kind == UsdLayerEditValueKind.Absent ? null : typeName.AsToken(),
                variability.Kind == UsdLayerEditValueKind.Absent
                    ? null
                    : (UsdLayerEditVariability)new UsdEditReader(variability.Payload[4..]).U32(),
                custom.Kind == UsdLayerEditValueKind.Absent ? null : custom.AsBoolean(), value);
            if (declarations.TryGetValue(address.Path, out UsdLayerAuthoredOpinion? previous))
            {
                if (previous.PropertyKind != opinion.PropertyKind || previous.TypeName != opinion.TypeName ||
                    previous.Variability != opinion.Variability || previous.Custom != opinion.Custom)
                {
                    throw UsdEditingValidation.InvalidPacket("inconsistent declarations for one property");
                }
            }
            else
            {
                declarations.Add(address.Path, opinion);
            }
            opinions[index] = opinion;
        }
        reader.End();
        return new UsdLayerAuthoredSnapshot(identity, revision, opinions, bytes);
    }

    internal static UsdLayerCheckpoint DecodeCheckpoint(ReadOnlySpan<byte> bytes)
    {
        var reader = new UsdEditReader(bytes);
        reader.Header(4);
        (UsdLayerIdentity identity, ulong revision) = reader.Identity();
        string identifier = reader.Text();
        string anchor = reader.Text();
        if (identifier.Length == 0)
        {
            throw UsdEditingValidation.InvalidPacket("empty checkpoint identifier");
        }
        int count = reader.Count(UsdEditingValidation.MaximumItems, 12);
        var specs = new Dictionary<string, CheckpointSpec>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            string path = reader.Text();
            uint type = reader.U32();
            ValidateSpecPath(path, type);
            int fieldCount = reader.Count(128, 8);
            var fields = new HashSet<string>(StringComparer.Ordinal);
            string? typeName = null;
            HashSet<string> primChildren = [];
            HashSet<string> propertyChildren = [];
            for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                string name = reader.Text();
                ReadOnlySpan<byte> payload = reader.ValuePayload();
                var kind = (UsdLayerEditValueKind)BinaryPrimitives.ReadUInt32LittleEndian(payload);
                if (name.Length == 0 || kind == UsdLayerEditValueKind.Absent || !fields.Add(name))
                {
                    throw UsdEditingValidation.InvalidPacket("empty or duplicate checkpoint field");
                }
                ValidateKnownField(name, kind, type);
                switch (name)
                {
                    case "typeName":
                        typeName = new UsdEditReader(payload[4..]).Text();
                        break;
                    case "primChildren":
                        primChildren = new HashSet<string>(
                            new UsdEditReader(payload[4..]).Strings(unique: true), StringComparer.Ordinal);
                        break;
                    case "properties":
                        propertyChildren = new HashSet<string>(
                            new UsdEditReader(payload[4..]).Strings(unique: true), StringComparer.Ordinal);
                        break;
                }
            }
            if (!specs.TryAdd(path, new CheckpointSpec(type, typeName, primChildren, propertyChildren)))
            {
                throw UsdEditingValidation.InvalidPacket("duplicate checkpoint spec");
            }
        }
        reader.End();
        ValidateTopology(specs);
        return new UsdLayerCheckpoint(identity, revision, identifier, anchor, bytes);
    }

    internal static void MatchAddresses(
        IReadOnlyList<UsdLayerEditAddress> expected, IReadOnlyList<UsdLayerEditAddress> actual, bool nativeResult)
    {
        bool matches = expected.Count == actual.Count;
        for (int index = 0; matches && index < expected.Count; index++)
        {
            matches = expected[index] == actual[index];
        }
        if (!matches)
        {
            if (nativeResult)
            {
                throw UsdEditingValidation.InvalidPacket("result address count/order differs from request");
            }
            throw new ArgumentException("Restore snapshots must have the same ordered addresses.", nameof(actual));
        }
    }

    private static void ValidateAddressCount(int count)
    {
        if (count is < 1 or > UsdEditingValidation.MaximumAddresses)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Supply 1..256 unique editing addresses.");
        }
    }

    private static void ValidateDeclarationValue(UsdLayerEditValue value, UsdLayerEditValueKind kind)
    {
        if (value.Kind != UsdLayerEditValueKind.Absent && value.Kind != kind)
        {
            throw UsdEditingValidation.InvalidPacket("invalid declaration field type");
        }
    }

    private static void ValidateSpecPath(string path, uint type)
    {
        try
        {
            switch (type)
            {
                case 7 when path == "/":
                    break;
                case 6:
                    UsdEditingValidation.PrimPath(path, nameof(path));
                    break;
                case 1:
                case 8:
                    UsdEditingValidation.PropertyPath(path, nameof(path));
                    break;
                default:
                    throw UsdEditingValidation.InvalidPacket("unsupported checkpoint spec kind or path");
            }
        }
        catch (ArgumentException)
        {
            throw UsdEditingValidation.InvalidPacket("invalid checkpoint spec path");
        }
    }

    private static void ValidateKnownField(string name, UsdLayerEditValueKind kind, uint specType)
    {
        bool valid = name switch
        {
            "primChildren" => kind == UsdLayerEditValueKind.TokenVector && specType is 6 or 7,
            "properties" => kind == UsdLayerEditValueKind.TokenVector && specType == 6,
            "typeName" => kind == UsdLayerEditValueKind.Token && specType is 1 or 6,
            "variability" => kind == UsdLayerEditValueKind.Variability && specType is 1 or 8,
            "custom" => kind == UsdLayerEditValueKind.Boolean && specType is 1 or 8,
            "specifier" => kind == UsdLayerEditValueKind.Specifier && specType == 6,
            "timeSamples" => kind == UsdLayerEditValueKind.TimeSamples && specType == 1,
            "connectionPaths" => kind == UsdLayerEditValueKind.PathList && specType == 1,
            "targetPaths" => kind == UsdLayerEditValueKind.PathList && specType == 8,
            "subLayers" => kind == UsdLayerEditValueKind.StringVector && specType == 7,
            "customData" => kind == UsdLayerEditValueKind.Dictionary,
            "customLayerData" => kind == UsdLayerEditValueKind.Dictionary && specType == 7,
            _ => true
        };
        if (!valid)
        {
            throw UsdEditingValidation.InvalidPacket("invalid native field type or spec kind");
        }
    }

    private static void ValidateTopology(Dictionary<string, CheckpointSpec> specs)
    {
        if (!specs.TryGetValue("/", out CheckpointSpec? root) || root.Type != 7)
        {
            throw UsdEditingValidation.InvalidPacket("checkpoint pseudo-root missing");
        }
        foreach ((string path, CheckpointSpec spec) in specs)
        {
            if (path != "/")
            {
                bool property = spec.Type is 1 or 8;
                int separator = property ? path.IndexOf('.', StringComparison.Ordinal) : path.LastIndexOf('/');
                string parent = separator == 0 ? "/" : path[..separator];
                string name = path[(separator + 1)..];
                if (!specs.TryGetValue(parent, out CheckpointSpec? parentSpec) || parentSpec.Type is not (6 or 7) ||
                    !ReadChildren(parentSpec, property).Contains(name))
                {
                    throw UsdEditingValidation.InvalidPacket("checkpoint parent or child inventory missing");
                }
            }
            ValidateChildren(specs, path, spec, property: false);
            ValidateChildren(specs, path, spec, property: true);
            if (spec.Type == 1 && string.IsNullOrEmpty(spec.TypeName))
            {
                throw UsdEditingValidation.InvalidPacket("checkpoint attribute declaration missing");
            }
        }
    }

    private static void ValidateChildren(
        Dictionary<string, CheckpointSpec> specs, string path, CheckpointSpec spec, bool property)
    {
        foreach (string child in ReadChildren(spec, property))
        {
            if (child.Contains('/', StringComparison.Ordinal))
            {
                throw UsdEditingValidation.InvalidPacket("invalid checkpoint child name");
            }
            string childPath = property ? path + "." + child : (path == "/" ? "/" : path + "/") + child;
            uint childType = property ? 1u : 6u;
            ValidateSpecPath(childPath, childType);
            if (!specs.TryGetValue(childPath, out CheckpointSpec? childSpec) ||
                (property ? childSpec.Type is not (1 or 8) : childSpec.Type != 6))
            {
                throw UsdEditingValidation.InvalidPacket("checkpoint child spec missing or invalid");
            }
        }
    }

    private static HashSet<string> ReadChildren(CheckpointSpec spec, bool property) =>
        property ? spec.PropertyChildren : spec.PrimChildren;

    private sealed record CheckpointSpec(
        uint Type, string? TypeName, HashSet<string> PrimChildren, HashSet<string> PropertyChildren);
}
