using Microsoft.Extensions.Logging;

namespace M3LParser.Logging;

// DebugLogger 클래스 구현 (NullLogger 대체)
internal class DebugLogger : ILogger
{
    private readonly string _name;

    public DebugLogger(string name = null)
    {
        _name = name ?? "Default";
    }

    public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var output = $"[{logLevel}] {_name}: {message}";

        // 디버그 출력으로 로그 메시지 출력
        System.Diagnostics.Debug.WriteLine(output);

        // 예외가 있으면 스택 트레이스도 출력
        if (exception != null)
        {
            System.Diagnostics.Debug.WriteLine(exception.ToString());
        }
    }

    // NullScope 구현
    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        private NullScope() { }
        public void Dispose() { }
    }
}