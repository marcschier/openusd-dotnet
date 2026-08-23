// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>One extracted physics object read directly out of an extraction page.</summary>
public readonly struct UsdPhysicsExtractionObject : IEquatable<UsdPhysicsExtractionObject>
{
    private const int Section = PhysicsExtractAbi.SectionObjects;

    private readonly UsdPhysicsExtractionPage? _page;
    private readonly int _index;

    internal UsdPhysicsExtractionObject(UsdPhysicsExtractionPage page, int index)
    {
        _page = page;
        _index = index;
    }

    /// <summary>Gets the zero based index of this object inside its page.</summary>
    public int Index => _index;

    /// <summary>Gets the stable identity of this object.</summary>
    public ulong Id => Page.FieldU64(Section, _index, 0);

    /// <summary>Gets the identity of the closest enclosing physics object, or zero.</summary>
    public ulong ParentId => Page.FieldU64(Section, _index, 8);

    /// <summary>Gets the identity of the prototype object an instance came from, or zero.</summary>
    public ulong PrototypeId => Page.FieldU64(Section, _index, 16);

    /// <summary>Gets the absolute path of the source prim.</summary>
    public string Path => Page.FieldText(Section, _index, 24);

    /// <summary>Gets the name of the source prim.</summary>
    public string Name => Page.FieldText(Section, _index, 28);

    /// <summary>Gets the concrete prim type of the source prim.</summary>
    public string TypeName => Page.FieldText(Section, _index, 32);

    /// <summary>Gets what this object is.</summary>
    public UsdPhysicsExtractionObjectKind Kind =>
        (UsdPhysicsExtractionObjectKind)Page.FieldU32(Section, _index, 36);

    /// <summary>Gets the simulation domains this object belongs to.</summary>
    public UsdPhysicsExtractionDomains Domains =>
        (UsdPhysicsExtractionDomains)Page.FieldU32(Section, _index, 40);

    /// <summary>Gets how this object participates in simulation.</summary>
    public UsdPhysicsExtractionObjectTraits Flags =>
        (UsdPhysicsExtractionObjectTraits)Page.FieldU32(Section, _index, 44);

    /// <summary>Gets the collision geometry a collider resolved to.</summary>
    public UsdPhysicsExtractionGeometryKind Geometry =>
        (UsdPhysicsExtractionGeometryKind)Page.FieldU32(Section, _index, 48);

    /// <summary>Gets the object index of the owning scene, or <c>-1</c>.</summary>
    public int SceneIndex => Page.FieldI32(Section, _index, 52);

    /// <summary>Gets the object index of the enclosing rigid body, or <c>-1</c>.</summary>
    public int ParentBodyIndex => Page.FieldI32(Section, _index, 56);

    /// <summary>Gets the first property index that belongs to this object.</summary>
    public int PropertyStart => (int)Page.FieldU32(Section, _index, 60);

    /// <summary>Gets how many properties belong to this object.</summary>
    public int PropertyCount => (int)Page.FieldU32(Section, _index, 64);

    /// <summary>Gets the first relationship index that belongs to this object.</summary>
    public int RelationshipStart => (int)Page.FieldU32(Section, _index, 68);

    /// <summary>Gets how many relationships belong to this object.</summary>
    public int RelationshipCount => (int)Page.FieldU32(Section, _index, 72);

    /// <summary>Gets the first collider point index that belongs to this object.</summary>
    public int PointStart => (int)Page.FieldU32(Section, _index, 76);

    /// <summary>Gets how many collider points belong to this object.</summary>
    public int PointCount => (int)Page.FieldU32(Section, _index, 80);

    /// <summary>Gets the first triangle index slot that belongs to this object.</summary>
    public int IndexStart => (int)Page.FieldU32(Section, _index, 84);

    /// <summary>Gets how many triangle index slots belong to this object.</summary>
    public int IndexCount => (int)Page.FieldU32(Section, _index, 88);

    /// <summary>Gets how many diagnostics name this object.</summary>
    public int DiagnosticCount => (int)Page.FieldU32(Section, _index, 92);

    /// <summary>Gets the world position in simulation space and meters.</summary>
    public (double X, double Y, double Z) Position => (
        Page.FieldF64(Section, _index, 96),
        Page.FieldF64(Section, _index, 104),
        Page.FieldF64(Section, _index, 112));

    /// <summary>Gets the world orientation in simulation space.</summary>
    public (double W, double X, double Y, double Z) Rotation => (
        Page.FieldF64(Section, _index, 120),
        Page.FieldF64(Section, _index, 128),
        Page.FieldF64(Section, _index, 136),
        Page.FieldF64(Section, _index, 144));

    /// <summary>Gets the world scale, which units never affect.</summary>
    public (double X, double Y, double Z) Scale => (
        Page.FieldF64(Section, _index, 152),
        Page.FieldF64(Section, _index, 160),
        Page.FieldF64(Section, _index, 168));

    /// <summary>Gets the collision extent in meters, whose meaning depends on the geometry.</summary>
    public (double X, double Y, double Z) Extent => (
        Page.FieldF64(Section, _index, 176),
        Page.FieldF64(Section, _index, 184),
        Page.FieldF64(Section, _index, 192));

    /// <summary>Gets the geometry axis, where zero is X, one is Y, and two is Z.</summary>
    public int GeometryAxis => (int)Page.FieldU32(Section, _index, 200);

    /// <summary>Gets whether this object is still enabled after diagnostics were applied.</summary>
    public bool IsEnabled =>
        (Flags & UsdPhysicsExtractionObjectTraits.Enabled) != 0 &&
        (Flags & UsdPhysicsExtractionObjectTraits.DisabledByDiagnostic) == 0;

    private UsdPhysicsExtractionPage Page =>
        _page ?? throw new InvalidOperationException(
            "This extraction object view was not bound to a page.");

    /// <summary>Compares two views for equality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when both views name the same record.</returns>
    public static bool operator ==(
        UsdPhysicsExtractionObject left, UsdPhysicsExtractionObject right) => left.Equals(right);

    /// <summary>Compares two views for inequality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when the views name different records.</returns>
    public static bool operator !=(
        UsdPhysicsExtractionObject left, UsdPhysicsExtractionObject right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsExtractionObject other) =>
        ReferenceEquals(_page, other._page) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsExtractionObject other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_page, _index);
}

