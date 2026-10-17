using PzLauncher.Core.Common;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

[Collection(UiTextCultureCollection.Name)]
public class ErrorMessagesTests : UiTextCultureTest
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

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void ToUserText_GameExitDetails_AreIncluded(AppLanguage language)
    {
        Localization.SetLanguage(language);
        const string log = @"D:\Games\PZ\hs_err_pid42.log";
        var crashed = new Error(ErrorCode.LaunchGameCrashed, Fallback, ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = log, ["exitCode"] = "134" },
        };
        var early = new Error(ErrorCode.LaunchGameExitedEarly, Fallback, ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["exitCode"] = "1", ["output"] = "Unrecognized VM option 'Foo'" },
        };

        var crashedText = ErrorMessages.ToUserText(crashed);
        var earlyText = ErrorMessages.ToUserText(early);

        Assert.Contains(log, crashedText, StringComparison.Ordinal);
        Assert.Contains("134", crashedText, StringComparison.Ordinal);
        Assert.Contains("Unrecognized VM option 'Foo'", earlyText, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void ToUserText_PreloadErrors_IncludeTheModId(AppLanguage language)
    {
        Localization.SetLanguage(language);
        ErrorCode[] codes =
        [
            ErrorCode.ZombieBuddyModNotPreloadable, ErrorCode.ZombieBuddyPreloadConfigInvalid, ErrorCode.ZombieBuddyPreloadConfigIoFailed,
        ];

        foreach (var code in codes)
        {
            var error = new Error(code, Fallback, ErrorKind.Validation)
            {
                Details = new Dictionary<string, string> { ["modId"] = "SomeMod", ["path"] = @"C:\zb\config.json", ["reason"] = "r" },
            };

            Assert.Contains("SomeMod", ErrorMessages.ToUserText(error), StringComparison.Ordinal);
        }
    }
}
