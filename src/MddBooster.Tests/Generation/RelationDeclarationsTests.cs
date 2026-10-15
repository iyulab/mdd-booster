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

    private const string PostWithAuthor =
        "## Post\n- id: identifier @pk @generated\n- author_id: identifier @reference(Person)\n- name: string(20)\n\n";

    /// <summary>
    /// An entry that contradicts the keys is the parser's error (<c>M3L-E010</c>) — the build stops on it before the
    /// generator's own analysis runs, so the generator does not report it a second time under its own code.
    /// </summary>
    [Theory]
    [InlineData("### Relations\n- >ghost\n  - target: Person\n  - from: nope_id\n", "nope_id")]
    [InlineData("### Relations\n- >author\n  - target: Post\n  - from: author_id\n", "\"Person\"")]
    [InlineData("### Relations\n- @relation(comments, <- Person.id)\n", "Person.id")]
    public void An_entry_that_contradicts_the_keys_is_the_parsers_error(string section, string named)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-rel-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + Person + PostWithAuthor + section);
        try
        {
            var error = Assert.Single(new M3lLoader().ValidateFiles([tmp]).Errors, e => e.Code == "M3L-E010");
            Assert.Contains(named, error.Message);
            var (models, ast) = Load(Person + PostWithAuthor + section);
            Assert.DoesNotContain(Analyze(models, ast), d => d.Code == "MDD050");
        }
        finally { File.Delete(tmp); }
    }

    [Theory]
    [InlineData("### Relations\n- >name\n  - target: Person\n  - from: author_id\n", "'name'")]
    [InlineData("### Relations\n- <posts\n  - target: Person\n", "target: Model.key_field")]
    public void An_entry_the_generator_cannot_name_a_navigation_from_stops_the_build(string section, string named)
    {
        var (models, ast) = Load(Person + PostWithAuthor + section);
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
        Assert.Contains("modelBuilder.Entity<Post>().HasOne(e => e.Writer).WithMany().HasForeignKey(e => e.AuthorId).OnDelete(DeleteBehavior.NoAction);", code);
        Assert.Contains("modelBuilder.Entity<PostExt>().HasOne(e => e.Writer).WithMany().HasForeignKey(e => e.AuthorId).OnDelete(DeleteBehavior.NoAction);", code);
        // A conventionally named key is configured too — with the delete action the schema has.
        Assert.Contains("modelBuilder.Entity<Post>().HasOne(e => e.Editor).WithMany().HasForeignKey(e => e.EditorId).OnDelete(DeleteBehavior.NoAction);", code);
    }

    [Fact]
    public void A_self_referencing_one_to_one_is_configured_with_its_key_and_both_ends()
    {
        var (models, _) = Load(SelfOneToOne + "\n### Relations\n- <previous\n  - target: Step.next_id\n");
        Assert.Contains("modelBuilder.Entity<StepExt>().HasOne(e => e.Next).WithOne(e => e.Previous).HasForeignKey<StepExt>(e => e.NextId).OnDelete(DeleteBehavior.NoAction);",
            DbContext(models));
    }

    private const string PostWithComments =
        "## Post\n- id: identifier @pk @generated\n- title: string(50)\n\n### Relations\n- <comments\n  - target: Comment.post_id\n\n" +
        "## Comment\n- id: identifier @pk @generated\n- post_id: identifier @reference(Post)\n- body: string(200)\n";

    private static IReadOnlyList<ReverseCollections.Collection> Collections(IReadOnlyList<ResolvedModel> models)
    {
        var oneToOne = OneToOneRelationships.Describe(models);
        return ReverseCollections.Describe(models, oneToOne, ManyToOneNavigations.Describe(models, oneToOne));
    }

    private static (string Write, string Read) RenderWithCollections(IReadOnlyList<ResolvedModel> models, string name)
    {
        var oneToOne = OneToOneRelationships.Describe(models);
        var manyToOne = ManyToOneNavigations.Describe(models, oneToOne);
        var pair = EntityPairRenderer.Render(models.Single(m => m.Name == name), "Test.Ns",
            oneToOne: oneToOne, allModels: models, manyToOne: manyToOne,
            collections: ReverseCollections.Describe(models, oneToOne, manyToOne));
        return (pair.Write, pair.Read);
    }

    private static string DbContextWithCollections(IReadOnlyList<ResolvedModel> models)
    {
        var oneToOne = OneToOneRelationships.Describe(models);
        var manyToOne = ManyToOneNavigations.Describe(models, oneToOne);
        return DbContextRenderer.Render(models, "AppDbContext", "Test.Entities", oneToOne: oneToOne, manyToOne: manyToOne,
            collections: ReverseCollections.Describe(models, oneToOne, manyToOne));
    }

    /// <summary>
    /// A reverse entry naming an ordinary key gives the target's read type a collection of the dependents' read types,
    /// paired in EF with the key's many-to-one navigation — one relationship, two ends. The write entity gains nothing:
    /// a collection on it would make a body carry nested rows the write path does not write.
    /// </summary>
    [Fact]
    public void A_reverse_entry_on_an_ordinary_key_is_a_collection_on_the_read_type()
    {
        var (models, ast) = Load(PostWithComments);
        var (write, read) = RenderWithCollections(models, "Post");
        Assert.Contains("public ICollection<CommentExt> Comments { get; set; } = [];", read);
        Assert.Contains("using System.Collections.Generic;", read);
        Assert.DoesNotContain("Comments", write);
        Assert.Contains(
            "modelBuilder.Entity<CommentExt>().HasOne(e => e.Post).WithMany(e => e.Comments).HasForeignKey(e => e.PostId).OnDelete(DeleteBehavior.NoAction);",
            DbContextWithCollections(models));
        Assert.DoesNotContain(Analyze(models, ast), d => d.Code is "MDD027" or "MDD049" or "MDD050");
        Assert.Empty(AstAccounting.ListUnconsumed(ast));
    }

    [Fact]
    public void A_read_type_without_a_collection_renders_as_before()
    {
        var (models, _) = Load(PostWithComments.Replace("### Relations\n- <comments\n  - target: Comment.post_id\n\n", ""));
        var (_, read) = RenderWithCollections(models, "Post");
        Assert.DoesNotContain("ICollection", read);
        Assert.DoesNotContain("System.Collections.Generic", read);
        Assert.Contains(".WithMany().HasForeignKey(e => e.PostId)", DbContextWithCollections(models));
    }

    /// <summary>The entry written as a directive, or with its key as its own <c>from:</c>, is the same collection.</summary>
    [Theory]
    [InlineData("- @relation(comments, <- Comment.post_id)\n")]
    [InlineData("- <comments\n  - target: Comment\n  - from: post_id\n")]
    public void Every_spelling_of_the_reverse_entry_is_the_same_collection(string entry)
    {
        var (models, _) = Load(PostWithComments.Replace("- <comments\n  - target: Comment.post_id\n", entry));
        var c = Assert.Single(Collections(models));
        Assert.Equal(("Post", "Comment", "post_id", "Comments", "Post"), (c.PrincipalModel, c.DependentModel, c.ForeignKeyField, c.Name, c.ForwardName));
    }

    [Fact]
    public void A_collection_whose_name_is_taken_is_left_out_and_the_build_says_so()
    {
        var (models, ast) = Load(PostWithComments.Replace("- title: string(50)\n", "- title: string(50)\n- comments: integer\n"));
        Assert.NotNull(Assert.Single(Collections(models)).CollidesWith);
        var (_, read) = RenderWithCollections(models, "Post");
        Assert.DoesNotContain("ICollection", read);
        var warning = Assert.Single(Analyze(models, ast), d => d.Code == "MDD027");
        Assert.Contains("'Comments'", warning.Message);
        Assert.Contains(".WithMany().HasForeignKey(e => e.PostId)", DbContextWithCollections(models));
    }

    /// <summary>
    /// A key whose forward navigation is not generated (here: no <c>_id</c> to strip, no declared name) still gets the
    /// declared collection — configured from the collection's own end.
    /// </summary>
    [Fact]
    public void A_collection_without_a_forward_navigation_is_configured_from_its_own_end()
    {
        var (models, _) = Load(PostWithComments
            .Replace("- target: Comment.post_id", "- target: Comment.parent")
            .Replace("- post_id: identifier @reference(Post)", "- parent: identifier @reference(Post)"));
        Assert.Null(Assert.Single(Collections(models)).ForwardName);
        Assert.Contains(
            "modelBuilder.Entity<PostExt>().HasMany(e => e.Comments).WithOne().HasForeignKey(e => e.Parent).OnDelete(DeleteBehavior.NoAction);",
            DbContextWithCollections(models));
    }

    [Fact]
    public void A_reverse_entry_on_an_internal_key_makes_no_collection_and_is_reported_as_unread()
    {
        var (models, ast) = Load(PostWithComments.Replace("- post_id: identifier @reference(Post)", "- post_id: identifier @reference(Post) @internal"));
        Assert.Empty(Collections(models));
        var line = Assert.Single(AstAccounting.ListUnconsumed(ast));
        Assert.Contains("@internal", line);
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
