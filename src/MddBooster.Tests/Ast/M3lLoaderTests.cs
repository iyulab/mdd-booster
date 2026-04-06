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
    public void Load_FileNotFound_ThrowsM3lLoadException()
    {
        var loader = new M3lLoader();
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.m3l.md");

        var ex = Assert.Throws<M3lLoadException>(() => loader.LoadFile(missingPath));
        Assert.Equal(missingPath, ex.SourceFile);
    }
}
