// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed record ViewerCameraBookmarkCapture(
    ViewerAuthoredEditCapture Authored, string SourceStamp, IReadOnlyList<ViewerCameraBookmark> Items);

internal sealed class ViewerCameraBookmarkCatalog(ViewerAuthoredEditController editor)
{
    internal const string PrimPath = "/__OpenUsdViewerReview";
    internal const string PropertyName = "openusdViewer:cameraBookmarks";
    internal static UsdLayerEditAddress Address { get; } = new(
        $"{PrimPath}.{PropertyName}", UsdLayerEditField.Default);
    private static readonly UsdPropertyInspectionLimits InspectionLimits = new(
        maximumPropertyCount: 1, maximumTextBytes: 65_536, previewElements: 0,
        timeSamplePreview: 0, targetPreview: 0, maximumMetadataWork: 262_144);

    internal async Task<ViewerCameraBookmarkCapture> ReadAsync(CancellationToken cancellationToken = default)
    {
        ViewerAuthoredEditCapture capture = await editor.CaptureAsync([Address], cancellationToken)
            .ConfigureAwait(false);
        string stamp = CreateSourceStamp(editor.CameraBookmarkSourceBinding);
        return new ViewerCameraBookmarkCapture(capture, stamp, Decode(capture.Snapshot, stamp));
    }

    internal Task<ViewerAuthoredEditResult> ApplyAsync(
        ViewerCameraBookmarkCapture expected, IReadOnlyList<ViewerCameraBookmark> items, string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        string[] records = ViewerCameraBookmarkCodec.EncodeCatalog(items);
        _ = ViewerCameraBookmarkCodec.DecodeCatalog(records, expected.SourceStamp);
        return editor.ApplyAsync(expected.Authored,
            [UsdLayerEdit.Set(Address, UsdLayerEditValue.FromStringArray(records), "string[]",
                UsdLayerEditVariability.Uniform, creationCustom: true)],
            description, Guid.NewGuid(), cancellationToken);
    }

    internal static bool UsesReservedNamespace(IReadOnlyList<UsdLayerEditAddress> addresses) =>
        addresses.Any(static address => address.Path.StartsWith(PrimPath + ".", StringComparison.Ordinal) ||
            address.Path.StartsWith(PrimPath + "/", StringComparison.Ordinal));

    internal static void RequireAddress(IReadOnlyList<UsdLayerEditAddress> addresses)
    {
        if (addresses.Count != 1 || addresses[0] != Address)
        {
            throw new NotSupportedException("The saved-view namespace permits only its exact catalog transaction.");
        }
    }

    internal static void ValidateAccess(
        UsdStage stage, UsdLayer review, UsdLayerAuthoredSnapshot snapshot, UsdReviewSourceBinding binding)
    {
        RequireAddress(snapshot.Addresses);
        if (!binding.HasSamePayload(stage.CaptureReviewSourceBinding()))
        {
            throw new InvalidOperationException("The verified source changed; saved views were kept.");
        }
        UsdLayerEditingState state = review.GetEditingState();
        if (state.Role != UsdLayerRole.UserReview || !state.CanAttemptAuthoredEdits)
        {
            throw new NotSupportedException("Saved views require the admitted editable review layer.");
        }
        _ = Decode(snapshot, CreateSourceStamp(binding));
        string[] layers = stage.GetLayerStackIdentifiers();
        if (layers.Length > 256)
        {
            throw new NotSupportedException("Saved-view ownership supports at most 256 local layers.");
        }
        foreach (string identifier in layers)
        {
            if (identifier == review.Identifier)
            {
                continue;
            }
            using UsdLayer other = stage.GetLocalLayer(identifier);
            if (other.CaptureAuthored([Address]).Opinions[0].PropertyKind != UsdLayerPropertyKind.Absent)
            {
                throw new InvalidDataException("The reserved saved-view property collides with another layer.");
            }
        }
        if (!stage.HasPrim(PrimPath))
        {
            return;
        }
        UsdPrim prim = stage.GetPrim(PrimPath);
        if (snapshot.Opinions[0].PropertyKind == UsdLayerPropertyKind.Absent || prim.TypeName.Length != 0 ||
            prim.IsDefined() || prim.IsAbstract() || prim.IsInPrototype() || !prim.IsActive() ||
            prim.GetChildren().Count != 0)
        {
            throw new InvalidDataException("The reserved saved-view namespace is occupied by other scene data.");
        }
        UsdPrimPropertySnapshot properties = stage.GetPrimPropertySnapshot(PrimPath, null, InspectionLimits);
        if (properties.Properties is not [UsdAttributePropertySnapshot property] ||
            property.Name != PropertyName || property.TypeName != "string[]" || !property.IsCustom ||
            property.Variability != UsdPropertyVariability.Uniform ||
            property.TimeSamples.Count != 0 || property.Connections.Count != 0)
        {
            throw new InvalidDataException("The reserved saved-view namespace has incompatible properties.");
        }
    }

