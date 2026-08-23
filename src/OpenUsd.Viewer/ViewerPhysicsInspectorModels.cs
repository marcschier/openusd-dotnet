// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Physics;

namespace OpenUsd.Viewer;

/// <summary>Classifies whether the inspector may author one physics property.</summary>
internal enum ViewerPhysicsAuthorability
{
    /// <summary>The property can be authored.</summary>
    Editable,

    /// <summary>The built world does not simulate the domain the property belongs to.</summary>
    UnsupportedCapability,

    /// <summary>The managed runtime cannot round-trip the property's value type.</summary>
    UnsupportedType,

    /// <summary>The property describes simulation output rather than an authored input.</summary>
    Derived,
}

/// <summary>
/// The stock <c>UsdPhysics</c> properties the inspector knows how to label and, where the managed
/// runtime can round-trip the value type, author.
/// </summary>
/// <remarks>
/// <para>
/// The project-owned <c>openUsdPhysics</c> schema is generated with value types the managed runtime
/// round-trips on purpose, so every scalar property it declares is authorable. The stock schema is
/// not: its masses, frictions, break forces, joint limits, and joint drives are <c>float</c>, its
/// centres of mass and joint frames are <c>point3f</c> and <c>quatf</c>, and its velocities are
/// <c>vector3f</c> - the scalar ABI matches value types exactly and carries none of those. Those
/// rows are still listed, with their extracted values and the exact reason they are read only,
/// because hiding them would tell the user the scene has no such setting.
/// </para>
/// <para>
/// Multiple-apply joint limits and drives cannot be spelled as fixed names - the instance is part
/// of the property name - so they are matched by prefix and leaf exactly the way the native
/// extractor matches them.
/// </para>
/// </remarks>
internal static class ViewerPhysicsCoreProperties
{
    /// <summary>The reason a stock property that the scalar ABI cannot carry is read only.</summary>
    internal const string UnsupportedTypeDetail =
        "The managed runtime carries bool, int64, double, string, token, and float3 scalars only. " +
        "This property's exact value type is not one of them, so the inspector shows the extracted " +
        "value but cannot author it.";

    private static readonly Dictionary<string, ViewerPhysicsCoreProperty> Table = Build();

    /// <summary>Looks up the stock description of one property name.</summary>
    /// <param name="name">The authored property name, including any instance segment.</param>
    /// <returns>The description, or <see langword="null"/> when the name is not stock physics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    internal static ViewerPhysicsCoreProperty? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Table.TryGetValue(name, out ViewerPhysicsCoreProperty? exact))
        {
            return exact;
        }

