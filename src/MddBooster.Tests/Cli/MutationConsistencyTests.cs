using System.Text.RegularExpressions;
using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Mutation consistency: change the model in one small, known way and the generated SQL must
/// change in the matching way and nowhere else. The generators are tested one feature at a time
/// and the acceptance gate checks one model as a whole; neither says what a *change* to the model
/// does to the output, which is the question a second build asks. A relation like "adding a field
/// adds a column line to that table and moves nothing else" holds without running anything the
/// output describes, so it needs no database.
/// </summary>
/// <remarks>
/// The relations are stated over lines of the generated files, compared as multisets with the
/// trailing comma ignored (a column list's last line has none, so reordering moves it).
/// Each relation runs on both dialects, over the first few entities of the checked-in acceptance
/// model that the operator can touch, and a failure names the entity, the file and the lines that moved.
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class MutationConsistencyTests
{
    public static IEnumerable<object[]> Dialects() => [["tsql"], ["postgres"]];

    /// <summary>How many entities each relation is run on — each is one build per dialect.</summary>
    private const int EntitiesPerRelation = 8;

    private const string Probe = "mutation_probe_note";

    // ---- reading the model text -------------------------------------------------------------

    /// <summary>An entity block of the model text: its name and the indices of its field lines.</summary>
    private sealed record Entity(string Name, IReadOnlyList<int> FieldLines);

    private static bool IsFieldLine(string line) => line.StartsWith("- ", StringComparison.Ordinal);

    /// <summary>A stored field with nothing derived, referenced or keyed about it: the lines the operators may touch.</summary>
    private static bool IsPlainField(string line) =>
        IsFieldLine(line)
        && !line.Contains("@lookup") && !line.Contains("@rollup") && !line.Contains("@computed")
        && !line.Contains("@reference") && !line.Contains("@pk") && !line.Contains("@primary");

    private static string FieldName(string line) => line[2..line.IndexOf(':')].Trim();

    /// <summary>The "## Name" blocks that are entities (not enums, interfaces, views, aspects or extensions).</summary>
    private static List<Entity> ReadEntities(string[] lines)
    {
        var entities = new List<Entity>();
        string? name = null;
        List<int>? fields = null;
        void Close()
        {
            if (name is not null && fields is { Count: > 0 }) entities.Add(new Entity(name, fields));
            name = null;
            fields = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Close();
                var heading = line[3..].Trim();
                if (heading.Contains("::enum") || heading.Contains("::interface") || heading.Contains("::view")
                    || heading.Contains("::aspect") || heading.Contains("::extend"))
                {
                    continue;
                }
                name = heading.Split(' ', ':')[0];
                fields = [];
            }
            else if (line.StartsWith("#", StringComparison.Ordinal))
            {
                // A sub-heading ("### Source", ...) ends the field list of the block.
                Close();
            }
            else if (fields is not null && IsFieldLine(line))
            {
                fields.Add(i);
            }
        }
        Close();
        return entities;
    }

    // ---- generating --------------------------------------------------------------------------

    private static Dictionary<string, string> Generate(string modelText, string dialect)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-mutation-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "db");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        try
        {
            File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), modelText);
            File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
            {
              "sources": ["./tables.m3l.md"],
              "targets": [
                { "type": "Sql", "dialect": "{{dialect}}", "projectPath": "../db", "emitSqlProj": false }
              ]
            }
            """);

            Assert.Equal(0, new BuildCommand().Run(mddDir));

            return Directory.EnumerateFiles(dbDir, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    f => Path.GetRelativePath(dbDir, f).Replace('\\', '/'),
                    f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // ---- comparing ---------------------------------------------------------------------------

    /// <summary>
    /// The comma-separated items of every line. A column list has one per line and a view's SELECT
    /// list has them all on one, so cutting at the commas makes "a column appeared" look the same in
    /// both — and makes a reorder in either a change of order only.
    /// </summary>
    private static IEnumerable<string> Items(string text) =>
        text.Split('\n').SelectMany(SplitAtTopLevelCommas).Select(i => i.Trim()).Where(i => i.Length > 0);

    /// <summary>Cuts at commas outside parentheses, so <c>DECIMAL(12,2)</c> stays whole.</summary>
    private static IEnumerable<string> SplitAtTopLevelCommas(string line)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '(') depth++;
            else if (line[i] == ')') depth--;
            else if (line[i] == ',' && depth == 0)
            {
                yield return line[start..i];
                start = i + 1;
            }
        }
        yield return line[start..];
    }

    /// <summary>(items only in <paramref name="after"/>, items only in <paramref name="before"/>), as multisets.</summary>
    private static (List<string> Added, List<string> Removed) LineDelta(string before, string after)
    {
        var counts = new Dictionary<string, int>();
        foreach (var l in Items(before))
            counts[l] = counts.GetValueOrDefault(l) - 1;
        foreach (var l in Items(after))
            counts[l] = counts.GetValueOrDefault(l) + 1;

        var added = counts.Where(kv => kv.Value > 0).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        var removed = counts.Where(kv => kv.Value < 0).SelectMany(kv => Enumerable.Repeat(kv.Key, -kv.Value)).ToList();
        return (added, removed);
    }

    private static string Squash(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static List<string> ChangedFiles(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var all = before.Keys.Union(after.Keys).OrderBy(k => k, StringComparer.Ordinal);
        return all.Where(k => !before.TryGetValue(k, out var b) || !after.TryGetValue(k, out var a) || a != b).ToList();
    }

    private static bool BelongsTo(string file, Entity entity) =>
        Squash(Path.GetFileNameWithoutExtension(file)).StartsWith(Squash(entity.Name), StringComparison.Ordinal);

    private static bool IsTableFile(string file) =>
        file.Contains("tables_gen/", StringComparison.OrdinalIgnoreCase);

    // ---- the sweep ---------------------------------------------------------------------------

    /// <param name="Lines">The mutated model text, as lines.</param>
    /// <param name="Column">The column the mutation is about, as the model names it.</param>
    private sealed record Mutation(string[] Lines, string Column);

    /// <summary>
    /// Applies <paramref name="mutate"/> to the first few entities it can touch and checks every changed
    /// file with <paramref name="check"/>. Returns the violations, and how many entities were run.
    /// </summary>
    private static (List<string> Violations, int Ran) Sweep(
        string dialect,
        Func<string[], Entity, Mutation?> mutate,
        Func<string, (List<string> Added, List<string> Removed), string, List<string>> check)
    {
        var text = File.ReadAllText(AcceptanceModel.FixturePath).Replace("\r\n", "\n");
        var lines = text.Split('\n');
        var before = Generate(text, dialect);
        var violations = new List<string>();
        var ran = 0;

        foreach (var entity in ReadEntities(lines))
        {
            if (ran == EntitiesPerRelation) break;
            if (mutate(lines, entity) is not { } mutation) continue;
            ran++;

            Dictionary<string, string> after;
            try
            {
                after = Generate(string.Join('\n', mutation.Lines), dialect);
            }
            catch (Exception e)
            {
                violations.Add($"[{dialect}] {entity.Name}: the mutated model did not build: {e.Message.Split('\n')[0]}");
                continue;
            }

            var changed = ChangedFiles(before, after);
            var strangers = changed.Where(f => !BelongsTo(f, entity)).ToList();
            if (strangers.Count > 0)
            {
                violations.Add($"[{dialect}] {entity.Name}: changed files that are not {entity.Name}'s: {string.Join(", ", strangers)}");
            }
            if (!changed.Any(IsTableFile))
            {
                violations.Add($"[{dialect}] {entity.Name}: its table file did not change; changed: [{string.Join(", ", changed)}]");
            }

            foreach (var file in changed.Where(f => BelongsTo(f, entity)))
            {
                var delta = LineDelta(before.GetValueOrDefault(file, ""), after[file]);
                violations.AddRange(check($"[{dialect}] {entity.Name} {file}", delta, mutation.Column));
            }
        }
        return (violations, ran);
    }

    private static bool CarriesColumn(string line, string column) =>
        Squash(line).Contains(Squash(column), StringComparison.Ordinal);

    private static IEnumerable<string> AllCarry(string where, string what, List<string> lines, string column) =>
        lines.Where(l => !CarriesColumn(l, column)).Select(l => $"{where}: {what} line does not carry '{column}': '{l}'");

    private static void AssertClean((List<string> Violations, int Ran) result)
    {
        Assert.True(result.Ran > 0, "the operator found no entity of the acceptance model it could touch");
        Assert.True(result.Violations.Count == 0, string.Join(Environment.NewLine, result.Violations));
    }

    // ---- the operators -----------------------------------------------------------------------

    private static Mutation? AddField(string[] lines, Entity entity)
    {
        // After the first field line, where a column list can take one more.
        var at = entity.FieldLines[0];
        return new Mutation(lines.Take(at + 1).Append($"- {Probe}: string(30)").Concat(lines.Skip(at + 1)).ToArray(), Probe);
    }

    private static Mutation? SwapAdjacentFields(string[] lines, Entity entity)
    {
        for (var k = 0; k + 1 < entity.FieldLines.Count; k++)
        {
            var a = entity.FieldLines[k];
            var b = entity.FieldLines[k + 1];
            if (b != a + 1 || !IsPlainField(lines[a]) || !IsPlainField(lines[b])) continue;
            var swapped = (string[])lines.Clone();
            (swapped[a], swapped[b]) = (swapped[b], swapped[a]);
            return new Mutation(swapped, FieldName(lines[a]));
        }
        return null;
    }

    private static readonly Regex StringLength = new(@"string\((\d+)\)", RegexOptions.Compiled);

    private static Mutation? LengthenString(string[] lines, Entity entity)
    {
        foreach (var at in entity.FieldLines)
        {
            if (!IsPlainField(lines[at])) continue;
            var m = StringLength.Match(lines[at]);
            if (!m.Success) continue;
            var changed = (string[])lines.Clone();
            changed[at] = StringLength.Replace(lines[at], $"string({int.Parse(m.Groups[1].Value) + 1})", 1);
            return new Mutation(changed, FieldName(lines[at]));
        }
        return null;
    }

    private static Mutation? RemoveField(string[] lines, Entity entity)
    {
        var text = string.Join('\n', lines);
        foreach (var at in entity.FieldLines)
        {
            if (!IsPlainField(lines[at])) continue;
            var name = FieldName(lines[at]);
            // Only a field nothing else in the model names: removing a referenced one is a different test.
            if (Regex.Matches(text, $@"\b{Regex.Escape(name)}\b").Count != 1) continue;
            return new Mutation(lines.Where((_, i) => i != at).ToArray(), name);
        }
        return null;
    }

    /// <summary>A type written as <c>name: type?</c> — nullable — and nothing between the type and the question mark.</summary>
    private static readonly Regex NullableType = new(@"^(- \w+: \w+(?:\([^)]*\))?)\?(?= |$)", RegexOptions.Compiled);

    private static Mutation? RequireField(string[] lines, Entity entity)
    {
        foreach (var at in entity.FieldLines)
        {
            if (!IsPlainField(lines[at])) continue;
            if (!NullableType.IsMatch(lines[at])) continue;
            var changed = (string[])lines.Clone();
            changed[at] = NullableType.Replace(lines[at], "$1", 1);
            return new Mutation(changed, FieldName(lines[at]));
        }
        return null;
    }

    // ---- enums: a value added to a type used by stored columns ---------------------------------

    /// <summary>An enum block of the model text: its name and the index of its first value line.</summary>
    private sealed record EnumBlock(string Name, int FirstValue);

    private static List<EnumBlock> ReadEnums(string[] lines)
    {
        var enums = new List<EnumBlock>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal) || !lines[i].Contains("::enum")) continue;
            var name = lines[i][3..].Trim().Split(' ', ':')[0];
            for (var j = i + 1; j < lines.Length && !lines[j].StartsWith('#'); j++)
            {
                if (!IsFieldLine(lines[j])) continue;
                enums.Add(new EnumBlock(name, j));
                break;
            }
        }
        return enums;
    }

    /// <summary>
    /// The entities that store a column of <paramref name="enumName"/>: those with a field of that type
    /// in their own block, or in a block they inherit from (<c>## Thing : Base</c>), however far up.
    /// </summary>
    private static HashSet<string> EntitiesStoring(string[] lines, string enumName)
    {
        var uses = new Regex($@"^- \w+: {Regex.Escape(enumName)}\b");
        var direct = new HashSet<string>(StringComparer.Ordinal);
        var bases = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? block = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                block = heading.Split(' ', ':')[0];
                var inherits = heading.Contains(" : ") ? heading[(heading.IndexOf(" : ", StringComparison.Ordinal) + 3)..] : "";
                bases[block] = inherits.Split(',').Select(b => b.Trim().Split(' ', ':')[0]).Where(b => b.Length > 0).ToList();
            }
            else if (line.StartsWith('#'))
            {
                block = null;
            }
            else if (block is not null && uses.IsMatch(line))
            {
                direct.Add(block);
            }
        }

        bool Reaches(string name, HashSet<string> seen) =>
            direct.Contains(name)
            || (bases.TryGetValue(name, out var parents) && seen.Add(name) && parents.Any(p => Reaches(p, seen)));

        return ReadEntities(lines).Select(e => e.Name).Where(n => Reaches(n, [])).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>A value name of the given length, which is what sets the width of an enum's columns (never below 20).</summary>
    private static string ValueNameOfLength(int length) => "z".PadRight(length, 'z');

    /// <summary>
    /// Adds one value to each of the first few enums that some entity stores a column of, builds, and hands
    /// the entities that store it and the files that changed to <paramref name="check"/>.
    /// </summary>
    private static (List<string> Violations, int Ran) EnumSweep(
        string dialect, int valueNameLength, Func<string, HashSet<string>, Dictionary<string, string>, Dictionary<string, string>, List<string>> check)
    {
        var text = File.ReadAllText(AcceptanceModel.FixturePath).Replace("\r\n", "\n");
        var lines = text.Split('\n');
        var before = Generate(text, dialect);
        var violations = new List<string>();
        var ran = 0;

        foreach (var block in ReadEnums(lines))
        {
            if (ran == EntitiesPerRelation) break;
            var storing = EntitiesStoring(lines, block.Name);
            if (storing.Count == 0) continue;
            ran++;

            var mutated = lines.Take(block.FirstValue)
                .Append($"- {ValueNameOfLength(valueNameLength)}: \"probe\"")
                .Concat(lines.Skip(block.FirstValue)).ToArray();

            Dictionary<string, string> after;
            try
            {
                after = Generate(string.Join('\n', mutated), dialect);
            }
            catch (Exception e)
            {
                violations.Add($"[{dialect}] enum {block.Name}: the mutated model did not build: {e.Message.Split('\n')[0]}");
                continue;
            }
            violations.AddRange(check($"[{dialect}] enum {block.Name}", storing, before, after));
        }
        return (violations, ran);
    }

    // ---- the relations -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Making_a_nullable_field_required_turns_only_its_column_to_not_null_in_that_entitys_files(string dialect) =>
        AssertClean(Sweep(dialect, RequireField, (where, d, column) =>
            AllCarry(where, "an added", d.Added, column)
                .Concat(AllCarry(where, "a removed", d.Removed, column))
                .Concat(d.Added.Count == d.Removed.Count ? [] : [$"{where}: {d.Added.Count} lines added, {d.Removed.Count} removed"])
                .Concat(d.Added.Where(l => !l.Contains("NOT NULL")).Select(l => $"{where}: an added line is not NOT NULL: '{l}'"))
                .Concat(d.Removed.Where(l => l.Contains("NOT NULL")).Select(l => $"{where}: a removed line was already NOT NULL: '{l}'"))
                .ToList()));

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_value_no_longer_than_the_columns_already_are_changes_no_generated_file(string dialect) =>
        AssertClean(EnumSweep(dialect, valueNameLength: 3, (where, _, before, after) =>
            ChangedFiles(before, after).Select(f => $"{where}: a value that fits changed '{f}'").ToList()));

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_longer_value_widens_only_the_columns_of_that_enum_in_the_files_of_the_entities_that_store_it(string dialect)
    {
        const int Width = 90;
        AssertClean(EnumSweep(dialect, Width, (where, storing, before, after) =>
        {
            var violations = new List<string>();
            var changed = ChangedFiles(before, after);
            if (changed.Count == 0) violations.Add($"{where}: a value of {Width} characters changed no file");

            foreach (var file in changed)
            {
                var name = Squash(Path.GetFileNameWithoutExtension(file));
                if (!IsTableFile(file) || !storing.Any(e => Squash(e) == name))
                {
                    violations.Add($"{where}: changed '{file}', which is not the table of an entity that stores the enum");
                    continue;
                }
                var delta = LineDelta(before.GetValueOrDefault(file, ""), after[file]);
                if (delta.Added.Count == 0 || delta.Added.Count != delta.Removed.Count)
                    violations.Add($"{where} {file}: {delta.Added.Count} lines added, {delta.Removed.Count} removed");
                violations.AddRange(delta.Added.Where(l => !l.Contains($"({Width})")).Select(l => $"{where} {file}: an added line is not the widened column: '{l}'"));
                violations.AddRange(delta.Removed.Where(l => l.Contains($"({Width})")).Select(l => $"{where} {file}: a removed line already had the width: '{l}'"));
            }
            return violations;
        }));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Adding_a_field_adds_lines_for_it_to_that_entitys_files_and_changes_nothing_else(string dialect) =>
        AssertClean(Sweep(dialect, AddField, (where, d, column) =>
            d.Removed.Select(l => $"{where}: a line disappeared: '{l}'")
                .Concat(d.Added.Count == 0 ? [$"{where}: changed without adding a line"] : [])
                .Concat(AllCarry(where, "an added", d.Added, column)).ToList()));

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Swapping_two_adjacent_fields_changes_only_their_order_in_that_entitys_files(string dialect) =>
        AssertClean(Sweep(dialect, SwapAdjacentFields, (where, d, _) =>
            d.Added.Concat(d.Removed).Select(l => $"{where}: more than the order changed: '{l}'").ToList()));

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Lengthening_a_string_rewrites_only_the_lines_of_that_column_in_that_entitys_files(string dialect) =>
        AssertClean(Sweep(dialect, LengthenString, (where, d, column) =>
            AllCarry(where, "an added", d.Added, column)
                .Concat(AllCarry(where, "a removed", d.Removed, column))
                .Concat(d.Added.Count == d.Removed.Count ? [] : [$"{where}: {d.Added.Count} lines added, {d.Removed.Count} removed"])
                .ToList()));

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Removing_a_field_removes_only_the_lines_of_that_column_from_that_entitys_files(string dialect) =>
        AssertClean(Sweep(dialect, RemoveField, (where, d, column) =>
            d.Added.Select(l => $"{where}: a line appeared: '{l}'")
                .Concat(d.Removed.Count == 0 ? [$"{where}: changed without removing a line"] : [])
                .Concat(AllCarry(where, "a removed", d.Removed, column)).ToList()));

    // ---- the comparison holds itself to what it must see ---------------------------------------

    // The relations pass when nothing moves, so the comparison itself is checked against an added
    // line, a removed line, a reorder that is not a change of lines, and a trailing comma that is
    // not a change at all.
    [Fact]
    public void The_line_comparison_sees_additions_removals_and_reorders_for_what_they_are()
    {
        var (added, removed) = LineDelta("a,\nb,\nc", "a,\nb,\nx,\nc");
        Assert.Equal(["x"], added);
        Assert.Empty(removed);

        (added, removed) = LineDelta("a,\nb,\nc", "a,\nb");
        Assert.Empty(added);
        Assert.Equal(["c"], removed);

        (added, removed) = LineDelta("a,\nb,\nc", "a,\nc,\nb");
        Assert.Empty(added);
        Assert.Empty(removed);

        // A comma inside a type is not a separator.
        (added, removed) = LineDelta("[A] DECIMAL(12,2) NULL,\n[B] INT", "[B] INT");
        Assert.Empty(added);
        Assert.Equal(["[A] DECIMAL(12,2) NULL"], removed);

        // A SELECT list is one line: a column added to it is an item added, a reorder is nothing.
        (added, removed) = LineDelta("SELECT t.a, t.b, t.c", "SELECT t.a, t.b, t.x, t.c");
        Assert.Equal(["t.x"], added);
        Assert.Empty(removed);

        (added, removed) = LineDelta("SELECT t.a, t.b, t.c", "SELECT t.a, t.c, t.b");
        Assert.Empty(added);
        Assert.Empty(removed);
    }

    [Fact]
    public void A_changed_file_is_reported_and_an_unchanged_one_is_not()
    {
        var before = new Dictionary<string, string> { ["t/A.sql"] = "x", ["t/B.sql"] = "y" };
        var after = new Dictionary<string, string> { ["t/A.sql"] = "x", ["t/B.sql"] = "z", ["t/C.sql"] = "n" };

        Assert.Equal(["t/B.sql", "t/C.sql"], ChangedFiles(before, after));
    }

    [Fact]
    public void The_entities_read_from_the_model_text_are_the_entities_and_nothing_else()
    {
        var model = new[]
        {
            "# Namespace: t", "", "## Color ::enum", "- red: \"Red\"", "", "## Base ::interface", "- created: timestamp", "",
            "## Thing : Base", "- id: identifier @pk", "- name: string(10)", "", "### Source", "- ignored: string(1)",
        };

        var entities = ReadEntities(model);

        var thing = Assert.Single(entities);
        Assert.Equal("Thing", thing.Name);
        Assert.Equal([9, 10], thing.FieldLines.ToArray());
    }
}
