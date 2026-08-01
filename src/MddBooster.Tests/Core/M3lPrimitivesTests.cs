using MddBooster.Core.Types;

namespace MddBooster.Tests.Core;

/// <summary>
/// The implicit-bound table mirrors a value the language specification owns
/// (§10.4.2, Semantic Types). A mirror with no check against its source is just
/// a second source waiting to disagree — and because the parser reports the
/// semantic type name unchanged rather than expanding it, nothing else in the
/// pipeline would notice the drift.
/// </summary>
public class M3lPrimitivesTests
{
    /// <summary>
    /// The specification's own numbers, transcribed. This is the authority the
    /// table is checked against, so it is written out rather than derived.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> Specified =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["email"] = 320,
            ["phone"] = 20,
            ["url"] = 2048,
        };

    /// <summary>
    /// One deviation is permitted and it is named here. Widening is safe in the
    /// direction that matters — a value the specification allows is still
    /// accepted — whereas adopting the narrower bound would have to narrow
    /// columns that may already hold longer values, which is a migration a
    /// generator cannot carry out on a consumer's behalf.
    /// </summary>
    private static readonly IReadOnlySet<string> PermittedDeviations =
        new HashSet<string>(StringComparer.Ordinal) { "phone" };

    [Fact]
    public void Every_implicit_bound_matches_the_specification_unless_recorded_as_a_deviation()
    {
        var unexpected = M3lPrimitives.ImplicitMaxLength
            .Where(kv => !PermittedDeviations.Contains(kv.Key))
            .Where(kv => !Specified.TryGetValue(kv.Key, out var spec) || spec != kv.Value)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unexpected);
    }

    /// <summary>
    /// Pins the deviation itself. Removing a deviation is a decision; drifting
    /// into a second one is not, and without this the first test would accept
    /// any value at all for a type once its name appeared in the permitted set.
    /// </summary>
    [Fact]
    public void The_only_deviation_is_the_recorded_one_and_it_is_wider_than_the_specification()
    {
        Assert.Equal(["phone"], PermittedDeviations.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var name in PermittedDeviations)
        {
            Assert.True(M3lPrimitives.ImplicitMaxLength[name] > Specified[name],
                $"'{name}' deviates from the specification but is not wider than it.");
        }
    }

    /// <summary>
    /// The specification lists semantic types this generator does not implement
    /// at all; those are absent from the primitive set and reaching one throws.
    /// What must not happen is the reverse — a bound recorded for a type the
    /// generators will never be asked about, which reads as coverage that is
    /// not there.
    /// </summary>
    [Fact]
    public void Every_type_carrying_an_implicit_bound_is_a_known_primitive()
    {
        var orphans = M3lPrimitives.ImplicitMaxLength.Keys
            .Where(k => !M3lPrimitives.All.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void A_type_without_an_implicit_bound_reports_none()
    {
        Assert.Null(M3lPrimitives.ImplicitMaxLengthOf("string"));
        Assert.Null(M3lPrimitives.ImplicitMaxLengthOf("text"));
        Assert.Null(M3lPrimitives.ImplicitMaxLengthOf(null));
    }
}
