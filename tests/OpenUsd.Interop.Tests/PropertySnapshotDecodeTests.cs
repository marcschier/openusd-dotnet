// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Interop.Tests;

public sealed class PropertySnapshotDecodeTests
{
    [Test]
    public async Task OwnedDecodeCopiesValuesTimesAndGraphIdentityBeforeExactlyOneRelease()
    {
        int releases = 0;
        OpenUsdNativePropertySnapshot snapshot = Decode("", _ => releases++);
        await Assert.That(releases).IsEqualTo(1);
        await Assert.That(snapshot.PrimPath).IsEqualTo("/Subject");
        await Assert.That(snapshot.ChangeSerial).IsEqualTo(73UL);
        await Assert.That(snapshot.Entries[0].Elements.Single()).IsEqualTo("42");
        await Assert.That(snapshot.Entries[0].Times.Length).IsEqualTo(2);
        await Assert.That(snapshot.Entries[0].Times[0]).IsEqualTo(1d);
        await Assert.That(snapshot.Entries[0].Times[1]).IsEqualTo(3d);
        await Assert.That(snapshot.Entries[0].Targets.Single()).IsEqualTo("/Other");
        await Assert.That(snapshot.Entries[0].ValueSource!.SpecPath).IsEqualTo("/Subject.answer");
    }

