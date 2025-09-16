using Microsoft.Extensions.Logging;

namespace MDDBooster.Logging;

/// <summary>
/// MDDBooster와 관련 프로젝트의 로깅을 관리하는 정적 클래스
/// </summary>
public static class LoggingManager
{
    private static ILoggerFactory _loggerFactory;

    /// <summary>
    /// 로깅 시스템을 초기화합니다.
    /// </summary>
    public static void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        // M3LParser 로깅 초기화
        var m3lLogger = _loggerFactory.CreateLogger("M3LParser");
        M3LParser.Logging.AppLog.Logger = m3lLogger;
    }

    /// <summary>
    /// 특정 카테고리에 대한 ILogger 인스턴스를 생성합니다.
    /// </summary>
    public static ILogger<T> CreateLogger<T>()
    {
        if (_loggerFactory == null)
            throw new InvalidOperationException("LoggingManager has not been initialized. Call Initialize() first.");

        return _loggerFactory.CreateLogger<T>();
    }

    /// <summary>
    /// 특정 카테고리에 대한 ILogger 인스턴스를 생성합니다.
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        if (_loggerFactory == null)
            throw new InvalidOperationException("LoggingManager has not been initialized. Call Initialize() first.");

        return _loggerFactory.CreateLogger(categoryName);
    }
}