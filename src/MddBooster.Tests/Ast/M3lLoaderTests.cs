using MddBooster.Core.Ast;

namespace MddBooster.Tests.Ast;

public class M3lLoaderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Load_ValidBankAccountFixture_ReturnsAstWithOneModelAndOneInterface()
    {
        var loader = new M3lLoader();
        var ast = loader.LoadFile(FixturePath("bank-account.m3l.md"));

        Assert.NotNull(ast);
        Assert.Single(ast.Models);
        Assert.Equal("BankAccount", ast.Models[0].Name);
        Assert.Single(ast.Interfaces);
        Assert.Equal("Timestampable", ast.Interfaces[0].Name);
    }

    [Fact]
    public void Load_AstErrors_ThrowsM3lLoadException()
    {
        var loader = new M3lLoader();
        var tmpFile = Path.Combine(Path.GetTempPath(), $"invalid-{Guid.NewGuid():N}.m3l.md");
        // Unresolved inheritance reference causes AST errors even when parse succeeds
        File.WriteAllText(tmpFile, "# Namespace: test\n\n## Foo : NonExistent\n- id: identifier @pk\n");

        try
        {
            Assert.Throws<M3lLoadException>(() => loader.LoadFile(tmpFile));
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void LoadFiles_CrossFileInterfaceInheritance_ResolvesWithoutE007()
    {
        var loader = new M3lLoader();
        var dir = Path.Combine(Path.GetTempPath(), $"m3l-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var common = Path.Combine(dir, "common.m3l.md");
        var asset = Path.Combine(dir, "asset.m3l.md");
        File.WriteAllText(common,
            "# Namespace: test.common\n\n## Timestampable ::interface\n\n" +
            "- created_at: timestamp \"생성 일시\"\n- updated_at: timestamp \"수정 일시\"\n");
        File.WriteAllText(asset,
            "# Namespace: test.asset\n\n## Asset : Timestampable\n\n" +
            "- id: identifier @pk @generated\n- name: string(100) @not_null \"이름\"\n");

        try
        {
            var ast = loader.LoadFiles([common, asset]);

            Assert.NotNull(ast);
            var model = Assert.Single(ast.Models);
            Assert.Equal("Asset", model.Name);
            // Rust resolve가 부모 인터페이스 필드를 model.Fields에 평탄화한다.
            Assert.Contains(model.Fields, f => f.Name == "created_at");
            Assert.Contains(model.Fields, f => f.Name == "updated_at");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadFiles_UnresolvedInheritance_StillThrows()
    {
        var loader = new M3lLoader();
        var tmpFile = Path.Combine(Path.GetTempPath(), $"invalid-multi-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmpFile, "# Namespace: test\n\n## Foo : NonExistent\n- id: identifier @pk\n");

        try
        {
            Assert.Throws<M3lLoadException>(() => loader.LoadFiles([tmpFile]));
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void Load_FileNotFound_ThrowsM3lLoadException()
    {
        var loader = new M3lLoader();
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.m3l.md");

        var ex = Assert.Throws<M3lLoadException>(() => loader.LoadFile(missingPath));
        Assert.Equal(missingPath, ex.SourceFile);
    }
}
