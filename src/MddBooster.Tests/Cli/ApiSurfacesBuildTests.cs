using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// The Api target's <c>surfaces</c> option — which runtime surfaces the generated registration
/// wires. A deployment that serves only OData should not carry a second, authenticated surface it
/// never protects: authorization policies are attached per surface, so an unused one stays open
/// under the fallback policy. Omitting the option keeps both surfaces, byte for byte.
/// </summary>
public class ApiSurfacesBuildTests
{
    private const string Canon = """
# Namespace: test.surfaces

## Customer

- id: identifier @pk @generated
- name: string(50) @not_null "이름"
- created_at: timestamp @not_null "생성시각"
- updated_at: timestamp @not_null "수정시각"

## Profile ::aspect(Customer)

- bio: text? "소개"
- created_at: timestamp @not_null "생성시각"
- updated_at: timestamp @not_null "수정시각"
""";

    private static string Scaffold(string apiExtra, out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-surf-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(Path.Combine(root, "api"));
        Directory.CreateDirectory(Path.Combine(root, "ent"));
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Canon);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
            {
              "sources": ["./tables.m3l.md"],
              "targets": [
                { "type": "Model", "projectPath": "../ent", "namespace": "S.Entities", "dbContextName": "SDb" },
                { "type": "Api", "projectPath": "../api", "namespace": "S.Server"{{apiExtra}} }
              ]
            }
            """);
        return mddDir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private static string ApiFile(string root, string name) =>
        Path.Combine(root, "api", "Api_gen", name);

    [Fact]
    public void Omitting_surfaces_registers_both_like_before()
    {
        var mddDir = Scaffold("", out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            var reg = File.ReadAllText(ApiFile(root, "ApiRegistration_gen.cs"));
            Assert.Contains("options.ODataModel.AddEntityPair<", reg);
            Assert.Contains("options.GraphQL.AddEntityPair<", reg);
            Assert.Contains("options.ODataModel.DeclareSharedKey(", reg);
            Assert.True(File.Exists(ApiFile(root, "Controllers_gen.cs")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Naming_both_surfaces_is_byte_identical_to_omitting_the_option()
    {
        var omitted = Scaffold("", out var rootA);
        var named = Scaffold(""", "surfaces": ["graphql", "odata"]""", out var rootB);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(omitted));
            Assert.Equal(0, new BuildCommand().Run(named));
            foreach (var file in new[] { "ApiRegistration_gen.cs", "Controllers_gen.cs" })
                Assert.Equal(File.ReadAllText(ApiFile(rootA, file)), File.ReadAllText(ApiFile(rootB, file)));
        }
        finally { Cleanup(rootA); Cleanup(rootB); }
    }

    [Fact]
    public void OData_only_emits_no_GraphQL_registration()
    {
        var mddDir = Scaffold(""", "surfaces": ["odata"]""", out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            var reg = File.ReadAllText(ApiFile(root, "ApiRegistration_gen.cs"));
            Assert.Contains("options.ODataModel.AddEntityPair<", reg);
            Assert.Contains("options.ODataModel.DeclareSharedKey(", reg);
            Assert.DoesNotContain("options.GraphQL.", reg);
            Assert.True(File.Exists(ApiFile(root, "Controllers_gen.cs")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void GraphQL_only_emits_no_OData_registration_controllers_or_shared_keys()
    {
        var mddDir = Scaffold(""", "surfaces": ["graphql"]""", out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            var reg = File.ReadAllText(ApiFile(root, "ApiRegistration_gen.cs"));
            Assert.Contains("options.GraphQL.AddEntityPair<", reg);
            Assert.DoesNotContain("options.ODataModel.", reg);
            Assert.False(File.Exists(ApiFile(root, "Controllers_gen.cs")));
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("[]")]                      // registers nothing — a typo, not a configuration
    [InlineData("""["odata", "odata"]""")]  // a duplicate is a mistake to name, not to fold
    [InlineData("""["rest"]""")]            // unknown surface
    [InlineData("""["OData"]""")]           // values are exact, like every other enum-like key
    public void An_invalid_surfaces_value_fails_the_build(string surfaces)
    {
        var mddDir = Scaffold($""", "surfaces": {surfaces}""", out var root);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Surfaces_on_a_non_Api_target_fails_the_build()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-surf-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(Path.Combine(root, "ent"));
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Canon);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": ["./tables.m3l.md"],
              "targets": [
                { "type": "Model", "projectPath": "../ent", "namespace": "S.Entities", "dbContextName": "SDb", "surfaces": ["odata"] }
              ]
            }
            """);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }
}