    internal static IReadOnlyList<ViewerCameraBookmark> Decode(UsdLayerAuthoredSnapshot snapshot, string sourceStamp)
    {
        RequireAddress(snapshot.Addresses);
        if (snapshot.ByteLength > ViewerCameraBookmarkCodec.MaximumCatalogBytes + 4096)
        {
            throw new InvalidDataException("The authored saved-view packet exceeds the bounded catalog domain.");
        }
        UsdLayerAuthoredOpinion opinion = snapshot.Opinions[0];
        if (opinion.PropertyKind == UsdLayerPropertyKind.Absent)
        {
            return Array.Empty<ViewerCameraBookmark>();
        }
        if (opinion.PropertyKind != UsdLayerPropertyKind.Attribute || opinion.TypeName != "string[]" ||
            opinion.Variability != UsdLayerEditVariability.Uniform || opinion.Custom != true ||
            opinion.Value.Kind != UsdLayerEditValueKind.StringArray)
        {
            throw new InvalidDataException("The review saved-view catalog declaration or value is incompatible.");
        }
        return ViewerCameraBookmarkCodec.DecodeCatalog(opinion.Value.AsStringArray(), sourceStamp);
    }

    internal static void ValidateChange(
        IReadOnlyList<UsdLayerEdit> edits, UsdLayerAuthoredSnapshot before, string sourceStamp)
    {
        RequireAddress(before.Addresses);
        if (edits.Count != 1 || edits[0] is not
            { Operation: UsdLayerEditOperation.Set, Value.Kind: UsdLayerEditValueKind.StringArray } change ||
            change.Address != Address)
        {
            throw new NotSupportedException("Saved-view changes require one exact complete-list set.");
        }
        if (before.Opinions[0].PropertyKind == UsdLayerPropertyKind.Absent &&
            (change.CreationTypeName != "string[]" ||
                change.CreationVariability != UsdLayerEditVariability.Uniform || !change.CreationCustom))
        {
            throw new InvalidDataException("Saved-view creation requires a custom uniform string[] declaration.");
        }
        _ = ViewerCameraBookmarkCodec.DecodeCatalog(change.Value.AsStringArray(), sourceStamp);
    }

    internal static string CreateSourceStamp(UsdReviewSourceBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        append("OpenUsd.Viewer.saved-views.source.v1");
        append(normalize(binding.SourceRootPath));
        append(binding.SourceFingerprint);
        append(normalize(binding.AssetAnchor));
        Span<byte> numbers = stackalloc byte[12];
        foreach (UsdReviewDependency dependency in binding.Dependencies)
        {
            append(normalize(dependency.Path));
            append(dependency.Sha256);
            BinaryPrimitives.WriteUInt64LittleEndian(numbers, dependency.ByteLength);
            BinaryPrimitives.WriteInt32LittleEndian(numbers[8..], (int)dependency.Kind);
            hash.AppendData(numbers);
        }
        return Convert.ToHexString(hash.GetHashAndReset());

        void append(string text)
        {
            byte[] bytes = ViewerCameraBookmarkCodec.Utf8.GetBytes(text);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        static string normalize(string path) => Path.GetFullPath(path).ToUpperInvariant();
    }
}