        return FindMultiApply(name);
    }

    private static ViewerPhysicsCoreProperty? FindMultiApply(string name)
    {
        (string Prefix, string Leaf, string Label, string Documentation)[] entries =
        [
            ("drive:", "physics:type",
                "Drive Type",
                "Whether the drive applies a force or an acceleration."),
            ("drive:", "physics:stiffness",
                "Drive Stiffness",
                "Spring stiffness pulling the axis toward its target position."),
            ("drive:", "physics:damping",
                "Drive Damping",
                "Damping pulling the axis toward its target velocity."),
            ("drive:", "physics:maxForce",
                "Drive Max Force",
                "Upper bound on the force or acceleration the drive may apply."),
            ("drive:", "physics:targetPosition",
                "Drive Target Position",
                "Position the drive pulls the axis toward."),
            ("drive:", "physics:targetVelocity",
                "Drive Target Velocity",
                "Velocity the drive pulls the axis toward."),
            ("limit:", "physics:low",
                "Limit Low",
                "Lower bound of the limited joint axis."),
            ("limit:", "physics:high",
                "Limit High",
                "Upper bound of the limited joint axis."),
        ];

        for (int index = 0; index < entries.Length; index++)
        {
            (string prefix, string leaf, string label, string documentation) = entries[index];
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name.EndsWith(leaf, StringComparison.Ordinal))
            {
                continue;
            }

            string instance = name[prefix.Length..^leaf.Length].Trim(':');
            string qualified = instance.Length == 0
                ? label
                : string.Create(CultureInfo.InvariantCulture, $"{label} ({instance})");
            bool isToken = leaf.EndsWith("type", StringComparison.Ordinal);

            // Joint drives and limits are float, which the exact-match scalar ABI does not carry.
            return new ViewerPhysicsCoreProperty(
                qualified,
                documentation,
                isToken ? ViewerPhysicsValueKind.Token : ViewerPhysicsValueKind.Number,
                isToken ? ["force", "acceleration"] : [],
                UsdPhysicsCapability.Articulations,
                IsAuthorable: false);
        }

        return null;
    }

    private static Dictionary<string, ViewerPhysicsCoreProperty> Build()
    {
        var table = new Dictionary<string, ViewerPhysicsCoreProperty>(StringComparer.Ordinal);

        void add(
            string name,
            string label,
            string documentation,
            ViewerPhysicsValueKind kind,
            UsdPhysicsCapability capability,
            bool authorable,
            params string[] tokens) =>
            table[name] = new ViewerPhysicsCoreProperty(
                label, documentation, kind, tokens, capability, authorable);

        add("physics:gravityDirection", "Gravity Direction",
            "Direction gravity pulls in, in stage space.",
            ViewerPhysicsValueKind.Vector3, UsdPhysicsCapability.None, false);
        add("physics:gravityMagnitude", "Gravity Magnitude",
            "Gravity strength in stage linear units per second squared.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.None, false);

        add("physics:rigidBodyEnabled", "Rigid Body Enabled",
            "Whether the body participates in the simulation at all.",
            ViewerPhysicsValueKind.Bool, UsdPhysicsCapability.RigidBodies, true);
        add("physics:kinematicEnabled", "Kinematic",
            "Whether the body is driven by authored motion instead of by the solver.",
            ViewerPhysicsValueKind.Bool, UsdPhysicsCapability.RigidBodies, true);
        add("physics:startsAsleep", "Starts Asleep",
            "Whether the body begins the simulation asleep.",
            ViewerPhysicsValueKind.Bool, UsdPhysicsCapability.RigidBodies, true);
        add("physics:velocity", "Linear Velocity",
            "Initial linear velocity, in stage linear units per second.",
            ViewerPhysicsValueKind.Vector3, UsdPhysicsCapability.RigidBodies, false);
        add("physics:angularVelocity", "Angular Velocity",
            "Initial angular velocity, in degrees per second.",
            ViewerPhysicsValueKind.Vector3, UsdPhysicsCapability.RigidBodies, false);
        add("physics:mass", "Mass",
            "Body mass in stage mass units.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.RigidBodies, false);
        add("physics:density", "Density",
            "Density used to derive a mass that is not authored directly.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.RigidBodies, false);
        add("physics:centerOfMass", "Center Of Mass",
            "Centre of mass in body local space.",
            ViewerPhysicsValueKind.Vector3, UsdPhysicsCapability.RigidBodies, false);
        add("physics:diagonalInertia", "Diagonal Inertia",
            "Diagonalised inertia tensor in body local space.",
            ViewerPhysicsValueKind.Vector3, UsdPhysicsCapability.RigidBodies, true);
        add("physics:principalAxes", "Principal Axes",
            "Rotation from body local space into the principal inertia frame.",
            ViewerPhysicsValueKind.Unsupported, UsdPhysicsCapability.RigidBodies, false);

        add("physics:collisionEnabled", "Collision Enabled",
            "Whether the collider collides at all.",
            ViewerPhysicsValueKind.Bool, UsdPhysicsCapability.RigidBodies, true);
        add("physics:approximation", "Mesh Approximation",
            "How a mesh collider is approximated for collision.",
            ViewerPhysicsValueKind.Token, UsdPhysicsCapability.RigidBodies, true,
            "none", "convexDecomposition", "convexHull", "boundingSphere", "boundingCube",
            "meshSimplification");
        add("physics:invertFilteredGroups", "Invert Filtered Groups",
            "Whether the collision group's filter list is an allow list instead of a deny list.",
            ViewerPhysicsValueKind.Bool, UsdPhysicsCapability.RigidBodies, true);
        add("physics:mergeGroup", "Merge Group",
            "Name of the merge group this collision group joins.",
            ViewerPhysicsValueKind.Text, UsdPhysicsCapability.RigidBodies, true);

        add("physics:staticFriction", "Static Friction",
            "Friction coefficient applied while the contact is not sliding.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.RigidBodies, false);
        add("physics:dynamicFriction", "Dynamic Friction",
            "Friction coefficient applied while the contact slides.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.RigidBodies, false);
        add("physics:restitution", "Restitution",
            "How much relative normal speed a contact returns.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.RigidBodies, false);

        add("physics:jointEnabled", "Joint Enabled",
            "Whether the joint constrains its bodies.",
            ViewerPhysicsValueKind.Bool, UsdPhysicsCapability.Articulations, true);
        add("physics:excludeFromArticulation", "Exclude From Articulation",
            "Whether the joint is a maximal-coordinate joint rather than an articulation joint.",
            ViewerPhysicsValueKind.Bool, UsdPhysicsCapability.Articulations, true);
        add("physics:axis", "Joint Axis",
            "Axis the joint rotates or slides along.",
            ViewerPhysicsValueKind.Token, UsdPhysicsCapability.Articulations, true,
            "X", "Y", "Z");
        add("physics:breakForce", "Break Force",
            "Linear force above which the joint breaks.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:breakTorque", "Break Torque",
            "Torque above which the joint breaks.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:lowerLimit", "Lower Limit",
            "Lower bound of the joint's limited axis.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:upperLimit", "Upper Limit",
            "Upper bound of the joint's limited axis.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:minDistance", "Minimum Distance",
            "Shortest separation a distance joint allows.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:maxDistance", "Maximum Distance",
            "Longest separation a distance joint allows.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:coneAngle0Limit", "Cone Angle 0 Limit",
            "First cone half-angle a spherical joint allows.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:coneAngle1Limit", "Cone Angle 1 Limit",
            "Second cone half-angle a spherical joint allows.",
            ViewerPhysicsValueKind.Number, UsdPhysicsCapability.Articulations, false);
        add("physics:localPos0", "Local Frame 0 Position",
            "Joint frame position on the first body, in that body's local space.",
            ViewerPhysicsValueKind.Vector3, UsdPhysicsCapability.Articulations, false);
        add("physics:localPos1", "Local Frame 1 Position",
            "Joint frame position on the second body, in that body's local space.",
            ViewerPhysicsValueKind.Vector3, UsdPhysicsCapability.Articulations, false);
        add("physics:localRot0", "Local Frame 0 Rotation",
            "Joint frame rotation on the first body.",
            ViewerPhysicsValueKind.Unsupported, UsdPhysicsCapability.Articulations, false);
        add("physics:localRot1", "Local Frame 1 Rotation",
            "Joint frame rotation on the second body.",
            ViewerPhysicsValueKind.Unsupported, UsdPhysicsCapability.Articulations, false);
        return table;
    }
}

/// <summary>One stock physics property the inspector knows how to present.</summary>
/// <param name="Label">The label the inspector shows.</param>
/// <param name="Documentation">The sentence describing what the property does.</param>
/// <param name="Kind">The value the property carries.</param>
/// <param name="Tokens">The tokens a token property accepts, or an empty list.</param>
/// <param name="RequiredCapability">The capability a built world must report.</param>
/// <param name="IsAuthorable">Whether the managed runtime can round-trip the value type.</param>
internal sealed record ViewerPhysicsCoreProperty(
    string Label,
    string Documentation,
    ViewerPhysicsValueKind Kind,
    IReadOnlyList<string> Tokens,
    UsdPhysicsCapability RequiredCapability,
    bool IsAuthorable);

/// <summary>One property row the physics inspector shows for one simulated object.</summary>
/// <param name="PrimPath">The prim the property is authored on.</param>
/// <param name="Name">The authored property name.</param>
/// <param name="Label">The label the inspector shows.</param>
/// <param name="Documentation">The sentence describing what the property does.</param>
/// <param name="Kind">The value the property carries.</param>
/// <param name="Tokens">The tokens a token property accepts, or an empty list.</param>
/// <param name="ValueText">The extracted value, formatted for display.</param>
/// <param name="Source">Which schema opinion the extracted value came from.</param>
/// <param name="Authorability">Whether the inspector may author the property, and why not.</param>
/// <param name="Detail">The sentence explaining the row's state.</param>
internal sealed record ViewerPhysicsPropertyRow(
    string PrimPath,
    string Name,
    string Label,
    string Documentation,
    ViewerPhysicsValueKind Kind,
    IReadOnlyList<string> Tokens,
    string ValueText,
    string Source,
    ViewerPhysicsAuthorability Authorability,
    string Detail)
{
    /// <summary>Gets a value indicating whether the inspector may author this row.</summary>
    internal bool IsEditable => Authorability == ViewerPhysicsAuthorability.Editable;

    /// <summary>Gets the one-line state shown beside the value.</summary>
    internal string StatusText => Authorability switch
    {
        ViewerPhysicsAuthorability.Editable => Source,
        ViewerPhysicsAuthorability.UnsupportedCapability => "not simulated",
        ViewerPhysicsAuthorability.UnsupportedType => "read only",
        _ => "derived",
    };
}

/// <summary>What kinds of runtime command one extracted object can actually receive.</summary>
/// <remarks>
/// <para>
/// The retained world dispatches a command by looking its target identity up in the map that
/// matches the command: a move goes to the controller map, a driver input to the vehicle map, and
/// everything else to the actor map. An identity that is real but lives in the wrong map is refused
/// with a per-object diagnostic, so offering the operator a control that can only ever be refused is
/// a lie the viewer can avoid telling. This flag set is what the interaction controls gate on.
/// </para>
/// <para>
/// The flags describe the object, not the stage: a joint, a material, or a tendon composes into
/// something the solver reads but nothing a command can address, and those sections honestly offer
/// no interaction at all.
/// </para>
/// </remarks>
[Flags]
internal enum ViewerPhysicsCommandability
{
    /// <summary>The object receives no runtime command.</summary>
    None = 0,

    /// <summary>Forces, torques, wake, sleep, clears, and interactive dragging.</summary>
    Body = 1,

    /// <summary>Character controller move commands.</summary>
    Controller = 2,

    /// <summary>Vehicle driver input.</summary>
    Vehicle = 4,

    /// <summary>Scene-wide gravity.</summary>
    Scene = 8,

    /// <summary>Impulses and angular impulses.</summary>
    /// <remarks>
    /// An impulse is a separate flag from <see cref="Body"/> because a reduced-coordinate
    /// articulation link takes forces but not impulses: PhysX documents that the impulse and
    /// velocity-change force modes "can not be applied to articulation links". A link therefore
    /// offers force, torque, wake, sleep, clear, and drag, and honestly refuses impulses.
    /// </remarks>
    Impulse = 16,
}

/// <summary>One inspector section describing a simulated object's properties.</summary>
/// <param name="ObjectId">The extractor's stable identity for the object.</param>
/// <param name="PrimPath">The prim the section describes.</param>
/// <param name="Kind">The extracted object kind.</param>
/// <param name="Detail">The sentence describing the object's simulated state.</param>
/// <param name="Diagnostics">Every diagnostic the extractor reported for this object.</param>
/// <param name="Rows">The property rows, ordered by name.</param>
/// <param name="TargetId">The retained world's identity for the object commands must address.</param>
/// <param name="TargetPath">The authored prim the target identity was composed from.</param>
/// <param name="Commandability">Which runtime commands the target accepts.</param>
/// <remarks>
/// <see cref="ObjectId"/> and <see cref="TargetId"/> are identities in two different spaces and are
/// never interchangeable. The extractor hashes a path and an object type so that one prim's several
/// records stay distinguishable, which is exactly what selection anchoring needs; the composer
/// hashes the composed object's address so the solver can find it, which is what a command needs.
/// Comparing one to the other always fails, so each is used only for the job it names.
/// </remarks>
internal sealed record ViewerPhysicsObjectSection(
    ulong ObjectId,
    string PrimPath,
    string Kind,
    string Detail,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<ViewerPhysicsPropertyRow> Rows,
    ulong TargetId = 0UL,
    string TargetPath = "",
    ViewerPhysicsCommandability Commandability = ViewerPhysicsCommandability.None)
{
    /// <summary>Reports whether the object accepts one kind of runtime command.</summary>
    /// <param name="required">The command family the interaction needs.</param>
    internal bool Accepts(ViewerPhysicsCommandability required) =>
        TargetId != 0UL && (Commandability & required) == required && required != 0;

    /// <summary>Gets the number of rows the inspector may author.</summary>
    internal int EditableCount
    {
        get
        {
            var count = 0;
            for (int index = 0; index < Rows.Count; index++)
            {
                if (Rows[index].IsEditable)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets the header the inspector shows for the section.</summary>
    internal string Header => string.Create(
        CultureInfo.InvariantCulture,
        $"{PrimPath} · {Kind} · {EditableCount}/{Rows.Count} editable");
}

/// <summary>Decides whether authoring one physics property can change simulated behaviour.</summary>
/// <remarks>
/// <para>
/// The answer comes from the generated catalog rather than from a hand-maintained list, so a schema
/// added later is classified correctly without anyone remembering to update this. Only the
/// simulation metadata domain is neutral: it records provenance about a simulation and is never an
/// input to one.
/// </para>
/// <para>
/// Everything the catalog does not describe is treated as relevant. Rebuilding a world that did not
/// need it costs time; continuing to simulate a world the stage no longer describes silently shows
/// the user something that is not true.
/// </para>
/// </remarks>
internal static class ViewerPhysicsAuthoringClassifier
{
    /// <summary>The catalog domain whose properties never change simulated behaviour.</summary>
    internal const string NeutralDomain = "Metadata";

    /// <summary>Reports whether authoring one property leaves the built world valid.</summary>
    /// <param name="name">The authored property name.</param>
    /// <returns><see langword="true"/> when the property cannot change the simulation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    internal static bool IsSimulationNeutral(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ViewerPhysicsSchemaProjection.FindProperty(name) is { } descriptor &&
            string.Equals(descriptor.Domain, NeutralDomain, StringComparison.Ordinal);
    }
}

/// <summary>Decides whether the inspector may author one extracted property.</summary>
/// <remarks>
/// Every refusal names its own cause. "Not editable" on its own is indistinguishable from a bug, so
/// a row that cannot be authored says whether the world does not simulate its domain, whether the
/// runtime cannot carry its value type, or whether the value is derived rather than authored.
/// </remarks>
internal static class ViewerPhysicsEditability
{
    /// <summary>The property namespace of every project-owned physics property.</summary>
    internal const string ProjectNamespace = "openUsdPhysics:";

    /// <summary>Classifies one property.</summary>
    /// <param name="name">The authored property name.</param>
    /// <param name="kind">The value the property carries.</param>
    /// <param name="requiredCapability">The capability a built world must report.</param>
    /// <param name="features">The capabilities the built world reports.</param>
    /// <param name="isAuthorable">Whether the managed runtime can round-trip the value type.</param>
    /// <returns>Whether the row is editable, and the sentence explaining why not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    internal static (ViewerPhysicsAuthorability Authorability, string Detail) Classify(
        string name,
        ViewerPhysicsValueKind kind,
        UsdPhysicsCapability requiredCapability,
        UsdPhysicsCapability features,
        bool isAuthorable)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (kind == ViewerPhysicsValueKind.Unsupported || !isAuthorable)
        {
            return (
                ViewerPhysicsAuthorability.UnsupportedType,
                ViewerPhysicsCoreProperties.UnsupportedTypeDetail);
        }

        if (requiredCapability != UsdPhysicsCapability.None &&
            (features & requiredCapability) == 0)
        {
            string reason = string.Create(
                CultureInfo.InvariantCulture,
                $"The built world does not simulate {requiredCapability}.");
            return (
                ViewerPhysicsAuthorability.UnsupportedCapability,
                reason + " Authoring this property would change the file without changing what " +
                "you are watching.");
        }

        return (
            ViewerPhysicsAuthorability.Editable,
            name.StartsWith(ProjectNamespace, StringComparison.Ordinal)
                ? "Authored into the session overlay's user layer."
                : "Authored into the session overlay's user layer over the stock opinion.");
    }
}
