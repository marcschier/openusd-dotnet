// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Physics;

namespace OpenUsd.Viewer;

/// <summary>One extracted physics property, already detached from the stage.</summary>
/// <param name="Name">The authored property name, including any instance segment.</param>
/// <param name="ValueText">The extracted value, formatted for display.</param>
/// <param name="Source">Which schema opinion the value came from.</param>
/// <param name="IsAuthored">Whether the scene authors an opinion rather than inheriting one.</param>
internal sealed record ViewerPhysicsExtractedProperty(
    string Name,
    string ValueText,
    string Source,
    bool IsAuthored);

/// <summary>One extracted physics object, already detached from the stage.</summary>
/// <param name="ObjectId">The extractor's stable identity for the object.</param>
/// <param name="PrimPath">The absolute authored prim path.</param>
/// <param name="Kind">The extracted object kind.</param>
/// <param name="IsEnabled">Whether the object is simulated.</param>
/// <param name="Properties">Every extracted property, in extraction order.</param>
/// <param name="Diagnostics">Every diagnostic the extractor reported for this object.</param>
/// <param name="TargetId">The retained world's identity for the object commands must address.</param>
/// <param name="TargetPath">The authored prim the target identity was composed from.</param>
/// <param name="Commandability">Which runtime commands the target accepts.</param>
/// <remarks>
/// <para>
/// One prim commonly produces several extracted objects - a rigid body, its collider, and the
/// vehicle applied to it all live on the same path - so the path alone does not name an object.
/// The extractor's identity does, which is what lets the inspector keep a selection on the exact
/// object the operator chose rather than on whichever section for that path happens to come first.
/// </para>
/// <para>
/// <see cref="ObjectId"/> is not a simulation identity. The extractor hashes the path and the
/// object type; the composer hashes the composed object's address. The identity a command has to
/// carry is therefore resolved separately and travels as <see cref="TargetId"/>.
/// </para>
/// </remarks>
internal sealed record ViewerPhysicsExtractedObject(
    ulong ObjectId,
    string PrimPath,
    string Kind,
    bool IsEnabled,
    IReadOnlyList<ViewerPhysicsExtractedProperty> Properties,
    IReadOnlyList<string> Diagnostics,
    ulong TargetId = 0UL,
    string TargetPath = "",
    ViewerPhysicsCommandability Commandability = ViewerPhysicsCommandability.None);

/// <summary>One whole extraction, projected into renderer-neutral records.</summary>
/// <param name="Revision">The extraction revision the document was produced at.</param>
/// <param name="Objects">Every extracted object, in extraction order.</param>
/// <param name="Detail">A sentence describing how the document was produced.</param>
internal sealed record ViewerPhysicsExtractionDocument(
    ulong Revision,
    IReadOnlyList<ViewerPhysicsExtractedObject> Objects,
    string Detail)
{
    /// <summary>Gets the document of a stage that carries no physics.</summary>
    internal static ViewerPhysicsExtractionDocument Empty { get; } =
        new(0, [], "No physics object was extracted from this stage.");
}

