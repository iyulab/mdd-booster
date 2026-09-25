using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// The build command's own arguments are read as options before any of them is
/// read as a path — a flag must never come back as "config directory not found".
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class BuildArgumentTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void A_help_flag_after_build_prints_the_build_usage_and_succeeds(string flag)
    {
        using var stdout = new ConsoleOutCapture(this);
        using var stderr = new ConsoleErrorCapture(this);

        var exit = Program.Main(["build", flag]);

        Assert.Equal(0, exit);
        Assert.Contains("Usage: mdd build", stdout.Text);
        Assert.Equal("", stderr.Text);
    }

    [Fact]
    public void An_unknown_option_is_named_as_an_option_not_as_a_missing_directory()
    {
        using var stdout = new ConsoleOutCapture(this);
        using var stderr = new ConsoleErrorCapture(this);

        var exit = Program.Main(["build", "--dry-run"]);

        Assert.Equal(1, exit);
        Assert.Contains("알 수 없는 옵션: '--dry-run'", stderr.Text);
        Assert.DoesNotContain("설정 디렉터리", stderr.Text);
        Assert.Contains("Usage: mdd build", stdout.Text);
    }

    [Fact]
    public void A_second_positional_argument_is_refused_rather_than_ignored()
    {
        using var stdout = new ConsoleOutCapture(this);
        using var stderr = new ConsoleErrorCapture(this);

        var exit = Program.Main(["build", "one", "two"]);

        Assert.Equal(1, exit);
        Assert.Contains("'two'", stderr.Text);
    }
}
