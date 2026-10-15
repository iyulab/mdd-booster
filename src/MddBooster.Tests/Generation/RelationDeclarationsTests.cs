using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generation;

/// <summary>
/// <c>### Relations</c> (and <c>@relation</c>) names the navigations a <c>@reference</c> key defines — the forward one
/// on the key's model, the reverse one of a one-to-one on the target. The generator reads the declared name where the
/// key cannot supply one, and does not invent one where nothing is declared.
/// </summary>
public class RelationDeclarationsTests
{
    private static (IReadOnlyList<ResolvedModel> Models, M3lAst Ast) Load(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-rel-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return (new InterfaceResolver(ast).ResolveAll(), ast);
        }
        finally { File.Delete(tmp); }
    }

    private static IReadOnlyList<SemanticDiagnostic> Analyze(IReadOnlyList<ResolvedModel> models, M3lAst ast)
        => new SemanticAnalyzer(models, ast.Enums).Analyze();

    private static (string Write, string Read) Render(IReadOnlyList<ResolvedModel> models, string name)
    {
        var oneToOne = OneToOneRelationships.Describe(models);
        var manyToOne = ManyToOneNavigations.Describe(models, oneToOne);
        var pair = EntityPairRenderer.Render(models.Single(m => m.Name == name), "Test.Ns",
            oneToOne: oneToOne, allModels: models, manyToOne: manyToOne);
        return (pair.Write, pair.Read);
    }

    private const string Person = "## Person\n- id: identifier @pk @generated\n- name: string(50)\n\n";

    [Fact]
    public void A_key_not_named_xxx_id_gets_the_navigation_its_entry_names()
    {
        var (models, ast) = Load(Person +
            "## Post\n- id: identifier @pk @generated\n- owner: identifier @reference(Person)\n\n" +
            "### Relations\n- >owned_by\n  - target: Person\n  - from: owner\n");
        var (write, read) = Render(models, "Post");
        Assert.Contains("public Person OwnedBy { get; set; } = null!;", write);
        Assert.Contains("OwnedBy", read);
        Assert.DoesNotContain(Analyze(models, ast), d => d.Code is "MDD049" or "MDD050");
    }

    [Fact]
    public void A_key_not_named_xxx_id_without_a_declared_name_gets_no_navigation_and_the_build_says_so()
    {
        var (models, ast) = Load(Person +
            "## Post\n- id: identifier @pk @generated\n- owner: identifier @reference(Person)\n");
        var (write, _) = Render(models, "Post");
        Assert.DoesNotContain("Person Owner", write);
        var warning = Assert.Single(Analyze(models, ast), d => d.Code == "MDD049");
        Assert.Equal(SemanticSeverity.Warning, warning.Severity);
        Assert.Contains("from: owner", warning.Message);
    }

    [Fact]
    public void A_declared_name_replaces_the_derived_one_in_both_spellings()
    {
        var (models, _) = Load(Person +
            "## Post\n- id: identifier @pk @generated\n- author_id: identifier @reference(Person)\n" +
            "- parent_id: identifier? @reference(Post)\n\n" +
            "### Relations\n- >writer\n  - target: Person\n  - from: author_id\n" +
            "- @relation(parent_post, -> Post, from: parent_id)\n");
        var (write, read) = Render(models, "Post");
        Assert.Contains("public Person Writer { get; set; } = null!;", write);
        Assert.Contains("public Post? ParentPost { get; set; }", write);
        Assert.DoesNotContain(" Author {", write);
        Assert.Contains("Writer", read);
        Assert.Contains("ParentPost", read);
    }

    private const string SelfOneToOne =
        "## Step\n- id: identifier @pk @generated\n- next_id: identifier? @reference(Step) @unique\n";

    [Fact]
    public void A_self_referencing_one_to_one_is_generated_once_its_reverse_side_is_named()
    {
        var (models, ast) = Load(SelfOneToOne + "\n### Relations\n- <previous\n  - target: Step.next_id\n");
        var relationship = Assert.Single(OneToOneRelationships.Describe(models));
        Assert.Equal(("Next", "Previous"), (relationship.ForwardName, relationship.ReverseName));
        var (_, read) = Render(models, "Step");
        Assert.Contains(" Next {", read);
        Assert.Contains(" Previous {", read);
        Assert.DoesNotContain(Analyze(models, ast), d => d.Code is "MDD049" or "MDD050");
    }

    [Fact]
    public void A_self_referencing_one_to_one_without_a_reverse_name_is_left_out_and_the_build_says_so()
    {
        var (models, ast) = Load(SelfOneToOne);
        Assert.Empty(OneToOneRelationships.Describe(models));
        var warning = Assert.Single(Analyze(models, ast), d => d.Code == "MDD049");
        Assert.Contains("target: Step.next_id", warning.Message);
    }

    [Fact]
    public void The_target_names_the_reverse_side_of_a_one_to_one()
    {
        var (models, _) = Load(
            "## Asset\n- id: identifier @pk @generated\n\n### Relations\n- @relation(profile, <- AssetMaintenanceProfile.asset_id)\n\n" +
            "## AssetMaintenanceProfile\n- id: identifier @pk @generated\n- asset_id: identifier @reference(Asset) @unique\n");
        var relationship = Assert.Single(OneToOneRelationships.Describe(models));
        Assert.Equal("Profile", relationship.ReverseName); // not the derived MaintenanceProfile
    }

    [Theory]
    [InlineData("### Relations\n- >ghost\n  - target: Person\n  - from: nope_id\n", "nope_id")]
    [InlineData("### Relations\n- >author\n  - target: Post\n  - from: author_id\n", "'Person'")]
    [InlineData("### Relations\n- @relation(comments, <- Person.id)\n", "Person.id")]
    [InlineData("### Relations\n- >name\n  - target: Person\n  - from: author_id\n", "'name'")]
    public void An_entry_that_does_not_match_the_keys_stops_the_build(string section, string named)
    {
        var (models, ast) = Load(Person +
            "## Post\n- id: identifier @pk @generated\n- author_id: identifier @reference(Person)\n- name: string(20)\n\n" + section);
        var error = Assert.Single(Analyze(models, ast), d => d.Code == "MDD050");
        Assert.Equal(SemanticSeverity.Error, error.Severity);
        Assert.Contains(named, error.Message);
    }

    private static string DbContext(IReadOnlyList<ResolvedModel> models)
    {
        var oneToOne = OneToOneRelationships.Describe(models);
        return DbContextRenderer.Render(models, "AppDbContext", "Test.Entities",
            oneToOne: oneToOne, manyToOne: ManyToOneNavigations.Describe(models, oneToOne));
    }

    /// <summary>
    /// A navigation named otherwise than its key's stem is configured with its key on both entities — convention would
    /// pair <c>Writer</c> with a shadow <c>WriterId</c> and leave <c>AuthorId</c> unmapped.
    /// </summary>
    [Fact]
    public void A_navigation_named_otherwise_than_its_key_is_configured_with_the_key()
    {
        var (models, _) = Load(Person +
            "## Post\n- id: identifier @pk @generated\n- author_id: identifier @reference(Person)\n" +
            "- editor_id: identifier? @reference(Person)\n\n" +
            "### Relations\n- >writer\n  - target: Person\n  - from: author_id\n");
        var code = DbContext(models);
        Assert.Contains("modelBuilder.Entity<Post>().HasOne(e => e.Writer).WithMany().HasForeignKey(e => e.AuthorId);", code);
        Assert.Contains("modelBuilder.Entity<PostExt>().HasOne(e => e.Writer).WithMany().HasForeignKey(e => e.AuthorId);", code);
        // A conventionally named key is still left to convention on the write entity.
        Assert.DoesNotContain("modelBuilder.Entity<Post>().HasOne(e => e.Editor)", code);
    }

    [Fact]
    public void A_self_referencing_one_to_one_is_configured_with_its_key_and_both_ends()
    {
        var (models, _) = Load(SelfOneToOne + "\n### Relations\n- <previous\n  - target: Step.next_id\n");
        Assert.Contains("modelBuilder.Entity<StepExt>().HasOne(e => e.Next).WithOne(e => e.Previous).HasForeignKey<StepExt>(e => e.NextId);",
            DbContext(models));
    }

    [Fact]
    public void An_entry_on_delete_reaches_the_relationship()
    {
        var (models, _) = Load(Person +
            "## Post\n- id: identifier @pk @generated\n- author_id: identifier @reference(Person)\n\n" +
            "### Relations\n- >author\n  - target: Person\n  - from: author_id\n  - on_delete: restrict\n");
        var post = models.Single(m => m.Name == "Post");
        Assert.Equal(ReferentialAction.Restrict, OnDeleteRule.For(post, post.Fields.Single(f => f.Name == "author_id")));
    }
}