/// <summary>
/// Turns one extraction document into the sections the physics inspector edits from.
/// </summary>
/// <remarks>
/// <para>
/// The projection is pure so the capability gating, the labelling, and the per-object diagnostics
/// can be asserted without a stage, a solver, or a running window. It is also the single place that
/// decides whether a row is editable, so the toolbar, the inspector, and the undo history can never
/// disagree about what may be authored.
/// </para>
/// <para>
/// Every extracted property produces a row, including one no schema describes. A property the
/// viewer cannot name is still something the scene authored, and silently dropping it would make
/// the inspector look complete while hiding an input the simulation is actually reading.
/// </para>
/// </remarks>
internal static class ViewerPhysicsInspectorProjector
{
    /// <summary>Projects one extraction document.</summary>
    /// <param name="document">The extracted objects.</param>
    /// <param name="features">The capabilities the built world reports.</param>
    /// <returns>One section per extracted object, in extraction order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    internal static IReadOnlyList<ViewerPhysicsObjectSection> Project(
        ViewerPhysicsExtractionDocument document,
        UsdPhysicsCapability features)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Objects.Count == 0)
        {
            return [];
        }

        var sections = new List<ViewerPhysicsObjectSection>(document.Objects.Count);
        for (int index = 0; index < document.Objects.Count; index++)
        {
            ViewerPhysicsExtractedObject item = document.Objects[index];
            var rows = new List<ViewerPhysicsPropertyRow>(item.Properties.Count);
            for (int property = 0; property < item.Properties.Count; property++)
            {
                rows.Add(ProjectRow(item.PrimPath, item.Properties[property], features));
            }

            rows.Sort(static (left, right) =>
                string.CompareOrdinal(left.Label, right.Label));
            sections.Add(new ViewerPhysicsObjectSection(
                item.ObjectId,
                item.PrimPath,
                item.Kind,
                Describe(item),
                item.Diagnostics,
                rows,
                item.TargetId,
                item.TargetPath,
                item.Commandability));
        }

        return sections;
    }

    /// <summary>Finds the row one property name produces inside a projected section list.</summary>
    /// <param name="sections">The projected sections.</param>
    /// <param name="primPath">The prim the property is authored on.</param>
    /// <param name="name">The authored property name.</param>
    /// <returns>The row, or <see langword="null"/> when the section does not carry it.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static ViewerPhysicsPropertyRow? FindRow(
        IReadOnlyList<ViewerPhysicsObjectSection> sections,
        string primPath,
        string name)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(primPath);
        ArgumentNullException.ThrowIfNull(name);
        for (int index = 0; index < sections.Count; index++)
        {
            ViewerPhysicsObjectSection section = sections[index];
            if (!string.Equals(section.PrimPath, primPath, StringComparison.Ordinal))
            {
                continue;
            }

            for (int row = 0; row < section.Rows.Count; row++)
            {
                if (string.Equals(section.Rows[row].Name, name, StringComparison.Ordinal))
                {
                    return section.Rows[row];
                }
            }
        }

        return null;
    }

    private static ViewerPhysicsPropertyRow ProjectRow(
        string primPath,
        ViewerPhysicsExtractedProperty property,
        UsdPhysicsCapability features)
    {
        if (ViewerPhysicsSchemaProjection.FindProperty(property.Name) is { } declared)
        {
            (ViewerPhysicsAuthorability authorability, string detail) =
                ViewerPhysicsEditability.Classify(
                    property.Name,
                    declared.Kind,
                    declared.RequiredCapability,
                    features,
                    declared.IsEditable);
            return new ViewerPhysicsPropertyRow(
                primPath,
                property.Name,
                declared.Label,
                declared.Documentation,
                declared.Kind,
                declared.Tokens,
                FormatValue(property, declared.DefaultText),
                DescribeSource(property),
                authorability,
                detail);
        }

        if (ViewerPhysicsCoreProperties.Find(property.Name) is { } core)
        {
            (ViewerPhysicsAuthorability authorability, string detail) =
                ViewerPhysicsEditability.Classify(
                    property.Name,
                    core.Kind,
                    core.RequiredCapability,
                    features,
                    core.IsAuthorable);
            return new ViewerPhysicsPropertyRow(
                primPath,
                property.Name,
                core.Label,
                core.Documentation,
                core.Kind,
                core.Tokens,
                FormatValue(property, string.Empty),
                DescribeSource(property),
                authorability,
                detail);
        }

        return new ViewerPhysicsPropertyRow(
            primPath,
            property.Name,
            property.Name,
            "No schema the viewer knows declares this property.",
            ViewerPhysicsValueKind.Unsupported,
            [],
            FormatValue(property, string.Empty),
            DescribeSource(property),
            ViewerPhysicsAuthorability.UnsupportedType,
            "The viewer cannot type this property, so it is shown exactly as extracted.");
    }

    private static string FormatValue(ViewerPhysicsExtractedProperty property, string fallback)
    {
        if (property.ValueText.Length != 0)
        {
            return property.ValueText;
        }

        return fallback.Length == 0 ? "(no value)" : fallback + " (schema fallback)";
    }

    private static string DescribeSource(ViewerPhysicsExtractedProperty property) =>
        property.IsAuthored ? property.Source : property.Source + " (fallback)";

    private static string Describe(ViewerPhysicsExtractedObject item) => item.IsEnabled
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{item.Kind} at {item.PrimPath} is simulated.")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{item.Kind} at {item.PrimPath} is disabled and is not simulated.");
}
