// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Physics;
using OpenUsd.Physics.Schema;

namespace OpenUsd.Viewer;

/// <summary>Identifies the value one authorable physics property carries.</summary>
internal enum ViewerPhysicsValueKind
{
    /// <summary>A checkbox.</summary>
    Bool,

    /// <summary>A real number.</summary>
    Number,

    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>Free text.</summary>
    Text,

    /// <summary>One of a fixed set of tokens.</summary>
    Token,

    /// <summary>Three real components.</summary>
    Vector3,

    /// <summary>A value the inspector describes but cannot author.</summary>
    Unsupported,
}

/// <summary>Three components of an authorable physics vector.</summary>
/// <param name="X">The first component.</param>
/// <param name="Y">The second component.</param>
/// <param name="Z">The third component.</param>
internal readonly record struct ViewerPhysicsVector3(double X, double Y, double Z)
    : IUsdDetachedResult
{
    /// <summary>Gets the zero vector.</summary>
    internal static ViewerPhysicsVector3 Zero => default;

    /// <summary>Gets a value indicating whether every component is finite.</summary>
    internal bool IsFinite =>
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    /// <summary>Gets the Euclidean length.</summary>
    internal double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    /// <summary>Adds two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    internal static ViewerPhysicsVector3 Add(
        ViewerPhysicsVector3 left,
        ViewerPhysicsVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary>Subtracts one vector from another.</summary>
    /// <param name="left">The vector subtracted from.</param>
    /// <param name="right">The vector subtracted.</param>
    /// <returns>The component-wise difference.</returns>
    internal static ViewerPhysicsVector3 Subtract(
        ViewerPhysicsVector3 left,
        ViewerPhysicsVector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Scales a vector.</summary>
    /// <param name="value">The vector to scale.</param>
    /// <param name="scale">The scalar.</param>
    /// <returns>The scaled vector.</returns>
    internal static ViewerPhysicsVector3 Scale(ViewerPhysicsVector3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    /// <summary>Computes the dot product of two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The dot product.</returns>
    internal static double Dot(ViewerPhysicsVector3 left, ViewerPhysicsVector3 right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    /// <summary>Computes the cross product of two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The cross product.</returns>
    internal static ViewerPhysicsVector3 Cross(
        ViewerPhysicsVector3 left,
        ViewerPhysicsVector3 right) =>
        new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    /// <summary>Returns the unit vector, or zero when the vector is degenerate.</summary>
    /// <returns>The normalized vector.</returns>
    internal ViewerPhysicsVector3 Normalized()
    {
        double length = Length;
        return length > 0d && double.IsFinite(length) ? Scale(this, 1d / length) : Zero;
    }

    /// <summary>Formats the vector the way the inspector shows it.</summary>
    /// <returns>The formatted vector.</returns>
    internal string Format() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:0.######}, {Y:0.######}, {Z:0.######})");
}

/// <summary>One authorable physics value, including the absence of an authored opinion.</summary>
/// <param name="Kind">The value the property carries.</param>
/// <param name="IsAuthored">Whether the prim carries an opinion at all.</param>
/// <param name="BoolValue">The boolean component.</param>
/// <param name="NumberValue">The real component.</param>
/// <param name="IntegerValue">The whole-number component.</param>
/// <param name="TextValue">The text or token component.</param>
/// <param name="VectorValue">The three-component vector.</param>
/// <remarks>
/// The unauthored state is a value rather than a null, because undo has to be able to restore it.
/// An inspector that could only write values would turn "this prim inherits the schema fallback"
/// into "this prim was authored to the fallback" the first time anyone touched the field, and no
/// later undo could ever put the fallback back.
/// </remarks>
internal readonly record struct ViewerPhysicsValue(
    ViewerPhysicsValueKind Kind,
    bool IsAuthored,
    bool BoolValue,
    double NumberValue,
    long IntegerValue,
    string TextValue,
    ViewerPhysicsVector3 VectorValue) : IUsdDetachedResult
{
    /// <summary>Creates the value of a property that carries no authored opinion.</summary>
    /// <param name="kind">The value the property carries.</param>
    /// <returns>The unauthored value.</returns>
    internal static ViewerPhysicsValue Unauthored(ViewerPhysicsValueKind kind) =>
        new(kind, false, false, 0d, 0L, string.Empty, ViewerPhysicsVector3.Zero);

    /// <summary>Creates an authored boolean.</summary>
    /// <param name="value">The authored value.</param>
    /// <returns>The value.</returns>
    internal static ViewerPhysicsValue FromBool(bool value) =>
        new(ViewerPhysicsValueKind.Bool, true, value, 0d, 0L, string.Empty, ViewerPhysicsVector3.Zero);

    /// <summary>Creates an authored real number.</summary>
    /// <param name="value">The authored value.</param>
    /// <returns>The value.</returns>
    internal static ViewerPhysicsValue FromNumber(double value) =>
        new(ViewerPhysicsValueKind.Number, true, false, value, 0L, string.Empty, ViewerPhysicsVector3.Zero);

    /// <summary>Creates an authored whole number.</summary>
    /// <param name="value">The authored value.</param>
    /// <returns>The value.</returns>
    internal static ViewerPhysicsValue FromInteger(long value) =>
        new(ViewerPhysicsValueKind.Integer, true, false, 0d, value, string.Empty, ViewerPhysicsVector3.Zero);

    /// <summary>Creates authored free text.</summary>
    /// <param name="value">The authored value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    internal static ViewerPhysicsValue FromText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(ViewerPhysicsValueKind.Text, true, false, 0d, 0L, value, ViewerPhysicsVector3.Zero);
    }

    /// <summary>Creates an authored token.</summary>
    /// <param name="value">The authored token.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    internal static ViewerPhysicsValue FromToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(ViewerPhysicsValueKind.Token, true, false, 0d, 0L, value, ViewerPhysicsVector3.Zero);
    }

    /// <summary>Creates an authored vector.</summary>
    /// <param name="value">The authored vector.</param>
    /// <returns>The value.</returns>
    internal static ViewerPhysicsValue FromVector(ViewerPhysicsVector3 value) =>
        new(ViewerPhysicsValueKind.Vector3, true, false, 0d, 0L, string.Empty, value);

    /// <summary>Formats the value the way the inspector shows it.</summary>
    /// <param name="fallback">The schema fallback shown when nothing is authored.</param>
    /// <returns>The formatted value.</returns>
    internal string Format(string fallback = "")
    {
        if (!IsAuthored)
        {
            return fallback.Length == 0 ? "(unauthored)" : fallback;
        }

        return Kind switch
        {
            ViewerPhysicsValueKind.Bool => BoolValue ? "true" : "false",
            ViewerPhysicsValueKind.Number =>
                string.Create(CultureInfo.InvariantCulture, $"{NumberValue:0.######}"),
            ViewerPhysicsValueKind.Integer =>
                IntegerValue.ToString(CultureInfo.InvariantCulture),
            ViewerPhysicsValueKind.Vector3 => VectorValue.Format(),
            ViewerPhysicsValueKind.Text or ViewerPhysicsValueKind.Token => TextValue,
            _ => "(not editable)",
        };
    }
}

/// <summary>Parses inspector text into an authorable physics value.</summary>
/// <remarks>
/// Parsing refuses rather than coerces. A mass field that silently turned "12kg" into 12 would be
/// authoring a number the user never typed, and a field that turned an unparsable string into zero
/// would author a mass of zero into the scene the user is about to simulate.
/// </remarks>
internal static class ViewerPhysicsValueParser
{
    /// <summary>Parses one inspector field.</summary>
    /// <param name="kind">The value the property carries.</param>
    /// <param name="tokens">The tokens a token property accepts, or an empty list.</param>
    /// <param name="text">The text the user typed.</param>
    /// <param name="value">Receives the parsed value.</param>
    /// <param name="error">Receives the refusal, or an empty string.</param>
    /// <returns><see langword="true"/> when the text parses.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tokens"/> or <paramref name="text"/> is null.
    /// </exception>
    internal static bool TryParse(
        ViewerPhysicsValueKind kind,
        IReadOnlyList<string> tokens,
        string text,
        out ViewerPhysicsValue value,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.Trim();
        value = ViewerPhysicsValue.Unauthored(kind);
        error = string.Empty;

        switch (kind)
        {
            case ViewerPhysicsValueKind.Bool:
                if (bool.TryParse(trimmed, out bool flag))
                {
                    value = ViewerPhysicsValue.FromBool(flag);
                    return true;
                }

                error = "Enter true or false.";
                return false;

            case ViewerPhysicsValueKind.Number:
                if (double.TryParse(
                        trimmed,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double number) &&
                    double.IsFinite(number))
                {
                    value = ViewerPhysicsValue.FromNumber(number);
                    return true;
                }

                error = "Enter a finite number.";
                return false;

            case ViewerPhysicsValueKind.Integer:
                if (long.TryParse(
                    trimmed,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long integer))
                {
                    value = ViewerPhysicsValue.FromInteger(integer);
                    return true;
                }

                error = "Enter a whole number.";
                return false;

            case ViewerPhysicsValueKind.Token:
                if (tokens.Count == 0)
                {
                    value = ViewerPhysicsValue.FromToken(trimmed);
                    return true;
                }

                for (int index = 0; index < tokens.Count; index++)
                {
                    if (string.Equals(tokens[index], trimmed, StringComparison.Ordinal))
                    {
                        value = ViewerPhysicsValue.FromToken(tokens[index]);
                        return true;
                    }
                }

                error = "Choose one of: " + string.Join(", ", tokens);
                return false;

            case ViewerPhysicsValueKind.Text:
                value = ViewerPhysicsValue.FromText(text);
                return true;

            case ViewerPhysicsValueKind.Vector3:
                return TryParseVector(trimmed, out value, out error);

            default:
                error = "This property is described by the schema but cannot be edited here.";
                return false;
        }
    }

    private static bool TryParseVector(
        string text,
        out ViewerPhysicsValue value,
        out string error)
    {
        value = ViewerPhysicsValue.Unauthored(ViewerPhysicsValueKind.Vector3);
        string cleaned = text.Replace("(", " ", StringComparison.Ordinal)
            .Replace(")", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal);
        string[] parts = cleaned.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            error = "Enter three numbers, for example 0 -9.81 0.";
            return false;
        }

        Span<double> components = stackalloc double[3];
        for (int index = 0; index < 3; index++)
        {
            if (!double.TryParse(
                    parts[index],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double component) ||
                !double.IsFinite(component))
            {
                error = "Every component must be a finite number.";
                return false;
            }

            components[index] = component;
        }

        value = ViewerPhysicsValue.FromVector(
            new ViewerPhysicsVector3(components[0], components[1], components[2]));
        error = string.Empty;
        return true;
    }
}

