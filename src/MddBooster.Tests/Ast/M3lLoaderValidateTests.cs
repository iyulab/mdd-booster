using MddBooster.Core.Ast;

namespace MddBooster.Tests.Ast;

/// <summary>
/// 검증기는 파서와 <b>다른 층</b>이다 — 등록된 <c>::attribute</c> 의 오용(M3L-W005~W008) 같은
/// 규칙은 검증기에만 있어서, 파서 경고만 표면화하던 동안 소비자는 <c>::attribute</c> 를 등록해도
/// 오용을 볼 방법이 없었다.
/// </summary>
/// <remarks>
/// 그리고 레지스트리 선언과 사용이 다른 파일에 있으면 <b>한 단위로 검증해야만</b> 만난다:
/// 파일별 독립 검증은 그 파일 혼자서 아는 것만 보고하므로 경고를 놓치거나 오탐한다. 파싱을
/// 병합 단위로 하는 이유(§2.1 Rule 3)가 검증에도 그대로 적용되는 자리다.
/// </remarks>
public class M3lLoaderValidateTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"m3l-validate-{Guid.NewGuid():N}");

    public M3lLoaderValidateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string RegistryFile =
        "# Registry\n\n## help ::attribute\n- target: [value, field]\n- type: string\n";

    [Fact]
    public void ValidateFiles_RegistryDeclaredInAnotherFile_IsStillEnforced()
    {
        var registry = Write("registry.m3l.md", RegistryFile);
        var usage = Write("usage.m3l.md", "# Usage\n\n## Kind ::enum\n- alpha: \"Alpha\" @help(123)\n");

        var diagnostics = new M3lLoader().ValidateFiles([registry, usage]);

        var warning = Assert.Single(diagnostics.Warnings, d => d.Code == "M3L-W005");
        Assert.Equal(usage, warning.File);
        Assert.Empty(diagnostics.Errors);
    }

    /// <summary>
    /// 대조군 — 등록된 속성을 규칙대로 쓰면 아무 말도 하지 않는다. 경고가 나오는 쪽만 고정하면
    /// "언제나 경고한다"도 같은 테스트를 통과한다.
    /// </summary>
    [Fact]
    public void ValidateFiles_ConformingUsage_IsSilent()
    {
        var registry = Write("registry.m3l.md", RegistryFile);
        var usage = Write("usage.m3l.md", "# Usage\n\n## Kind ::enum\n- alpha: \"Alpha\" @help(\"도움말\")\n");

        var diagnostics = new M3lLoader().ValidateFiles([registry, usage]);

        Assert.DoesNotContain(diagnostics.Warnings, d => d.Code == "M3L-W005");
        Assert.Empty(diagnostics.Errors);
    }

    /// <summary>
    /// 필드 쪽 사용도 같은 레지스트리로 검사된다 — enum 값만 걸리고 필드는 안 걸리면
    /// <c>target</c> 목록이 반만 동작하는 것이다.
    /// </summary>
    [Fact]
    public void ValidateFiles_ChecksEveryDeclaredTarget()
    {
        var registry = Write("registry.m3l.md", RegistryFile);
        var usage = Write(
            "usage.m3l.md",
            "# Usage\n\n## Item\n- id: identifier @pk\n- note: string(100)? @help(true) \"비고\"\n");

        var diagnostics = new M3lLoader().ValidateFiles([registry, usage]);

        var warning = Assert.Single(diagnostics.Warnings, d => d.Code == "M3L-W005");
        Assert.Contains("note", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 없는 파일에 대해 <see cref="M3lLoader.LoadFiles"/> 와 같은 예외를 낸다 — 두 진입점이 같은
    /// 입력 계약을 공유하므로(내부적으로 같은 곳에서 JSON 을 만든다) 실패도 같은 모양이어야 한다.
    /// </summary>
    [Fact]
    public void ValidateFiles_MissingFile_ThrowsM3lLoadException()
    {
        var missing = Path.Combine(_dir, "absent.m3l.md");

        Assert.Throws<M3lLoadException>(() => new M3lLoader().ValidateFiles([missing]));
    }
}
