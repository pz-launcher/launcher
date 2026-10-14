using PzLauncher.Core.Common;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

[Collection(StringsCultureCollection.Name)]
public class ErrorMessagesTests : StringsCultureTest
{
    private const string Fallback = "FALLBACK-DEVELOPER-MESSAGE";

    public static TheoryData<ErrorCode, AppLanguage> AllCodesInAllLanguages
    {
        get
        {
            var data = new TheoryData<ErrorCode, AppLanguage>();
            foreach (var code in Enum.GetValues<ErrorCode>())
            {
                foreach (var language in Enum.GetValues<AppLanguage>())
                {
                    data.Add(code, language);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllCodesInAllLanguages))]
    public void ToUserText_EveryErrorCodeInEveryLanguage_IsTranslatedUserText(ErrorCode code, AppLanguage language)
    {
        var error = new Error(code, Fallback, ErrorKind.Unexpected);
        var english = ErrorMessages.ToUserText(error);

        Localization.SetLanguage(language);
        var text = ErrorMessages.ToUserText(error);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.NotEqual(Fallback, text);
        if (language != AppLanguage.English)
        {
            Assert.NotEqual(english, text);
        }
    }

    [Fact]
    public void ToUserText_UndefinedCode_FallsBackToMessage() =>
        Assert.Equal(Fallback, ErrorMessages.ToUserText(new Error((ErrorCode)int.MaxValue, Fallback, ErrorKind.Unexpected)));

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void ToUserText_PathDetail_IsIncluded(AppLanguage language)
    {
        Localization.SetLanguage(language);

        Assert.Contains(@"D:\Games\PZ", ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.InstallPathNotFound, @"D:\Games\PZ")), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void FolderNotLocal_IncludesLocation(AppLanguage language)
    {
        Localization.SetLanguage(language);

        Assert.Contains("smb://nas/pz", ErrorMessages.FolderNotLocal("smb://nas/pz"), StringComparison.Ordinal);
    }
}
