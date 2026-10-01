using System.Reflection;
using System.Runtime.CompilerServices;
using MddBooster.Cli.Commands;

[assembly: InternalsVisibleTo("MddBooster.Tests")]

namespace MddBooster.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            var exitCode = args[0] switch
            {
                "build" => RunBuild(args),
                "--help" or "-h" or "help" => PrintUsage(),
                "--version" or "-v" or "version" => PrintVersion(),
                _ => UnknownCommand(args[0]),
            };

            // Only on the actual work command — keeps --help/version output clean and scriptable.
            if (args[0] == "build" && !args.Skip(1).Any(IsHelpFlag))
                UpdateNotifier.CheckAndNotify(GetCurrentVersion());

            return exitCode;
        }
        catch (Exception ex)
        {
            return ReportUnexpected(ex);
        }
    }

    /// <summary>
    /// The answer to a failure nothing translated — a defect or an environment this tool did not
    /// anticipate, as opposed to a model or configuration error, which have their own exit codes.
    /// One line by default; <c>MDD_DEBUG</c> adds the stack trace.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Main"/> so the behaviour can be tested with an exception of the test's
    /// choosing. Each translated failure removes a way to reach this from the command line, and a test
    /// that depends on one of the remaining ways loses its subject the day that one is translated too.
    /// </remarks>
    internal static int ReportUnexpected(Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        if (Environment.GetEnvironmentVariable("MDD_DEBUG") is not null)
        {
            Console.Error.WriteLine(ex.StackTrace);
        }
        return 1;
    }

    private static int RunBuild(string[] args)
    {
        var rest = args.Skip(1).ToArray();

        // Options are recognised before anything is read as a path: a flag handed to
        // the build command must never surface as "config directory not found".
        if (rest.Any(IsHelpFlag))
            return PrintBuildUsage();

        var unknownOption = rest.FirstOrDefault(a => a.StartsWith('-'));
        if (unknownOption is not null)
        {
            Console.Error.WriteLine($"알 수 없는 옵션: '{unknownOption}'");
            PrintBuildUsage();
            return 1;
        }

        if (rest.Length > 1)
        {
            Console.Error.WriteLine($"설정 디렉터리는 하나만 받습니다 — 추가 인자: '{rest[1]}'");
            PrintBuildUsage();
            return 1;
        }

        var configDir = rest.Length == 1 ? rest[0] : Environment.CurrentDirectory;
        return new BuildCommand().Run(configDir);
    }

    private static bool IsHelpFlag(string arg) => arg is "--help" or "-h";

    private static int PrintBuildUsage()
    {
        Console.WriteLine("Usage: mdd build [<config-dir>]");
        Console.WriteLine();
        Console.WriteLine("  <config-dir>   mdd.json 이 있는 디렉터리 (생략하면 현재 디렉터리)");
        Console.WriteLine("  -h, --help     이 메시지 출력");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("mdd — M3L 코드 생성기");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  mdd build [<config-dir>]   현재 또는 지정 디렉터리의 mdd.json을 실행");
        Console.WriteLine("  mdd version                 실행 중인 바이너리 버전 + 빌드 시각 + 경로");
        Console.WriteLine("  mdd help                    이 메시지 출력");
        return 0;
    }

    private static int PrintVersion()
    {
        var asm = typeof(Program).Assembly;
        var location = asm.Location;
        var built = !string.IsNullOrEmpty(location) && File.Exists(location)
            ? File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm:ss")
            : "?";

        Console.WriteLine($"mdd {GetCurrentVersion()}");
        Console.WriteLine($"  built: {built}");
        Console.WriteLine($"  path:  {location}");
        return 0;
    }

    private static string GetCurrentVersion()
    {
        var asm = typeof(Program).Assembly;
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString() ?? "unknown";
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"알 수 없는 커맨드: '{cmd}'");
        PrintUsage();
        return 1;
    }
}