    [Test]
    public async Task NativeFailureReleasesUnexpectedOwnerAndPreservesTheDiagnostic()
    {
        int releases = 0;
        OpenUsdNativeException? error = null;
        try
        {
            _ = Decode("native-failure", _ => releases++);
        }
        catch (OpenUsdNativeException exception)
        {
            error = exception;
        }
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);
        await Assert.That(error.Message).Contains("native refusal");
        await Assert.That(releases).IsEqualTo(1);
    }

    [Test]
    public async Task PublicWireLayoutsMatchTheOwnedCContract()
    {
        await Assert.That(Marshal.SizeOf<OpenUsdNativePropertyLimits>()).IsEqualTo(40);
        await Assert.That(Marshal.SizeOf<OpenUsdNativePropertyPreview>()).IsEqualTo(24);
        await Assert.That(Marshal.SizeOf<OpenUsdNativePropertyEntryRecord>()).IsEqualTo(128);
        await Assert.That(Marshal.SizeOf<OpenUsdNativePropertyAssetRecord>()).IsEqualTo(8);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeRuntime.NativePropertyView>())
            .IsEqualTo(40 + (14 * IntPtr.Size));
    }

    [Test]
    [Arguments("missing-array-flag")]
    [Arguments("header")]
    [Arguments("version")]
    [Arguments("complete")]
    [Arguments("complete-mismatch")]
    [Arguments("time-nan")]
    [Arguments("default-time")]
    [Arguments("time-flag")]
    [Arguments("reserved")]
    [Arguments("limits")]
    [Arguments("work")]
    [Arguments("entry-null")]
    [Arguments("entry-alignment")]
    [Arguments("entry-size")]
    [Arguments("address-overflow")]
    [Arguments("text-budget")]
    [Arguments("offset-null")]
    [Arguments("offsets-size")]
    [Arguments("offset")]
    [Arguments("utf8")]
    [Arguments("unterminated")]
    [Arguments("prim-path")]
    [Arguments("name")]
    [Arguments("kind")]
    [Arguments("flags")]
    [Arguments("authored-unknown")]
    [Arguments("source-pair")]
    [Arguments("source-path")]
    [Arguments("row-range")]
    [Arguments("value-range")]
    [Arguments("value-total")]
    [Arguments("unknown-complete-total")]
    [Arguments("preview-limit")]
    [Arguments("preview-text")]
    [Arguments("status")]
    [Arguments("reason")]
    [Arguments("times-null")]
    [Arguments("times-duplicate")]
    [Arguments("times-infinite")]
    [Arguments("targets-relative")]
    [Arguments("asset-range")]
    [Arguments("resolve-state")]
    public async Task MalformedOwnedDataIsRejectedAndItsOwnerIsReleased(string corruption)
    {
        int releases = 0;
        await Assert.That(() => Decode(corruption, _ => releases++)).Throws<OpenUsdNativeException>();
        await Assert.That(releases).IsEqualTo(1);
    }

    private static unsafe OpenUsdNativePropertySnapshot Decode(string corruption, Action<nint> release)
    {
        string[] fields = ["/Subject", "answer", "int", "anon:test", "/Subject.answer", "", "", "42", "/Other"];
        switch (corruption)
        {
            case "missing-array-flag":
                fields[2] = "int[]";
                break;
            case "prim-path":
                fields[0] = "/";
                break;
            case "name":
                fields[1] = "bad/name";
                break;
            case "source-pair":
                fields[3] = "";
                break;
            case "source-path":
                fields[4] = "/NotAProperty";
                break;
            case "targets-relative":
                fields[8] = "relative";
                break;
        }
        (byte[] data, nuint[] packedOffsets) = NativeStringListPacking.Pack(fields);
        uint[] offsets = packedOffsets.Select(offset => (uint)offset).ToArray();
        double[] times = [1, 3];
        OpenUsdNativePropertyEntryRecord record = new()
        {
            Name = 1,
            Type = 2,
            Flags = 27,
            ResolveSource = 2,
            ValueState = 1,
            SourceLayer = 3,
            SourcePath = 4,
            TargetSourceLayer = 5,
            TargetSourcePath = 6,
            Value = new(1, 7, 1, 0, 0),
            TimeSamples = new(2, 0, 2, 0, 0),
            Targets = new(1, 8, 1, 0, 0)
        };
        var limits = new OpenUsdNativePropertyLimits(40, 1, 4096, 1024 * 1024, 16, 16, 16, 262_144, 4096, 0);
        fixed (byte* dataPointer = data)
        fixed (uint* offsetPointer = offsets)
        fixed (double* timePointer = times)
        {
            var view = new OpenUsdNativeRuntime.NativePropertyView
            {
                StructSize = (uint)sizeof(OpenUsdNativeRuntime.NativePropertyView),
                Version = 1,
                ChangeSerial = 73,
                IsComplete = 1,
                MetadataWork = 19,
                Entries = &record,
                EntriesSize = (nuint)sizeof(OpenUsdNativePropertyEntryRecord),
                EntryCount = 1,
                Times = timePointer,
                TimesSize = 16,
                TimeCount = 2,
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetPointer,
                OffsetsSize = (nuint)(offsets.Length * sizeof(uint)),
                StringCount = (nuint)offsets.Length
            };
            switch (corruption)
            {
                case "header":
                    view.StructSize--;
                    break;
                case "version":
                    view.Version++;
                    break;
                case "complete":
                    view.IsComplete = 2;
                    break;
                case "complete-mismatch":
                    view.IsComplete = 0;
                    break;
                case "time-nan":
                    view.TimeCode = double.NaN;
                    break;
                case "default-time":
                    view.TimeCode = 1;
                    break;
                case "time-flag":
                    view.TimeSampled = 2;
                    break;
                case "reserved":
                    view.Reserved = 1;
                    break;
                case "limits":
                    limits = limits with { PreviewElements = 17 };
                    break;
                case "work":
                    view.MetadataWork = limits.MaximumMetadataWork + 1;
                    break;
                case "entry-null":
                    view.Entries = null;
                    break;
                case "entry-alignment":
                    view.Entries = (OpenUsdNativePropertyEntryRecord*)((byte*)&record + 1);
                    break;
                case "entry-size":
                    view.EntriesSize--;
                    break;
                case "address-overflow":
                    view.Entries = (OpenUsdNativePropertyEntryRecord*)(nuint.MaxValue - 7);
                    break;
                case "text-budget":
                    limits = limits with { MaximumTextBytes = 1 };
                    break;
                case "offset-null":
                    view.Offsets = null;
                    break;
                case "offsets-size":
                    view.OffsetsSize--;
                    break;
                case "offset":
                    offsets[1]--;
                    break;
                case "utf8":
                    data[1] = 0xff;
                    break;
                case "unterminated":
                    data[^1] = 1;
                    break;
                case "kind":
                    record.Kind = 2;
                    break;
                case "flags":
                    record.Flags |= 32;
                    break;
                case "authored-unknown":
                    record.Flags &= ~8u;
                    break;
                case "row-range":
                    record.SourcePath++;
                    break;
                case "value-range":
                    record.Value = record.Value with { Offset = 8 };
                    break;
                case "value-total":
                    record.Value = record.Value with { TotalCount = 0 };
                    break;
                case "unknown-complete-total":
                    record.Value = record.Value with { TotalCount = ulong.MaxValue };
                    break;
                case "preview-limit":
                    limits = limits with { PreviewElements = 0 };
                    break;
                case "preview-text":
                    limits = limits with { MaximumPreviewTextBytes = 1 };
                    break;
                case "status":
                    record.Value = record.Value with { Status = 4 };
                    break;
                case "reason":
                    record.Value = record.Value with { Reason = 10 };
                    break;
                case "times-null":
                    view.Times = null;
                    break;
                case "times-duplicate":
                    times[1] = 1;
                    break;
                case "times-infinite":
                    times[1] = double.PositiveInfinity;
                    break;
                case "asset-range":
                    record.AssetCount = 1;
                    break;
                case "resolve-state":
                    record.ResolveSource = 6;
                    break;
            }
            return OpenUsdNativeRuntime.CompletePropertySnapshotQuery(
                corruption == "native-failure" ? OpenUsdNativeStatus.NativeError : OpenUsdNativeStatus.Ok,
                Encoding.UTF8.GetBytes("native refusal\0"), 0, 123, view, limits, release);
        }
    }
}
