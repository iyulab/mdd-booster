using MddBooster.Core.Generation;
using MddBooster.Tests.TestSupport;

namespace MddBooster.Tests.Generation;

public class ExtensionCoverageTests
{
    [Fact]
    public void One_line_per_extended_model_sources_in_merge_order()
    {
        var (_, models) = TwoFileModel.Load(
            ("base.m3l.md", "## Asset\n- id: identifier @pk\n\n## Plain\n- id: identifier @pk\n"),
            ("insp.m3l.md", "# Prefix: insp\n\n## Asset ::extend\n- insp_a: string?\n- insp_b: string?\n"),
            ("std.m3l.md", "## Asset ::extend\n- warranty_until: date?\n"),
            ("insp2.m3l.md", "# Prefix: insp\n\n## Asset ::extend\n- insp_c: string?\n"));

        Assert.Equal(["Asset ← insp(3), (standard)(1)"], ExtensionCoverage.Describe(models));
    }

    [Fact]
    public void Nothing_when_no_model_is_extended()
    {
        var (_, models) = TwoFileModel.Load(("base.m3l.md", "## Asset\n- id: identifier @pk\n"));
        Assert.Empty(ExtensionCoverage.Describe(models));
    }
}
