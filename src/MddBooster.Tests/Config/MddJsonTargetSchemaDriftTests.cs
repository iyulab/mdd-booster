using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MddBooster.Cli.Config;

namespace MddBooster.Tests.Config;

/// <summary>
/// <see cref="MddJsonTarget"/> and <c>schemas/mdd.schema.json</c> are two independent,
/// hand-maintained sources of truth for the same shape — the C# class as one flat type
/// covering every target kind, the schema split into four <c>$defs</c> (one per
/// <c>type</c>). Nothing forces a property added to one to appear in the other; today
/// only <c>MddSchemaTests.Published_sample_validates_against_the_schema</c> exercises a
/// single fixed sample, which would keep passing even if a brand-new property existed on
/// only one side.
/// </summary>
/// <remarks>
/// Reads the published schema itself (via the same <c>contract/mdd.schema.json</c> link
/// <see cref="MddSchemaTests"/> uses), not a copy declared here — a copy would be the
/// same drift risk one level up.
/// </remarks>
public sealed class MddJsonTargetSchemaDriftTests
{
    private static readonly string SchemaPath =
        Path.Combine(AppContext.BaseDirectory, "contract", "mdd.schema.json");

    private static IReadOnlySet<string> JsonPropertyNamesOnMddJsonTarget() =>
        typeof(MddJsonTarget)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Union of every property named under the four target <c>$defs</c>
    /// (sqlTarget/modelTarget/apiTarget/typeScriptTarget) — the schema's side of the
    /// same shape <see cref="MddJsonTarget"/> models as one flat class.
    /// </summary>
    private static IReadOnlySet<string> PropertyNamesAcrossTargetDefs()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var defs = doc.RootElement.GetProperty("$defs");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var defName in new[] { "sqlTarget", "modelTarget", "apiTarget", "typeScriptTarget" })
        {
            foreach (var prop in defs.GetProperty(defName).GetProperty("properties").EnumerateObject())
                names.Add(prop.Name);
        }
        return names;
    }

    [Fact]
    public void Every_MddJsonTarget_property_is_declared_on_some_target_in_the_schema()
    {
        var codeOnly = JsonPropertyNamesOnMddJsonTarget()
            .Except(PropertyNamesAcrossTargetDefs())
            .ToList();

        Assert.True(codeOnly.Count == 0,
            "MddJsonTarget declares propert" + (codeOnly.Count == 1 ? "y" : "ies") +
            $" the schema's four target $defs never mention: {string.Join(", ", codeOnly)}. " +
            "A consumer using it would be silently accepted by the CLI but rejected (or " +
            "silently stripped) by anything validating against schemas/mdd.schema.json — " +
            "add it to the matching $def(s), or remove it from MddJsonTarget if it's dead.");
    }

    [Fact]
    public void Every_schema_target_property_has_a_matching_MddJsonTarget_property()
    {
        var schemaOnly = PropertyNamesAcrossTargetDefs()
            .Except(JsonPropertyNamesOnMddJsonTarget())
            .ToList();

        Assert.True(schemaOnly.Count == 0,
            "schemas/mdd.schema.json declares propert" + (schemaOnly.Count == 1 ? "y" : "ies") +
            $" MddJsonTarget has no [JsonPropertyName] for: {string.Join(", ", schemaOnly)}. " +
            "A config using it would validate against the schema but ConfigLoader would " +
            "silently ignore it at build time — the exact failure class this schema exists " +
            "to catch (see MddSchemaTests), just on the class side instead of the config side.");
    }

    /// <summary>
    /// The same two-way check at the root: <see cref="MddJsonConfig"/>'s own keys against the schema's
    /// top-level <c>properties</c> (which also names the optional <c>$schema</c> pointer the class
    /// does not model). The target check alone let root keys drift — the schema's root is
    /// <c>additionalProperties:false</c>, so a root key missing from it is flagged by every editor
    /// that validates the file.
    /// </summary>
    [Fact]
    public void The_root_keys_of_MddJsonConfig_and_the_schema_agree()
    {
        var code = typeof(MddJsonConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToHashSet(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var schema = doc.RootElement.GetProperty("properties").EnumerateObject()
            .Select(p => p.Name)
            .Where(n => n != "$schema")
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(code.SetEquals(schema),
            $"Root keys differ — only in MddJsonConfig: {string.Join(", ", code.Except(schema))}; "
            + $"only in the schema: {string.Join(", ", schema.Except(code))}.");
    }
}
