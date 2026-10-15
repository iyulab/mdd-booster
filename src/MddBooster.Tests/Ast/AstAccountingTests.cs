using MddBooster.Core.Ast;

namespace MddBooster.Tests.Ast;

/// <summary>
/// 2026-07-22 — standalone `::view`가 생성 파이프라인에서 무경고로 탈락(silent).
/// 회계 원칙: 파싱된 모든 요소는 소비되거나 경고된다.
/// </summary>
public class AstAccountingTests
{
    /// <summary>
    /// The standalone view is consumed now — the view resolver reads every one, and a view that is
    /// not generated is reported by the build with its reason (no Sql target, a shape the generator
    /// does not emit). Listing it here as well would say "nothing reads this" about something the
    /// build just resolved.
    /// </summary>
    [Fact]
    public void Standalone_view_is_parsed_into_Views_and_is_not_reported_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "standalone-view.m3l.md"));

        // 파서는 §4.7 view를 Models가 아닌 Views 컬렉션으로 분리한다.
        Assert.DoesNotContain(ast.Models, m => m.Name == "VAssetFailureStats");
        Assert.Contains(ast.Views, v => v.Name == "VAssetFailureStats");

        var unconsumed = AstAccounting.ListUnconsumed(ast);

        Assert.DoesNotContain(unconsumed, u => u.StartsWith("::view", StringComparison.Ordinal));
    }

    /// <summary>
    /// A section whose name the language does not define cannot be read by any
    /// target — it arrives as unstructured entries and nothing looks at them.
    /// Dropping it in silence is the same failure the standalone view had.
    /// </summary>
    [Fact]
    public void A_section_the_language_does_not_define_is_reported_unconsumed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## A
- id: identifier @pk @generated
- order_id: identifier @not_null

### PrimaryKey
- fields: [id, order_id]
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);

            Assert.Contains("A: ### PrimaryKey", AstAccounting.ListUnconsumed(ast));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// The sections the language does define stay quiet here: `### Indexes` is
    /// read by the Sql target, and reporting a section that is consumed would
    /// train the reader to ignore this warning.
    /// </summary>
    [Fact]
    public void A_consumed_section_is_not_reported_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "table-with-labeled-indexes.m3l.md"));

        Assert.NotEmpty(ast.Models.SelectMany(m => m.Sections?.Indexes ?? []));
        Assert.Empty(AstAccounting.ListUnconsumed(ast));
    }

    /// <summary>
    /// `### Relations`, `### Behaviors` and `### Metadata` are sections the language
    /// defines, and no target reads any of them — generating the same model with and
    /// without all three produced byte-identical output across every target. Silence
    /// about them is the same silence a section the language does not name got.
    /// </summary>
    private static void AssertSectionIsReportedUnconsumed(string section, string expected)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, $@"# Namespace: x

## B
- id: identifier @pk @generated

## A
- id: identifier @pk @generated
- b_id: identifier @reference(B) @not_null

