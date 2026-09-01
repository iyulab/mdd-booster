using System.Reflection;
using MddBooster.Cli.Commands;

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
            if (args[0] == "build")
                UpdateNotifier.CheckAndNotify(GetCurrentVersion());

            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("MDD_DEBUG") is not null)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return 1;
        }
    }

    private static int RunBuild(string[] args)
    {
        var configDir = args.Length >= 2 ? args[1] : Environment.CurrentDirectory;
        return new BuildCommand().Run(configDir);
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
