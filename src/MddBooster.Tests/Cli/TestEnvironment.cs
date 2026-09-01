using System.Runtime.CompilerServices;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Runs once before any test in this assembly. A test invoking <c>Program.Main(["build", ...])</c>
/// must never trigger <see cref="MddBooster.Cli.UpdateNotifier"/>'s live NuGet lookup — that would
/// make test runs depend on network reachability and on whatever the real latest published
/// version happens to be at run time, neither of which this suite should ever depend on.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    public static void DisableUpdateCheck() =>
        Environment.SetEnvironmentVariable("MDD_NO_UPDATE_CHECK", "1");
}
