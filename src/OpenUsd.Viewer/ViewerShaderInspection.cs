// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal sealed record ViewerShaderInspection(
    string Summary,
    IReadOnlyList<UsdShaderProperty> Properties,
    bool HasDefinition) : IUsdDetachedResult
{
    private const string MissingIdentifier =
        "No shader info:id is available. Source-asset definitions are not resolved here.";

    internal static ViewerShaderInspection Read(
        string implementationSource, string identifier, Func<UsdShaderNodeDefinitionSnapshot> readRegistry,
        UsdPropertyValueState identifierState = UsdPropertyValueState.Value)
    {
        ArgumentNullException.ThrowIfNull(implementationSource);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(readRegistry);
        if (implementationSource != "id")
        {
            return Unavailable(
                $"Shader implementation source '{ViewerScalarFormatter.Bound(implementationSource, 128)}' " +
                "does not use info:id. Source assets and code are not resolved here.");
        }
        if (!Enum.IsDefined(identifierState))
        {
            throw new ArgumentOutOfRangeException(nameof(identifierState));
        }
        if (identifierState == UsdPropertyValueState.Blocked)
        {
            return Unavailable("Shader info:id is blocked; no registry definition was selected.");
        }
        if (identifierState == UsdPropertyValueState.Deferred)
        {
            return Unavailable("Shader info:id is deferred; its identifier has not been proven.");
        }
        if (identifierState == UsdPropertyValueState.Unset)
        {
            return Unavailable(MissingIdentifier);
        }
        return string.IsNullOrWhiteSpace(identifier)
            ? Unavailable(MissingIdentifier)
            : Create(identifier, readRegistry());
    }

    internal static ViewerShaderInspection Create(string identifier, UsdShaderNodeDefinitionSnapshot registry)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(registry);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Unavailable(MissingIdentifier);
        }
        if (registry.IsTruncated)
        {
            return Unavailable("Sdr registry snapshot is truncated; a unique definition cannot be proven.");
        }
        UsdShaderNodeDefinition? match = null;
        foreach (UsdShaderNodeDefinition definition in registry.Definitions)
        {
            if (!string.Equals(definition.Identifier, identifier, StringComparison.Ordinal))
            {
                continue;
            }
            if (match is not null)
            {
                return Unavailable("Multiple Sdr definitions match this info:id; no definition was selected.");
            }
            match = definition;
        }
        if (match is null)
        {
            return Unavailable("No Sdr definition matches this info:id. The authored graph remains unchanged.");
        }
        if (!match.IsValid)
        {
            return Unavailable("The matching Sdr definition is invalid; its property metadata is unavailable.");
        }
        return new ViewerShaderInspection(
            $"Sdr definition: {ViewerScalarFormatter.Bound(identifier, 256)}; " +
            $"system: {ViewerScalarFormatter.Bound(match.ShadingSystem, 128)}; " +
            $"context: {ViewerScalarFormatter.Bound(match.Context, 128)}. Read-only registry metadata.",
            Array.AsReadOnly(match.Properties.ToArray()),
            HasDefinition: true);
    }

    internal string? DescribeProperty(string attributeName)
    {
        ArgumentNullException.ThrowIfNull(attributeName);
        if (!HasDefinition)
        {
            return null;
        }
        UsdShaderPropertyDirection direction;
        string name;
        if (attributeName.StartsWith("inputs:", StringComparison.Ordinal))
        {
            direction = UsdShaderPropertyDirection.Input;
            name = attributeName["inputs:".Length..];
        }
        else if (attributeName.StartsWith("outputs:", StringComparison.Ordinal))
        {
            direction = UsdShaderPropertyDirection.Output;
            name = attributeName["outputs:".Length..];
        }
        else
        {
            return null;
        }
        UsdShaderProperty? match = null;
        foreach (UsdShaderProperty property in Properties)
        {
            if (property.Direction != direction || !string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                continue;
            }
            if (match is not null)
            {
                return "Ambiguous property metadata in this Sdr definition; no type or connection claim.";
            }
            match = property;
        }
        return match is null
            ? "No matching property in this Sdr definition; authored property and connections are preserved."
            : $"Sdr {(direction == UsdShaderPropertyDirection.Input ? "input" : "output")}; " +
                $"type: {ViewerScalarFormatter.Bound(match.Type, 128)}; " +
                $"array: {(match.IsArray ? "Yes" : "No")}; " +
                $"connectable: {(match.IsConnectable ? "Yes" : "No")}. Not edit permission.";
    }

    private static ViewerShaderInspection Unavailable(string summary) => new(summary, [], HasDefinition: false);
}
