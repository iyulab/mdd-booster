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
}
