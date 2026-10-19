using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>mdd.json</c> <c>owners.&lt;p&gt;.namePrefix: false</c> — an owner that is a layer, not a module: its model names do
/// not begin with the prefix, and the naming rules (<c>MDD017</c>, <c>MDD019</c>) and the generated <c>::aspect</c>
/// navigation all read it the same way.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class OwnerNamingBuildTests
{
    private const string LayerModel = """
# Namespace: test.layer
# Prefix: core

## Asset
- id: identifier @pk @generated
- name: string(100)

## AssetInstrumentProfile ::aspect(Asset)
- range: integer?
""";

    /// <summary>The <c>owners</c> key with one entry, as a line of <c>mdd.json</c>.</summary>
    private static string Owners(string owner, string entry) => $"\"owners\": {{ \"{owner}\": {{ {entry} }} }},";

    private static string Scaffold(string model, string owners, out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-owner-naming-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var sqlDir = Path.Combine(root, "db");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(sqlDir);
        File.WriteAllText(Path.Combine(sqlDir, "Test.sqlproj"), """
<Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
  <PropertyGroup>
    <Name>Test</Name>
    <DSP>Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider</DSP>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./tables.m3l.md"],
  {{owners}}
  "targets": [ { "type": "Sql", "projectPath": "../db", "schema": "dbo" } ]
}
""");
        return mddDir;
    }

    [Fact]
    public void A_layer_owner_names_its_models_without_the_prefix()
    {
        var mddDir = Scaffold(LayerModel, Owners("core", "\"namePrefix\": false"), out var root);

        using var stderr = new ConsoleErrorCapture(this);
        using var stdout = new ConsoleOutCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.DoesNotContain("MDD017", stderr.Text);
            Assert.DoesNotContain("MDD019", stderr.Text);
            // The navigation on the base reads the same answer (ResolvedModel.NamePrefix) — here <Name>.
            Assert.Contains("Asset.InstrumentProfile", stdout.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>Without the declaration the same model is a module's: both rules ask for the prefix.</summary>
    [Fact]
    public void Without_the_declaration_the_prefix_is_asked_for()
    {
        var mddDir = Scaffold(LayerModel, "", out var root);

        using var stderr = new ConsoleErrorCapture(this);
        using var stdout = new ConsoleOutCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.Contains("MDD017", stderr.Text);
            // <Base><Name> is a convention with or without a prefix — the navigation is the same either way.
            Assert.DoesNotContain("MDD019", stderr.Text);
            Assert.Contains("Asset.InstrumentProfile", stdout.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// An aspect whose name does not begin with its base is still <c>MDD019</c> in a layer — the base-name convention is
    /// not the owner's — but what it suggests no longer carries the prefix.
    /// </summary>
    [Theory]
    [InlineData(true, "'Asset<Name>'")]
    [InlineData(false, "'CoreAsset<Name>'")]
    public void An_unconventional_aspect_in_a_layer_is_suggested_a_name_without_the_prefix(bool layer, string suggested)
    {
        var model = LayerModel.Replace("AssetInstrumentProfile", "InstrumentProfile");
        var mddDir = Scaffold(model, layer ? Owners("core", "\"namePrefix\": false") : "", out var root);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.Contains("MDD019", stderr.Text);
            Assert.Contains(suggested, stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary><c>namePrefix: true</c> is the default, stated — it changes nothing.</summary>
    [Fact]
    public void Stating_the_default_changes_nothing()
    {
        var mddDir = Scaffold(LayerModel, Owners("core", "\"namePrefix\": true"), out var root);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.Contains("MDD017", stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>An owner no file declares is a misspelling the build would otherwise pass over silently.</summary>
    [Fact]
    public void An_owner_no_file_declares_is_a_configuration_error()
    {
        var mddDir = Scaffold(LayerModel, Owners("cor", "\"namePrefix\": false"), out var root);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
            Assert.Contains("'cor'", stderr.Text);
            Assert.Contains("core", stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>A key an owner entry does not define is reported like any unknown key.</summary>
    [Fact]
    public void An_unknown_key_in_an_owner_entry_is_MDD024()
    {
        var mddDir = Scaffold(LayerModel, Owners("core", "\"namePrefx\": false"), out var root);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.Contains("MDD024", stderr.Text);
            Assert.Contains("owners.core.namePrefx", stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