/// <summary>One resolved physics property read directly out of an extraction page.</summary>
public readonly struct UsdPhysicsExtractionProperty : IEquatable<UsdPhysicsExtractionProperty>
{
    private const int Section = PhysicsExtractAbi.SectionProperties;

    private readonly UsdPhysicsExtractionPage? _page;
    private readonly int _index;

    internal UsdPhysicsExtractionProperty(UsdPhysicsExtractionPage page, int index)
    {
        _page = page;
        _index = index;
    }

    /// <summary>Gets the zero based index of this property inside its page.</summary>
    public int Index => _index;

    /// <summary>Gets the namespace neutral canonical key.</summary>
    public UsdPhysicsExtractionKey Key => (UsdPhysicsExtractionKey)Page.FieldU32(Section, _index, 0);

    /// <summary>Gets the verbatim authored property name that won resolution.</summary>
    public string Name => Page.FieldText(Section, _index, 4);

    /// <summary>Gets how the value is stored.</summary>
    public UsdPhysicsExtractionValueKind ValueKind =>
        (UsdPhysicsExtractionValueKind)Page.FieldU32(Section, _index, 8);

    /// <summary>Gets how the property was authored and resolved.</summary>
    public UsdPhysicsExtractionPropertyTraits Flags =>
        (UsdPhysicsExtractionPropertyTraits)Page.FieldU32(Section, _index, 12);

    /// <summary>Gets which authored namespace supplied the winning opinion.</summary>
    public UsdPhysicsExtractionSource Source =>
        (UsdPhysicsExtractionSource)Page.FieldU32(Section, _index, 16);

    /// <summary>Gets the first shared number or text index for a composite value.</summary>
    public int ValueStart => (int)Page.FieldU32(Section, _index, 20);

    /// <summary>Gets how many shared numbers or texts the value spans.</summary>
    public int ValueCount => (int)Page.FieldU32(Section, _index, 24);

    /// <summary>Gets the scalar value, which is zero for composite values.</summary>
    public double Scalar => Page.FieldF64(Section, _index, 32);

    /// <summary>Gets whether this property carries text rather than numbers.</summary>
    public bool IsText =>
        ValueKind is UsdPhysicsExtractionValueKind.Text or UsdPhysicsExtractionValueKind.TextArray;

    private UsdPhysicsExtractionPage Page =>
        _page ?? throw new InvalidOperationException(
            "This extraction property view was not bound to a page.");

    /// <summary>Compares two views for equality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when both views name the same record.</returns>
    public static bool operator ==(
        UsdPhysicsExtractionProperty left,
        UsdPhysicsExtractionProperty right) => left.Equals(right);

    /// <summary>Compares two views for inequality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when the views name different records.</returns>
    public static bool operator !=(
        UsdPhysicsExtractionProperty left,
        UsdPhysicsExtractionProperty right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsExtractionProperty other) =>
        ReferenceEquals(_page, other._page) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsExtractionProperty other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_page, _index);
}

