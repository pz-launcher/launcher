using PzLauncher.Core.Common;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.Tests.Localization;

public class ErrorMessagesTests
{
    private const string Fallback = "FALLBACK-DEVELOPER-MESSAGE";

    public static TheoryData<ErrorCode> AllCodes => [.. Enum.GetValues<ErrorCode>()];

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void ToUserText_EveryErrorCode_DoesNotFallBackToMessage(ErrorCode code)
    {
        var text = ErrorMessages.ToUserText(new Error(code, Fallback, ErrorKind.Unexpected));

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.NotEqual(Fallback, text);
    }

    [Fact]
    public void ToUserText_UndefinedCode_FallsBackToMessage()
    {
        var text = ErrorMessages.ToUserText(new Error((ErrorCode)int.MaxValue, Fallback, ErrorKind.Unexpected));

        Assert.Equal(Fallback, text);
    }

    [Fact]
    public void ToUserText_PathDetail_IsIncluded()
    {
        var error = new Error(ErrorCode.InstallPathNotFound, Fallback, ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["path"] = @"D:\Games\PZ" },
        };

        Assert.Contains(@"D:\Games\PZ", ErrorMessages.ToUserText(error), StringComparison.Ordinal);
    }

    [Fact]
    public void FolderNotLocal_IncludesLocation()
    {
        Assert.Contains("smb://nas/pz", ErrorMessages.FolderNotLocal("smb://nas/pz"), StringComparison.Ordinal);
    }
}
