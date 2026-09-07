using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// 생성기가 <b>의도적으로 구현하지 않은</b> 축을 빌드 시점에 스스로 말하는지.
/// </summary>
/// <remarks>
/// 소비자 입장에서 「설계된 미구현」과 「결함」은 구분되지 않는다 — 폼이 그냥 텍스트 상자를 낸다.
/// 근거가 README 에 실측 표까지 갖춰 문서화돼 있어도 그것만으로는 닿지 않는다는 것이 관측됐고,
/// 닿지 않은 것은 문서가 아니라 <b>시점</b>이었다. 그래서 소비자가 반드시 보는 출력인 빌드에 건다.
/// <para>
/// 이 테스트가 고정하는 것은 문구가 아니라 <b>계약</b>이다: ⑴ 안내가 나오고 어느 필드인지 지목한다
/// ⑵ 「의도된 것」이라고 말한다 ⑶ 근거 위치를 가리킨다 ⑷ 진단이 아니다 ⑸ 해당 필드가 없거나 폼을
/// 만들지 않으면 조용하다. ⑷⑸ 가 없으면 이 줄은 소음이 되고, 소음이 된 줄은 곧 무시되며,
/// 무시되는 안내는 없는 것보다 나쁘다 — 있는 것처럼 보이기 때문이다.
/// </para>
/// <para>
/// ⑷ 는 접두사가 아니라 <b>스트림</b>으로 고정한다. 이 CLI 에서 stdout 은 정보(<c>[m3l] 로딩</c>·
/// <c>[ts] 완료</c>), stderr 은 진단(경고·오류)이고,
/// <c>LargeModelAcceptanceTests.Building_the_acceptance_fixture_reports_nothing_on_stderr</c> 가
/// 「깨끗한 모델은 stderr 가 비어 있다」를 재고 있다. 안내를 stderr 에 실으면 그 게이트를
/// <b>약화시켜야</b> 맞출 수 있다 — 대신 스트림을 맞췄고, 여기서 그 사실 자체를 고정한다.
/// 빌드가 <i>못 한</i> 일이 아니라 <i>한</i> 일에 대한 설명이므로 애초에 진단이 아니다.
/// </para>
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class IntentionalGapNoticeTests
{
    private const string WithTimeField =
        "# Namespace: X\n\n" +
        "## Shift\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(30) @not_null \"교대명\"\n" +
        "- starts_at: time @not_null \"시작시각\"\n";

    private const string WithoutTimeField =
        "# Namespace: X\n\n" +
        "## Shift\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(30) @not_null \"교대명\"\n";

    private const string TypeScriptTarget =
        """{ "type": "TypeScript", "outputPath": "../ts", "formsOutputPath": "../ts/forms" }""";

    private const string NoticePrefix = "[typescript] 안내:";

    private static string Scaffold(string canon, string targetsJson, out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-gapnotice-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(Path.Combine(root, "ts"));
        Directory.CreateDirectory(Path.Combine(root, "api"));
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), canon);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            $"{{\n  \"sources\": [\"./tables.m3l.md\"],\n  \"targets\": [{targetsJson}]\n}}");
        return mddDir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void Time_field_reaching_a_form_target_is_announced_as_deliberate_with_its_rationale()
    {
        var mddDir = Scaffold(WithTimeField, TypeScriptTarget, out var root);
        using var stdout = new ConsoleOutCapture(this);
        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = new BuildCommand().Run(mddDir);

        try
        {
            Assert.Equal(0, exitCode);

            // ⑴ 나온다, 그리고 어느 필드인지 지목한다 — 「어딘가에 있다」는 안내는 안내가 아니다.
            Assert.Contains(NoticePrefix, stdout.Text);
            Assert.Contains("Shift.starts_at", stdout.Text);

            // ⑵ 결함이 아니라고 말한다. 이것이 없으면 소비자는 고치려 들고, 고칠 것이 없으므로
            //    그 시도는 전부 낭비이며 결국 안내 자체를 끄게 만든다.
            Assert.Contains("의도된 것", stdout.Text);

            // ⑶ 근거 위치를 가리킨다 — 이 안내의 목적은 설명이 아니라 «도달»이다.
            Assert.Contains("README", stdout.Text);

            // ⑷ 진단이 아니다 — 스트림으로 고정한다. 클래스 remarks 참조.
            Assert.DoesNotContain(NoticePrefix, stderr.Text);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void No_time_field_means_no_notice()
    {
        var mddDir = Scaffold(WithoutTimeField, TypeScriptTarget, out var root);
        using var stdout = new ConsoleOutCapture(this);
        var exitCode = new BuildCommand().Run(mddDir);

        try
        {
            Assert.Equal(0, exitCode);
            Assert.DoesNotContain(NoticePrefix, stdout.Text);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Notice_is_scoped_to_form_targets()
    {
        // 폼을 만들지 않는 구성에서는 말할 것이 없다 — `time` 이 텍스트 상자로 나오는 것은
        // TypeScript 폼 렌더러의 결정이고, 서버 타깃만 쓰는 소비자에게는 해당 사항이 아니다.
        // 이 구분이 없으면 안내가 관계없는 빌드에도 붙어 소음이 된다.
        var mddDir = Scaffold(
            WithTimeField,
            """{ "type": "Api", "projectPath": "../api", "namespace": "T.Server" }""",
            out var root);
        using var stdout = new ConsoleOutCapture(this);
        var exitCode = new BuildCommand().Run(mddDir);

        try
        {
            Assert.Equal(0, exitCode);
            Assert.DoesNotContain(NoticePrefix, stdout.Text);
        }
        finally { Cleanup(root); }
    }
}
