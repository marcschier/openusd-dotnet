// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeRenderProductRecord(
    int Width,
    int Height,
    float PixelAspectRatio,
    float ApertureWidth,
    float ApertureHeight,
    float DataWindowMinX,
    float DataWindowMinY,
    float DataWindowMaxX,
    float DataWindowMaxY,
    int DisableMotionBlur,
    int DisableDepthOfField,
    uint Reserved,
    nuint StringOffset,
    nuint NamespacedSettingCount,
    nuint RenderVarIndexOffset,
    nuint RenderVarIndexCount);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeRenderVariableRecord(
    nuint StringOffset,
    nuint NamespacedSettingCount);

internal sealed record OpenUsdNativeRenderProductSpecification(
    string Path,
    string Name,
    string ProductType,
    string CameraPath,
    string AspectRatioConformPolicy,
    OpenUsdNativeRenderProductRecord Values,
    int[] RenderVariableIndices,
    string[] NamespacedSettingNames);

internal sealed record OpenUsdNativeRenderVariableSpecification(
    string Path,
    string DataType,
    string SourceName,
    string SourceType,
    string[] NamespacedSettingNames);

internal sealed record OpenUsdNativeRenderSpecification(
    string SettingsPath,
    OpenUsdNativeRenderProductSpecification[] Products,
    OpenUsdNativeRenderVariableSpecification[] RenderVariables,
    string[] IncludedPurposes,
    string[] MaterialBindingPurposes,
    string RenderingColorSpace,
    string[] NamespacedSettingNames);

public static unsafe partial class OpenUsdNativeRuntime
{
    internal const int RenderSpecificationMaximumProducts = 1024;
    internal const int RenderSpecificationMaximumVariables = 4096;
    internal const int RenderSpecificationMaximumIndices = 65536;
    internal const int RenderSpecificationMaximumSettingNames = 16384;
    internal const int RenderSpecificationMaximumPurposes = 64;
    internal const int RenderSpecificationMaximumStrings = 65536;
    internal const int RenderSpecificationMaximumStringBytes = 8 << 20;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRenderSpecificationView
    {
        internal uint StructSize;
        internal uint Version;
        internal int HasSettings;
        internal uint Reserved;
        internal OpenUsdNativeRenderProductRecord* Products;
        internal nuint ProductsSize;
        internal nuint ProductCount;
        internal OpenUsdNativeRenderVariableRecord* Variables;
        internal nuint VariablesSize;
        internal nuint VariableCount;
        internal uint* RenderVarIndices;
        internal nuint RenderVarIndicesSize;
        internal nuint RenderVarIndexCount;
        internal byte* Data;
        internal nuint DataSize;
        internal nuint* Offsets;
        internal nuint OffsetsSize;
        internal nuint StringCount;
        internal nuint IncludedPurposeCount;
        internal nuint MaterialBindingPurposeCount;
        internal nuint NamespacedSettingCount;
    }

