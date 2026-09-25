namespace MddBooster.Cli.Config;

/// <summary>
/// mdd.json 을 읽을 수 없거나 그 값을 쓸 수 없다 — 설정 오류(종료 코드 4). 메시지는 사용자가 고칠
/// 자리(파일·행·항목)를 먼저 말한다.
/// </summary>
public sealed class ConfigException(string message, string path, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>문제가 된 설정 파일의 경로.</summary>
    public string Path { get; } = path;
}