{section}
");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);

            Assert.Contains(expected, AstAccounting.ListUnconsumed(ast));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// A <c>### Relations</c> entry that names a key's navigation is read (RelationDeclarations) — what it says
    /// beyond the name, such as <c>load</c>, no target generates and is named on its own.
    /// </summary>
    [Fact]
    public void A_relations_entry_naming_a_navigation_is_read_and_only_its_unread_keys_are_reported()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: x\n\n## B\n- id: identifier @pk @generated\n\n## A\n- id: identifier @pk @generated\n" +
            "- b_id: identifier @reference(B) @not_null\n\n### Relations\n- >author\n  - target: B\n  - from: b_id\n  - load: eager\n");
        try
        {
            var unconsumed = AstAccounting.ListUnconsumed(new M3lLoader().LoadFile(tmp));
            Assert.Equal(["A: ### Relations '>author' 의 load"], unconsumed);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void A_behaviors_section_is_reported_unconsumed() =>
        AssertSectionIsReportedUnconsumed(
            """
### Behaviors
- on_create: set_timestamp
""",
            "A: ### Behaviors");

    [Fact]
    public void A_metadata_section_is_reported_unconsumed() =>
        AssertSectionIsReportedUnconsumed(
            """
### Metadata
- domain: "editorial"
""",
            "A: ### Metadata");

    [Fact]
    public void Model_only_ast_reports_nothing_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "bank-account.m3l.md"));

        Assert.Empty(AstAccounting.ListUnconsumed(ast));
    }

    /// <summary>
    /// Relationship notation written among the fields (§3.2.4 — the parser lifts it out with <c>declaredIn: "fields"</c> and
    /// warns <c>M3L-W009</c>) is the same relationship as under <c>### Relations</c>. None of these name a key's navigation, so
    /// each is reported by its own text with the reason it is not read.
    /// </summary>
    [Theory]
    [InlineData("- <>tags: many-to-many", "다대다")]
    [InlineData("- >category: one-to-one", "from: 키")]
    [InlineData("- <posts: one-to-many", "target: Model.key_field")]
    [InlineData("- >author", "from: 키")]
    public void Field_level_relation_notation_is_reported_by_its_text_with_the_reason(string line, string reason)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-rel-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, $"""
            # Namespace: x

            ## Post
            - id: identifier @primary
            - title: string(200)
            {line}
            """);
        try
        {
            var entry = Assert.Single(AstAccounting.ListUnconsumed(new M3lLoader().LoadFile(tmp)));
            Assert.StartsWith("Post: 관계 표기 '", entry, StringComparison.Ordinal);
            Assert.Contains(reason, entry, StringComparison.Ordinal);
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>A <c>### Relations</c> entry that names no key is reported under the section's name, with the reason.</summary>
    [Fact]
    public void A_relations_entry_that_names_no_key_is_reported_with_the_reason()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-relsec-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
            # Namespace: x

            ## Post
            - id: identifier @primary
            - title: string(200)

            ### Relations
            - author: >Author
            """);
        try
        {
            var entry = Assert.Single(AstAccounting.ListUnconsumed(new M3lLoader().LoadFile(tmp)));
            Assert.StartsWith("Post: ### Relations '", entry, StringComparison.Ordinal);
            Assert.Contains("from: 키", entry, StringComparison.Ordinal);
        }
        finally { File.Delete(tmp); }
    }
    /// <summary>
    /// An enum header's parent list is not reported: the parser joins the parent's values into the
    /// child's own (§3.1.6), parent's first, so every target that reads <c>Values</c> already
    /// carries the inherited members. Reporting the parent as unread would now be a false alarm on
    /// a declaration that works — the kind that teaches a reader to skip the whole list.
    /// </summary>
    [Fact]
    public void An_enums_parent_list_is_not_reported_because_the_parser_joins_its_values()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "enum-unread-axes.m3l.md"));

        // The premise: the parent name is still recorded, and its values arrive already merged.
        var extended = Assert.Single(ast.Enums, e => e.Name == "ExtendedStatus");
        Assert.Equal(["BasicStatus"], extended.Inherits);
        Assert.Equal(["draft", "done", "archived"], extended.Values.Select(v => v.Name));

        var unconsumed = AstAccounting.ListUnconsumed(ast);

        Assert.DoesNotContain(unconsumed, u => u.StartsWith("::enum", StringComparison.Ordinal));
    }

    /// <summary>
    /// A custom framework attribute (§2.5.6) is parsed and preserved, and no target reads it — the
    /// generator owns the mapping to each platform. It is reported instead of vanishing from the output,
    /// once per declaration: a field an interface declares is reported on the interface, not again on
    /// every model that takes it.
    /// </summary>
    [Fact]
    public void Framework_attributes_are_reported_once_where_declared()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## Versioned ::interface
- version: integer `[ConcurrencyCheck]`

## Account : Versioned
- id: identifier @pk @generated
- password: string(100) `[DataType(DataType.Password)]` `[JsonIgnore]`
- name: string(50)
""");
        try
        {
            var unconsumed = AstAccounting.ListUnconsumed(new M3lLoader().LoadFile(tmp))
                .Where(u => u.Contains("§2.5.6", StringComparison.Ordinal)).ToList();

            Assert.Equal(3, unconsumed.Count);
            Assert.Single(unconsumed, u => u.StartsWith("Versioned.version:", StringComparison.Ordinal) && u.Contains("ConcurrencyCheck", StringComparison.Ordinal));
            Assert.Single(unconsumed, u => u.StartsWith("Account.password:", StringComparison.Ordinal) && u.Contains("JsonIgnore", StringComparison.Ordinal));
            Assert.Single(unconsumed, u => u.StartsWith("Account.password:", StringComparison.Ordinal) && u.Contains("DataType", StringComparison.Ordinal));
        }
        finally { File.Delete(tmp); }
    }
}