/// <summary>Describes one authorable physics property, projected from the generated catalog.</summary>
/// <param name="SchemaIdentifier">The schema that declares the property.</param>
/// <param name="Domain">The simulation domain the schema belongs to.</param>
/// <param name="RequiredCapability">The capability a built world must report.</param>
/// <param name="Name">The namespaced property name authored on the prim.</param>
/// <param name="Label">The label the inspector shows.</param>
/// <param name="Documentation">The sentence describing what the property does.</param>
/// <param name="Kind">The value the property carries.</param>
/// <param name="Tokens">The tokens a token property accepts, or an empty list.</param>
/// <param name="DefaultText">The schema fallback, formatted the way USD writes it.</param>
internal sealed record ViewerPhysicsPropertyDescriptor(
    string SchemaIdentifier,
    string Domain,
    UsdPhysicsCapability RequiredCapability,
    string Name,
    string Label,
    string Documentation,
    ViewerPhysicsValueKind Kind,
    IReadOnlyList<string> Tokens,
    string DefaultText)
{
    /// <summary>Gets a value indicating whether the inspector can author this property.</summary>
    internal bool IsEditable => Kind != ViewerPhysicsValueKind.Unsupported;
}

/// <summary>Describes one authorable physics schema and every property it declares.</summary>
/// <param name="Identifier">The schema identifier authored in <c>apiSchemas</c> or as a type.</param>
/// <param name="Documentation">The sentence describing what the schema models.</param>
/// <param name="Domain">The simulation domain the schema belongs to.</param>
/// <param name="RequiredCapability">The capability a built world must report.</param>
/// <param name="IsTyped">Whether the schema is a concrete prim type rather than an API schema.</param>
/// <param name="Properties">Every declared property, ordered by name.</param>
internal sealed record ViewerPhysicsSchemaDescriptor(
    string Identifier,
    string Documentation,
    string Domain,
    UsdPhysicsCapability RequiredCapability,
    bool IsTyped,
    IReadOnlyList<ViewerPhysicsPropertyDescriptor> Properties);

