using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// The three form-import settings only mean anything if they survive the whole
/// path from <c>mdd.json</c> to the rendered file. Renderer-level tests can pass
/// while the key is misspelled, unread, or dropped on the way through the CLI —
/// which is where the value the consumer actually types gets lost.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class FormImportConfigE2ETests
{
    private const string Fixture = """
# Namespace: test.forms

## Priority ::enum

- low: "낮음"
- high: "높음"

## Task

- id: identifier @pk @generated
- title: string(50) @not_null "제목"
- priority: Priority @not_null "우선순위"
""";

    private static string Scaffold(string typeScriptTarget, out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-forms-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);

        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Fixture);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    {{typeScriptTarget}}
  ]
}
""");
        return mddDir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Runs a build with the given TypeScript target JSON and returns the rendered form.</summary>
    private static string BuildAndReadForm(string typeScriptTarget)
    {
        var mddDir = Scaffold(typeScriptTarget, out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            return File.ReadAllText(Path.Combine(root, "ui", "forms", "TaskForm_gen.tsx"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>Runs a build expected to fail on configuration and returns what it printed.</summary>
    private string BuildExpectingConfigError(string typeScriptTarget)
    {
        var mddDir = Scaffold(typeScriptTarget, out var root);
        try
        {
            using var err = new ConsoleErrorCapture(this);
            Assert.Equal(4, new BuildCommand().Run(mddDir));
            Assert.False(Directory.Exists(Path.Combine(root, "ui", "forms")),
                "a configuration error must stop the build before anything is written");
            return err.Text;
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Declared_form_import_modules_reach_the_rendered_file()
    {
        var form = BuildAndReadForm("""
    {
      "type": "TypeScript",
      "outputPath": "../ui/types",
      "formsOutputPath": "../ui/forms",
      "formLayoutImport": "@example/layout",
      "formControlsImport": "@example/controls",
      "formSelectOptionsImport": "@example/enum-options"
    }
""");

        Assert.Contains("from '@example/layout'", form);
        Assert.Contains("from '@example/controls'", form);
        Assert.Contains("from '@example/enum-options'", form);
    }

    /// <summary>
    /// The modules a generated form imports from are the consumer's to name — there
    /// is no default to fall back on, because any default is a guess at a folder
    /// layout or at a component library this generator does not depend on. The
    /// build stops before writing anything and names exactly the keys left out.
    /// </summary>
    [Fact]
    public void A_forms_emitting_target_must_name_every_import_module()
    {
        var stderr = BuildExpectingConfigError("""
    {
      "type": "TypeScript",
      "outputPath": "../ui/types",
      "formsOutputPath": "../ui/forms",
      "formControlsImport": "@example/controls"
    }
""");

        Assert.Contains("formLayoutImport", stderr);
        Assert.Contains("formSelectOptionsImport", stderr);
        Assert.DoesNotContain("formControlsImport", stderr);
    }

    /// <summary>
    /// The other direction: naming the modules on a target that emits no forms
    /// configures nothing, and saying so beats ignoring it.
    /// </summary>
    [Fact]
    public void Import_modules_on_a_target_without_forms_are_rejected()
    {
        var stderr = BuildExpectingConfigError("""
    {
      "type": "TypeScript",
      "outputPath": "../ui/types",
      "formLayoutImport": "@example/layout"
    }
""");

        Assert.Contains("formLayoutImport", stderr);
        Assert.Contains("formsOutputPath", stderr);
    }
}
