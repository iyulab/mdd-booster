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
            return args[0] switch
            {
                "build" => RunBuild(args),
                "--help" or "-h" or "help" => PrintUsage(),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
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
        Console.WriteLine("  mdd help                    이 메시지 출력");
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"알 수 없는 커맨드: '{cmd}'");
        PrintUsage();
        return 1;
    }
}
