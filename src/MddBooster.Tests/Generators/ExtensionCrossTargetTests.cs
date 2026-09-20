using System.Text.RegularExpressions;
using MddBooster.Generators.Model;
using MddBooster.Generators.TypeScript;
using MddBooster.Tests.Generators.TypeScript;
using MddBooster.Tests.TestSupport;

namespace MddBooster.Tests.Generators;

public class ExtensionCrossTargetTests
{
    private static readonly (string, string)[] Files =
    [
        ("base.m3l.md", "## Asset\n- id: identifier @pk\n- name: string(100)\n"),
        ("insp.m3l.md", "# Prefix: insp\n\n## Asset ::extend\n- insp_grade: string(20)?\n- insp_zone: string(10)? @group(\"Location\")\n"),
    ];

    [Fact]
    public void Extension_fields_default_to_their_owner_as_group_in_both_targets()
    {
        var (_, models) = TwoFileModel.Load(Files);
        var asset = models.Single();

        var cs = EntityPairRenderer.Render(asset, "Ex.Entities").Write;
        Assert.Matches(new Regex(@"\[Display\([^\]]*GroupName = ""insp""[^\]]*\)\](?:\s*\[[^\]]*\])*\s+public string\? InspGrade"), cs);
        Assert.Matches(new Regex(@"\[Display\([^\]]*GroupName = ""Location""[^\]]*\)\](?:\s*\[[^\]]*\])*\s+public string\? InspZone"), cs);
        Assert.DoesNotMatch(new Regex(@"GroupName[^\]]*\]\s+public string Name"), cs);

        var ts = TsFieldSchemaRenderer.RenderAll(models);
        Assert.Contains("group: 'insp'", ts.Replace('"', '\''));
        Assert.Contains("group: 'Location'", ts.Replace('"', '\''));
    }

    [Fact]
    public void Extension_fields_default_to_their_owner_as_form_section_too()
    {
        var (_, models) = TwoFileModel.Load(Files);

        var form = TsFormRenderer.RenderAll(models, [], FormImportFixtures.TestImports)["Asset"];

        var inspSection = SectionBody(form, "insp");
        Assert.Contains("onChange({ InspGrade: v })", inspSection);
        Assert.DoesNotContain("onChange({ Name: v })", inspSection);

        var locationSection = SectionBody(form, "Location");
        Assert.Contains("onChange({ InspZone: v })", locationSection);
    }

    /// <summary>Isolates one rendered &lt;FormSection title="..."&gt; block's body for assertions.</summary>
    private static string SectionBody(string form, string sectionTitle)
    {
        var match = Regex.Match(
            form,
            $@"<FormSection title=""{Regex.Escape(sectionTitle)}"".*?>(.*?)</FormSection>",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"no <FormSection title=\"{sectionTitle}\"> in rendered form");
        return match.Groups[1].Value;
    }

    [Fact]
    public void Extension_fields_reach_the_table_the_entity_and_the_ts_type_as_ordinary_columns()
    {
        var (_, models) = TwoFileModel.Load(Files);
        var asset = models.Single();

        var sql = MddBooster.Generators.Sql.TableRenderer.Render(asset, "dbo");
        Assert.Contains("[InspGrade] NVARCHAR(20) NULL", sql);

        var pair = EntityPairRenderer.Render(asset, "Ex.Entities");
        Assert.Contains("public string? InspGrade { get; set; }", pair.Write);
        Assert.Contains("public string? InspGrade { get; set; }", pair.Read);
        Assert.Contains("string? InspGrade { get; }", pair.Interface);

        var ts = MddBooster.Generators.TypeScript.TsInterfaceRenderer.RenderAll(models, new HashSet<string>());
        Assert.Contains("InspGrade: string | null", ts);
    }
}
