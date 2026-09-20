using Microsoft.CodeAnalysis;

namespace MddBooster.Tests.TestSupport;

/// <summary>
/// The reference set a probe compilation of generated C# is given: the whole shared
/// framework, taken from the host's trusted-platform-assemblies list.
/// </summary>
/// <remarks>
/// The obvious alternative — <c>AppDomain.CurrentDomain.GetAssemblies()</c> — is not the
/// same set. It is whatever the test process happens to have <em>loaded</em>, so an
/// assembly the generated code needs but nothing in the test project references at run
/// time is simply absent, and the probe fails with <c>CS0246</c> on a type the framework
/// does in fact ship. That is a property of the harness, not of the generated code, and it
/// changes as the generator's output grows: emitting a <c>DisplayAttribute</c> on an enum
/// member was enough to expose it, because <c>System.ComponentModel.Annotations</c> is not
/// loaded by a test run that never touches it.
/// </remarks>
internal static class BclReferences
{
    public static IEnumerable<MetadataReference> All() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
}
