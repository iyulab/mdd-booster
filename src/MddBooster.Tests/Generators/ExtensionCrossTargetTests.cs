using System.Text.RegularExpressions;
using MddBooster.Generators.Model;
using MddBooster.Generators.TypeScript;
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
}