/// <summary>
/// Projects the generated physics schema catalog into the descriptors the inspector edits from.
/// </summary>
/// <remarks>
/// <para>
/// The inspector never hard-codes a field list. Every domain - rigid bodies, colliders, materials,
/// joints, articulations, tendons, mimic joints, character controllers, vehicles, and the GPU
/// domains - comes from the same generated catalog the schema itself is generated from, so a domain
/// whose runtime support is still being built is presented as soon as its schema exists and cannot
/// silently drift away from the properties the schema declares.
/// </para>
/// <para>
/// Array-valued and relationship properties are projected as
/// <see cref="ViewerPhysicsValueKind.Unsupported"/> rather than omitted. Hiding them would tell the
/// user their scene carries no such setting; describing them and refusing to author them tells the
/// truth about what this inspector can do.
/// </para>
/// </remarks>
internal static class ViewerPhysicsSchemaProjection
{
    private static readonly ViewerPhysicsSchemaDescriptor[] Cached = Build();

    /// <summary>Gets every described schema, ordered by domain and identifier.</summary>
    internal static IReadOnlyList<ViewerPhysicsSchemaDescriptor> Schemas => Cached;

    /// <summary>Finds the descriptor of one schema identifier.</summary>
    /// <param name="identifier">The schema identifier to look up.</param>
    /// <returns>The descriptor, or <see langword="null"/> when it is not described.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is null.</exception>
    internal static ViewerPhysicsSchemaDescriptor? Find(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        for (int index = 0; index < Cached.Length; index++)
        {
            if (string.Equals(Cached[index].Identifier, identifier, StringComparison.Ordinal))
            {
                return Cached[index];
            }
        }

        return null;
    }

