// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using OpenUsd.Physics.Schema;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Locks the generated <c>openUsdPhysics</c> managed facades to the generated precedence manifest so a
/// hand edit to either side, or a stale regeneration, fails the build instead of drifting silently.
/// </summary>
public sealed class OpenUsdPhysicsSchemaFacadeTests
{
    private static readonly JsonDocumentOptions ManifestOptions = new() { AllowTrailingCommas = false };

    [Test]
    public async Task TokenConstantsCoverEveryManifestPropertyExactly()
    {
        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonElement property in ManifestProperties())
        {
            expected.Add(property.GetProperty("property").GetString()!);
        }

        var actual = new SortedSet<string>(StringComparer.Ordinal);
        foreach (FieldInfo field in TokenConstants())
        {
            string value = (string)field.GetRawConstantValue()!;
            if (value.StartsWith("openUsdPhysics:", StringComparison.Ordinal))
            {
                actual.Add(value);
            }
        }

        await Assert.That(actual).IsEquivalentTo(expected)
            .Because("OpenUsdPhysicsTokens must expose exactly the properties in precedence-manifest.json");
        await Assert.That(expected.Count).IsEqualTo(351);
    }

    [Test]
    public async Task TokenConstantNamesFollowTheGroupAndLeafConvention()
    {
        foreach (JsonElement property in ManifestProperties())
        {
            string path = property.GetProperty("property").GetString()!;
            string[] segments = path.Split(':');
            string constantName = PascalCase(segments[1]) + PascalCase(segments[2]);

            FieldInfo? field = typeof(OpenUsdPhysicsTokens).GetField(
                constantName, BindingFlags.Public | BindingFlags.Static);

            await Assert.That(field).IsNotNull()
                .Because($"OpenUsdPhysicsTokens.{constantName} should carry '{path}'");
            await Assert.That((string)field!.GetRawConstantValue()!).IsEqualTo(path);
        }
    }

    [Test]
    public async Task EveryManifestPropertyHasAFacadeMember()
    {
        foreach (JsonElement property in ManifestProperties())
        {
            string path = property.GetProperty("property").GetString()!;
            string schema = property.GetProperty("schema").GetString()!;
            string member = PascalCase(path.Split(':')[2]);
            Type facade = FacadeType(schema);

            if (property.GetProperty("type").GetString() == "rel")
            {
                await Assert.That(facade.GetMethod("Get" + member, Type.EmptyTypes)).IsNotNull()
                    .Because($"{schema}.Get{member}() should read the '{path}' relationship");
                await Assert.That(facade.GetMethod("Set" + member)).IsNotNull();
                await Assert.That(facade.GetMethod("Clear" + member, Type.EmptyTypes)).IsNotNull();
            }
            else
            {
                PropertyInfo? info = facade.GetProperty(member);
                await Assert.That(info).IsNotNull()
                    .Because($"{schema}.{member} should read and write '{path}'");
                await Assert.That(info!.CanRead).IsTrue();
                await Assert.That(info.CanWrite).IsTrue();
            }
        }
    }

    [Test]
    public async Task EveryManifestSchemaHasAFacadeWithAMatchingIdentifier()
    {
        var schemas = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonElement property in ManifestProperties())
        {
            schemas.Add(property.GetProperty("schema").GetString()!);
        }

        await Assert.That(schemas.Count).IsEqualTo(40);

        foreach (string schema in schemas)
        {
            Type facade = FacadeType(schema);
            FieldInfo? identifier = facade.GetField(
                "SchemaIdentifier", BindingFlags.Public | BindingFlags.Static);

            await Assert.That(identifier).IsNotNull().Because($"{schema} should declare SchemaIdentifier");
            await Assert.That((string)identifier!.GetRawConstantValue()!).IsEqualTo(schema);
            await Assert.That(facade.IsValueType).IsTrue().Because("facades are allocation-free wrappers");
            await Assert.That(facade.GetProperty("Prim")).IsNotNull();
        }
    }

    [Test]
    public async Task ApiFacadesExposeHasAndTypedFacadesExposeIsAAndDefine()
    {
        var kinds = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement property in ManifestProperties())
        {
            kinds[property.GetProperty("schema").GetString()!] =
                property.GetProperty("schemaKind").GetString()!;
        }

        foreach ((string schema, string kind) in kinds)
        {
            Type facade = FacadeType(schema);
            await Assert.That(facade.GetMethod("Wrap")).IsNotNull();

            if (kind == "concreteTyped")
            {
                await Assert.That(facade.GetMethod("IsA")).IsNotNull()
                    .Because($"{schema} is a concrete typed prim schema");
                await Assert.That(facade.GetMethod("Define")).IsNotNull();
            }
            else
            {
                await Assert.That(facade.GetMethod("Has")).IsNotNull()
                    .Because($"{schema} is an API schema");
            }
        }
    }

    [Test]
    public async Task EmbeddedResourcesMatchTheGeneratedPluginFiles()
    {
        string resources = Path.Combine(
            FindRepositoryRoot(), "schemas", "openUsdPhysics", "resources");

        await Assert.That(OpenUsdPhysicsSchemaResources.ReadPlugInfo())
            .IsEqualTo(File.ReadAllText(Path.Combine(resources, "plugInfo.json")))
            .Because("the embedded plugInfo.json must be byte-identical to the generated artifact");
        await Assert.That(OpenUsdPhysicsSchemaResources.ReadGeneratedSchema())
            .IsEqualTo(File.ReadAllText(Path.Combine(resources, "generatedSchema.usda")))
            .Because("the embedded generatedSchema.usda must be byte-identical to the generated artifact");
    }

    [Test]
    public async Task ExtractPluginToWritesTheDiscoveryLayout()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "openUsdPhysics-extract-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        try
        {
            string extracted = OpenUsdPhysicsSchemaResources.ExtractPluginTo(root);

            await Assert.That(extracted).IsEqualTo(
                Path.Combine(root, "openUsdPhysics", "resources"))
                .Because("PXR_PLUGINPATH_NAME must point at the directory that holds plugInfo.json");
            await Assert.That(File.Exists(Path.Combine(extracted, "plugInfo.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(extracted, "generatedSchema.usda"))).IsTrue();
            await Assert.That(File.ReadAllText(Path.Combine(extracted, "plugInfo.json")))
                .IsEqualTo(OpenUsdPhysicsSchemaResources.ReadPlugInfo());
            await Assert.That(File.ReadAllText(Path.Combine(extracted, "generatedSchema.usda")))
                .IsEqualTo(OpenUsdPhysicsSchemaResources.ReadGeneratedSchema());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ExtractPluginToRejectsABlankDirectory()
    {
        await Assert.That(() => OpenUsdPhysicsSchemaResources.ExtractPluginTo(" "))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PluginNameAndNamespaceStayAlignedWithTheGeneratedTokens()
    {
        await Assert.That(OpenUsdPhysicsSchemaResources.PluginName).IsEqualTo("openUsdPhysics");
        await Assert.That(OpenUsdPhysicsTokens.PropertyNamespace).IsEqualTo("openUsdPhysics");
        await Assert.That(OpenUsdPhysicsSchemaResources.ReadPlugInfo())
            .Contains("\"openUsdPhysics\"")
            .Because("the embedded plugInfo.json registers the project-owned plugin name");
    }

    [Test]
    public async Task ManifestRanksOpenUsdPhysicsAboveForeignOpinions()
    {
        using JsonDocument manifest = ReadManifest();
        JsonElement namespaces = manifest.RootElement.GetProperty("namespaces");
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (JsonElement entry in namespaces.EnumerateArray())
        {
            string name = entry.GetProperty("namespace").GetString()!;
            ranks[name] = entry.GetProperty("rank").GetInt32();
            sources[name] = entry.GetProperty("source").GetString()!;
        }

        await Assert.That(ranks["openUsdPhysics"]).IsEqualTo(0);
        await Assert.That(sources["openUsdPhysics"]).IsEqualTo("project");
        await Assert.That(ranks["physx"]).IsGreaterThan(ranks["openUsdPhysics"])
            .Because("foreign physx opinions are a weaker optional raw input");
        await Assert.That(sources["physx"]).IsEqualTo("foreign")
            .Because("no NVIDIA PhysxSchema definition is vendored by this repository");
        await Assert.That(ranks["physics"]).IsGreaterThan(ranks["physx"]);
    }

    [Test]
    public async Task ManifestReportsEveryAgreedDomainAsCovered()
    {
        using JsonDocument manifest = ReadManifest();

        foreach (JsonElement gap in manifest.RootElement.GetProperty("gaps").EnumerateArray())
        {
            string domain = gap.GetProperty("domain").GetString()!;
            await Assert.That(gap.GetProperty("covered").GetBoolean()).IsTrue()
                .Because($"domain '{domain}' must be representable by openUsdPhysics:* schemas");
            await Assert.That(gap.GetProperty("reason").GetString()).IsNotNull();
        }
    }

    private static IEnumerable<JsonElement> ManifestProperties()
    {
        using JsonDocument manifest = ReadManifest();
        foreach (JsonElement property in manifest.RootElement.GetProperty("properties").EnumerateArray())
        {
            yield return property.Clone();
        }
    }

    private static JsonDocument ReadManifest()
    {
        string path = Path.Combine(
            FindRepositoryRoot(), "schemas", "openUsdPhysics", "precedence-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(path), ManifestOptions);
    }

    private static IEnumerable<FieldInfo> TokenConstants() =>
        typeof(OpenUsdPhysicsTokens)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string));

    private static Type FacadeType(string schema) =>
        typeof(OpenUsdPhysicsTokens).Assembly.GetType(
            "OpenUsd.Physics.Schema." + schema, throwOnError: true)!;

    private static string PascalCase(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output path.");
    }
}
