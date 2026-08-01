using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// The three form-import settings only mean anything if they survive the whole
/// path from <c>mdd.json</c> to the rendered file. Renderer-level tests can pass
/// while the key is misspelled, unread, or dropped on the way through the CLI —
/// which is where the value the consumer actually types gets lost.
/// </summary>
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

    /// <summary>Runs a build with the given TypeScript target JSON and returns the rendered form.</summary>
    private static string BuildAndReadForm(string typeScriptTarget)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-forms-{Guid.NewGuid():N}");
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

        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            return File.ReadAllText(Path.Combine(root, "ui", "forms", "TaskForm_gen.tsx"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
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
    /// A consumer who upgrades without touching their config must get the file
    /// they already had. This is the compatibility claim of the whole change,
    /// asserted where a consumer would experience it.
    /// </summary>
    [Fact]
    public void Omitting_them_leaves_a_configuration_free_consumer_unchanged()
    {
        var form = BuildAndReadForm("""
    {
      "type": "TypeScript",
      "outputPath": "../ui/types",
      "formsOutputPath": "../ui/forms"
    }
""");

        Assert.Contains("from '@iyulab/enterprise'", form);
        Assert.Contains("from '../components/ui'", form);
        Assert.Contains("from '../lib/select-options'", form);

        // The sibling layout this consumer declared is also the historical one,
        // so the derived specifier has to come out identical too.
        Assert.Contains("from '../types/entities_gen'", form);
    }
}
