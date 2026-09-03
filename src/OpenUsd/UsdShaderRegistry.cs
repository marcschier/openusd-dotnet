// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Identifies whether a <see cref="UsdShaderProperty"/> is an input or an output.</summary>
public enum UsdShaderPropertyDirection
{
    /// <summary>An input property.</summary>
    Input = 0,
    /// <summary>An output property.</summary>
    Output = 1
}

/// <summary>One shading input or output on a <see cref="UsdShaderNodeDefinition"/>.</summary>
public sealed record UsdShaderProperty(
    string Name,
    string Type,
    UsdShaderPropertyDirection Direction,
    bool IsArray,
    bool IsConnectable);

/// <summary>
/// One shader node definition read from the process-global OpenUSD Sdr/Ndr registry: standard
/// UsdShade built-ins such as UsdPreviewSurface and UsdUVTexture, MaterialX standard-library
/// nodes when the usdMtlx discovery plugin is registered, and any MDL nodes an optional MDL SDK
/// parser plugin has registered.
/// </summary>
public sealed record UsdShaderNodeDefinition(
    string Identifier,
    string Name,
    string Function,
    string ShadingSystem,
    string Context,
    string ResolvedDefinitionUri,
    string ResolvedImplementationUri,
    string ImplementationName,
    IReadOnlyList<UsdShaderProperty> Properties,
    bool IsValid)
{
    /// <inheritdoc />
    public bool Equals(UsdShaderNodeDefinition? other) =>
        other is not null &&
        Identifier == other.Identifier &&
        Name == other.Name &&
        Function == other.Function &&
        ShadingSystem == other.ShadingSystem &&
        Context == other.Context &&
        ResolvedDefinitionUri == other.ResolvedDefinitionUri &&
        ResolvedImplementationUri == other.ResolvedImplementationUri &&
        ImplementationName == other.ImplementationName &&
        Properties.SequenceEqual(other.Properties) &&
        IsValid == other.IsValid;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Identifier,
            Name,
            Function,
            ShadingSystem,
            Context,
            HashCode.Combine(
                ResolvedDefinitionUri,
                ResolvedImplementationUri,
                ImplementationName,
                RecordCollectionFormatting.SequenceHashCode(Properties),
                IsValid));

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(UsdShaderNodeDefinition)} {{ {nameof(Identifier)} = {Identifier}, " +
        $"{nameof(Name)} = {Name}, {nameof(Function)} = {Function}, " +
        $"{nameof(ShadingSystem)} = {ShadingSystem}, {nameof(Context)} = {Context}, " +
        $"{nameof(ResolvedDefinitionUri)} = {ResolvedDefinitionUri}, " +
        $"{nameof(ResolvedImplementationUri)} = {ResolvedImplementationUri}, " +
        $"{nameof(ImplementationName)} = {ImplementationName}, " +
        $"{nameof(Properties)} = {RecordCollectionFormatting.FormatSequence(Properties)}, " +
        $"{nameof(IsValid)} = {IsValid} }}";
}

/// <summary>
/// One bulk, detached snapshot of the process-global OpenUSD Sdr/Ndr shader node-definition
/// registry, produced by <see cref="UsdShaderRegistry.GetNodeDefinitionsSnapshot"/>.
/// </summary>
/// <remarks>
/// The native page this snapshot is decoded from is bounded and truncates rather than growing
/// without bound. <see cref="IsTruncated"/> is <see langword="true"/> whenever the registry held
/// more nodes, properties, or string bytes than the page's fixed capacity, in which case
/// <see cref="Definitions"/> is a valid but incomplete prefix of the registry, never a
/// silently-wrong or corrupted one.
/// </remarks>
public sealed record UsdShaderNodeDefinitionSnapshot(
    IReadOnlyList<UsdShaderNodeDefinition> Definitions,
    bool IsTruncated)
{
    /// <inheritdoc />
    public bool Equals(UsdShaderNodeDefinitionSnapshot? other) =>
        other is not null &&
        Definitions.SequenceEqual(other.Definitions) &&
        IsTruncated == other.IsTruncated;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(RecordCollectionFormatting.SequenceHashCode(Definitions), IsTruncated);

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(UsdShaderNodeDefinitionSnapshot)} {{ {nameof(Definitions)} = " +
        $"{RecordCollectionFormatting.FormatSequence(Definitions)}, {nameof(IsTruncated)} = {IsTruncated} }}";
}

/// <summary>
/// Bulk, read-only introspection of the process-global OpenUSD Sdr/Ndr shader node-definition
/// registry. The registry is independent of any stage: it never mutates or depends on any open
/// stage, and every call here observes the same process-wide registry state.
/// </summary>
public static class UsdShaderRegistry
{
    /// <summary>
    /// Enumerates every shader node definition the registry can currently discover as one bounded
    /// snapshot, explicit about whether the underlying native page was truncated at capacity.
    /// Parsing every node the registry is aware of on first use is potentially expensive; the
    /// native runtime performs it exactly once per call, and results reflect whatever discovery
    /// plugins -- UsdShade built-ins, MaterialX's usdMtlx discovery, and an optional MDL parser
    /// plugin -- are registered at the time of the call. Prefer this over
    /// <see cref="GetNodeDefinitions"/> for any caller that must know whether the registry's
    /// bounded introspection page held everything the registry currently has.
    /// </summary>
    public static UsdShaderNodeDefinitionSnapshot GetNodeDefinitionsSnapshot()
    {
        OpenUsdNativeSdrNodeDefinitionPage page = OpenUsdNativeRuntime.GetSdrNodeDefinitions();
        var result = new UsdShaderNodeDefinition[page.Definitions.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Convert(page.Definitions[i]);
        }
        return new UsdShaderNodeDefinitionSnapshot(Array.AsReadOnly(result), page.IsTruncated);
    }

    /// <summary>
    /// Convenience wrapper over <see cref="GetNodeDefinitionsSnapshot"/> for callers that do not
    /// need truncation awareness. Throws <see cref="InvalidOperationException"/> if the registry's
    /// bounded introspection page was truncated, rather than silently returning an incomplete
    /// list; use <see cref="GetNodeDefinitionsSnapshot"/> when the registry is large enough that
    /// truncation is a real possibility and must be observed rather than treated as an error.
    /// </summary>
    public static IReadOnlyList<UsdShaderNodeDefinition> GetNodeDefinitions()
    {
        UsdShaderNodeDefinitionSnapshot snapshot = GetNodeDefinitionsSnapshot();
        if (snapshot.IsTruncated)
        {
            throw new InvalidOperationException(
                "The Sdr/Ndr shader node-definition registry page was truncated at its bounded " +
                "capacity. Call UsdShaderRegistry.GetNodeDefinitionsSnapshot() to observe " +
                "IsTruncated explicitly instead of silently receiving an incomplete list.");
        }
        return snapshot.Definitions;
    }

    /// <summary>
    /// Attempts to resolve one shader node definition from a source asset, optionally scoped by
    /// <paramref name="subIdentifier"/> when the asset defines multiple node definitions and by
    /// <paramref name="shadingSystem"/> when the asset can represent more than one source type --
    /// the same concepts <c>UsdShadeShader</c> exposes as <c>info:&lt;sourceType&gt;:sourceAsset</c>
    /// and <c>info:&lt;sourceType&gt;:sourceAsset:subIdentifier</c>. Returns <see langword="false"/>,
    /// not an exception, when no registered parser plugin resolves the asset -- for example, an
    /// MDL asset when no MDL SDK parser plugin is registered. Throws
    /// <see cref="InvalidOperationException"/> in the (essentially unreachable, for any real
    /// shader definition) case where the resolved node's own bounded property page was truncated,
    /// rather than silently returning an incomplete property list.
    /// </summary>
    public static bool TryGetNodeDefinitionFromAsset(
        string sourceAsset,
        string? subIdentifier,
        string? shadingSystem,
        out UsdShaderNodeDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAsset);
        bool found = OpenUsdNativeRuntime.TryGetSdrNodeDefinitionFromAsset(
            sourceAsset,
            subIdentifier,
            shadingSystem,
            out OpenUsdNativeSdrNodeDefinition native,
            out bool isTruncated);
        if (isTruncated)
        {
            throw new InvalidOperationException(
                "The resolved shader node definition's bounded property page was truncated at " +
                "its capacity, so its property list would be incomplete.");
        }

        if (!found)
        {
            definition = null;
            return false;
        }

        definition = Convert(native);
        return true;
    }

    private static UsdShaderNodeDefinition Convert(OpenUsdNativeSdrNodeDefinition native)
    {
        var properties = new UsdShaderProperty[native.Properties.Length];
        for (int i = 0; i < properties.Length; i++)
        {
            OpenUsdNativeSdrProperty property = native.Properties[i];
            properties[i] = new UsdShaderProperty(
                property.Name,
                property.Type,
                (UsdShaderPropertyDirection)property.Direction,
                property.IsArray,
                property.IsConnectable);
        }

        return new UsdShaderNodeDefinition(
            native.Identifier,
            native.Name,
            native.Function,
            native.ShadingSystem,
            native.Context,
            native.ResolvedDefinitionUri,
            native.ResolvedImplementationUri,
            native.ImplementationName,
            Array.AsReadOnly(properties),
            native.IsValid);
    }
}
