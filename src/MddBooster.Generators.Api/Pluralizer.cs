namespace MddBooster.Generators.Api;

/// <summary>
/// Backwards-compatible shim that forwards to the canonical
/// <see cref="MddBooster.Core.Naming.Pluralizer"/>. New code should call the
/// Core helper directly; this wrapper exists so C25 tests and any external
/// callers keep working through the Phase H cleanup.
/// </summary>
public static class Pluralizer
{
    public static string Pluralize(string name) =>
        MddBooster.Core.Naming.Pluralizer.Pluralize(name);
}