/// <summary>One resolved physics relationship read directly out of an extraction page.</summary>
public readonly struct UsdPhysicsExtractionRelationship
    : IEquatable<UsdPhysicsExtractionRelationship>
{
    private const int Section = PhysicsExtractAbi.SectionRelationships;

    private readonly UsdPhysicsExtractionPage? _page;
    private readonly int _index;

    internal UsdPhysicsExtractionRelationship(UsdPhysicsExtractionPage page, int index)
    {
        _page = page;
        _index = index;
    }

    /// <summary>Gets the zero based index of this relationship inside its page.</summary>
    public int Index => _index;

    /// <summary>Gets the namespace neutral canonical key.</summary>
    public UsdPhysicsExtractionKey Key => (UsdPhysicsExtractionKey)Page.FieldU32(Section, _index, 0);

    /// <summary>Gets the verbatim authored relationship name.</summary>
    public string Name => Page.FieldText(Section, _index, 4);

    /// <summary>Gets the first target index that belongs to this relationship.</summary>
    public int TargetStart => (int)Page.FieldU32(Section, _index, 8);

    /// <summary>Gets how many targets belong to this relationship.</summary>
    public int TargetCount => (int)Page.FieldU32(Section, _index, 12);

    /// <summary>Gets how the relationship was authored and resolved.</summary>
    public UsdPhysicsExtractionPropertyTraits Flags =>
        (UsdPhysicsExtractionPropertyTraits)Page.FieldU32(Section, _index, 16);

    private UsdPhysicsExtractionPage Page =>
        _page ?? throw new InvalidOperationException(
            "This extraction relationship view was not bound to a page.");

    /// <summary>Compares two views for equality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when both views name the same record.</returns>
    public static bool operator ==(
        UsdPhysicsExtractionRelationship left,
        UsdPhysicsExtractionRelationship right) => left.Equals(right);

    /// <summary>Compares two views for inequality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when the views name different records.</returns>
    public static bool operator !=(
        UsdPhysicsExtractionRelationship left,
        UsdPhysicsExtractionRelationship right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsExtractionRelationship other) =>
        ReferenceEquals(_page, other._page) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsExtractionRelationship other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_page, _index);
}