    /// <summary>Finds the descriptor of one namespaced property name.</summary>
    /// <param name="name">The namespaced property name.</param>
    /// <returns>The descriptor, or <see langword="null"/> when it is not described.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    internal static ViewerPhysicsPropertyDescriptor? FindProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (int index = 0; index < Cached.Length; index++)
        {
            IReadOnlyList<ViewerPhysicsPropertyDescriptor> properties = Cached[index].Properties;
            for (int property = 0; property < properties.Count; property++)
            {
                if (string.Equals(properties[property].Name, name, StringComparison.Ordinal))
                {
                    return properties[property];
                }
            }
        }

        return null;
    }

    private static ViewerPhysicsSchemaDescriptor[] Build()
    {
        IReadOnlyList<OpenUsdPhysicsSchemaDescriptor> source =
            OpenUsdPhysicsPropertyCatalog.Schemas;
        var schemas = new ViewerPhysicsSchemaDescriptor[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            OpenUsdPhysicsSchemaDescriptor schema = source[index];
            string domain = schema.Domain.ToString();
            var properties = new ViewerPhysicsPropertyDescriptor[schema.Properties.Count];
            for (int property = 0; property < schema.Properties.Count; property++)
            {
                OpenUsdPhysicsPropertyDescriptor declared = schema.Properties[property];
                properties[property] = new ViewerPhysicsPropertyDescriptor(
                    schema.Identifier,
                    domain,
                    schema.RequiredCapability,
                    declared.Name,
                    declared.DisplayName,
                    declared.Documentation,
                    MapKind(declared.Kind),
                    declared.AllowedTokens,
                    declared.DefaultText);
            }

            schemas[index] = new ViewerPhysicsSchemaDescriptor(
                schema.Identifier,
                schema.Documentation,
                domain,
                schema.RequiredCapability,
                schema.IsTyped,
                properties);
        }

        return schemas;
    }

    private static ViewerPhysicsValueKind MapKind(OpenUsdPhysicsValueKind kind) => kind switch
    {
        OpenUsdPhysicsValueKind.Bool => ViewerPhysicsValueKind.Bool,
        OpenUsdPhysicsValueKind.Double => ViewerPhysicsValueKind.Number,
        OpenUsdPhysicsValueKind.Int64 => ViewerPhysicsValueKind.Integer,
        OpenUsdPhysicsValueKind.String => ViewerPhysicsValueKind.Text,
        OpenUsdPhysicsValueKind.Token => ViewerPhysicsValueKind.Token,
        OpenUsdPhysicsValueKind.Float3 => ViewerPhysicsValueKind.Vector3,
        _ => ViewerPhysicsValueKind.Unsupported,
    };
}
