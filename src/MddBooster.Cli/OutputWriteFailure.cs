namespace MddBooster.Cli;

/// <summary>
/// Puts a file-system refusal to write generated output into words a user can act on.
/// </summary>
/// <remarks>
/// The runtime's message is kept, marked as the runtime's — it is where the file path is, and a
/// cause this does not recognise is still reported rather than guessed at.
/// </remarks>
public static class OutputWriteFailure
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int DiskFull = unchecked((int)0x80070070);
    private const int HandleDiskFull = unchecked((int)0x80070027);

    /// <summary>Whether <paramref name="ex"/> is a refusal this describes, rather than a defect.</summary>
    public static bool IsWriteFailure(Exception ex) => ex is IOException or UnauthorizedAccessException;

    public static string Describe(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var cause = ex switch
        {
            UnauthorizedAccessException => "파일이 읽기 전용이거나 쓸 권한이 없습니다",
            PathTooLongException => "경로가 너무 깁니다",
            DirectoryNotFoundException => "경로의 일부가 없습니다",
            IOException io when io.HResult is SharingViolation or LockViolation
                => "다른 프로그램이 그 파일을 열고 있습니다(편집기·빌드 서버·IDE)",
            IOException io when io.HResult is DiskFull or HandleDiskFull => "디스크 공간이 부족합니다",
            _ => "파일 시스템이 쓰기를 거절했습니다",
        };
        return $"{cause} (원문: {ex.Message})";
    }
}
