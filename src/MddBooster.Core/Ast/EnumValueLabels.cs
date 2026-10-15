using M3L.Native;

namespace MddBooster.Core.Ast;

/// <summary>
/// The display text of an enum value — one home for every target that names a value to a person: the Model target's
/// <c>[Display(Name)]</c> and the TypeScript label maps.
/// </summary>
public static class EnumValueLabels
{
    /// <summary>
    /// The label the model declares for <paramref name="value"/> — the specification's <c>name(Label)</c> form, or the
    /// value's description when it declares none (the specification's own fallback for a consumer that needs one).
    /// <see langword="null"/> when it declares neither; a target that must name every value supplies its own default.
    /// </summary>
    public static string? Display(EnumValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!string.IsNullOrWhiteSpace(value.Label)) return value.Label;
        return string.IsNullOrWhiteSpace(value.Description) ? null : value.Description;
    }
}
