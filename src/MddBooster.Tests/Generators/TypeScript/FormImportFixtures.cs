using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators.TypeScript;

/// <summary>
/// The import specifiers renderer-level tests render against.
/// </summary>
/// <remarks>
/// <see cref="TsFormRenderer"/> takes these rather than defaulting them, because
/// the specifier for the generator's own output depends on where the caller
/// writes both file sets. Tests that exercise the renderer in isolation still
/// have to name one, so they name the layout the generator emitted before the
/// specifier was derived — that keeps every pre-existing assertion about the
/// rendered text meaningful and unchanged.
/// <para>
/// That the derivation actually produces this value for that layout is asserted
/// where it belongs — against <see cref="TypeScriptGenerator"/>, which owns the
/// two directories. It is not re-stated here.
/// </para>
/// <para>
/// The three foreign modules have no default either, so the fixture names
/// placeholders that point at no real package — renderer tests assert the
/// shape of the file, not where a consumer keeps its components.
/// </para>
/// </remarks>
internal static class FormImportFixtures
{
    public static readonly TsFormModuleImports TestModules = new()
    {
        Layout = "@example/layout",
        Controls = "@example/controls",
        SelectOptions = "@example/enum-options",
    };

    public static readonly TsFormImports TestImports = new()
    {
        GeneratedTypesBase = "../types",
        Modules = TestModules,
    };
}
