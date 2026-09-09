using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

public class InterfaceResolverTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Resolve_BankAccountWithTimestampable_IncludesInheritedFields()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var resolver = new InterfaceResolver(ast);

        var resolved = resolver.ResolveAll();

        var bankAccount = Assert.Single(resolved);
        Assert.Equal("BankAccount", bankAccount.Name);

        var fieldNames = bankAccount.Fields.Select(f => f.Name).ToList();
        Assert.Contains("id", fieldNames);
        Assert.Contains("bank_name", fieldNames);
        Assert.Contains("created_at", fieldNames);  // 인터페이스에서 상속
        Assert.Contains("updated_at", fieldNames);  // 인터페이스에서 상속
    }

    [Fact]
    public void Resolve_OwnFieldsComeBeforeInheritedFields()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll();
        var bankAccount = resolved.Single();

        var names = bankAccount.Fields.Select(f => f.Name).ToList();
        var idIndex = names.IndexOf("id");
        var createdAtIndex = names.IndexOf("created_at");

        Assert.True(idIndex >= 0 && createdAtIndex >= 0);
        Assert.True(idIndex < createdAtIndex,
            "모델의 자체 필드가 인터페이스 상속 필드보다 먼저 나와야 한다.");
    }

    /// <summary>
    /// 2026-09-09 — 명세 §3.2.4 의 관계 표기 필드가 해석 결과에 남아 렌더러까지 흘러가
    /// <i>「필드 '…'에 타입이 없습니다」</i>로 빌드를 죽였다. 이 생성기는 관계를
    /// <c>@reference</c> 로만 읽으므로 그 필드는 컬럼이 아니다 — <c>AstAccounting</c> 이
    /// 로드 시점에 경고로 가시화한 뒤(조용한 탈락이 아니다) 여기서 떨어져야 한다.
    /// </summary>
    [Fact]
    public void Relation_notation_fields_are_dropped_so_renderers_never_see_a_typeless_column()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-res-rel-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
            # Namespace: x

            ## Post
            - id: identifier @primary
            - title: string(200)
            - <>tags: many-to-many
            """);
        try
        {
            var resolved = new InterfaceResolver(new M3lLoader().LoadFile(tmp))
                .ResolveAll().Single(m => m.Name == "Post");

            Assert.DoesNotContain(resolved.Fields,
                f => M3lRelationNotation.IsRelationNotation(f));

            // 남은 필드는 그대로여야 한다 — 떨구기가 정상 컬럼까지 먹으면 안 된다.
            Assert.Equal(["id", "title"], resolved.Fields.Select(f => f.Name));
        }
        finally { File.Delete(tmp); }
    }
}
