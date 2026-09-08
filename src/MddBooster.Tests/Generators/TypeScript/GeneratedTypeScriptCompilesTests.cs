using System.Diagnostics;
using System.Runtime.InteropServices;
using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Generators.TypeScript;

/// <summary>
/// Compiles this generator's own TypeScript output with <c>tsc --strict</c>, against
/// declarations of the consumer modules the README puts under contract.
/// </summary>
/// <remarks>
/// Every other TypeScript test in this suite asserts over the emitted <em>string</em>.
/// That is a different question from whether the string type-checks, and the two came
/// apart: a <c>text</c> field's textarea hard-coded <c>null</c> as its clear value, which
/// a NOT NULL column's <c>Partial&lt;T&gt;</c> field rejects — code no consumer could
/// compile. 657 string assertions were green; <c>tsc</c> found it on the first run.
/// <para>
/// The README says so out loud ("mdd-booster 자체 테스트는 생성된 TS를 컴파일하지 않으므로,
/// 이 계약의 위반은 소비앱 빌드에서만 드러난다"). This closes that, and the README sentence
/// should go when it does.
/// </para>
/// <para>
/// <b>There is deliberately no skip path.</b> The same reasoning as
/// <see cref="AcceptanceModel"/>: a gate that returns quietly when its toolchain is absent
/// reports as passing on every machine that lacks it, counting toward the suite total while
/// asserting nothing. So this restores the toolchain itself when it is missing — which
/// removes the "absent" case rather than tolerating it — and fails loudly, with the command
/// to run, when even that cannot happen.
/// </para>
/// </remarks>
public sealed class GeneratedTypeScriptCompilesTests
{
    /// <summary>
    /// The gate runs in the source tree, not the test output: npm puts <c>node_modules</c>
    /// beside <c>package.json</c>, and the project file deliberately does not copy 24 MB of
    /// toolchain into <c>bin</c>.
    /// </summary>
    private static string GateDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MddBooster.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "MddBooster.Tests", "fixtures", "ts-contract");
    }

    private static (int ExitCode, string Output) Run(string fileName, string arguments, string workingDirectory)
    {
        // `npm`/`npx` are batch shims on Windows and are not executable images there.
        var onWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var psi = new ProcessStartInfo
        {
            FileName = onWindows ? "cmd.exe" : fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (onWindows) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(fileName); }
        foreach (var a in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderr);
    }

    [Fact]
    public void Generated_TypeScript_compiles_against_the_documented_consumer_contract()
    {
        var gate = GateDirectory();
        Assert.True(Directory.Exists(gate), $"the TypeScript gate is missing at '{gate}'");

        if (!Directory.Exists(Path.Combine(gate, "node_modules")))
        {
            var (restoreCode, restoreOutput) = Run("npm", "ci --no-audit --no-fund", gate);
            Assert.True(restoreCode == 0,
                $"the gate's toolchain could not be restored (`npm ci` exited {restoreCode}) in '{gate}'.\n" +
                restoreOutput +
                "\nThis test compiles generated TypeScript, so it needs Node and one offline-cacheable " +
                "install (typescript + @types/react, ~3 packages). It does not skip when they are " +
                "absent: a gate that passes without running is worse than no gate.");
        }

        // The generator writes into the gate directory; regenerate every run so the gate
        // can never be checking output from an earlier build of the generator.
        var generated = Path.Combine(gate, "generated");
        if (Directory.Exists(generated)) Directory.Delete(generated, recursive: true);

        // In-process, like the other end-to-end gates in this suite. Shelling out to
        // `dotnet run` would build the CLI again in whatever configuration that command
        // defaults to — which is not the one the test run was asked for.
        var exitCode = new BuildCommand().Run(gate);
        Assert.True(exitCode == 0, $"generating the gate's TypeScript failed (exit {exitCode})");

        var (tscCode, tscOutput) = Run("npx", "tsc -p tsconfig.json", gate);
        Assert.True(tscCode == 0,
            "generated TypeScript does not compile against the consumer contract this " +
            "repository publishes — a consumer would hit exactly this:\n" + tscOutput);
    }
}