    internal static OpenUsdNativeRenderSpecification? GetRenderSpecification(
        OpenUsdNativeStage stage,
        string? settingsPath)
    {
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(stage);
        var view = new NativeRenderSpecificationView
        {
            StructSize = (uint)sizeof(NativeRenderSpecificationView),
            Version = 1
        };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.RenderGetSpecification(
                lease.Handle, settingsPath, out nint owner, ref view, ref error);
            try
            {
                ThrowIfFailed(status, errorBytes, error);
                if ((owner == 0) != (view.HasSettings == 0))
                {
                    throw InvalidRenderSpecification("the owner does not match the presence flag");
                }
                OpenUsdNativeRenderSpecification? result = DecodeRenderSpecification(view);
                if (settingsPath is not null && (result is null || result.SettingsPath != settingsPath))
                {
                    throw InvalidRenderSpecification("the result does not match the explicit settings path");
                }
                return result;
            }
            finally
            {
                if (owner != 0)
                {
                    NativeMethods.RenderSpecificationRelease(owner);
                }
            }
        }
    }

    internal static OpenUsdNativeRenderSpecification? DecodeRenderSpecification(NativeRenderSpecificationView view)
    {
        if (view.StructSize != sizeof(NativeRenderSpecificationView) || view.Version != 1 ||
            view.Reserved != 0 || view.HasSettings is not (0 or 1) ||
            view.IncludedPurposeCount > RenderSpecificationMaximumPurposes ||
            view.MaterialBindingPurposeCount > RenderSpecificationMaximumPurposes ||
            view.NamespacedSettingCount > RenderSpecificationMaximumSettingNames)
        {
            throw InvalidRenderSpecification("invalid header, version or top-level counts");
        }
        ValidateRenderBuffer(view.Products, view.ProductsSize, view.ProductCount,
            RenderSpecificationMaximumProducts, (nuint)sizeof(nuint));
        ValidateRenderBuffer(view.Variables, view.VariablesSize, view.VariableCount,
            RenderSpecificationMaximumVariables, (nuint)sizeof(nuint));
        ValidateRenderBuffer(view.RenderVarIndices, view.RenderVarIndicesSize, view.RenderVarIndexCount,
            RenderSpecificationMaximumIndices, sizeof(uint));
        ValidateRenderBuffer(view.Offsets, view.OffsetsSize, view.StringCount,
            RenderSpecificationMaximumStrings, (nuint)sizeof(nuint));
        ValidateRenderBuffer(view.Data, view.DataSize, view.DataSize, RenderSpecificationMaximumStringBytes, 1);
        if (view.HasSettings == 0)
        {
            if (view.ProductCount != 0 || view.VariableCount != 0 || view.RenderVarIndexCount != 0 ||
                view.StringCount != 0 || view.DataSize != 0 || view.IncludedPurposeCount != 0 ||
                view.MaterialBindingPurposeCount != 0 || view.NamespacedSettingCount != 0)
            {
                throw InvalidRenderSpecification("an absent default includes snapshot data");
            }
            return null;
        }

        string[] strings = NativePackedStringListDecoder.Decode(
            new ReadOnlySpan<byte>(view.Data, (int)view.DataSize),
            new ReadOnlySpan<nuint>(view.Offsets, (int)view.StringCount),
            "render specification string table");
        int cursor = 0;
        string[] root = TakeRenderStrings(strings, ref cursor, 2);
        ValidateRenderPath(root[0]);
        string[] purposes = TakeRenderStrings(strings, ref cursor, view.IncludedPurposeCount);
        string[] bindingPurposes = TakeRenderStrings(strings, ref cursor, view.MaterialBindingPurposeCount);
        int settingCount = 0;
        string[] names = TakeRenderSettingNames(strings, ref cursor, view.NamespacedSettingCount, ref settingCount);
        var productRecords = new ReadOnlySpan<OpenUsdNativeRenderProductRecord>(view.Products, (int)view.ProductCount);
        var products = new OpenUsdNativeRenderProductSpecification[productRecords.Length];
        var indices = new ReadOnlySpan<uint>(view.RenderVarIndices, (int)view.RenderVarIndexCount);
        nuint indexCursor = 0;
        var referencedVariables = new bool[(int)view.VariableCount];
        int nextVariable = 0;
        var productPaths = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < products.Length; index++)
        {
            OpenUsdNativeRenderProductRecord record = productRecords[index];
            if (record.Reserved != 0 || record.StringOffset != (nuint)cursor ||
                record.RenderVarIndexOffset != indexCursor ||
                record.RenderVarIndexCount > view.RenderVarIndexCount - indexCursor ||
                record.Width <= 0 || record.Height <= 0 ||
                !PositiveFinite(record.PixelAspectRatio) || !PositiveFinite(record.ApertureWidth) ||
                !PositiveFinite(record.ApertureHeight) ||
                !float.IsFinite(record.DataWindowMinX) || !float.IsFinite(record.DataWindowMinY) ||
                !float.IsFinite(record.DataWindowMaxX) || !float.IsFinite(record.DataWindowMaxY) ||
                !PositiveFinite(record.DataWindowMaxX - record.DataWindowMinX) ||
                !PositiveFinite(record.DataWindowMaxY - record.DataWindowMinY) ||
                record.DisableMotionBlur is not (0 or 1) || record.DisableDepthOfField is not (0 or 1))
            {
                throw InvalidRenderSpecification("invalid product dimensions, flags or noncanonical ranges");
            }
            string[] fields = TakeRenderStrings(strings, ref cursor, 5);
            ValidateRenderPath(fields[0]);
            ValidateRenderPath(fields[3]);
            if (!productPaths.Add(fields[0]) || fields[4] is not
                ("expandAperture" or "cropAperture" or "adjustApertureWidth" or
                "adjustApertureHeight" or "adjustPixelAspectRatio"))
            {
                throw InvalidRenderSpecification("duplicate product paths or invalid conform policy");
            }
            string[] productNames = TakeRenderSettingNames(
                strings, ref cursor, record.NamespacedSettingCount, ref settingCount);
            int[] productIndices = new int[(int)record.RenderVarIndexCount];
            for (int variableIndex = 0; variableIndex < productIndices.Length; variableIndex++)
            {
                uint variable = indices[(int)indexCursor + variableIndex];
                if (variable >= view.VariableCount)
                {
                    throw InvalidRenderSpecification("a product refers outside the variable table");
                }
                if (!referencedVariables[variable])
                {
                    if (variable != nextVariable++)
                    {
                        throw InvalidRenderSpecification("variables are not in native first-use order");
                    }
                    referencedVariables[variable] = true;
                }
                productIndices[variableIndex] = (int)variable;
            }
            indexCursor += record.RenderVarIndexCount;
            products[index] = new OpenUsdNativeRenderProductSpecification(
                fields[0], fields[1], fields[2], fields[3], fields[4], record, productIndices, productNames);
        }
        if (indexCursor != view.RenderVarIndexCount || nextVariable != (int)view.VariableCount)
        {
            throw InvalidRenderSpecification("unreferenced variable or index data");
        }

        var variableRecords = new ReadOnlySpan<OpenUsdNativeRenderVariableRecord>(
            view.Variables, (int)view.VariableCount);
        var variables = new OpenUsdNativeRenderVariableSpecification[variableRecords.Length];
        var variablePaths = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < variables.Length; index++)
        {
            OpenUsdNativeRenderVariableRecord record = variableRecords[index];
            if (record.StringOffset != (nuint)cursor)
            {
                throw InvalidRenderSpecification("noncanonical variable string range");
            }
            string[] fields = TakeRenderStrings(strings, ref cursor, 4);
            ValidateRenderPath(fields[0]);
            if (!variablePaths.Add(fields[0]))
            {
                throw InvalidRenderSpecification("duplicate variable paths");
            }
            variables[index] = new OpenUsdNativeRenderVariableSpecification(
                fields[0], fields[1], fields[2], fields[3],
                TakeRenderSettingNames(strings, ref cursor, record.NamespacedSettingCount, ref settingCount));
        }
        if (cursor != strings.Length)
        {
            throw InvalidRenderSpecification("unreferenced string data");
        }
        return new OpenUsdNativeRenderSpecification(
            root[0], products, variables, purposes, bindingPurposes, root[1], names);
    }

    private static void ValidateRenderBuffer<T>(T* pointer, nuint bytes, nuint count, nuint maximum, nuint alignment)
        where T : unmanaged
    {
        if (count > maximum || bytes != count * (nuint)sizeof(T) ||
            (pointer == null) != (count == 0) || (nuint)pointer % alignment != 0 ||
            (nuint)pointer > nuint.MaxValue - bytes)
        {
            throw InvalidRenderSpecification("invalid buffer size, pointer, alignment or budget");
        }
    }

    private static string[] TakeRenderStrings(string[] strings, ref int cursor, nuint count)
    {
        if (count > (nuint)(strings.Length - cursor))
        {
            throw InvalidRenderSpecification("string range exceeds the packed table");
        }
        string[] result = strings[cursor..(cursor + (int)count)];
        cursor += (int)count;
        return result;
    }

    private static string[] TakeRenderSettingNames(string[] strings, ref int cursor, nuint count, ref int total)
    {
        if (count > (nuint)(RenderSpecificationMaximumSettingNames - total))
        {
            throw InvalidRenderSpecification("unevaluated setting names exceed the declared budget");
        }
        string[] result = TakeRenderStrings(strings, ref cursor, count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in result)
        {
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                throw InvalidRenderSpecification("invalid or duplicate unevaluated setting name");
            }
        }
        total += (int)count;
        return result;
    }

    private static void ValidateRenderPath(string path)
    {
        if (!OpenUsdIdentifierValidation.IsAbsolutePrimPath(path))
        {
            throw InvalidRenderSpecification("invalid absolute prim path");
        }
    }

    private static bool PositiveFinite(float value) => float.IsFinite(value) && value > 0;

    private static OpenUsdNativeException InvalidRenderSpecification(string detail) =>
        new(OpenUsdNativeStatus.NativeError,
            $"The native runtime returned an invalid render specification: {detail}.");
}