/// <summary>One relationship target read directly out of an extraction page.</summary>
public readonly struct UsdPhysicsExtractionTarget : IEquatable<UsdPhysicsExtractionTarget>
{
    private const int Section = PhysicsExtractAbi.SectionTargets;

    private readonly UsdPhysicsExtractionPage? _page;
    private readonly int _index;

    internal UsdPhysicsExtractionTarget(UsdPhysicsExtractionPage page, int index)
    {
        _page = page;
        _index = index;
    }

    /// <summary>Gets the zero based index of this target inside its page.</summary>
    public int Index => _index;

    /// <summary>Gets the content hash of the target path.</summary>
    public ulong TargetId => Page.FieldU64(Section, _index, 0);

    /// <summary>Gets the absolute target path.</summary>
    public string Path => Page.FieldText(Section, _index, 8);

    /// <summary>Gets the object index the target resolves to, or <c>-1</c>.</summary>
    public int ObjectIndex => Page.FieldI32(Section, _index, 12);

    private UsdPhysicsExtractionPage Page =>
        _page ?? throw new InvalidOperationException(
            "This extraction target view was not bound to a page.");

    /// <summary>Compares two views for equality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when both views name the same record.</returns>
    public static bool operator ==(
        UsdPhysicsExtractionTarget left, UsdPhysicsExtractionTarget right) => left.Equals(right);

    /// <summary>Compares two views for inequality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when the views name different records.</returns>
    public static bool operator !=(
        UsdPhysicsExtractionTarget left, UsdPhysicsExtractionTarget right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsExtractionTarget other) =>
        ReferenceEquals(_page, other._page) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsExtractionTarget other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_page, _index);
}

/// <summary>One ordered extraction diagnostic read directly out of an extraction page.</summary>
public readonly struct UsdPhysicsExtractionDiagnostic
    : IEquatable<UsdPhysicsExtractionDiagnostic>
{
    private const int Section = PhysicsExtractAbi.SectionDiagnostics;

    private readonly UsdPhysicsExtractionPage? _page;
    private readonly int _index;

    internal UsdPhysicsExtractionDiagnostic(UsdPhysicsExtractionPage page, int index)
    {
        _page = page;
        _index = index;
    }

    /// <summary>Gets the zero based index of this diagnostic inside its page.</summary>
    public int Index => _index;

    /// <summary>Gets how serious the diagnostic is.</summary>
    public UsdPhysicsExtractionSeverity Severity =>
        (UsdPhysicsExtractionSeverity)Page.FieldU32(Section, _index, 0);

    /// <summary>Gets the concern the diagnostic describes.</summary>
    public UsdPhysicsExtractionCategory Category =>
        (UsdPhysicsExtractionCategory)Page.FieldU32(Section, _index, 4);

    /// <summary>Gets the specific diagnostic identity.</summary>
    public UsdPhysicsExtractionCode Code =>
        (UsdPhysicsExtractionCode)Page.FieldU32(Section, _index, 8);

    /// <summary>Gets the object index the diagnostic names, or <c>-1</c> for the stage.</summary>
    public int ObjectIndex => Page.FieldI32(Section, _index, 12);

    /// <summary>Gets the human readable message.</summary>
    public string Message => Page.FieldText(Section, _index, 16);

    /// <summary>Gets the canonical key the diagnostic names, when it names one.</summary>
    public UsdPhysicsExtractionKey Key =>
        (UsdPhysicsExtractionKey)Page.FieldU32(Section, _index, 20);

    /// <summary>Gets the stable identity of the object the diagnostic names, or zero.</summary>
    public ulong ObjectId => Page.FieldU64(Section, _index, 24);

    private UsdPhysicsExtractionPage Page =>
        _page ?? throw new InvalidOperationException(
            "This extraction diagnostic view was not bound to a page.");

    /// <summary>Compares two views for equality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when both views name the same record.</returns>
    public static bool operator ==(
        UsdPhysicsExtractionDiagnostic left,
        UsdPhysicsExtractionDiagnostic right) => left.Equals(right);

    /// <summary>Compares two views for inequality.</summary>
    /// <param name="left">The left view.</param>
    /// <param name="right">The right view.</param>
    /// <returns><see langword="true"/> when the views name different records.</returns>
    public static bool operator !=(
        UsdPhysicsExtractionDiagnostic left,
        UsdPhysicsExtractionDiagnostic right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsExtractionDiagnostic other) =>
        ReferenceEquals(_page, other._page) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsExtractionDiagnostic other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_page, _index);
}
